using Newtonsoft.Json;
using TwitchShoutout.Database.Models;

namespace TwitchShoutout.Server.Dtos;

public class UserDto
{
    [JsonProperty("id")] public string Id { get; set; }
    [JsonProperty("username")] public string Username { get; set; }
    [JsonProperty("display_name")] public string DisplayName { get; set; }
    [JsonProperty("timezone")] public string? Timezone { get; set; }
    [JsonProperty("profile_image_url")] public string? ProfileImageUrl { get; set; }
    [JsonProperty("offline_image_url")] public string? OfflineImageUrl { get; set; }
    [JsonProperty("color") ] public string? Color { get; set; }
    [JsonProperty("link")] public Uri Link { get; set; } = null!;
    [JsonProperty("enabled")] public bool Enabled { get; set; }
    [JsonProperty("is_live")] public bool IsLive { get; set; }
    [JsonProperty("channel")] public SimpleChannel SimpleChannel { get; set; }

    public UserDto()
    {
        //
    }
    
    public UserDto(TwitchUser user)
    {
        Id = user.Id;
        Username = user.Username;
        DisplayName = user.DisplayName;
        Timezone = user.Timezone;
        ProfileImageUrl = user.ProfileImageUrl;
        OfflineImageUrl = user.OfflineImageUrl;
        Color = user.Color;
        Link = new($"/profile/{Username}", UriKind.Relative);
        Enabled = user.Enabled;
        IsLive = user.IsLive;
        SimpleChannel = new(user.Channel);
    }
}

public class UserWithTokenDto: UserDto
{
    [JsonProperty("access_token")] public string AccessToken { get; set; }
    [JsonProperty("refresh_token")] public string RefreshToken { get; set; }
    [JsonProperty("token_expiry")] public DateTime? TokenExpiry { get; set; }
    
    public UserWithTokenDto(TwitchUser user, TokenResponse? tokenResponse = null) : base(user)
    {
        AccessToken = tokenResponse is not null && !string.IsNullOrEmpty(tokenResponse.AccessToken) 
            ? tokenResponse.AccessToken 
            : user.AccessToken ?? "";
        RefreshToken = tokenResponse is not null && !string.IsNullOrEmpty(tokenResponse.RefreshToken) 
            ? tokenResponse.RefreshToken  
            : user.RefreshToken ?? "";
        TokenExpiry = tokenResponse?.ExpiresIn is not null
            ? DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
            : user.TokenExpiry;
    }
}
