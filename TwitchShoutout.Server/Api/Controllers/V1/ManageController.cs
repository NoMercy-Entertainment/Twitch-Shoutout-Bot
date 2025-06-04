using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
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
        
        List<SimpleChannel> channels = _db.Channels
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

    [HttpDelete("{channelId}")]
    public IActionResult RemoveChannel(int channelId)
    {
        Channel? channel = _db.Channels.Find(channelId);
        if (channel == null)
            return NotFound("SimpleChannel not found.");

        _db.Channels.Remove(channel);
        _db.SaveChanges();
        
        return Ok($"SimpleChannel {channel.Name} removed.");
    }
    
    [HttpPatch("{channelId}")]
    public IActionResult UpdateChannel(int channelId, [FromBody] Channel updatedChannel)
    {
        Channel? channel = _db.Channels.Find(channelId);
        if (channel == null)
            return NotFound("SimpleChannel not found.");

        channel.Name = updatedChannel.Name;
        channel.Enabled = updatedChannel.Enabled;
        _db.SaveChanges();
        
        return Ok($"SimpleChannel {channel.Name} updated.");
    }
    
    [HttpPatch("{channelId}/able")]
    public IActionResult Able(int channelId, [FromBody] Channel updatedChannel)
    {
        Channel? channel = _db.Channels.Find(channelId);
        if (channel == null)
            return NotFound("SimpleChannel not found.");

        channel.Enabled = updatedChannel.Enabled;
        _db.SaveChanges();
        
        return Ok($"Changed bot status for channel {channel.Name} to {(channel.Enabled ? "enabled" : "disabled")}.");
    }
    
    [HttpGet("{channelId}/shoutouts")]
    public IActionResult GetShoutouts(string channelId)
    {
        List<Shoutout> shoutouts = _db.Shoutouts
            .Where(s => s.ChannelId == channelId)
            .Include(s => s.ShoutedUser)
            .ToList();

        return Ok(shoutouts);
    }

    [HttpPost("{channelId}/shoutouts")]
    public IActionResult AddShoutout(string channelId, [FromBody] ShoutoutRequest request)
    {
        TwitchUser? user = _db.TwitchUsers.FirstOrDefault(u => u.Username == request.Username);
        if (user == null)
        {
            return BadRequest($"User {request.Username} not found.");
        }

        Shoutout newShoutout = new()
        {
            ChannelId = channelId,
            ShoutedUserId = user.Id,
            MessageTemplate = request.MessageTemplate,
            Enabled = request.Enabled
        };

        _db.Shoutouts.Add(newShoutout);
        _db.SaveChanges();

        return CreatedAtAction(nameof(GetShoutouts), new { channelId = channelId }, newShoutout);
    }

    [HttpPut("{channelId}/shoutouts/{shoutoutId}")]
    public IActionResult UpdateShoutout(string channelId, int shoutoutId, [FromBody] ShoutoutRequest request)
    {
        Shoutout? shoutout = _db.Shoutouts.Find(shoutoutId);
        if (shoutout == null)
        {
            return NotFound($"Shoutout with id {shoutoutId} not found.");
        }

        TwitchUser? user = _db.TwitchUsers.FirstOrDefault(u => u.Username == request.Username);
        if (user == null)
        {
            return BadRequest($"User {request.Username} not found.");
        }

        shoutout.ShoutedUserId = user.Id;
        shoutout.MessageTemplate = request.MessageTemplate;
        shoutout.Enabled = request.Enabled;

        _db.Shoutouts.Update(shoutout);
        _db.SaveChanges();

        return Ok(shoutout);
    }

    [HttpDelete("{channelId}/shoutouts/{shoutoutId}")]
    public IActionResult DeleteShoutout(string channelId, int shoutoutId)
    {
        Shoutout? shoutout = _db.Shoutouts.Find(shoutoutId);
        if (shoutout == null)
        {
            return NotFound($"Shoutout with id {shoutoutId} not found.");
        }

        _db.Shoutouts.Remove(shoutout);
        _db.SaveChanges();

        return NoContent();
    }
}

public class ShoutoutRequest
{
    [JsonProperty("username")] public string Username { get; set; } = string.Empty;
    [JsonProperty("messageTemplate")] public string MessageTemplate { get; set; } = string.Empty;
    [JsonProperty("enabled")] public bool Enabled { get; set; }
}