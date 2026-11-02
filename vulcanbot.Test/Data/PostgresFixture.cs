using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using vulcanbot.Data;

namespace vulcanbot.Test.Data;

/// <summary>
/// Starts a single PostgreSQL container for all database tests in this namespace
/// and applies the EF Core migrations once.
/// </summary>
[SetUpFixture]
public class PostgresFixture
{
    private static PostgreSqlContainer? _container;

    public static string ConnectionString =>
        _container?.GetConnectionString() ?? throw new InvalidOperationException("Container not started.");

    public static VulcanDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<VulcanDbContext>().UseNpgsql(ConnectionString).Options);

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

        await _container.StartAsync();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}
