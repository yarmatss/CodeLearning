using CodeLearning.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeLearning.Infrastructure.Data.Configurations;

public class SubchapterConfiguration : IEntityTypeConfiguration<Subchapter>
{
    public void Configure(EntityTypeBuilder<Subchapter> builder)
    {
        // Chapter -> Subchapters (cascade)
        builder.HasOne(s => s.Chapter)
            .WithMany(ch => ch.Subchapters)
            .HasForeignKey(s => s.ChapterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
