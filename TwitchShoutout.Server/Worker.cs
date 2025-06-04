using System.Configuration;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchShoutout.Server.Config;
using TwitchShoutout.Server.Helpers;
using TwitchShoutout.Server.Services;

namespace TwitchShoutout.Server;

public class Worker : BackgroundService
{
    private readonly TwitchClient _client = new();
    private static readonly Dictionary<string, DateTime> ShoutoutCooldowns = new();
    private static TwitchApiService ApiService { get; set; } = null!;
    private static TwitchAuthService AuthService { get; set; } = null!;   
    private readonly TwitchMessageProcessingService _messageProcessingService;
    
    public Worker(TwitchApiService twitchApiService, TwitchAuthService twitchAuthService)
    {
        ApiService = twitchApiService;
        AuthService = twitchAuthService;
        
        _messageProcessingService = new(_client, ApiService);
        
        if (TwitchAuthService.IsTokenExpired())
        {
            TwitchAuthService.RefreshAccessTokenAsync().Wait();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (string.IsNullOrEmpty(Globals.BotUsername) || string.IsNullOrEmpty(Globals.BotUsername))
                throw new ConfigurationErrorsException("Missing environment variables for Twitch bot.");
            
            TwitchAuthService.TokenRefreshed += OnTokenRefreshed;
            
            ConnectionCredentials credentials = new(Globals.BotUsername, Globals.AccessToken);
            _client.Initialize(credentials, Globals.ChannelName);

            _client.OnMessageReceived += Client_OnMessageReceived;

            _client.OnConnected += (_, _) =>
            {
                Console.WriteLine(
                    $"Connected to Twitch as {Globals.BotUsername} and joined channel {Globals.ChannelName}.");
                _client.SendMessage(Globals.BotUsername,
                    "Hello everyone! I'm here to give shoutouts! Type !shoutout to get one! 🎉");
            };
            _client.OnError += (_, e) =>
            {
                Console.WriteLine($"Error: {e.Exception.Message}");
            };
            
            TwitchAuthService.StartAutoRefresh(stoppingToken);

            await Task.Run(() => _client.Connect(), stoppingToken);
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown, ignore
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ExecuteAsync: {ex.Message}");
            throw;
        }
        finally
        {
            // Cleanup
            TwitchAuthService.TokenRefreshed -= OnTokenRefreshed;
            TwitchAuthService.StopAutoRefresh();
            
            if (_client.IsConnected)
            {
                try
                {
                    await Task.Run(() => _client.Disconnect(), stoppingToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error disconnecting client: {ex.Message}");
                }
            }
        }
    }
    
    private async void OnTokenRefreshed(object? sender, string newToken)
    {
        try
        {
            ConnectionCredentials newCredentials = new(Globals.BotId, newToken);
            await Task.Run(() => _client.Disconnect());
            _client.Initialize(newCredentials, Globals.BotUsername);
            await Task.Run(() => _client.Connect());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling token refresh: {ex.Message}");
        }
    }

    private void Client_OnMessageReceived(object? sender, OnMessageReceivedArgs e)
    {
        ParsedCommand? parsedCommand = ParsedCommand.TryParse(e);
        try
        {
            if (parsedCommand is not null && parsedCommand.IsCommand)
            {
                // Use the service here
                _messageProcessingService.HandleCommand(parsedCommand).Wait();
                return;
            }
            // Use the service here
            _messageProcessingService.HandleMessage(parsedCommand).Wait();
        }
        catch (Exception)
        {
            //
        }
    }
}