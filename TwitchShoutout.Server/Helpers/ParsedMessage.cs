using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchShoutout.Server.Config;

namespace TwitchShoutout.Server.Helpers;

public class ParsedMessage
{
    public ChatMessage ChatMessage { get; }
    public bool IsCommand { get; }
    public bool IsModerator => ChatMessage.IsModerator;
    public bool IsBroadcaster => ChatMessage.IsBroadcaster;
    public bool IsAdmin => ChatMessage.IsModerator || ChatMessage.IsBroadcaster;
    public bool IsBot => ChatMessage.Username.Equals(Globals.BotUsername, StringComparison.OrdinalIgnoreCase);
    public string FullMessage { get; }
    public string? CommandName { get; }
    public string? Arguments { get; }
    public string[] ArgumentList => Arguments?.Split([' '], StringSplitOptions.RemoveEmptyEntries) ?? [];
    public string Key => $"{ChatMessage.Channel}-{CommandName}:{Arguments}";

    private ParsedMessage(OnMessageReceivedArgs e)
    {
        ChatMessage = e.ChatMessage;
        FullMessage = e.ChatMessage.Message;

        // Parse command if message starts with ?
        if (FullMessage.StartsWith("?"))
        {
            IsCommand = true;
            string[] parts = FullMessage.Split([' '], StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length > 0)
            {
                CommandName = parts[0][1..].ToLowerInvariant().Replace("@", "");
                Arguments = parts.Length > 1 ? string.Join(" ", parts[1..]) : string.Empty;
            }
        }
    }

    public static ParsedMessage Parse(OnMessageReceivedArgs e)
    {
        return new(e);
    }
}