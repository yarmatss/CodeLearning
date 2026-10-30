using CodeLearning.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeLearning.Infrastructure.Data.Configurations;

public class StudentBlockProgressConfiguration : IEntityTypeConfiguration<StudentBlockProgress>
{
    public void Configure(EntityTypeBuilder<StudentBlockProgress> builder)
    {
        builder.HasOne(p => p.Student)
            .WithMany(u => u.BlockProgress)
            .HasForeignKey(p => p.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Block)
            .WithMany(b => b.StudentProgress)
            .HasForeignKey(p => p.BlockId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.StudentId, p.BlockId })
            .IsUnique();
    }
}
