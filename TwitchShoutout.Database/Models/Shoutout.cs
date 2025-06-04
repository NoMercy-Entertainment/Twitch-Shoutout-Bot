using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace TwitchShoutout.Database.Models;

[PrimaryKey(nameof(Id))]
[Index(nameof(ChannelId), nameof(ShoutedUserId), IsUnique = true)]
public class Shoutout
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [MaxLength(50)]
    [JsonProperty("id")] public string Id { get; set; } = null!;
    [JsonProperty("enabled")] public bool Enabled { get; set; }
    [JsonProperty("message")] public string MessageTemplate { get; set; } = string.Empty;
    
    [JsonProperty("channel_id")] public string ChannelId { get; set; } = null!;
    [JsonProperty("channel")]  public Channel Channel { get; set; } = null!;
    
    [JsonProperty("user_id")] public string ShoutedUserId { get; set; } = null!;
    [JsonProperty("user")] public TwitchUser ShoutedUser { get; set; } = null!;

    public Shoutout()
    {
        //
    }
    public Shoutout(string id, string channelId)
    {
        Id = id;
        ChannelId = channelId;
        Enabled = true;
    }
    
}

public class SimpleShoutout
{
    [JsonProperty("channel_id")] public string ChannelId { get; set; }
    [JsonProperty("user_id")] public string ShoutedUserId { get; set; }
    [JsonProperty("enabled")] public bool Enabled { get; set; }
    [JsonProperty("message")] public string Message { get; set; }
    [JsonProperty("username")] public string Username { get; set; }
    [JsonProperty("display_name")] public string DisplayName { get; set; }
    
    public SimpleShoutout(Shoutout shoutout)
    {
        ChannelId = shoutout.ChannelId;
        ShoutedUserId = shoutout.ShoutedUserId;
        Enabled = shoutout.Enabled;
        Message = shoutout.MessageTemplate;
        
        Username = shoutout.ShoutedUser.Username;
        DisplayName = shoutout.ShoutedUser.DisplayName;
    }
}