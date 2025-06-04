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
    
    public TwitchApiService(BotDbContext dbContext)
    {
        DbContext = dbContext;
        
        string botId = GetUser(Globals.BotUsername).Result?.Id ?? throw new("Failed to fetch bot user id.");
        Globals.BotId = botId;
    }
    
    public async Task<UserInfo?> GetUser(string? userId = null)
    {
        if (string.IsNullOrEmpty(Globals.AccessToken)) throw new("No access token provided.");
        
        RestClient client = new(Globals.TwitchApiUrl);
        RestRequest request = new("users");
        request.AddHeader("Authorization", $"Bearer {Globals.AccessToken}");
        request.AddHeader("Client-Id", Globals.ClientId);
        
        // if user is a number string, it is a user id
        if (!string.IsNullOrEmpty(userId) && userId.All(char.IsDigit))
        {
            request.AddQueryParameter("id", userId);
        }
        // if user is not a number string, it is a username
        else if (!string.IsNullOrEmpty(userId))
        {
            request.AddQueryParameter("login", userId);
        }

        RestResponse response = await client.ExecuteAsync(request);

        if (response is { IsSuccessful: false, Content: not null })
        {
            TwitchErrorResponse? errorResponse = response.Content.FromJson<TwitchErrorResponse>();
            throw new(errorResponse?.Message ?? "Failed to fetch user information.");
        }

        UserInfoResponse? userInfoResponse = response.Content?.FromJson<UserInfoResponse>();
        if (userInfoResponse is null) throw new("Failed to parse user information.");
        
        return userInfoResponse.Data.FirstOrDefault();
    }
    
    public async Task<List<UserInfo>?> GetUsers(string[] userIds)
    {        
        if (string.IsNullOrEmpty(Globals.AccessToken)) throw new("No access token provided.");
        if (userIds.Any(string.IsNullOrEmpty)) throw new("Invalid user id provided.");
        if (userIds.Length == 0) throw new("userIds must contain at least 1 userId");
        if (userIds.Length > 100) throw new("Too many user ids provided.");
        
        RestClient client = new(Globals.TwitchApiUrl);
        RestRequest request = new("users");
        request.AddHeader("Authorization", $"Bearer {Globals.AccessToken}");
        request.AddHeader("Client-Id", Globals.ClientId);
        
        foreach (string id in userIds)
        {
            request.AddQueryParameter("user_id", id);
        }
        
        RestResponse response = await client.ExecuteAsync(request);
        if (response is { IsSuccessful: false, Content: not null })
        {
            TwitchErrorResponse? errorResponse = response.Content.FromJson<TwitchErrorResponse>();
            throw new(errorResponse?.Message ?? "Failed to fetch user information.");
        }

        UserInfoResponse? userInfoResponse = response.Content?.FromJson<UserInfoResponse>();
        if (userInfoResponse is null) throw new("Failed to parse user information.");
        
        return userInfoResponse.Data;
    }
    
    public async Task<GetUserChatColorResponse?> BotGetUserChatColors(string[] userIds)
    {
        if (userIds.Any(string.IsNullOrEmpty)) throw new("Invalid user id provided.");
        if (userIds.Length == 0) throw new($"userIds must contain at least 1 userId");
        if (userIds.Length > 100) throw new("Too many user ids provided.");
        
        RestClient client = new(Globals.TwitchApiUrl);
        RestRequest request = new($"chat/color");
        request.AddHeader("Authorization", $"Bearer {Globals.AccessToken}");
        request.AddHeader("Client-Id", Globals.ClientId);

        foreach (string id in userIds)
        {
            request.AddQueryParameter("user_id", id);
        }

        RestResponse response = await client.ExecuteAsync(request);
        if (response is { IsSuccessful: false, Content: not null })
        {
            TwitchErrorResponse? errorResponse = response.Content.FromJson<TwitchErrorResponse>();
            throw new(errorResponse?.Message ?? "Failed to fetch user color.");
        }
        
        GetUserChatColorResponse? colors = response.Content?.FromJson<GetUserChatColorResponse>();
        if (colors is null) throw new("Failed to parse user chat color.");
        
        return colors;
    }
    
    public async Task<ChannelResponse> GetUserModerators(string userId, TokenResponse? tokenResponse = null)
    {
        if (string.IsNullOrEmpty(Globals.AccessToken)) throw new("No access token provided.");
        if (string.IsNullOrEmpty(userId)) throw new("No user id provided.");
        
        RestClient client = new(Globals.TwitchApiUrl);
        
        RestRequest request = new("moderation/channels");
                    request.AddHeader("Authorization", $"Bearer {tokenResponse?.AccessToken ?? Globals.AccessToken}");
                    request.AddHeader("client-id", Globals.ClientId);
                    request.AddParameter("user_id", userId);

        RestResponse response = await client.ExecuteAsync(request);
        if (response is { IsSuccessful: false, Content: not null })
        {
            TwitchErrorResponse? errorResponse = response.Content.FromJson<TwitchErrorResponse>();
            throw new(errorResponse?.Message ?? "Failed to fetch user information.");
        }
        
        ChannelResponse? channelResponse = response.Content?.FromJson<ChannelResponse>();
        if (channelResponse == null) throw new("Invalid response from Twitch.");
        
        return channelResponse;
    }

    public async Task Shoutout(string chatMessageChannel, string chatMessageUserName)
    {
        if (string.IsNullOrEmpty(chatMessageChannel)) throw new("No channel provided.");
        if (string.IsNullOrEmpty(chatMessageUserName)) throw new("No user provided.");
        
        RestClient client = new(Globals.TwitchApiUrl);

        UserInfo? user = await GetUser(chatMessageUserName);
        if (user is null) throw new($"User {chatMessageUserName} not found.");
        
        RestRequest request = new("chat/shoutouts", Method.Post);
        request.AddHeader("Authorization", $"Bearer {Globals.AccessToken}");
        request.AddHeader("client-id", Globals.ClientId);
        
        request.AddParameter("from_broadcaster_id", chatMessageChannel);
        request.AddParameter("to_broadcaster_id", user.Id);
        request.AddParameter("moderator_id", Globals.BotId);
        
        RestResponse response = await client.ExecuteAsync(request);
        if (response is { IsSuccessful: false, Content: not null })
        {
            TwitchErrorResponse? errorResponse = response.Content.FromJson<TwitchErrorResponse>();
            throw new(errorResponse?.Message ?? "Failed to shoutout user.");
        }
    }

    public async Task<TwitchUser> FetchUser(TokenResponse? tokenResponse = null, string? countryCode = null, string? id = null)
    {
        UserInfo? userInfo = await GetUser(id);
        if (userInfo is null) throw new("Failed to fetch user information.");
        
        IEnumerable<string>? zoneIds = TzdbDateTimeZoneSource.Default.ZoneLocations?
            .Where(x => x.CountryCode == countryCode)
            .Select(x => x.ZoneId)
            .ToList();
        
        TwitchUser user = new()
        {
            Id = userInfo.Id,
            Username = userInfo.Login,
            DisplayName = userInfo.DisplayName,
            Description = userInfo.Description,
            ProfileImageUrl = userInfo.ProfileImageUrl,
            OfflineImageUrl = userInfo.OfflineImageUrl,
            BroadcasterType = userInfo.BroadcasterType,
            Timezone = id == Globals.BotId ? "UTC" : zoneIds?.FirstOrDefault(),
            AccessToken = tokenResponse?.AccessToken,
            RefreshToken = tokenResponse?.RefreshToken,
            TokenExpiry = tokenResponse?.ExpiresIn is not null 
                ? DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn) 
                : null,
        };

        Channel channel = new()
        {
            Id = userInfo.Id,
            Name = userInfo.Login,
            Enabled = true,
            User = user
        };

        user.Channel = channel;
        
        GetUserChatColorResponse? colors = await BotGetUserChatColors([userInfo.Id]);
        
        string? color = colors?.Data.First().Color;
        
        user.Color = string.IsNullOrEmpty(color)
            ? "#9146FF"
            : color;
        
        await DbContext.TwitchUsers.Upsert(user)
            .On(u => u.Id)
            .WhenMatched((oldUser, newUser) => new()
            {
                Username = newUser.Username,
                DisplayName = newUser.DisplayName,
                ProfileImageUrl = newUser.ProfileImageUrl,
                OfflineImageUrl = newUser.OfflineImageUrl,
                Color = newUser.Color,
                BroadcasterType = newUser.BroadcasterType,
            })
            .RunAsync();

        await DbContext.Channels.Upsert(channel)
            .On(c => c.Id)
            .WhenMatched((oldChannel, newChannel) => new()
            {
                Name = newChannel.Name,
                Enabled = newChannel.Enabled,
            })
            .RunAsync();
        
        return user;
    }
    public async Task FetchModeration(string userInfoId, TokenResponse tokenResponse)
    {
        
        ChannelResponse moderators = await GetUserModerators(userInfoId, tokenResponse);
        foreach (ChannelData channelData in moderators.Data)
        {
            TwitchUser moderatorInfo = await FetchUser(id: channelData.Id);

            ChannelModerators channelModerators = new()
            {
                ChannelId = userInfoId,
                UserId = moderatorInfo.Id,
            };
                
            await DbContext.ChannelModerators.Upsert(channelModerators)
                .On(m => new { m.ChannelId, m.UserId })
                .WhenMatched((oldModerator, newModerator) => new()
                {
                    ChannelId = newModerator.ChannelId,
                    UserId = newModerator.UserId
                })
                .RunAsync();
        }
    }
}