// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable MemberCanBeMadeStatic.Global

using System.Collections.Specialized;
using System.Globalization;
using System.Web;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using RestSharp;
using TwitchShoutout.Database;
using TwitchShoutout.Database.Models;
using TwitchShoutout.Server.Config;
using TwitchShoutout.Server.Dtos;
using TwitchShoutout.Server.Helpers;
using File = System.IO.File;

namespace TwitchShoutout.Server.Services;

public class TwitchAuthService
{
    private static PeriodicTimer? _refreshTimer;
    public static event EventHandler<string>? TokenRefreshed;
    private static BotDbContext _db;

    private static bool IsAutoRefreshEnabled { get; set; }
    
    public TwitchAuthService(BotDbContext dbContext)
    {
        _db = dbContext;
    }

    public static async Task<string> RefreshAccessTokenAsync()
    {
        Console.WriteLine("Refreshing Twitch access token...");
        using HttpClient client = new();
        
        HttpRequestMessage request = new(HttpMethod.Post, "https://id.twitch.tv/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "client_id", Globals.ClientId },
                { "client_secret", Globals.ClientSecret },
                { "grant_type", "refresh_token" },
                { "refresh_token", Globals.RefreshToken }
            })
        };
        
        HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        string jsonResponse = await response.Content.ReadAsStringAsync();
        TwitchAuthResponse authResponse = JsonConvert.DeserializeObject<TwitchAuthResponse>(jsonResponse);
        
        await File.WriteAllTextAsync(Globals.TokenFilePath, jsonResponse);

        Globals.AccessToken = authResponse.AccessToken ?? throw new InvalidOperationException("Failed to deserialize the response from Twitch.");
        Globals.RefreshToken = authResponse.RefreshToken ?? throw new InvalidOperationException("Refresh token is missing in the response.");
        Globals.ExpiresIn = authResponse.ExpiresIn.ToString();
        Globals.ExpiresAt = DateTime.UtcNow.AddSeconds(authResponse.ExpiresIn).ToString("o", CultureInfo.InvariantCulture);
        
        return Globals.AccessToken;
    }
    
    public static bool IsTokenExpired()
    {
        return DateTime.UtcNow >= DateTime.Parse(Globals.ExpiresAt, CultureInfo.InvariantCulture);
    }
    
    public static void StartAutoRefresh(CancellationToken cancellationToken)
    {
        if (IsAutoRefreshEnabled) return;

        IsAutoRefreshEnabled = true;
        _ = Task.Run(async () =>
        {
            try
            {
                while (IsAutoRefreshEnabled && !cancellationToken.IsCancellationRequested)
                {
                    DateTime expiresAt = DateTime.Parse(Globals.ExpiresAt, CultureInfo.InvariantCulture);
                    TimeSpan timeUntilExpiry = expiresAt - DateTime.UtcNow;
                
                    TimeSpan waitTime = timeUntilExpiry.Subtract(TimeSpan.FromMinutes(5));
                
                    if (waitTime > TimeSpan.Zero)
                    {
                        await Task.Delay(waitTime, cancellationToken);
                    }
                
                    string newToken = await RefreshAccessTokenAsync();
                    TokenRefreshed?.Invoke(null, newToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown, ignore
            }
            finally
            {
                IsAutoRefreshEnabled = false;
            }
        }, cancellationToken);
    }

    public static void StopAutoRefresh()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        IsAutoRefreshEnabled = false;
    }
    
    
    public async Task<TokenResponse> Callback(string code)
    {
        RestClient restClient = new(Globals.TwitchAuthUrl);
        
        RestRequest request = new("token", Method.Post);
                    request.AddParameter("client_id", Globals.ClientId);
                    request.AddParameter("client_secret", Globals.ClientSecret);
                    request.AddParameter("code", code);
                    request.AddParameter("scope", string.Join(' ', Globals.Scopes.Keys));
                    request.AddParameter("grant_type", "authorization_code");
                    request.AddParameter("redirect_uri", Globals.RedirectUri);

        RestResponse response = await restClient.ExecuteAsync(request);

        if (!response.IsSuccessful || response.Content is null) 
            throw new(response.Content ?? "Failed to fetch token from Twitch.");
        
        TokenResponse? tokenResponse = response.Content.FromJson<TokenResponse>();
        if (tokenResponse == null) throw new("Invalid response from Twitch.");

        return tokenResponse;
    }
    
    public async Task<ValidatedTokenResponse> ValidateToken(string accessToken)
    {
        RestClient client = new(Globals.TwitchAuthUrl);
        RestRequest request = new("validate");
                    request.AddHeader("Authorization", $"Bearer {accessToken}");

        RestResponse response = await client.ExecuteAsync(request);
        if (!response.IsSuccessful || response.Content is null)
            throw new(response.Content ?? "Failed to fetch token from Twitch.");
            
        ValidatedTokenResponse? tokenResponse = response.Content.FromJson<ValidatedTokenResponse>();
        if (tokenResponse == null) throw new("Invalid response from Twitch.");

        return tokenResponse;
    }

    public Task<ValidatedTokenResponse> ValidateToken(HttpRequest request)
    {
        string authorizationHeader = request.Headers["Authorization"].First() ?? throw new InvalidOperationException();
        string accessToken = authorizationHeader["Bearer ".Length..];
        
        return ValidateToken(accessToken);
    }
    
    public async Task<TokenResponse> RefreshToken(string refreshToken)
    {
        RestClient client = new(Globals.TwitchAuthUrl);
        
        RestRequest request = new("token", Method.Post);
                    request.AddParameter("client_id", Globals.ClientId);
                    request.AddParameter("client_secret", Globals.ClientSecret);
                    request.AddParameter("refresh_token", refreshToken);
                    request.AddParameter("grant_type", "refresh_token");

        RestResponse response = await client.ExecuteAsync(request);
        if (!response.IsSuccessful || response.Content is null)
            throw new(response.Content ?? "Failed to fetch token from Twitch.");

        TokenResponse? tokenResponse = response.Content?.FromJson<TokenResponse>();
        if (tokenResponse == null) throw new("Invalid response from Twitch.");

        return tokenResponse;
    }
    
    public async Task RevokeToken(string accessToken)
    {
        RestClient client = new(Globals.TwitchAuthUrl);
        
        RestRequest request = new("revoke", Method.Post);
                    request.AddParameter("client_id", Globals.ClientId);
                    request.AddParameter("token", accessToken);
                    request.AddParameter("token_type_hint", "access_token");

        RestResponse response = await client.ExecuteAsync(request);
        if (!response.IsSuccessful || response.Content is null)
            throw new(response.Content ?? "Failed to fetch token from Twitch.");
    }
    
    public string GetRedirectUrl()
    {
        NameValueCollection query = HttpUtility.ParseQueryString(string.Empty);
                            query.Add("response_type", "code");
                            query.Add("client_id", Globals.ClientId);
                            query.Add("redirect_uri", Globals.RedirectUri);
                            query.Add("scope", string.Join(' ', Globals.Scopes.Keys));
        
        UriBuilder uriBuilder = new(Globals.TwitchAuthUrl + "/authorize")
        {
            Query = query.ToString(),
            Scheme = Uri.UriSchemeHttps,
        };
        
        return uriBuilder.ToString();
    }

    public async Task<DeviceCodeResponse> Authorize()
    {
        RestClient client = new(Globals.TwitchAuthUrl);
        
        RestRequest request = new("device", Method.Post);
                    request.AddParameter("client_id", Globals.ClientId);
                    request.AddParameter("scopes", string.Join(' ', Globals.Scopes.Keys));

        RestResponse response = await client.ExecuteAsync(request);
        
        if (!response.IsSuccessful || response.Content is null) 
            throw new(response.Content ?? "Failed to fetch device code from Twitch.");

        DeviceCodeResponse? deviceCodeResponse = response.Content.FromJson<DeviceCodeResponse>();
        if (deviceCodeResponse == null) throw new("Invalid response from Twitch.");

        return deviceCodeResponse;
    }
    
    public async Task<TokenResponse> PollForToken(string deviceCode)
    {
        RestClient restClient = new(Globals.TwitchAuthUrl);
        
        RestRequest request = new("token", Method.Post);
                    request.AddParameter("client_id", Globals.ClientId);
                    request.AddParameter("client_secret", Globals.ClientSecret);
                    request.AddParameter("grant_type", "urn:ietf:params:oauth:grant-type:device_code");
                    request.AddParameter("device_code", deviceCode);
                    request.AddParameter("scopes", string.Join(' ', Globals.Scopes.Keys));

        RestResponse response = await restClient.ExecuteAsync(request);
        if (!response.IsSuccessful || response.Content is null) 
            throw new(response.Content ?? "Failed to fetch token from Twitch.");

        TokenResponse? tokenResponse = response.Content.FromJson<TokenResponse>();
        if (tokenResponse == null) throw new("Invalid response from Twitch.");

        return tokenResponse;
    }

    public TwitchUser? GetRestoredById(string authModelId)
    {
        lock (_db)
        {
            return _db.TwitchUsers.FirstOrDefault(u => u.Id == authModelId);
        }
    }
}

public struct TwitchAuthResponse
{
    [JsonProperty("access_token")] public string AccessToken { get; set; }
    [JsonProperty("expires_in")] public long ExpiresIn { get; set; }
    [JsonProperty("refresh_token")] public string RefreshToken { get; set; }
    [JsonProperty("scope")] public string[] Scope { get; set; }
    [JsonProperty("token_type")] public string TokenType { get; set; }
}