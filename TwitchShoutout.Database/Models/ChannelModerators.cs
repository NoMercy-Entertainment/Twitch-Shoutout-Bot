using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace TwitchShoutout.Database.Models;

[PrimaryKey(nameof(ChannelId), nameof(UserId))]
[Index(nameof(UserId))]
[Index(nameof(ChannelId))]
public class ChannelModerators
{
    [JsonProperty("channel_id")] public string ChannelId { get; set; }
    public Channel Channel { get; set; } = null!;

    [JsonProperty("user_id")] public string UserId { get; set; }
    public TwitchUser User { get; set; } = null!;

    public ChannelModerators()
    {
        //
    }
    
    public ChannelModerators(string channelId, string userId)
    {
        ChannelId = channelId;
        UserId = userId;
    }
}

public class SimpleChannelModerator
{
    [JsonProperty("channel_id")] public string ChannelId { get; set; }
    [JsonProperty("user_id")] public string UserId { get; set; }
    [JsonProperty("user_name")] public string Username { get; set; }
    [JsonProperty("display_name")] public string DisplayName { get; set; }

    public SimpleChannelModerator(TwitchUser user, Channel channel)
    {
        ChannelId = channel.Id;
        UserId = user.Id;
        Username = user.Username;
        DisplayName = user.DisplayName;
    }
}