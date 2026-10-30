using CodeLearning.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Data.Configurations;

public static class TimestampModelBuilderExtensions
{
    // BaseEntity configuration (CreatedAt, UpdatedAt auto-set)
    public static void ConfigureTimestamps(this ModelBuilder builder)
    {
        var baseEntityTypes = builder.Model.GetEntityTypes()
            .Where(entityType => typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            .ToList();

        foreach (var entityType in baseEntityTypes)
        {
            builder.Entity(entityType.ClrType)
                .Property<DateTimeOffset>("CreatedAt")
                .HasDefaultValueSql("NOW()")
                .ValueGeneratedOnAdd();

            builder.Entity(entityType.ClrType)
                .Property<DateTimeOffset>("UpdatedAt")
                .HasDefaultValueSql("NOW()")
                .ValueGeneratedOnAddOrUpdate();
        }
    }
}
