using System.Diagnostics.CodeAnalysis;
using Asp.Versioning;

namespace TwitchShoutout.Server.Api.Swagger;
internal class DefaultSunsetPolicyManager : ISunsetPolicyManager
{
    public bool TryGetPolicy(string? name, ApiVersion? apiVersion, [MaybeNullWhen(false)] out SunsetPolicy sunsetPolicy)
    {
        sunsetPolicy = new();
        return true;
    }
}