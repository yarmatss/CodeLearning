using CodeLearning.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeLearning.Infrastructure.Data.Configurations;

public class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        builder.HasOne(s => s.Problem)
            .WithMany(p => p.Submissions)
            .HasForeignKey(s => s.ProblemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Student)
            .WithMany(u => u.Submissions)
            .HasForeignKey(s => s.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Language)
            .WithMany(l => l.Submissions)
            .HasForeignKey(s => s.LanguageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => s.StudentId);
        builder.HasIndex(s => s.ProblemId);
    }
}
