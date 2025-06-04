using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace TwitchShoutout.Database.Models;

[PrimaryKey(nameof(Id))]
public class TwitchUser
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [MaxLength(50)]
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;

    [MaxLength(255)]
    [JsonProperty("user_name")] public string Username { get; set; } = string.Empty;

    [MaxLength(255)]
    [JsonProperty("display_name")] public string DisplayName { get; set; } = string.Empty;

    [MaxLength(50)]
    [JsonProperty("timezone")] public string? Timezone { get; set; }
    
    [NotMapped, JsonIgnore]
    public TimeZoneInfo? TimeZoneInfo => !string.IsNullOrEmpty(Timezone) 
        ? TimeZoneInfo.FindSystemTimeZoneById(Timezone) 
        : null;
    
    [JsonIgnore]
    [JsonProperty("access_token")] public string? AccessToken { get; set; }

    [JsonIgnore]
    [JsonProperty("refresh_token")] public string? RefreshToken { get; set; }
    
    [MaxLength(255)]
    [JsonProperty("description")] public string Description { get; set; } = string.Empty;
    
    [MaxLength(2048)]
    [JsonProperty("profile_image_url")] public string ProfileImageUrl { get; set; } = string.Empty;
    
    [MaxLength(2048)]
    [JsonProperty("offline_image_url")]  public string OfflineImageUrl { get; set; } = string.Empty;
    
    [MaxLength(7)]
    [JsonProperty("color")] public string? Color { get; set; }
    
    [MaxLength(50)]
    [JsonProperty("broadcaster_type")] public string BroadcasterType { get; set; } = string.Empty;
    
    [JsonProperty("enabled")] public bool Enabled { get; set; }

    [JsonProperty("is_live")] public bool IsLive { get; set; }

    [JsonIgnore]
    public DateTime? TokenExpiry { get; set; }

    [JsonProperty("channel")] public virtual Channel Channel { get; set; } = null!;
}

public class SimpleUser
{
    [JsonProperty("id")] public string Id { get; set; }
    [JsonProperty("user_name")] public string Username { get; set; }
    [JsonProperty("display_name")] public string DisplayName { get; set; }
    [JsonProperty("description")] public string Description { get; set; }
    [JsonProperty("profile_image_url")] public string ProfileImageUrl { get; set; }
    [JsonProperty("offline_image_url")]  public string OfflineImageUrl { get; set; }
    [JsonProperty("color")] public string? Color { get; set; }
    [JsonProperty("broadcaster_type")] public string BroadcasterType { get; set; }
    [JsonProperty("enabled")] public bool Enabled { get; set; }
    [JsonProperty("is_live")] public bool IsLive { get; set; }
    
    public SimpleUser(TwitchUser user)
    {
        Id = user.Id;
        Username = user.Username;
        DisplayName = user.DisplayName;
        Description = user.Description;
        ProfileImageUrl = user.ProfileImageUrl;
        OfflineImageUrl = user.OfflineImageUrl;
        Color = user.Color;
        BroadcasterType = user.BroadcasterType;
        Enabled = user.Enabled;
        IsLive = user.IsLive;
    }
}