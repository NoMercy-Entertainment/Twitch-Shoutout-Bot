using TwitchShoutout.Database;
using TwitchShoutout.Database.Models;
using TwitchShoutout.Server.Services;

namespace TwitchShoutout.Server.Managers;

public class ChannelManager
{
    private readonly WorkerService _workerService;
    private readonly BotDbContext _dbContext;

    public async Task JoinChannel(TwitchUser broadcaster)
    {
        Channel channel = new()
        {
            Id = broadcaster.Id,
            Name = broadcaster.Username,
            Enabled = true,
            User = broadcaster
        };

        _dbContext.Channels.Add(channel);
        await _dbContext.SaveChangesAsync();

        // Notify worker to connect to new channel
        await _workerService.ConnectToChannel(channel, CancellationToken.None);
    }
}