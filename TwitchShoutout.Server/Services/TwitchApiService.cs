// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable MemberCanBeMadeStatic.Global

using Microsoft.EntityFrameworkCore;
using NodaTime.TimeZones;
using RestSharp;
using TwitchShoutout.Database;
using TwitchShoutout.Database.Models;
using TwitchShoutout.Server.Config;
using TwitchShoutout.Server.Dtos;
using TwitchShoutout.Server.Helpers;
using TwitchLib.Api.Helix.Models.Chat.GetUserChatColor;

namespace TwitchShoutout.Server.Services;

public class TwitchApiService
{
    private static BotDbContext DbContext { get; set; } = null!;
    private readonly RestClient _client;

    public TwitchApiService(BotDbContext dbContext)
    {
        DbContext = dbContext;
        _client = new(Globals.TwitchApiUrl);
        
        string botId = GetUser(Globals.BotUsername).Result?.Id ?? throw new("Failed to fetch bot user id.");
        Globals.BotId = botId;
    }

    private RestRequest CreateTwitchRequest(string endpoint, Method method = Method.Get, TokenResponse? tokenResponse = null)
    {
        if (string.IsNullOrEmpty(Globals.AccessToken)) throw new("No access token provided.");

        RestRequest request = new(endpoint, method);
        request.AddHeader("Authorization", $"Bearer {tokenResponse?.AccessToken ?? Globals.AccessToken}");
        request.AddHeader("Client-Id", Globals.ClientId);
        return request;
    }

    private async Task<T> ExecuteTwitchRequest<T>(RestRequest request) where T : class
    {
        RestResponse response = await _client.ExecuteAsync(request);
        
        if (response is { IsSuccessful: false, Content: not null })
        {
            TwitchErrorResponse? errorResponse = response.Content.FromJson<TwitchErrorResponse>();
            throw new(errorResponse?.Message ?? "Failed to execute Twitch API request.");
        }

        T? result = response.Content?.FromJson<T>();
        if (result == null) throw new("Failed to parse API response.");
        
        return result;
    }

    public async Task<UserInfo?> GetUser(string? userId = null)
    {
        RestRequest request = CreateTwitchRequest("users");
        
        if (!string.IsNullOrEmpty(userId))
        {
            request.AddQueryParameter(userId.All(char.IsDigit) ? "id" : "login", userId);
        }

        UserInfoResponse response = await ExecuteTwitchRequest<UserInfoResponse>(request);
        return response.Data.FirstOrDefault();
    }

    public async Task<List<UserInfo>?> GetUsers(string[] userIds)
    {
        if (!ValidateUserIds(userIds)) throw new("Invalid user ids provided.");

        RestRequest request = CreateTwitchRequest("users");
        userIds.ToList().ForEach(id => request.AddQueryParameter("user_id", id));

        UserInfoResponse response = await ExecuteTwitchRequest<UserInfoResponse>(request);
        return response.Data;
    }

    public async Task<GetUserChatColorResponse?> BotGetUserChatColors(string[] userIds)
    {
        if (!ValidateUserIds(userIds)) throw new("Invalid user ids provided.");

        RestRequest request = CreateTwitchRequest("chat/color");
        userIds.ToList().ForEach(id => request.AddQueryParameter("user_id", id));

        return await ExecuteTwitchRequest<GetUserChatColorResponse>(request);
    }

    public async Task Shoutout(string chatMessageChannel, string chatMessageUserName)
    {
        if (string.IsNullOrEmpty(chatMessageChannel) || string.IsNullOrEmpty(chatMessageUserName))
            throw new("Channel and user must be provided.");

        UserInfo user = await GetUser(chatMessageUserName) ?? throw new($"User {chatMessageUserName} not found.");

        RestRequest request = CreateTwitchRequest("chat/shoutouts", Method.Post);
        request.AddParameter("from_broadcaster_id", chatMessageChannel);
        request.AddParameter("to_broadcaster_id", user.Id);
        request.AddParameter("moderator_id", Globals.BotId);

        await ExecuteTwitchRequest<object>(request);
    }

