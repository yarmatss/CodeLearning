using CodeLearning.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace CodeLearning.Infrastructure.Data.Configurations;

public class StudentQuizAttemptConfiguration : IEntityTypeConfiguration<StudentQuizAttempt>
{
    public void Configure(EntityTypeBuilder<StudentQuizAttempt> builder)
    {
        // JSON column for Answers
        builder.Property(e => e.Answers)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<QuizAnswerData>>(v, (JsonSerializerOptions?)null) ?? new List<QuizAnswerData>()
            )
            .Metadata.SetValueComparer(CreateJsonValueComparer<QuizAnswerData>());

        builder.HasOne(a => a.Student)
            .WithMany(u => u.QuizAttempts)
            .HasForeignKey(a => a.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Quiz)
            .WithMany(q => q.StudentAttempts)
            .HasForeignKey(a => a.QuizId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.StudentId, a.QuizId })
            .IsUnique();
    }

    private static ValueComparer<List<T>> CreateJsonValueComparer<T>()
    {
        return new ValueComparer<List<T>>(
            (c1, c2) => JsonSerializer.Serialize(c1) == JsonSerializer.Serialize(c2),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v!.GetHashCode())),
            c => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(c))!
        );
    }
}
