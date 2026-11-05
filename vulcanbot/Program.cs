using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using vulcanbot.Data;
using vulcanbot.Service;
using vulcanbot.Service.Hebe;

var command = args.FirstOrDefault();
var isCommand = command is "register" or "sync";
var builder = Host.CreateApplicationBuilder(isCommand ? args[1..] : args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

builder.Services.AddDbContext<VulcanDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddHttpClient<HebeClient>();
builder.Services.AddTransient<VulcanRegistrationService>();
builder.Services.AddTransient<HomeworkSyncService>();

using var host = builder.Build();

if (command == "register")
{
    await RegisterAsync(host.Services, args.ElementAtOrDefault(1));
    return;
}

if (command == "sync")
{
    Environment.ExitCode = await SyncAsync(host.Services);
    return;
}

await host.RunAsync();

// Usage: vulcanbot register [file]
// Without a file, reads the JSON from the #ap input on https://eduvulcan.pl/api/ap from stdin.
static async Task RegisterAsync(IServiceProvider services, string? path)
{
    string apInput;
    if (path is not null)
    {
        apInput = await File.ReadAllTextAsync(path);
    }
    else
    {
        Console.Error.WriteLine("Log in on https://eduvulcan.pl, open https://eduvulcan.pl/api/ap and paste the value of");
        Console.Error.WriteLine("the hidden #ap input (browser console: copy(document.querySelector('#ap').value)).");
        Console.Error.WriteLine("Finish with Ctrl+D:");
        apInput = await Console.In.ReadToEndAsync();
    }

    await using var scope = services.CreateAsyncScope();
    var registration = scope.ServiceProvider.GetRequiredService<VulcanRegistrationService>();
    var db = scope.ServiceProvider.GetRequiredService<VulcanDbContext>();

    await db.Database.MigrateAsync();
    var result = await registration.RegisterDeviceAsync(apInput, CancellationToken.None);

    foreach (var account in result.ToAccounts())
    {
        var existing = await db.Accounts.SingleOrDefaultAsync(a =>
            a.Symbol == account.Symbol && a.PupilId == account.PupilId);

        if (existing is null)
        {
            db.Accounts.Add(account);
        }
        else
        {
            // Re-registration replaces the device keys and refreshes student data, keeping synced items.
            account.Id = existing.Id;
            account.CreatedAt = existing.CreatedAt;
            account.LastSyncedAt = existing.LastSyncedAt;
            db.Entry(existing).CurrentValues.SetValues(account);
        }

        Console.WriteLine($"{(existing is null ? "Registered" : "Re-registered")}: " +
                          $"{account.FirstName} {account.Surname}, {account.ClassDisplay}, {account.UnitName}");
    }

    await db.SaveChangesAsync();
}

// Usage: vulcanbot sync
// Fetches homework due from 2 weeks ago to 2 months ahead for every registered account.
static async Task<int> SyncAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<VulcanDbContext>();
    var homeworkSync = scope.ServiceProvider.GetRequiredService<HomeworkSyncService>();

    await db.Database.MigrateAsync();

    var today = DateOnly.FromDateTime(DateTime.Now);
    var accounts = await db.Accounts.OrderBy(a => a.Surname).ThenBy(a => a.FirstName).ToListAsync();
    if (accounts.Count == 0)
    {
        Console.Error.WriteLine("No registered accounts — run 'register' first.");
        return 1;
    }

    var failed = 0;
    foreach (var account in accounts)
    {
        try
        {
            var result = await homeworkSync.SyncAsync(account, today.AddDays(-14), today.AddMonths(2),
                CancellationToken.None);
            Console.WriteLine($"{account.FirstName} {account.Surname}: homework fetched {result.Fetched}, " +
                              $"added {result.Added}, updated {result.Updated}");
        }
        catch (HebeException e)
        {
            // One broken account (e.g. device unregistered in eduVULCAN) shouldn't stop the others.
            Console.Error.WriteLine($"{account.FirstName} {account.Surname}: {e.Message}");
            db.ChangeTracker.Clear();
            failed++;
        }
    }

    return failed == 0 ? 0 : 1;
}
