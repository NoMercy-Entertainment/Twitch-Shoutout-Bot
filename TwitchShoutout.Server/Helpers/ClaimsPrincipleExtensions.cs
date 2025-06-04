using System.Security.Authentication;
using System.Security.Claims;
using TwitchShoutout.Database;
using TwitchShoutout.Database.Models;

namespace TwitchShoutout.Server.Helpers;

public static class ClaimsPrincipleExtensions
{
    private static BotDbContext BotDbContext { get; } = new();
    
    public static readonly List<TwitchUser> Users = BotDbContext.TwitchUsers.ToList();
    
    public static string? UserId(this ClaimsPrincipal? principal)
    {
        string? userId = principal?
            .FindFirst(ClaimTypes.NameIdentifier)?
            .Value;

        return userId;
    }

    public static string Role(this ClaimsPrincipal? principal)
    {
        return principal?
                   .FindFirst(ClaimTypes.Role)?
                   .Value
               ?? throw new AuthenticationException("Role not found");
    }

    public static string UserName(this ClaimsPrincipal? principal)
    {
        try
        {
            return principal?.FindFirst("name")?.Value
                   ?? principal?.FindFirst(ClaimTypes.GivenName)?.Value + " " +
                   principal?.FindFirst(ClaimTypes.Surname)?.Value;
        }
        catch (Exception)
        {
            throw new AuthenticationException("User name not found");
        }
    }

    public static string Email(this ClaimsPrincipal? principal)
    {
        try
        {
            return principal?.FindFirst(ClaimTypes.Email)?.Value
                   ?? throw new AuthenticationException("Email not found");
        }
        catch (Exception)
        {
            throw new AuthenticationException("User name not found");
        }
    }

    public static TwitchUser? User(this ClaimsPrincipal? principal)
    {
        string? userId = principal?
            .FindFirst(ClaimTypes.NameIdentifier)?
            .Value;
        
        using BotDbContext dbContext = new();

        return userId is null
            ? null
            : dbContext.TwitchUsers
                .FirstOrDefault(user => user.Id == userId);
    }
}