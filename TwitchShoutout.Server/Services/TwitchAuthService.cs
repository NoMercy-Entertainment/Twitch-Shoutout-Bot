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
    private static BotDbContext DbContext { get; set; } = null!;
    private static bool IsAutoRefreshEnabled { get; set; }
    private readonly RestClient _authClient;
    private readonly RestClient _apiClient;

    public TwitchAuthService(BotDbContext dbContext)
    {
        DbContext = dbContext;
        _authClient = new(Globals.TwitchAuthUrl);
        _apiClient = new(Globals.TwitchApiUrl);
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

    public async Task<TokenResponse> Callback(string code)
    {
        RestRequest request = CreateAuthRequest("token")
            .AddParameter("code", code)
            .AddParameter("scope", string.Join(' ', Globals.Scopes.Keys))
            .AddParameter("grant_type", "authorization_code")
            .AddParameter("redirect_uri", Globals.RedirectUri);

        return await ExecuteRequest<TokenResponse>(request, _authClient);
    }

    public async Task<ValidatedTokenResponse> ValidateToken(string accessToken)
    {
        RestRequest request = new RestRequest("validate")
            .AddHeader("Authorization", $"Bearer {accessToken}");

        return await ExecuteRequest<ValidatedTokenResponse>(request, _authClient);
    }

    public Task<ValidatedTokenResponse> ValidateToken(HttpRequest request)
    {
        string authHeader = request.Headers["Authorization"].First() ?? throw new("Missing Authorization header");
        string accessToken = authHeader["Bearer ".Length..];
        return ValidateToken(accessToken);
    }

    public async Task<TokenResponse> RefreshToken(string refreshToken)
    {
        RestRequest request = CreateAuthRequest("token")
            .AddParameter("refresh_token", refreshToken)
            .AddParameter("grant_type", "refresh_token");

        return await ExecuteRequest<TokenResponse>(request, _authClient);
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

    public async Task<TokenResponse> PollForToken(string deviceCode)
    {
        RestRequest request = CreateAuthRequest("token")
            .AddParameter("grant_type", "urn:ietf:params:oauth:grant-type:device_code")
            .AddParameter("device_code", deviceCode)
            .AddParameter("scopes", string.Join(' ', Globals.Scopes.Keys));

        return await ExecuteRequest<TokenResponse>(request, _authClient);
    }

    public static async Task<TwitchAuthResponse> RefreshAccessTokenAsync(string? refreshToken = null)
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

    public static void StartAutoRefresh(CancellationToken cancellationToken)
    {
        if (IsAutoRefreshEnabled) return;

        IsAutoRefreshEnabled = true;
        _ = AutoRefreshTask(cancellationToken);
    }

    private static async Task AutoRefreshTask(CancellationToken cancellationToken)
    {
        try
        {
            while (IsAutoRefreshEnabled && !cancellationToken.IsCancellationRequested)
            {
                await HandleTokenRefresh(cancellationToken);
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
    }

    private static async Task HandleTokenRefresh(CancellationToken cancellationToken)
    {
        DateTime expiresAt = DateTime.Parse(Globals.ExpiresAt, CultureInfo.InvariantCulture);
        TimeSpan timeUntilExpiry = expiresAt - DateTime.UtcNow;
        TimeSpan waitTime = timeUntilExpiry.Subtract(TimeSpan.FromMinutes(5));

        if (waitTime > TimeSpan.Zero)
        {
            await Task.Delay(waitTime, cancellationToken);
        }

        TwitchAuthResponse authResponse = await RefreshAccessTokenAsync();
        await UpdateTokens(authResponse, cancellationToken);
    }

    private static async Task UpdateTokens(TwitchAuthResponse authResponse, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Globals.TokenFilePath, authResponse.ToJson(), cancellationToken);

        Globals.AccessToken = authResponse.AccessToken ?? throw new("Failed to deserialize the response from Twitch.");
        Globals.RefreshToken = authResponse.RefreshToken ?? throw new("Refresh token is missing in the response.");
        Globals.ExpiresIn = authResponse.ExpiresIn.ToString();
        Globals.ExpiresAt = DateTime.UtcNow.AddSeconds(authResponse.ExpiresIn).ToString("o", CultureInfo.InvariantCulture);

        TokenRefreshed?.Invoke(null, authResponse.AccessToken);
    }

    public static void StopAutoRefresh()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        IsAutoRefreshEnabled = false;
    }

    public TwitchUser? GetRestoredById(string authModelId)
    {
        lock (DbContext)
        {
            return DbContext.TwitchUsers.FirstOrDefault(u => u.Id == authModelId);
        }
    }
}