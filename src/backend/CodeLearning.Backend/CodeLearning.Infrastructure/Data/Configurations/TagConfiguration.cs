using CodeLearning.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeLearning.Infrastructure.Data.Configurations;

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.HasIndex(t => t.Name)
            .IsUnique();

        var seedDate = SeedConstants.SeedDate;
        builder.HasData(
            new Tag { Id = Guid.Parse("a1111111-1111-1111-1111-111111111111"), Name = "Algorithms", CreatedAt = seedDate, UpdatedAt = seedDate },
            new Tag { Id = Guid.Parse("a2222222-2222-2222-2222-222222222222"), Name = "Data Structures", CreatedAt = seedDate, UpdatedAt = seedDate },
            new Tag { Id = Guid.Parse("a3333333-3333-3333-3333-333333333333"), Name = "OOP", CreatedAt = seedDate, UpdatedAt = seedDate },
            new Tag { Id = Guid.Parse("a4444444-4444-4444-4444-444444444444"), Name = "Arrays", CreatedAt = seedDate, UpdatedAt = seedDate },
            new Tag { Id = Guid.Parse("a5555555-5555-5555-5555-555555555555"), Name = "Strings", CreatedAt = seedDate, UpdatedAt = seedDate },
            new Tag { Id = Guid.Parse("a6666666-6666-6666-6666-666666666666"), Name = "Sorting", CreatedAt = seedDate, UpdatedAt = seedDate },
            new Tag { Id = Guid.Parse("a7777777-7777-7777-7777-777777777777"), Name = "Recursion", CreatedAt = seedDate, UpdatedAt = seedDate },
            new Tag { Id = Guid.Parse("a8888888-8888-8888-8888-888888888888"), Name = "Dynamic Programming", CreatedAt = seedDate, UpdatedAt = seedDate });
    }
}
