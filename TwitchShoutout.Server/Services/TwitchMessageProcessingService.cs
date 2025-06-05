using Microsoft.EntityFrameworkCore;
using TwitchLib.Client;
using TwitchShoutout.Database;
using TwitchShoutout.Database.Models;
using TwitchShoutout.Server.Helpers;

namespace TwitchShoutout.Server.Services;

public class TwitchMessageProcessingService
{
    private readonly Dictionary<string, TwitchClient> _clients;
    private readonly TwitchApiService _apiService;
    internal readonly TimeSpan ChannelCooldown = TimeSpan.FromMinutes(2);
    internal TimeSpan UserShoutoutInterval { get; set; } = TimeSpan.FromMinutes(10);
    internal readonly TimeSpan UserCooldown = TimeSpan.FromHours(1);

    internal readonly Random Random = new();
    internal static readonly string[] AnnouncementColors = { "blue", "green", "orange", "purple", "primary" };


    public TwitchMessageProcessingService(Dictionary<string, TwitchClient> clients, TwitchApiService apiService)
    {
        _clients = clients;
        _apiService = apiService;
    }

    private static readonly Dictionary<string, (string Description, bool ModOnly)> Commands = new()
    {
        { "join", ("Add bot to your channel", false) },
        { "help", ("Show available commands", false) },
        { "so", ("Manual shoutout", true) },
        { "addso", ("Add auto-shoutout", true) },
        { "removeso", ("Remove auto-shoutout", true) },
        { "sotemplate", ("Set shoutout template", true) },
        { "resetso", ("Reset shoutout template", true) }
    };

    private TwitchClient GetClientForChannel(string channelName)
    {
        return _clients.TryGetValue(channelName, out TwitchClient? client)
            ? client
            : throw new($"No client found for channel {channelName}");
    }

    internal void SendMessage(string channelName, string message)
    {
        TwitchClient client = GetClientForChannel(channelName);
        client.SendMessage(channelName, message);
    }

    public async Task HandleCommand(ParsedMessage parsedMessage, Channel channel)
    {
        if (!parsedMessage.IsCommand) return;

        switch (parsedMessage.CommandName?.ToLower())
        {
            case "join":
                await HandleJoinCommand(parsedMessage);
                break;
            case "help":
                SendHelpMessage(channel.Name);
                break;
            case "so":
                if (await IsModerator(parsedMessage.ChatMessage.UserId, channel.Id))
                    await HandleShoutoutCommand(parsedMessage, channel);
                break;
            case "addso":
                if (await IsModerator(parsedMessage.ChatMessage.UserId, channel.Id))
                    await HandleAddShoutoutCommand(parsedMessage, channel);
                break;
            case "removeso":
                if (await IsModerator(parsedMessage.ChatMessage.UserId, channel.Id))
                    await HandleRemoveShoutoutCommand(parsedMessage, channel);
                break;
            case "sotemplate":
                if (await IsModerator(parsedMessage.ChatMessage.UserId, channel.Id))
                    await HandleSetTemplateCommand(parsedMessage, channel);
                break;
            case "resetso":
                if (await IsModerator(parsedMessage.ChatMessage.UserId, channel.Id))
                    await HandleResetTemplateCommand(parsedMessage, channel);
                break;
            default:
                SendMessage(channel.Name, "Invalid command. Use ?help to see available commands.");
                break;
        }
    }

    private void SendHelpMessage(string channel)
    {
        List<string> regularCommands = Commands
            .Where(c => !c.Value.ModOnly)
            .Select(c => $"?{c.Key} - {c.Value.Description}")
            .ToList();

        List<string> modCommands = Commands
            .Where(c => c.Value.ModOnly)
            .Select(c => $"?{c.Key} - {c.Value.Description} (Mod Only)")
            .ToList();

        string message = string.Join(" | ", regularCommands);
        if (modCommands.Any())
        {
            message += " | Mod Commands: " + string.Join(" | ", modCommands);
        }

        SendMessage(channel, message);
    }

