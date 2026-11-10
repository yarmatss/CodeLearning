using CodeLearning.Infrastructure.Data;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using System.Security.Cryptography;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace CodeLearning.Tests.Fixtures;

public class IntegrationTestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer;
    private readonly RedisContainer _redisContainer;
    private string? _truncateSql;

    public IntegrationTestWebAppFactory()
    {
        _postgresContainer = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("integrationtestdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .WithCleanUp(true)
            .Build();

        _redisContainer = new RedisBuilder("redis:8-alpine")
            .WithCleanUp(true)
            .Build();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Secrets are not stored in appsettings; supply test-only values here
        builder.UseSetting("ConnectionStrings:DefaultConnection", _postgresContainer.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Redis", _redisContainer.GetConnectionString());
        builder.UseSetting("JwtSettings:SecretKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));

        builder.ConfigureServices(services =>
        {
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));

            if (dbDescriptor != null)
            {
                services.Remove(dbDescriptor);
            }

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseNpgsql(_postgresContainer.GetConnectionString());
            });

            var redisDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IConnectionMultiplexer));

            if (redisDescriptor != null)
            {
                services.Remove(redisDescriptor);
            }

            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var configuration = ConfigurationOptions.Parse(_redisContainer.GetConnectionString());
                return ConnectionMultiplexer.Connect(configuration);
            });

            // Swaps RabbitMQ for the in-memory transport; published messages can be inspected through ITestHarness
            services.AddMassTransitTestHarness();

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.Migrate();
        });

        builder.UseEnvironment("Testing");
    }

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();
        await _redisContainer.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
        await base.DisposeAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _truncateSql ??= BuildTruncateSql(db);

        await db.Database.ExecuteSqlRawAsync(_truncateSql);
    }

    // Truncates every table in the EF model except those holding HasData seed rows (languages, tags),
    // so new entities are reset without editing a hard-coded list
    private static string BuildTruncateSql(ApplicationDbContext db)
    {
        var designTimeModel = db.GetService<IDesignTimeModel>().Model;
        var sqlHelper = db.GetService<ISqlGenerationHelper>();

        var tables = designTimeModel.GetEntityTypes()
            .Where(e => e.GetTableName() is not null && !e.GetSeedData().Any())
            .Select(e => sqlHelper.DelimitIdentifier(e.GetTableName()!, e.GetSchema()))
            .Distinct()
            .ToList();

        return $"TRUNCATE TABLE {string.Join(", ", tables)} CASCADE";
    }
}
