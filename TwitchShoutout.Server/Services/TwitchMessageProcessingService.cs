using Microsoft.EntityFrameworkCore;
using TwitchLib.Client;
using TwitchShoutout.Database;
using TwitchShoutout.Database.Models;
using TwitchShoutout.Server.Helpers;

namespace TwitchShoutout.Server.Services;

public class TwitchMessageProcessingService
{
    private readonly TwitchClient _client;
    private readonly TwitchApiService _apiService;
    private readonly Dictionary<string, DateTime> _shoutoutCooldowns = new();

    public TwitchMessageProcessingService(TwitchClient client, TwitchApiService apiService)
    {
        _client = client;
        _apiService = apiService;
    }

    public async Task HandleCommand(ParsedCommand command)
    {
        await using BotDbContext db = new();

        string commandKey = $"{command.ChatMessage.Channel}:{command.Prefix}{command.CommandName}";

        if (_shoutoutCooldowns.TryGetValue(commandKey, out DateTime lastUsed))
        {
            if ((DateTime.UtcNow - lastUsed).TotalSeconds < 30) return;
        }

        // Handle built-in sobot commands
        if (command.IsAdmin && command.Prefix.Equals("!sobot", StringComparison.OrdinalIgnoreCase))
        {
            switch (command.CommandName)
            {
                case "so":
                    await HandleShoutoutCommand(command, commandKey);
                    break;
                case "add":
                    await HandleAddShoutoutCommand(command, db);
                    break;
                case "remove":
                    await HandleRemoveShoutoutCommand(command, db);
                    break;
                default:
                    _client.SendMessage(command.ChatMessage.Channel, $"Unknown sobot command: {command.CommandName}. Usage: !sobot <so|add|remove> [arguments]");
                    break;
            }
        }
    }

    private async Task HandleCustomShoutoutCommand(ParsedCommand parsedCommand, string commandKey)
    {
        _client.SendMessage(parsedCommand.ChatMessage.Channel, parsedCommand.FullMessage);
        _shoutoutCooldowns[commandKey] = DateTime.UtcNow;
        await Task.CompletedTask;
    }

    private async Task HandleShoutoutCommand(ParsedCommand parsedCommand, string commandKey)
    {
        string shoutedUser = !string.IsNullOrWhiteSpace(parsedCommand.Arguments) 
            ? parsedCommand.ArgumentList[0].Replace("@","")
            : parsedCommand.ChatMessage.Username;
        
        string message = $"Check out @{shoutedUser} for more information!";

        _client.SendMessage(parsedCommand.ChatMessage.Channel, message);

        try
        {
            await _apiService.Shoutout(parsedCommand.ChatMessage.RoomId, shoutedUser);
        }
        catch (Exception exception)
        {
            _client.SendMessage(parsedCommand.ChatMessage.Channel, $"Failed to send shoutout: {exception.Message}");
        }

        _shoutoutCooldowns[commandKey] = DateTime.UtcNow;
    }

    private async Task HandleAddShoutoutCommand(ParsedCommand parsedCommand, BotDbContext db)
    {
        string shoutedUser = !string.IsNullOrWhiteSpace(parsedCommand.Arguments) 
            ? parsedCommand.ArgumentList[0].Replace("@","")
            : parsedCommand.ChatMessage.Username;
        
        TwitchUser? newUser = db.TwitchUsers
            .FirstOrDefault(u => u.Username == shoutedUser);
        
        if (newUser == null)
        {
            newUser = await _apiService.FetchUser(id: shoutedUser);
        }
        
        Shoutout? existingShoutout = await db.Shoutouts
            .FirstOrDefaultAsync(s => s.ShoutedUserId == newUser.Id);
        
        if (existingShoutout != null)
        {
            _client.SendMessage(parsedCommand.ChatMessage.Channel, $"Shoutout for {shoutedUser} already exists.");
            return;
        }
        
        if (parsedCommand.ChatMessage.RoomId == newUser.Id)
        {
            _client.SendMessage(parsedCommand.ChatMessage.Channel, "You cannot shoutout yourself.");
            return;
        }
        
        Shoutout newShoutout = new()
        {
            ChannelId = parsedCommand.ChatMessage.RoomId,
            Message = $"Check out @{shoutedUser}",
            Enabled = true,
            ShoutedUserId = newUser.Id
        };
        
        db.Shoutouts.Add(newShoutout);
        await db.SaveChangesAsync();
        
        _client.SendMessage(parsedCommand.ChatMessage.Channel, $"Shoutout for {shoutedUser} added successfully.");
    }

    private async Task HandleRemoveShoutoutCommand(ParsedCommand parsedCommand, BotDbContext db)
    {
        string shoutedUser = !string.IsNullOrWhiteSpace(parsedCommand.Arguments) 
            ? parsedCommand.ArgumentList[0].Replace("@","")
            : parsedCommand.ChatMessage.Username;
        
        TwitchUser? newUser = db.TwitchUsers
            .FirstOrDefault(u => u.Username == shoutedUser);
        
        if (newUser == null)
        {
            _client.SendMessage(parsedCommand.ChatMessage.Channel, $"User {shoutedUser} not found.");
            return;
        }
        
        Shoutout? existingShoutout = await db.Shoutouts
            .FirstOrDefaultAsync(s => s.ShoutedUserId == newUser.Id);
        
        if (existingShoutout == null)
        {
            _client.SendMessage(parsedCommand.ChatMessage.Channel, $"No shoutout found for {shoutedUser}.");
            return;
        }
        
        db.Shoutouts.Remove(existingShoutout);
        await db.SaveChangesAsync();
        
        _client.SendMessage(parsedCommand.ChatMessage.Channel, $"Shoutout for {shoutedUser} removed.");
    }

    public async Task HandleMessage(ParsedCommand parsedCommand)
    {
        await using BotDbContext db = new();
        
        await Task.CompletedTask;
    }
}