using DotNetEnv;
using Microsoft.AspNetCore.Builder;
using TwitchShoutout.Server.Config;

Env.Load();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

ServiceConfiguration.ConfigureServices(builder);

WebApplication app = builder.Build();

ApplicationConfiguration.ConfigureApp(app);

app.Run();