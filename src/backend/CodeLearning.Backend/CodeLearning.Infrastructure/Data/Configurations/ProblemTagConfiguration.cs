using CodeLearning.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeLearning.Infrastructure.Data.Configurations;

public class ProblemTagConfiguration : IEntityTypeConfiguration<ProblemTag>
{
    public void Configure(EntityTypeBuilder<ProblemTag> builder)
    {
        // Composite key for the Problem <-> Tag many-to-many join
        builder.HasKey(pt => new { pt.ProblemId, pt.TagId });

        builder.HasOne(pt => pt.Problem)
            .WithMany(p => p.ProblemTags)
            .HasForeignKey(pt => pt.ProblemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pt => pt.Tag)
            .WithMany(t => t.ProblemTags)
            .HasForeignKey(pt => pt.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
