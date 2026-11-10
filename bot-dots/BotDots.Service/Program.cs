using BotDots.Service;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

var section = builder.Configuration.GetSection(BotDotsOptions.SectionName);
var settings = section.Get<BotDotsOptions>() ?? new BotDotsOptions();
settings.Validate();

builder.Services.Configure<BotDotsOptions>(section);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<BotServerClient>(client =>
{
    client.BaseAddress = new Uri(settings.ServerUrl);
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient<IRouter, ValhallaRouter>(client =>
{
    client.BaseAddress = new Uri(settings.ValhallaUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton(sp => new Wanderer(sp.GetRequiredService<IRouter>(), sp.GetRequiredService<IOptions<BotDotsOptions>>().Value));
builder.Services.AddHostedService<BotDotWorker>();

builder.Build().Run();
