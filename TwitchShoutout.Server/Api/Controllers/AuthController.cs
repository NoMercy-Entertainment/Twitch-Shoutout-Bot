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
            TokenResponse tokenResponse = await twitchAuthService.Callback(code);
            
            string countryCode = Request.Headers["CF-IPCountry"].ToString();
            
            ValidatedTokenResponse validateResponse = await twitchAuthService.ValidateToken(tokenResponse.AccessToken);

            TwitchUser userInfo = await twitchApiService.FetchUser(tokenResponse, countryCode, validateResponse.UserId, enabled: true);
            
            await twitchApiService.FetchModeration(userInfo.Id, tokenResponse);

            return Ok(new
            {
                Message = "logged in successfully",
                User = new UserWithTokenDto(userInfo, tokenResponse),
            });
        }
        catch (Exception e)
        {
            return UnauthorizedResponse(e.Message);
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
            TokenResponse tokenResponse = await twitchAuthService.RefreshToken(data.RefreshToken);
            
            TwitchUser user = await dbContext.TwitchUsers
                .FirstAsync(u => u.Id == currentUser.Id);
            user.AccessToken = tokenResponse.AccessToken;
            user.RefreshToken = tokenResponse.RefreshToken;
            user.TokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
            await dbContext.SaveChangesAsync();

            return Ok(new
            {
                Message = "Token refreshed successfully",
                User = new UserWithTokenDto(user, tokenResponse),
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
            TokenResponse tokenResponse = await twitchAuthService.PollForToken(data.DeviceCode);
            
            string countryCode = Request.Headers["CF-IPCountry"].ToString();
            
            ValidatedTokenResponse validateResponse = await twitchAuthService.ValidateToken(tokenResponse.AccessToken);

            string? userId = validateResponse.UserId;

            TwitchUser userInfo = await twitchApiService.FetchUser(tokenResponse, countryCode, userId, enabled: true);
            
            return Ok(new
            {
                Message = "Moderator logged in successfully",
                User = new UserWithTokenDto(userInfo, tokenResponse),
            });

        }
        catch (Exception e)
        {
            return BadRequestResponse(e.Message);
        }
    }
}
