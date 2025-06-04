using Newtonsoft.Json;

namespace TwitchShoutout.Server.Dtos;

public class TwitchErrorResponse
{
    [JsonProperty("error")] public string? Error { get; set; }
    [JsonProperty("status")] public int? Status { get; set; }
    [JsonProperty("message")] public string? Message { get; set; }
}

public class DeviceCodeRequest
{
    [JsonProperty("device_code")] public string DeviceCode { get; set; } = string.Empty;
}

public class RefreshRequest
{
    [JsonProperty("refresh_token")] public string RefreshToken { get; set; } = null!;
}

public class DeviceCodeResponse
{
    [JsonProperty("device_code")] public string DeviceCode { get; set; } = string.Empty;
    [JsonProperty("user_code")] public string UserCode { get; set; } = string.Empty;
    [JsonProperty("verification_uri")] public string VerificationUri { get; set; } = string.Empty;
    [JsonProperty("expires_in")] public int ExpiresIn { get; set; }
    [JsonProperty("interval")] public int Interval { get; set; }
}

public class TokenResponse
{
    [JsonProperty("access_token")] public string AccessToken { get; set; } = string.Empty;
    [JsonProperty("refresh_token")] public string RefreshToken { get; set; } = string.Empty;
    [JsonProperty("expires_in")] public int ExpiresIn { get; set; }
}

public class ValidatedTokenResponse
{
    [JsonProperty("client_id")] public string ClientId { get; set; } = null!;
    [JsonProperty("login")] public string Login { get; set; } = null!;
    [JsonProperty("scopes")] public string[] Scopes { get; set; } = null!;
    [JsonProperty("user_id")] public string UserId { get; set; } = null!;
    [JsonProperty("expires_in")] public int ExpiresIn { get; set; }
}


public class UserInfoResponse
{
    [JsonProperty("data")] public List<UserInfo> Data { get; set; } = [];
}

public class UserInfo
{
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("login")] public string Login { get; set; } = string.Empty;
    [JsonProperty("display_name")] public string DisplayName { get; set; } = string.Empty;
    [JsonProperty("type")] public string Type { get; set; } = string.Empty;
    [JsonProperty("broadcaster_type")] public string BroadcasterType { get; set; } = string.Empty;
    [JsonProperty("description")] public string Description { get; set; } = string.Empty;
    [JsonProperty("profile_image_url")] public string ProfileImageUrl { get; set; } = string.Empty;
    [JsonProperty("offline_image_url")] public string OfflineImageUrl { get; set; } = string.Empty;
    [JsonProperty("view_count")] public int ViewCount { get; set; }
    [JsonProperty("created_at")] public DateTime CreatedAt { get; set; }
}