    public async Task<TwitchUser> FetchUser(TokenResponse? tokenResponse = null, string? countryCode = null, string? id = null, bool? enabled = false)
    {
        UserInfo userInfo = await GetUser(id) ?? throw new("Failed to fetch user information.");
        TwitchUser user = await CreateTwitchUser(userInfo, tokenResponse, countryCode, enabled ?? false);
        await UpsertUserData(user);
        return user;
    }

    public async Task FetchModeration(string userInfoId, TokenResponse tokenResponse)
    {
        RestRequest request = CreateTwitchRequest("moderation/channels", Method.Get, tokenResponse);
        request.AddParameter("user_id", userInfoId);

        ChannelResponse moderators = await ExecuteTwitchRequest<ChannelResponse>(request);
        await UpsertModerators(userInfoId, moderators);
    }

    private bool ValidateUserIds(string[] userIds) =>
        userIds.Length is > 0 and <= 100 && userIds.All(id => !string.IsNullOrEmpty(id));

    private async Task<TwitchUser> CreateTwitchUser(UserInfo userInfo, TokenResponse? tokenResponse, string? countryCode, bool enabled = false)
    {
        List<string>? zoneIds = TzdbDateTimeZoneSource.Default.ZoneLocations?
            .Where(x => x.CountryCode == countryCode)
            .Select(x => x.ZoneId)
            .ToList();

        GetUserChatColorResponse? colors = await BotGetUserChatColors([userInfo.Id]);
        string color = colors?.Data.First().Color ?? "#9146FF";

        return new()
        {
            Id = userInfo.Id,
            Username = userInfo.Login,
            DisplayName = userInfo.DisplayName,
            Description = userInfo.Description,
            ProfileImageUrl = userInfo.ProfileImageUrl,
            OfflineImageUrl = userInfo.OfflineImageUrl,
            BroadcasterType = userInfo.BroadcasterType,
            Timezone = userInfo.Id == Globals.BotId ? "UTC" : zoneIds?.FirstOrDefault(),
            AccessToken = tokenResponse?.AccessToken,
            RefreshToken = tokenResponse?.RefreshToken,
            TokenExpiry = tokenResponse?.ExpiresIn != null ? DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn) : null,
            Color = color,
            Channel = new()
            {
                Id = userInfo.Id,
                Name = userInfo.Login,
                Enabled = enabled
            }
        };
    }

    private async Task UpsertUserData(TwitchUser user)
    {
        await DbContext.TwitchUsers.Upsert(user)
            .On(u => u.Id)
            .WhenMatched((_, newUser) => new()
            {
                Username = newUser.Username,
                DisplayName = newUser.DisplayName,
                ProfileImageUrl = newUser.ProfileImageUrl,
                OfflineImageUrl = newUser.OfflineImageUrl,
                Color = newUser.Color,
                BroadcasterType = newUser.BroadcasterType,
            })
            .RunAsync();

        await DbContext.Channels.Upsert(user.Channel)
            .On(c => c.Id)
            .WhenMatched((_, newChannel) => new()
            {
                Name = newChannel.Name,
                Enabled = newChannel.Enabled,
            })
            .RunAsync();
    }

    private async Task UpsertModerators(string channelId, ChannelResponse moderators)
    {
        foreach (ChannelData channelData in moderators.Data)
        {
            TwitchUser moderatorInfo = await FetchUser(id: channelData.Id);
            ChannelModerators channelModerators = new()
            {
                ChannelId = channelId,
                UserId = moderatorInfo.Id,
            };

            await DbContext.ChannelModerators.Upsert(channelModerators)
                .On(m => new { m.ChannelId, m.UserId })
                .WhenMatched((_, newModerator) => new()
                {
                    ChannelId = newModerator.ChannelId,
                    UserId = newModerator.UserId
                })
                .RunAsync();
        }
    }
}