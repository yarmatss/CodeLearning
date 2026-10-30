using CodeLearning.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeLearning.Infrastructure.Data.Configurations;

public class StarterCodeConfiguration : IEntityTypeConfiguration<StarterCode>
{
    public void Configure(EntityTypeBuilder<StarterCode> builder)
    {
        // Problem -> StarterCodes (cascade)
        builder.HasOne(s => s.Problem)
            .WithMany(p => p.StarterCodes)
            .HasForeignKey(s => s.ProblemId)
            .OnDelete(DeleteBehavior.Cascade);

        // StarterCode -> Language
        builder.HasOne(s => s.Language)
            .WithMany(l => l.StarterCodes)
            .HasForeignKey(s => s.LanguageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
