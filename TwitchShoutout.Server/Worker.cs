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

            _ = Task.Run(async () =>
            {
                _ = StartTokenRefreshForChannels(stoppingToken);
                await InitializeChannelClients(stoppingToken);
                await StartAutomaticShoutouts(stoppingToken);
            }, stoppingToken);

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
        Console.WriteLine("Initializing bot client...");
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
        Console.WriteLine("Initializing channel clients...");
        List<Channel> enabledChannels = await _dbContext.Channels
            .Include(c => c.User)
            .Where(c => c.Enabled)
            .ToListAsync(stoppingToken);

        List<Task> connectTasks = enabledChannels.Select(channel =>
            ConnectToChannel(channel, stoppingToken)
        ).ToList();

        await Task.WhenAll(connectTasks);
    }

    internal async Task ConnectToChannel(Channel channel, CancellationToken stoppingToken)
    {
        try
        {
            if (_clients.ContainsKey(channel.Name))
            {
                Console.WriteLine($"Already connected to channel: {channel.Name}");
                return;
            }
            
            Console.WriteLine($"Connecting to channel: {channel.Name}");
            
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
        Console.WriteLine($"Setting up event handlers for channel: {channel.Name}");
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
        Console.WriteLine("Starting token refresh for all channels...");
        List<Channel> channels = await _dbContext.Channels
            .Include(c => c.User)
            .Where(c => c.Enabled)
            .ToListAsync(stoppingToken);

        List<Task> refreshTasks = channels.Select(channel =>
            Task.Run(() => _authService.StartTokenRefreshForChannel(channel.User, stoppingToken), stoppingToken)
        ).ToList();

        await Task.WhenAll(refreshTasks);
    }

    private async Task StartAutomaticShoutouts(CancellationToken stoppingToken)
    {
        Console.WriteLine("Starting automatic shoutouts...");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                List<Channel> enabledChannels = await _dbContext.Channels
                    .Include(c => c.Info)
                    .Include(c => c.User)
                    .Include(c => c.Shoutouts)
                    .ThenInclude(shoutout => shoutout.ShoutedUser)
                    .ThenInclude(user => user.Channel)
                    .ThenInclude(c => c.Info)
                    .Where(c => c.Enabled)
                    .Where(c => c.User.IsLive)
                    .ToListAsync(stoppingToken);

                foreach (Channel channel in enabledChannels)
                {
                    await ExecuteShoutout(channel);
                }

                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in automatic shoutouts: {ex.Message}");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); // Retry after 5 minutes
            }
        }
    }

    private async Task ExecuteShoutout(Channel channel)
    {
        try
        {
            await using BotDbContext db = new();
            
            TimeSpan channelTimeout = TimeSpan.FromMinutes(channel.ShoutoutInterval);

            if (channel.LastShoutout.HasValue && (DateTime.UtcNow - channel.LastShoutout) < channelTimeout)
            {
                TimeSpan timeLeft = channelTimeout - (DateTime.UtcNow - channel.LastShoutout.Value);
                Console.WriteLine($"Channel shoutout is on cooldown.  Try again in {timeLeft.Minutes}m {timeLeft.Seconds}s.");
                return;
            }

            // Get all shoutouts for the channel
            ICollection<Shoutout> availableShoutouts = channel.Shoutouts;

            if (availableShoutouts.Count == 0)
            {
                Console.WriteLine($"No shoutouts available for channel {channel.Name}.");
                return;
            }

            // Filter out shoutouts given in the last hour
            List<Shoutout> eligibleShoutouts = availableShoutouts
                .Where(s => s.LastShoutout == null || DateTime.UtcNow - s.LastShoutout >= TimeSpan.FromHours(1))
                .ToList();

            if (!eligibleShoutouts.Any())
            {
                Console.WriteLine($"No eligible shoutouts available for channel {channel.Name} at this time.");
                return;
            }

            Random random = new();
            int randomIndex = random.Next(eligibleShoutouts.Count);
            Shoutout shoutout = eligibleShoutouts[randomIndex];

            // Perform shoutout
            try
            {
                await _apiService.Shoutout(channel.Id, shoutout.ShoutedUserId);

                // Update shoutout timestamps
                channel.LastShoutout = DateTime.UtcNow;
                shoutout.LastShoutout = DateTime.UtcNow;
                db.Channels.Update(channel);
                db.Shoutouts.Update(shoutout);
                await db.SaveChangesAsync();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to send shoutout for {shoutout.ShoutedUser.Username} in channel {channel.Name}: {e.Message}");
                return;
            }

            string message = TwitchMessageProcessingService.ReplaceTemplatePlaceholders(channel, shoutout.ShoutedUser);
            string color = TwitchMessageProcessingService.AnnouncementColors[
                _messageProcessingService.Random.Next(TwitchMessageProcessingService.AnnouncementColors.Length)];
            await _apiService.SendAnnouncement(channel.Id, message, color);

            Console.WriteLine($"Automatic shoutout given in {channel.Name} to {shoutout.ShoutedUser.Username}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error executing shoutout for channel {channel.Name}: {ex.Message}");
        }
    }
}