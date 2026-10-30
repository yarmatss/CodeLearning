using CodeLearning.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeLearning.Infrastructure.Data.Configurations;

public class CourseBlockConfiguration : IEntityTypeConfiguration<CourseBlock>
{
    public void Configure(EntityTypeBuilder<CourseBlock> builder)
    {
        // Subchapter -> CourseBlocks (cascade)
        builder.HasOne(b => b.Subchapter)
            .WithMany(s => s.Blocks)
            .HasForeignKey(b => b.SubchapterId)
            .OnDelete(DeleteBehavior.Cascade);

        // CourseBlock -> TheoryContent (1:1, optional, no cascade)
        builder.HasOne(b => b.TheoryContent)
            .WithOne(t => t.Block)
            .HasForeignKey<CourseBlock>(b => b.TheoryContentId)
            .OnDelete(DeleteBehavior.Restrict);

        // CourseBlock -> VideoContent (1:1, optional, no cascade)
        builder.HasOne(b => b.VideoContent)
            .WithOne(v => v.Block)
            .HasForeignKey<CourseBlock>(b => b.VideoContentId)
            .OnDelete(DeleteBehavior.Restrict);

        // CourseBlock -> Quiz (1:1, optional, no cascade)
        builder.HasOne(b => b.Quiz)
            .WithOne(q => q.Block)
            .HasForeignKey<CourseBlock>(b => b.QuizId)
            .OnDelete(DeleteBehavior.Restrict);

        // CourseBlock -> Problem (1:N, optional, no cascade - Problem stays in pool)
        builder.HasOne(b => b.Problem)
            .WithMany(p => p.Blocks)
            .HasForeignKey(b => b.ProblemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => b.SubchapterId);
    }
}
