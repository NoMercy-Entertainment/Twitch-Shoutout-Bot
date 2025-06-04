using Microsoft.EntityFrameworkCore;
using TwitchLib.Client;
using TwitchLib.Client.Models;
using TwitchShoutout.Database;
using TwitchShoutout.Database.Models;
using TwitchShoutout.Server.Config;
using TwitchShoutout.Server.Helpers;
using TwitchShoutout.Server.Services;

namespace TwitchShoutout.Server;

public class Worker : BackgroundService
{
    private readonly Dictionary<string, TwitchClient> _clients = [];
    private readonly TwitchApiService _apiService;
    private readonly TwitchAuthService _authService;
    private TwitchMessageProcessingService _messageProcessingService = null!;
    private readonly BotDbContext _dbContext;
    private TwitchClient? _botClient;

    public Worker(TwitchApiService apiService, TwitchAuthService authService, BotDbContext dbContext)
    {
        _apiService = apiService;
        _authService = authService;
        _dbContext = dbContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Initialize bot client first
            await InitializeBotClient(stoppingToken);

            // Initialize message processing service with bot client
            _messageProcessingService = new(_clients, _apiService);
            // Load and initialize all enabled channels
            await InitializeChannelClients(stoppingToken);

            // Start token refresh for all channels
            await StartTokenRefreshForChannels(stoppingToken);
            
            stoppingToken.Register(async void () =>
            {
                try
                {
                    await HandleShutdown();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error during shutdown: {e.Message}");
                }
            });

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ExecuteAsync: {ex.Message}");
            throw;
        }
    }
    
    private async Task HandleShutdown()
    {
        try
        {
            // Get all enabled channels
            List<Channel> enabledChannels = await _dbContext.Channels
                .Include(c => c.User)
                .Where(c => c.Enabled)
                .ToListAsync();

            foreach (Channel channel in enabledChannels)
            {
                if (_clients.TryGetValue(channel.Name, out TwitchClient? client))
                {
                    // Send shutdown message
                    client.SendMessage(channel.Name, "Bot is shutting down for maintenance. We'll be back soon! 🔧");
                    
                    // Disconnect client
                    client.Disconnect();
                }
            }

            // Wait for messages to be sent
            await Task.Delay(1000);

            // Dispose clients
            foreach (TwitchClient client in _clients.Values)
            {
                client.Disconnect();
            }
            
            _clients.Clear();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during shutdown: {ex.Message}");
        }
    }

    private async Task InitializeBotClient(CancellationToken stoppingToken)
    {
        if (TwitchAuthService.IsTokenExpired())
        {
            await TwitchAuthService.RefreshAccessTokenAsync();
        }

        _botClient = new();
        ConnectionCredentials credentials = new(Globals.BotUsername, Globals.AccessToken);
        _botClient.Initialize(credentials, Globals.ChannelName);
        _clients[Globals.BotUsername] = _botClient;
        await ConnectClient(_botClient, Globals.BotUsername, stoppingToken);
    }

    private async Task InitializeChannelClients(CancellationToken stoppingToken)
    {
        List<Channel> enabledChannels = await _dbContext.Channels
            .Include(c => c.User)
            .Where(c => c.Enabled)
            .ToListAsync(stoppingToken);

        foreach (Channel channel in enabledChannels)
        {
            ConnectToChannel(channel, stoppingToken);
        }
    }

    internal async Task ConnectToChannel(Channel channel, CancellationToken stoppingToken)
    {
        try
        {
            TwitchClient client = new();
            ConnectionCredentials credentials = new(Globals.BotUsername, Globals.AccessToken);
            client.Initialize(credentials, channel.Name);
            
            stoppingToken.Register(() =>
            {
                Console.WriteLine("Stopping connection to channel: " + channel.Name);
                if (_clients.TryGetValue(channel.Name, out TwitchClient? existingClient))
                {
                    existingClient.Disconnect();
                    _clients.Remove(channel.Name);
                }
            });

            SetupClientEventHandlers(client, channel);
            
            _clients[channel.Name] = client;
            await ConnectClient(client, channel.Name, stoppingToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error connecting to channel {channel.Name}: {ex.Message}");
        }
    }

    private void SetupClientEventHandlers(TwitchClient client, Channel channel)
    {
        client.OnMessageReceived += (_, e) => 
        { 
            // if (e.ChatMessage.Username.Equals(Globals.BotUsername, StringComparison.OrdinalIgnoreCase))
            //     return;

            ParsedMessage parsedMessage = ParsedMessage.Parse(e);
            if (parsedMessage.IsCommand)
            {
                _messageProcessingService.HandleCommand(parsedMessage, channel).Wait();
            }
            else
            {
                _messageProcessingService.HandleMessage(parsedMessage, channel).Wait();
            }
        };

        client.OnConnected += (_, _) =>
        {
            Console.WriteLine($"Connected to channel: {channel.Name}");
            // client.SendMessage(channel.Name, "Bot connected and ready for shoutouts! 🎉");
        };
    }

    private static async Task ConnectClient(TwitchClient client, string channelName, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Run(client.Connect, stoppingToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error connecting to {channelName}: {ex.Message}");
        }
    }

    private async Task StartTokenRefreshForChannels(CancellationToken stoppingToken)
    {
        List<Channel> channels = await _dbContext.Channels
            .Include(c => c.User)
            .Where(c => c.Enabled)
            .ToListAsync(stoppingToken);

        foreach (Channel channel in channels)
        {
            await _authService.StartTokenRefreshForChannel(channel.User, stoppingToken);
        }
    }
}