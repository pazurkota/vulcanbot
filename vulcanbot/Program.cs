using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using vulcanbot.Data;
using vulcanbot.Service;
using vulcanbot.Service.Hebe;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

builder.Services.AddDbContext<VulcanDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<HebeApiClient>();
builder.Services.AddHttpClient<VulcanRoutingService>();
builder.Services.AddTransient<VulcanRegistrationService>();

using var host = builder.Build();
await host.RunAsync();
