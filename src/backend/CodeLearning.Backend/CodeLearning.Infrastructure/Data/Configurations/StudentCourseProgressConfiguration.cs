using CodeLearning.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeLearning.Infrastructure.Data.Configurations;

public class StudentCourseProgressConfiguration : IEntityTypeConfiguration<StudentCourseProgress>
{
    public void Configure(EntityTypeBuilder<StudentCourseProgress> builder)
    {
        builder.HasOne(p => p.Student)
            .WithMany(u => u.CourseProgress)
            .HasForeignKey(p => p.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Course)
            .WithMany(c => c.StudentProgress)
            .HasForeignKey(p => p.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.StudentId, p.CourseId })
            .IsUnique();
    }
}
