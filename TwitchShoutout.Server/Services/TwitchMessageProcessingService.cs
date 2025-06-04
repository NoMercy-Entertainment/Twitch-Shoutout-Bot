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
    private readonly Dictionary<string, DateTime> _shoutoutCooldowns = new();

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
        { "settemplate", ("Set shoutout template", true) },
        { "resettemplate", ("Reset shoutout template to default", true) }
    };
    
    private TwitchClient GetClientForChannel(string channelName)
    {
        return _clients.TryGetValue(channelName, out TwitchClient? client) 
            ? client 
            : throw new($"No client found for channel {channelName}");
    }

    private void SendMessage(string channelName, string message)
    {
        TwitchClient client = GetClientForChannel(channelName);
        client.SendMessage(channelName, message);
    }
    
    public async Task HandleCommand(ParsedMessage parsedMessage, Channel channel)
    {
        if (!parsedMessage.IsCommand) return;
        
        if (_shoutoutCooldowns.TryGetValue(parsedMessage.Key, out DateTime lastUsed))
        {
            if ((DateTime.UtcNow - lastUsed).TotalSeconds < 30) return;
        }

        switch (parsedMessage.CommandName?.ToLower())
        {
            case "so":
                await HandleShoutoutCommand(parsedMessage, channel);
                break;
            case "addso":
                if (parsedMessage.IsAdmin)
                    await HandleAddShoutoutCommand(parsedMessage, channel);
                break;
            case "delso":
                if (parsedMessage.IsAdmin)
                    await HandleRemoveShoutoutCommand(parsedMessage, channel);
                break;
            case "join":
                if (parsedMessage.IsBroadcaster)
                    await HandleJoinCommand(parsedMessage);
                break;
            case "help":
                SendHelpMessage(parsedMessage.ChatMessage.Channel);
                break;
            case "settemplate":
                if (parsedMessage.IsBroadcaster)
                    await HandleSetTemplateCommand(parsedMessage, channel);
                break;
            case "resettemplate":
                if (parsedMessage.IsBroadcaster)
                    await HandleResetTemplateCommand(parsedMessage, channel);
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
            .Select(c => $"?{c.Key} - {c.Value.Description}")
            .ToList();

        string message = string.Join(" | ", regularCommands);
        if (modCommands.Any())
        {
            message += " | Mod only: " + string.Join(" | ", modCommands);
        }

        SendMessage(channel, message);
    }
    
    private async Task<bool> IsModerator(string userId, string channelId)
    {
        await using BotDbContext db = new();
        return await db.ChannelModerators
            .AnyAsync(m => m.UserId == userId && m.ChannelId == channelId);
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
            Id = parsedMessage.ChatMessage.UserId,
            Name = parsedMessage.ChatMessage.Username,
            Enabled = true
        };
    
        db.Channels.Add(newChannel);
        await db.SaveChangesAsync();
    
        SendMessage(parsedMessage.ChatMessage.Channel, "Bot will join your channel! Use ?help to see available commands.");
    }
    
    private async Task HandleShoutoutCommand(ParsedMessage parsedMessage, Channel channel)
    {
        await using BotDbContext db = new();

        string shoutedUser = !string.IsNullOrWhiteSpace(parsedMessage.Arguments)
            ? parsedMessage.ArgumentList[0].Replace("@","").ToLower()
            : parsedMessage.ChatMessage.Username;

        TwitchUser user = await db.TwitchUsers
            .Include(twitchUser => twitchUser.Channel)
            .ThenInclude(c => c.Info)
            .FirstOrDefaultAsync(u => u.Username == shoutedUser) ?? await _apiService.FetchUser(id: shoutedUser);

        // Replace placeholders in the template
        string message = ReplaceTemplatePlaceholders(user);

        SendMessage(channel.Name, message);

        try
        {
            await _apiService.Shoutout(parsedMessage.ChatMessage.RoomId, shoutedUser);
        }
        catch (Exception exception)
        {
            SendMessage(channel.Name, $"Failed to send shoutout: {exception.Message}");
        }

        _shoutoutCooldowns[parsedMessage.Key] = DateTime.UtcNow;
    }
    
    private string ReplaceTemplatePlaceholders(TwitchUser user)
    {
        string shoutoutTemplate = user.Channel.ShoutoutTemplate ?? "Check out @{name}!";
        shoutoutTemplate = shoutoutTemplate.Replace("{name}", user.DisplayName);
        shoutoutTemplate = shoutoutTemplate.Replace("{subject}", user.Pronoun?.Subject ?? "they");
        shoutoutTemplate = shoutoutTemplate.Replace("{game}", user.Channel.Info.GameName);
        shoutoutTemplate = shoutoutTemplate.Replace("{title}", user.Channel.Info.Title);

        return shoutoutTemplate;
    }

    private async Task HandleAddShoutoutCommand(ParsedMessage parsedMessage, Channel channel)
    {
        await using BotDbContext db = new();
        
        string shoutedUser = !string.IsNullOrWhiteSpace(parsedMessage.Arguments) 
            ? parsedMessage.ArgumentList[0].Replace("@","")
            : parsedMessage.ChatMessage.Username;
        
        TwitchUser newUser = db.TwitchUsers
            .FirstOrDefault(u => u.Username == shoutedUser) ?? await _apiService.FetchUser(id: shoutedUser);

        Shoutout? existingShoutout = await db.Shoutouts
            .FirstOrDefaultAsync(s => s.ShoutedUserId == newUser.Id);
        
        if (existingShoutout != null)
        {
            SendMessage(channel.Name, $"Shoutout for {shoutedUser} already exists.");
            return;
        }
        
        if (parsedMessage.ChatMessage.RoomId == newUser.Id)
        {
            SendMessage(channel.Name, "You cannot shoutout yourself.");
            return;
        }
        
        Shoutout newShoutout = new()
        {
            ChannelId = parsedMessage.ChatMessage.RoomId,
            MessageTemplate = channel.ShoutoutTemplate ?? "Check out @{shoutedUser}!",
            Enabled = true,
            ShoutedUserId = newUser.Id
        };
        
        db.Shoutouts.Add(newShoutout);
        await db.SaveChangesAsync();
        
        SendMessage(channel.Name, $"Shoutout for {shoutedUser} added successfully.");
    }

    private async Task HandleRemoveShoutoutCommand(ParsedMessage parsedMessage, Channel channel)
    {
        await using BotDbContext db = new();
        
        string shoutedUser = !string.IsNullOrWhiteSpace(parsedMessage.Arguments) 
            ? parsedMessage.ArgumentList[0].Replace("@","")
            : parsedMessage.ChatMessage.Username;
        
        TwitchUser? newUser = db.TwitchUsers
            .FirstOrDefault(u => u.Username == shoutedUser);
        
        if (newUser == null)
        {
            SendMessage(channel.Name, $"User {shoutedUser} not found.");
            return;
        }
        
        Shoutout? existingShoutout = await db.Shoutouts
            .FirstOrDefaultAsync(s => s.ShoutedUserId == newUser.Id);
        
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
            SendMessage(channel.Name, "Please provide a shoutout template.");
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

        channel.ShoutoutTemplate = null; // Reset to default
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