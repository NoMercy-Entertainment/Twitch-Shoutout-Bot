using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TwitchShoutout.Database;
using TwitchShoutout.Database.Models;
using TwitchShoutout.Server.Dtos;
using TwitchShoutout.Server.Helpers;
using TwitchShoutout.Server.Services;

namespace TwitchShoutout.Server.Api.Controllers;

[ApiController]
[Tags("Authentication")]
[ApiVersionNeutral]
[AllowAnonymous]
[Route("oauth")]
public class AuthenticationController(BotDbContext dbContext, TwitchAuthService twitchAuthService, TwitchApiService twitchApiService) : BaseController
{
    // twitch sends the user back to this endpoint with a code
    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string? error)
    {
        if (!string.IsNullOrEmpty(error)) return BadRequestResponse(error);

        try
        {
            TwitchAuthResponse twitchAuthResponse = await twitchAuthService.Callback(code);
            
            string countryCode = Request.Headers["CF-IPCountry"].ToString();
            
            ValidatedTwitchAuthResponse validateResponse = await twitchAuthService.ValidateToken(twitchAuthResponse.AccessToken);

            TwitchUser userInfo = await twitchApiService.FetchUser(twitchAuthResponse, countryCode, validateResponse.UserId, enabled: true);
            userInfo.AccessToken = twitchAuthResponse.AccessToken;
            userInfo.RefreshToken = twitchAuthResponse.RefreshToken;
            userInfo.TokenExpiry = DateTime.UtcNow.AddSeconds(twitchAuthResponse.ExpiresIn);
            
            await dbContext.TwitchUsers.Upsert(userInfo)
                .On(u => u.Id)
                .WhenMatched((_, newUser) => new()
                {
                    AccessToken = newUser.AccessToken,
                    RefreshToken = newUser.RefreshToken,
                    TokenExpiry = newUser.TokenExpiry,
                    Enabled = true
                })
                .RunAsync();
            
            await twitchApiService.FetchModeration(userInfo.Id, twitchAuthResponse);;

            // Get Worker from singleton registration
            WorkerService workerService = HttpContext.RequestServices.GetRequiredService<WorkerService>();
        
            // Connect to the user's channel
            Channel channel = userInfo.Channel;
            if (channel.Enabled)
            {
                _ = Task.Run(() => workerService.ConnectToChannel(channel, CancellationToken.None));
            }
            
            _ = Task.Run(() => twitchAuthService.StartTokenRefreshForChannel(userInfo, CancellationToken.None));


            return Ok(new
            {
                Message = "logged in successfully",
                User = new UserWithTokenDto(userInfo, twitchAuthResponse),
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return UnprocessableEntityResponse(e.Message);
        }
    }

    [AllowAnonymous]
    [HttpGet("validate")]
    public async Task<IActionResult> ValidateSession()
    {
        TwitchUser? currentUser = User.User();
        if (currentUser is null) return UnauthenticatedResponse("User not logged in.");
        
        try
        {
            await twitchAuthService.ValidateToken(Request);
            
            return Ok(new
            {
                Message = "Session validated successfully",
                User = new UserWithTokenDto(currentUser),
            });
        }
        catch (Exception e)
        {
            return UnauthorizedResponse(e.Message);
        }
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest data)
    {
        TwitchUser? currentUser = User.User();
        if (currentUser is null) return UnauthenticatedResponse("User not logged in.");
        
        try
        {
            TwitchAuthResponse twitchAuthResponse = await twitchAuthService.RefreshToken(data.RefreshToken);
            
            TwitchUser user = await dbContext.TwitchUsers
                .FirstAsync(u => u.Id == currentUser.Id);
            user.AccessToken = twitchAuthResponse.AccessToken;
            user.RefreshToken = twitchAuthResponse.RefreshToken;
            user.TokenExpiry = DateTime.UtcNow.AddSeconds(twitchAuthResponse.ExpiresIn);
            await dbContext.SaveChangesAsync();

            return Ok(new
            {
                Message = "Token refreshed successfully",
                User = new UserWithTokenDto(user, twitchAuthResponse),
            });
        }
        catch (Exception e)
        {
            return UnauthorizedResponse(e.Message);
        }
    }

    [AllowAnonymous]
    [HttpDelete("account")]
    public async Task<IActionResult> DeleteAccount()
    {
        TwitchUser? currentUser = User.User();
        if (currentUser is null) return UnauthenticatedResponse("User not logged in.");

        try
        {
            await twitchAuthService.RevokeToken(currentUser.AccessToken!);

            TwitchUser user = await dbContext.TwitchUsers
                .FirstAsync(u => u.Id == currentUser.Id);

            // Clear auth data
            user.AccessToken = null;
            user.RefreshToken = null;
            user.TokenExpiry = null;

            await dbContext.SaveChangesAsync();

            return Ok(new { Message = "Account deleted successfully" });
        }
        catch (Exception ex)
        {
            return BadRequestResponse(ex.Message);
        }
    }

    // get a redirect url for the user to login directly to twitch
    [AllowAnonymous]
    [HttpGet("login")]
    public IActionResult Login()
    {
        try
        {
            string authorizationUrl = twitchAuthService.GetRedirectUrl();

            return Redirect(authorizationUrl);
        } 
        catch (Exception e)
        {
            return BadRequestResponse(e.Message);
        }
    }

    [AllowAnonymous]
    [HttpGet("authorize")]
    public async Task<IActionResult> Authorize()
    {
        try
        {
            DeviceCodeResponse deviceCodeResponse = await twitchAuthService.Authorize();

            return Ok(new
            {
                Message = "Please log in with Twitch",
                VerificationUrl = deviceCodeResponse.VerificationUri,
                deviceCodeResponse.DeviceCode
            });
        }
        catch (Exception e)
        {
            return BadRequestResponse(e.Message);
        }
    }

    [AllowAnonymous]
    [HttpPost("poll")]
    public async Task<IActionResult> PollForToken([FromBody] DeviceCodeRequest data)
    {
        try
        {
            TwitchAuthResponse twitchAuthResponse = await twitchAuthService.PollForToken(data.DeviceCode);
            
            string countryCode = Request.Headers["CF-IPCountry"].ToString();
            
            ValidatedTwitchAuthResponse validateResponse = await twitchAuthService.ValidateToken(twitchAuthResponse.AccessToken);

            string? userId = validateResponse.UserId;

            TwitchUser userInfo = await twitchApiService.FetchUser(twitchAuthResponse, countryCode, userId, enabled: true);
            
            return Ok(new
            {
                Message = "Moderator logged in successfully",
                User = new UserWithTokenDto(userInfo, twitchAuthResponse),
            });

        }
        catch (Exception e)
        {
            return BadRequestResponse(e.Message);
        }
    }
}
