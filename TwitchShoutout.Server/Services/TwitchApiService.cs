// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable MemberCanBeMadeStatic.Global

using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
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
    private readonly BotDbContext _dbContext;
    private readonly RestClient _client;
    private readonly ILogger<TwitchApiService> _logger;
    private readonly PronounService _pronounService;

    public TwitchApiService(BotDbContext dbContext, ILogger<TwitchApiService> logger, PronounService pronounService)
    {
        _dbContext = dbContext;
        _logger = logger;
        _client = new(Globals.TwitchApiUrl);
        
        _pronounService = pronounService;
        _pronounService.LoadPronouns().Wait();
        
        string botId = GetUser(Globals.BotUsername).Result?.Id ?? throw new("Failed to fetch bot user id.");
        Globals.BotId = botId;
    }

    private RestRequest CreateTwitchRequest(string endpoint, Method method = Method.Get, string? accessToken = null)
    {
        if (string.IsNullOrEmpty(Globals.AccessToken)) throw new("No access token provided.");

        RestRequest request = new(endpoint, method);
        request.AddHeader("Authorization", $"Bearer {accessToken ?? Globals.AccessToken}");
        request.AddHeader("Client-Id", Globals.ClientId);
        return request;
    }

    private async Task<T?> ExecuteTwitchRequest<T>(RestRequest request) where T : class
    {
        RestResponse response = await _client.ExecuteAsync(request);

        if (response.IsSuccessful)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                // Handle 204 No Content: return null, indicating success without content
                return null;
            }

            // Attempt to deserialize content for other successful status codes
            T? result = response.Content?.FromJson<T>();
            if (result == null && response.Content != null)
            {
                throw new("Failed to parse API response.");
            }
            return result;
        }
        
        TwitchErrorResponse? errorResponse = response.Content?.FromJson<TwitchErrorResponse>();
        throw new(errorResponse?.Message ?? "Failed to execute Twitch API request.");
    }

    public async Task<UserInfo?> GetUser(string? userId = null)
    {
        RestRequest request = CreateTwitchRequest("users");
        
        if (!string.IsNullOrEmpty(userId))
        {
            request.AddQueryParameter(userId.All(char.IsDigit) ? "id" : "login", userId);
        }

        UserInfoResponse? response = await ExecuteTwitchRequest<UserInfoResponse>(request);
        return response?.Data.FirstOrDefault();
    }

    public async Task<List<UserInfo>?> GetUsers(string[] userIds)
    {
        if (!ValidateUserIds(userIds)) throw new("Invalid user ids provided.");

        RestRequest request = CreateTwitchRequest("users");
        userIds.ToList().ForEach(id => request.AddQueryParameter("user_id", id));

        UserInfoResponse? response = await ExecuteTwitchRequest<UserInfoResponse>(request);
        return response?.Data;
    }

    public async Task<GetUserChatColorResponse?> BotGetUserChatColors(string[] userIds)
    {
        if (!ValidateUserIds(userIds)) throw new("Invalid user ids provided.");

        RestRequest request = CreateTwitchRequest("chat/color");
        userIds.ToList().ForEach(id => request.AddQueryParameter("user_id", id));

        return await ExecuteTwitchRequest<GetUserChatColorResponse>(request);
    }

    public async Task Shoutout(string chatMessageChannelId, string chatMessageUserName)
    {
        if (string.IsNullOrEmpty(chatMessageChannelId) || string.IsNullOrEmpty(chatMessageUserName))
            throw new("Channel and user must be provided.");

        UserInfo user = await GetUser(chatMessageUserName) ?? throw new($"User {chatMessageUserName} not found.");

        RestRequest request = CreateTwitchRequest("chat/shoutouts", Method.Post);
        request.AddParameter("from_broadcaster_id", chatMessageChannelId);
        request.AddParameter("to_broadcaster_id", user.Id);
        request.AddParameter("moderator_id", Globals.BotId);

        await ExecuteTwitchRequest<object>(request);
    }
    
    public async Task SendAnnouncement(string broadcasterId, string message, string? color = null)
    {
        RestRequest request = CreateTwitchRequest("chat/announcements", Method.Post);
        request.AddQueryParameter("broadcaster_id", broadcasterId);
        request.AddQueryParameter("moderator_id", Globals.BotId);

        AnnouncementRequest announcement = new()
        {
            Message = message,
            Color = color
        };

        request.AddJsonBody(announcement);

        await ExecuteTwitchRequest<object>(request);
    }
    
    private async Task<ChannelInfo?> FetchChannelInfo(string broadcasterId, TwitchAuthResponse? tokenResponse)
    {
        RestRequest request = CreateTwitchRequest($"channels?broadcaster_id={broadcasterId}", Method.Get, tokenResponse?.AccessToken);
        ChannelInfoResponse? response = await ExecuteTwitchRequest<ChannelInfoResponse>(request);
    
        ChannelInfoDto? dto = response?.Data.FirstOrDefault();
        if (dto == null) return null;

        return new()
        {
            Id = dto.BroadcasterId,
            Language = dto.Language,
            GameId = dto.GameId,
            GameName = dto.GameName,
            Title = dto.Title,
            Delay = dto.Delay,
            Tags = dto.Tags,
            ContentLabels = dto.ContentLabels,
            IsBrandedContent = dto.IsBrandedContent
        };
    }

    public async Task<TwitchUser> FetchUser(TwitchAuthResponse? tokenResponse = null, string? countryCode = null, string? id = null, bool? enabled = false)
    {
        UserInfo userInfo = await GetUser(id) ?? throw new("Failed to fetch user information.");
        TwitchUser user = await CreateTwitchUser(userInfo, tokenResponse, countryCode, enabled ?? false);
        
        ChannelInfo? channelInfo = await FetchChannelInfo(userInfo.Id, tokenResponse);
        if (channelInfo != null)
        {
            user.Channel.Info = channelInfo;
        }

        await UpsertUserData(user, enabled ?? false);
        
        return user;
    }

    public async Task FetchModeration(string userInfoId, TwitchAuthResponse twitchAuthResponse)
    {
        RestRequest request = CreateTwitchRequest("moderation/channels");
        request.AddParameter("user_id", userInfoId);

        ChannelResponse? moderators = await ExecuteTwitchRequest<ChannelResponse>(request);
        if (moderators == null || moderators.Data.Count == 0)
        {
            _logger.LogWarning($"No moderators found for user {userInfoId}.");
            return;
        }
        await UpsertModerators(userInfoId, moderators);
    }

    private static bool ValidateUserIds(string[] userIds) =>
        userIds.Length is > 0 and <= 100 && userIds.All(id => !string.IsNullOrEmpty(id));

    private async Task<TwitchUser> CreateTwitchUser(UserInfo userInfo, TwitchAuthResponse? tokenResponse, string? countryCode, bool enabled = false)
    {
        List<string>? zoneIds = TzdbDateTimeZoneSource.Default.ZoneLocations?
            .Where(x => x.CountryCode == countryCode)
            .Select(x => x.ZoneId)
            .ToList();

        GetUserChatColorResponse? colors = await BotGetUserChatColors([userInfo.Id]);
        string color = colors?.Data.First().Color ?? "#9146FF";
        
        Pronoun? pronoun = await _pronounService.GetUserPronoun(userInfo.Login);

        TwitchUser user = new()
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
            Pronoun = pronoun,
            Channel = new()
            {
                Id = userInfo.Id,
                Name = userInfo.Login,
            }
        };

        if (enabled)
        {
            user.Channel.Enabled = true;
        }

        return user;
    }

    private async Task UpsertUserData(TwitchUser user, bool enabled = false)
    {
        await _dbContext.TwitchUsers.Upsert(user)
            .On(u => u.Id)
            .WhenMatched((_, newUser) => new()
            {
                Username = newUser.Username,
                DisplayName = newUser.DisplayName,
                ProfileImageUrl = newUser.ProfileImageUrl,
                OfflineImageUrl = newUser.OfflineImageUrl,
                Color = newUser.Color,
                BroadcasterType = newUser.BroadcasterType,
                PronounData = newUser.PronounData,
            })
            .RunAsync();

        await _dbContext.Channels.Upsert(user.Channel)
            .On(c => c.Id)
            .WhenMatched((oldChannel, newChannel) => new()
            {
                Name = newChannel.Name,
                Enabled = enabled 
                    ? newChannel.Enabled 
                    : oldChannel.Enabled,
            })
            .RunAsync();
        
        await _dbContext.ChannelInfos.Upsert(user.Channel.Info)
            .On(c => c.Id)
            .WhenMatched((_, newInfo) => new()
            {
                Language = newInfo.Language,
                GameId = newInfo.GameId,
                GameName = newInfo.GameName,
                Title = newInfo.Title,
                Delay = newInfo.Delay,
                TagsJson = newInfo.TagsJson,
                LabelsJson = newInfo.LabelsJson,
                IsBrandedContent = newInfo.IsBrandedContent
            })
            .RunAsync();
    }

    private async Task UpsertModerators(string channelId, ChannelResponse moderators)
    {
        foreach (ChannelData channelData in moderators.Data)
        {
            TwitchUser moderatorInfo = await FetchUser(id: channelData.Id);
            ChannelModerator channelModerator = new()
            {
                ChannelId = channelId,
                UserId = moderatorInfo.Id,
            };

            await _dbContext.ChannelModerators.Upsert(channelModerator)
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

public class AnnouncementRequest
{
    [JsonProperty("message")] public string Message { get; set; } = string.Empty;
    [JsonProperty("color")] public string? Color { get; set; }
}