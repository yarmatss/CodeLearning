using CodeLearning.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeLearning.Infrastructure.Data.Configurations;

public class SubmissionTestResultConfiguration : IEntityTypeConfiguration<SubmissionTestResult>
{
    public void Configure(EntityTypeBuilder<SubmissionTestResult> builder)
    {
        builder.HasOne(r => r.Submission)
            .WithMany(s => s.TestResults)
            .HasForeignKey(r => r.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.TestCase)
            .WithMany(t => t.TestResults)
            .HasForeignKey(r => r.TestCaseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
