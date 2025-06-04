namespace TwitchShoutout.Server.Events;

public class TokenRefreshEventArgs : EventArgs
{
    public string UserId { get; }
    public string NewAccessToken { get; }

    public TokenRefreshEventArgs(string userId, string newAccessToken)
    {
        UserId = userId;
        NewAccessToken = newAccessToken;
    }
}