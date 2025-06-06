// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable MemberCanBeMadeStatic.Global

using System.Collections.Specialized;
using System.Globalization;
using System.Web;
using Microsoft.AspNetCore.Http;
using RestSharp;
using TwitchShoutout.Database;
using TwitchShoutout.Database.Models;
using TwitchShoutout.Server.Config;
using TwitchShoutout.Server.Dtos;
using TwitchShoutout.Server.Events;
using TwitchShoutout.Server.Helpers;

namespace TwitchShoutout.Server.Services;

public class TwitchAuthService
{
    public static event EventHandler<TokenRefreshEventArgs>? TokenRefreshed;
    private readonly BotDbContext _dbContext;
    private readonly ILogger<TwitchAuthService> _logger;
    private readonly RestClient _authClient;

    public TwitchAuthService(BotDbContext dbContext, ILogger<TwitchAuthService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
        _authClient = new(Globals.TwitchAuthUrl);
    }

    private RestRequest CreateAuthRequest(string endpoint, Method method = Method.Post)
    {
        RestRequest request = new(endpoint, method);
        request.AddParameter("client_id", Globals.ClientId);
        request.AddParameter("client_secret", Globals.ClientSecret);
        return request;
    }

    private async Task<T> ExecuteRequest<T>(RestRequest request, RestClient client) where T : class
    {
        RestResponse response = await client.ExecuteAsync(request);
        
        if (!response.IsSuccessful || response.Content is null)
            throw new(response.Content ?? $"Failed to execute request to {request.Resource}");

        T? result = response.Content.FromJson<T>();
        if (result == null) throw new("Invalid response from Twitch.");

        return result;
    }

    public async Task<TwitchAuthResponse> Callback(string code)
    {
        RestRequest request = CreateAuthRequest("token")
            .AddParameter("code", code)
            .AddParameter("scope", string.Join(' ', Globals.Scopes.Keys))
            .AddParameter("grant_type", "authorization_code")
            .AddParameter("redirect_uri", Globals.RedirectUri);

        return await ExecuteRequest<TwitchAuthResponse>(request, _authClient);
    }

    public async Task<ValidatedTwitchAuthResponse> ValidateToken(string accessToken)
    {
        RestRequest request = new RestRequest("validate")
            .AddHeader("Authorization", $"Bearer {accessToken}");

        return await ExecuteRequest<ValidatedTwitchAuthResponse>(request, _authClient);
    }

    public Task<ValidatedTwitchAuthResponse> ValidateToken(HttpRequest request)
    {
        string authHeader = request.Headers["Authorization"].First() ?? throw new("Missing Authorization header");
        string accessToken = authHeader["Bearer ".Length..];
        return ValidateToken(accessToken);
    }

    public async Task<TwitchAuthResponse> RefreshToken(string refreshToken)
    {
        RestRequest request = CreateAuthRequest("token")
            .AddParameter("refresh_token", refreshToken)
            .AddParameter("grant_type", "refresh_token");

        return await ExecuteRequest<TwitchAuthResponse>(request, _authClient);
    }

    public async Task RevokeToken(string accessToken)
    {
        RestRequest request = CreateAuthRequest("revoke")
            .AddParameter("token", accessToken)
            .AddParameter("token_type_hint", "access_token");

        await _authClient.ExecuteAsync(request);
    }

    public string GetRedirectUrl()
    {
        NameValueCollection query = HttpUtility.ParseQueryString(string.Empty);
        query.Add("response_type", "code");
        query.Add("client_id", Globals.ClientId);
        query.Add("redirect_uri", Globals.RedirectUri);
        query.Add("scope", string.Join(' ', Globals.Scopes.Keys));

        return new UriBuilder(Globals.TwitchAuthUrl + "/authorize")
        {
            Query = query.ToString(),
            Scheme = Uri.UriSchemeHttps,
        }.ToString();
    }

    public async Task<DeviceCodeResponse> Authorize()
    {
        RestRequest request = CreateAuthRequest("device")
            .AddParameter("scopes", string.Join(' ', Globals.Scopes.Keys));

        return await ExecuteRequest<DeviceCodeResponse>(request, _authClient);
    }

    public async Task<TwitchAuthResponse> PollForToken(string deviceCode)
    {
        RestRequest request = CreateAuthRequest("token")
            .AddParameter("grant_type", "urn:ietf:params:oauth:grant-type:device_code")
            .AddParameter("device_code", deviceCode)
            .AddParameter("scopes", string.Join(' ', Globals.Scopes.Keys));

        return await ExecuteRequest<TwitchAuthResponse>(request, _authClient);
    }

    public async Task<TwitchAuthResponse?> RefreshAccessTokenAsync(string? refreshToken = null)
    {
        _logger.LogInformation("Refreshing Twitch access token...");
        using HttpClient client = new();

        HttpRequestMessage request = new(HttpMethod.Post, "https://id.twitch.tv/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "client_id", Globals.ClientId },
                { "client_secret", Globals.ClientSecret },
                { "grant_type", "refresh_token" },
                { "refresh_token", refreshToken ?? Globals.RefreshToken }
            })
        };

        HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        string jsonResponse = await response.Content.ReadAsStringAsync();
        return jsonResponse.FromJson<TwitchAuthResponse>();
    }

    public static bool IsTokenExpired() =>
        DateTime.UtcNow >= DateTime.Parse(Globals.ExpiresAt, CultureInfo.InvariantCulture);

    public async Task StartTokenRefreshForChannel(TwitchUser user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(user.RefreshToken)) return;
        _logger.LogInformation($"Starting automatic token refresh for {user.Username}...");
    
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (user.TokenExpiry <= DateTime.UtcNow.AddMinutes(5))
                {
                    TwitchAuthResponse response = await RefreshToken(user.RefreshToken);
                    user.AccessToken = response.AccessToken;
                    user.RefreshToken = response.RefreshToken;
                    user.TokenExpiry = DateTime.UtcNow.AddSeconds(response.ExpiresIn);
                    
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    TokenRefreshed?.Invoke(this, new(user.Id, response.AccessToken));
                    
                    // Check if the user is the bot and update the JSON file
                    if (user.Username.Equals(Globals.BotUsername, StringComparison.OrdinalIgnoreCase))
                    {
                        Globals.AccessToken = response.AccessToken;
                        Globals.RefreshToken = response.RefreshToken;
                        Globals.ExpiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn).ToString("o", CultureInfo.InvariantCulture);
                        Globals.ExpiresIn = response.ExpiresIn.ToString();
                        
                        await File.WriteAllTextAsync(Globals.TokenFilePath, response.ToJson(), cancellationToken);

                        _logger.LogInformation("Bot token updated in .env file.");
                    }
                }
    
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogInformation($"Error refreshing token for {user.Username}: {ex.Message}");
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            }
        }
    }
}