using TwitchLib.Client.Events;
using TwitchLib.Client.Models;

namespace TwitchShoutout.Server.Helpers;

public class ParsedCommand
{
    public ChatMessage ChatMessage { get; set; }
    public string Prefix { get; }
    public bool IsCommand => Prefix.StartsWith("!", StringComparison.OrdinalIgnoreCase);
    public bool IsModerator => ChatMessage.IsModerator;
    public bool IsBroadcaster => ChatMessage.IsBroadcaster;
    public bool IsAdmin => ChatMessage.IsModerator || ChatMessage.IsBroadcaster;
    public string FullMessage { get; }
    public string CommandName { get; }
    public string Arguments { get; }
    public string[] ArgumentList => Arguments.Split([' '], StringSplitOptions.RemoveEmptyEntries);

    private ParsedCommand(OnMessageReceivedArgs e, string prefix, string commandName, string arguments)
    {
        FullMessage = e.ChatMessage.Message;
        Prefix = prefix;
        CommandName = commandName;
        Arguments = arguments.Trim();
        ChatMessage = e.ChatMessage;
    }

    public static ParsedCommand? TryParse(OnMessageReceivedArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.ChatMessage.Message) || !e.ChatMessage.Message.StartsWith("!"))
        {
            return null;
        }

        string[] parts = e.ChatMessage.Message.Split([' '], 3, StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length == 0) return null;

        string prefixAndCommand = parts[0];
        string commandName;
        string prefix;
        
        if (prefixAndCommand.Equals("!sobot", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
        {
            prefix = parts[0].ToLowerInvariant();
            commandName = parts[1].ToLowerInvariant().Replace("@", "");
        }
        else
        {
            prefix = "!";
            commandName = prefixAndCommand.Substring(1).ToLowerInvariant().Replace("@", "");
        }
        
        string arguments = parts.Length > (prefix.Equals("!sobot", StringComparison.OrdinalIgnoreCase) ? 2 : 1) 
            ? parts[prefix.Equals("!sobot", StringComparison.OrdinalIgnoreCase) ? 2 : 1] 
            : string.Empty;

        return new(e, prefix, commandName, arguments);
    }
}