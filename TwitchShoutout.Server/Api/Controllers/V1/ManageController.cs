using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TwitchShoutout.Database;
using TwitchShoutout.Database.Models;
using TwitchShoutout.Server.Helpers;

namespace TwitchShoutout.Server.Api.Controllers.V1;

[ApiController]
[Tags("Manage")]
[ApiVersion(1.0)]
[Authorize]
[Route("api/v{version:apiVersion}/manage")]
public class ManageController : BaseController
{
    private readonly BotDbContext _db;

    public ManageController(BotDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public IActionResult Index()
    {
        string? userId = User.UserId();
        
        List<SimpleChannel> channels  = _db.Channels
            .Where(c => c.Id == userId || c.ChannelModerators.Any(m => m.UserId == userId))
            .Include(c => c.User)
            .Include(c => c.ChannelModerators)
            .ThenInclude(m => m.User)
            .Include(c => c.Shoutouts)
            .Select(c => new SimpleChannel(c))
            .ToList();
        if (channels.Count == 0)
            return NotFound("No channels found.");
        
        return Ok(channels);
    }
    
    [HttpPost]
    public IActionResult AddChannel([FromBody] string channelName)
    {
        if (_db.Channels.Any(c => c.Name == channelName))
            return BadRequest("Bot is already in this channel.");

        _db.Channels.Add(new() { Name = channelName, Enabled = true });
        _db.SaveChanges();

        return Ok($"Bot added to {channelName}!");
    }

    [HttpDelete("{id}")]
    public IActionResult RemoveChannel(int id)
    {
        Channel? channel = _db.Channels.Find(id);
        if (channel == null)
            return NotFound("SimpleChannel not found.");

        _db.Channels.Remove(channel);
        _db.SaveChanges();
        
        return Ok($"SimpleChannel {channel.Name} removed.");
    }
    
    [HttpPatch("{id}")]
    public IActionResult UpdateChannel(int id, [FromBody] Channel updatedChannel)
    {
        Channel? channel = _db.Channels.Find(id);
        if (channel == null)
            return NotFound("SimpleChannel not found.");

        channel.Name = updatedChannel.Name;
        channel.Enabled = updatedChannel.Enabled;
        _db.SaveChanges();
        
        return Ok($"SimpleChannel {channel.Name} updated.");
    }
    
    [HttpPatch("{id}/able")]
    public IActionResult Able(int id, [FromBody] Channel updatedChannel)
    {
        Channel? channel = _db.Channels.Find(id);
        if (channel == null)
            return NotFound("SimpleChannel not found.");

        channel.Enabled = updatedChannel.Enabled;
        _db.SaveChanges();
        
        return Ok($"Changed bot status for channel {channel.Name} to {(channel.Enabled ? "enabled" : "disabled")}.");
    }
    
}