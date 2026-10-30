using CodeLearning.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Data.Configurations;

public static class TimestampModelBuilderExtensions
{
    // BaseEntity configuration (CreatedAt, UpdatedAt auto-set)
    public static void ConfigureTimestamps(this ModelBuilder builder)
    {
        var baseEntityClrTypes = builder.Model.GetEntityTypes()
            .Select(entityType => entityType.ClrType)
            .Where(clrType => typeof(BaseEntity).IsAssignableFrom(clrType))
            .ToList();

        foreach (var clrType in baseEntityClrTypes)
        {
            builder.Entity(clrType)
                .Property<DateTimeOffset>("CreatedAt")
                .HasDefaultValueSql("NOW()")
                .ValueGeneratedOnAdd();

            builder.Entity(clrType)
                .Property<DateTimeOffset>("UpdatedAt")
                .HasDefaultValueSql("NOW()")
                .ValueGeneratedOnAddOrUpdate();
        }
    }
}
