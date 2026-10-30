using CodeLearning.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeLearning.Infrastructure.Data.Configurations;

public class TestCaseConfiguration : IEntityTypeConfiguration<TestCase>
{
    public void Configure(EntityTypeBuilder<TestCase> builder)
    {
        // Problem -> TestCases (cascade)
        builder.HasOne(t => t.Problem)
            .WithMany(p => p.TestCases)
            .HasForeignKey(t => t.ProblemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