    private async Task<bool> IsModerator(string userId, string channelId)
    {
        await using BotDbContext db = new();
        return await db.ChannelModerators
            .AnyAsync(cm => cm.ChannelId == channelId && cm.UserId == userId);
    }

    private async Task HandleJoinCommand(ParsedMessage parsedMessage)
    {
        await using BotDbContext db = new();

        if (await db.Channels.AnyAsync(c => c.Name == parsedMessage.ChatMessage.Username))
        {
            SendMessage(parsedMessage.ChatMessage.Channel, "Bot is already in your channel!");
            return;
        }

        Channel newChannel = new()
        {
            Id = parsedMessage.ChatMessage.RoomId,
            Name = parsedMessage.ChatMessage.Username,
            Enabled = true,
            User = new() { Id = parsedMessage.ChatMessage.RoomId }
        };

        db.Channels.Add(newChannel);
        await db.SaveChangesAsync();

        SendMessage(parsedMessage.ChatMessage.Channel, "Bot will join your channel! Use ?help to see available commands.");
    }

    private async Task HandleShoutoutCommand(ParsedMessage parsedMessage, Channel channel)
    {
        try
        {
            await using BotDbContext db = new();

            string shoutedUser = !string.IsNullOrWhiteSpace(parsedMessage.Arguments)
                ? parsedMessage.ArgumentList[0].Replace("@", "").ToLower()
                : parsedMessage.ChatMessage.Username;

            TwitchUser user = await db.TwitchUsers
                                    .Include(twitchUser => twitchUser.Channel)
                                    .ThenInclude(c => c.Info)
                                    .FirstOrDefaultAsync(u => u.Username == shoutedUser)
                                ?? await _apiService.FetchUser(id: shoutedUser);
            
            if (parsedMessage.ChatMessage.RoomId == user.Id)
            {
                SendMessage(channel.Name, "Cannot shoutout yourself.");
                return;
            }
            
            
            TimeSpan channelTimeout = TimeSpan.FromMinutes(channel.ShoutoutInterval);

            if (channel.LastShoutout.HasValue && (DateTime.UtcNow - channel.LastShoutout) < channelTimeout)
            {
                TimeSpan timeLeft = channelTimeout - (DateTime.UtcNow - channel.LastShoutout.Value);
                Console.WriteLine($"Channel shoutout is on cooldown for {channel.Name}.  Try again in {timeLeft.Minutes}m {timeLeft.Seconds}s.");
                
                string message = ReplaceTemplatePlaceholders(channel, user);
                string color = AnnouncementColors[Random.Next(AnnouncementColors.Length)];

                await _apiService.SendAnnouncement(channel.Id, message, color);
                
                return;
            }
            

            // Check user cooldown
            Shoutout? shoutout = await db.Shoutouts
                .FirstOrDefaultAsync(s => s.ChannelId == channel.Id && s.ShoutedUserId == user.Id);

            if (shoutout?.LastShoutout.HasValue == true && (DateTime.UtcNow - shoutout.LastShoutout) < TimeSpan.FromHours(1))
            {
                TimeSpan timeLeft = UserCooldown - (DateTime.UtcNow - shoutout.LastShoutout.Value);
                Console.WriteLine($"User shoutout is on cooldown for {shoutedUser}.  Try again in {timeLeft.Hours}h {timeLeft.Minutes}m.");
                
                string message = ReplaceTemplatePlaceholders(channel, user);
                string color = AnnouncementColors[Random.Next(AnnouncementColors.Length)];

                await _apiService.SendAnnouncement(channel.Id, message, color);
                
                return;
            }

            await PerformShoutoutAndAnnounce(channel, parsedMessage, shoutedUser, user);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private async Task PerformShoutoutAndAnnounce(Channel channel, ParsedMessage parsedMessage, string shoutedUser, TwitchUser user)
    {
        try
        {
            await using BotDbContext db = new();

            channel.LastShoutout = DateTime.UtcNow;
            db.Channels.Update(channel);

            Shoutout shoutout = await db.Shoutouts
                .FirstAsync(s => s.ChannelId == channel.Id && s.ShoutedUserId == user.Id);

            shoutout.LastShoutout = DateTime.UtcNow;
            db.Shoutouts.Update(shoutout);

            await db.SaveChangesAsync();
            
            await _apiService.Shoutout(parsedMessage.ChatMessage.RoomId, shoutedUser);
        }
        catch (Exception exception)
        {
            SendMessage(channel.Name, $"Failed to send shoutout: {exception.Message}");
        }

        string message = ReplaceTemplatePlaceholders(channel, user);
        string color = AnnouncementColors[Random.Next(AnnouncementColors.Length)];

        await _apiService.SendAnnouncement(channel.Id, message, color);
    }

    internal static string ReplaceTemplatePlaceholders(Channel channel, TwitchUser user)
    {
        using BotDbContext db = new();
        string? messageTemplate = db.Shoutouts
            .FirstOrDefault(s => s.ChannelId == channel.Id && s.ShoutedUserId == user.Id)?.MessageTemplate;

        string shoutoutTemplate = messageTemplate ?? channel.ShoutoutTemplate;

        shoutoutTemplate = shoutoutTemplate.Replace("{name}", user.DisplayName);

        string subjectPronoun = user.Pronoun?.Subject ?? "They";
        string beVerb = subjectPronoun.ToLower() switch
        {
            "he" => "is",
            "she" => "is",
            "they" => "are",
            _ => "is"
        };
        string wasVerb = subjectPronoun.ToLower() switch
        {
            "he" => "was",
            "she" => "was",
            "they" => "were",
            _ => "was"
        };

        shoutoutTemplate = shoutoutTemplate.Replace("{presentTense}", beVerb.ToLower());
        shoutoutTemplate = shoutoutTemplate.Replace("{PresentTense}", beVerb);
        shoutoutTemplate = shoutoutTemplate.Replace("{pastTense}", wasVerb.ToLower());
        shoutoutTemplate = shoutoutTemplate.Replace("{PastTense}", wasVerb);
        shoutoutTemplate = shoutoutTemplate.Replace("{tense}", user.IsLive ? beVerb.ToLower() : wasVerb.ToLower());
        shoutoutTemplate = shoutoutTemplate.Replace("{Tense}", user.IsLive ? beVerb : wasVerb);

        ChannelInfo? channelInfo = user.Channel?.Info ?? user.Channel?.User.Channel.Info;

        if (string.IsNullOrWhiteSpace(channelInfo?.GameName) || string.IsNullOrWhiteSpace(channelInfo.Title))
        {
            return $"Check out @{user.DisplayName} give ${user.Pronoun?.Object ?? "them"} a follow!";
        }

        shoutoutTemplate = shoutoutTemplate.Replace("{subject}", user.Pronoun?.Subject.ToLower() ?? "they");
        shoutoutTemplate = shoutoutTemplate.Replace("{Subject}", user.Pronoun?.Subject ?? "They");
        shoutoutTemplate = shoutoutTemplate.Replace("{object}", user.Pronoun?.Object.ToLower() ?? "them");
        shoutoutTemplate = shoutoutTemplate.Replace("{Object}", user.Pronoun?.Object ?? "Them");

        shoutoutTemplate = shoutoutTemplate.Replace("{game}", channelInfo.GameName);
        shoutoutTemplate = shoutoutTemplate.Replace("{title}", channelInfo.Title);

        shoutoutTemplate = shoutoutTemplate.Replace("{link}", $"https://www.twitch.tv/{user.Username}");
        shoutoutTemplate = shoutoutTemplate.Replace("{username}", user.Username);
        shoutoutTemplate = shoutoutTemplate.Replace("{displayname}", user.DisplayName);
        shoutoutTemplate = shoutoutTemplate.Replace("{id}", user.Id);
        shoutoutTemplate = shoutoutTemplate.Replace("{status}", user.IsLive ? "live" : "offline");
        shoutoutTemplate = shoutoutTemplate.Replace("{Status}", user.IsLive ? "Live" : "Offline");

        return shoutoutTemplate;
    }

    private async Task HandleAddShoutoutCommand(ParsedMessage parsedMessage, Channel channel)
    {
        await using BotDbContext db = new();

        string shoutedUser = !string.IsNullOrWhiteSpace(parsedMessage.Arguments)
            ? parsedMessage.ArgumentList[0].Replace("@", "").ToLower()
            : throw new InvalidOperationException("No username provided.");

        TwitchUser newUser = await db.TwitchUsers
            .FirstOrDefaultAsync(u => u.Username == shoutedUser)
            ?? await _apiService.FetchUser(id: shoutedUser);

        Shoutout? existingShoutout = await db.Shoutouts
            .FirstOrDefaultAsync(s => s.ChannelId == channel.Id && s.ShoutedUserId == newUser.Id);

        if (existingShoutout != null)
        {
            SendMessage(channel.Name, $"Shoutout for {shoutedUser} already exists.");
            return;
        }

        if (parsedMessage.ChatMessage.RoomId == newUser.Id)
        {
            SendMessage(channel.Name, "Cannot add shoutout for yourself.");
            return;
        }

        Shoutout newShoutout = new()
        {
            Id = Guid.NewGuid().ToString(),
            ChannelId = channel.Id,
            ShoutedUserId = newUser.Id,
            Enabled = true
        };

        db.Shoutouts.Add(newShoutout);
        await db.SaveChangesAsync();

        SendMessage(channel.Name, $"Shoutout for {shoutedUser} added successfully.");
    }

    private async Task HandleRemoveShoutoutCommand(ParsedMessage parsedMessage, Channel channel)
    {
        await using BotDbContext db = new();

        string shoutedUser = !string.IsNullOrWhiteSpace(parsedMessage.Arguments)
            ? parsedMessage.ArgumentList[0].Replace("@", "").ToLower()
            : throw new InvalidOperationException("No username provided.");

        TwitchUser? newUser = await db.TwitchUsers
            .FirstOrDefaultAsync(u => u.Username == shoutedUser);

        if (newUser == null)
        {
            SendMessage(channel.Name, $"User {shoutedUser} not found.");
            return;
        }

        Shoutout? existingShoutout = await db.Shoutouts
            .FirstOrDefaultAsync(s => s.ChannelId == channel.Id && s.ShoutedUserId == newUser.Id);

        if (existingShoutout == null)
        {
            SendMessage(channel.Name, $"No shoutout found for {shoutedUser}.");
            return;
        }

        db.Shoutouts.Remove(existingShoutout);
        await db.SaveChangesAsync();

        SendMessage(channel.Name, $"Shoutout for {shoutedUser} removed.");
    }

    private async Task HandleSetTemplateCommand(ParsedMessage parsedMessage, Channel channel)
    {
        await using BotDbContext db = new();

        if (string.IsNullOrWhiteSpace(parsedMessage.Arguments))
        {
            SendMessage(channel.Name, "No template provided.");
            return;
        }

        channel.ShoutoutTemplate = parsedMessage.Arguments;
        db.Channels.Update(channel);
        await db.SaveChangesAsync();

        SendMessage(channel.Name, "Shoutout template updated successfully.");
    }

    private async Task HandleResetTemplateCommand(ParsedMessage parsedMessage, Channel channel)
    {
        await using BotDbContext db = new();

        channel.ShoutoutTemplate = BotDbConfig.DefaultShoutoutTemplate; // Reset to default
        db.Channels.Update(channel);
        await db.SaveChangesAsync();

        SendMessage(channel.Name, "Shoutout template reset to default.");
    }

    public async Task HandleMessage(ParsedMessage parsedMessage, Channel channel)
    {
        Console.WriteLine($"Processing message from {parsedMessage.ChatMessage.Username} in {channel.Name}: {parsedMessage.ChatMessage.Message}");
        await using BotDbContext db = new();

        await Task.CompletedTask;
    }
}