using CodeLearning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Api.Extensions;

public static class DatabaseMigrationExtensions
{
    public static async Task ApplyPendingMigrationsAsync(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();

        try
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            logger.LogInformation("Checking for pending database migrations...");

            var pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync()).ToList();
            if (pendingMigrations.Count > 0)
            {
                logger.LogInformation("Applying {Count} pending migration(s): {Migrations}",
                    pendingMigrations.Count,
                    string.Join(", ", pendingMigrations));

                await dbContext.Database.MigrateAsync();

                logger.LogInformation("Database migrations applied successfully");
            }
            else
            {
                logger.LogInformation("Database is up to date. No pending migrations");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while migrating the database");

            // allow app to start even if migrations fail
        }
    }
}
