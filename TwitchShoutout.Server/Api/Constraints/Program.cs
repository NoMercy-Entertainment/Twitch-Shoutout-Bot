using Microsoft.AspNetCore.Routing;

namespace TwitchShoutout.Server.Api.Constraints;

public class Program
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.Configure<RouteOptions>(options =>
        {
            options.ConstraintMap.Add("ulid", typeof(UlidRouteConstraint));
        });
    }
}