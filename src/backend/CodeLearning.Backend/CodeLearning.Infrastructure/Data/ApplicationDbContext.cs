using CodeLearning.Core.Entities;
using CodeLearning.Infrastructure.Data.Configurations;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Language> Languages => Set<Language>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Chapter> Chapters => Set<Chapter>();
    public DbSet<Subchapter> Subchapters => Set<Subchapter>();
    public DbSet<CourseBlock> CourseBlocks => Set<CourseBlock>();
    public DbSet<TheoryContent> TheoryContents => Set<TheoryContent>();
    public DbSet<VideoContent> VideoContents => Set<VideoContent>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
    public DbSet<QuizAnswer> QuizAnswers => Set<QuizAnswer>();
    public DbSet<Problem> Problems => Set<Problem>();
    public DbSet<TestCase> TestCases => Set<TestCase>();
    public DbSet<StarterCode> StarterCodes => Set<StarterCode>();
    public DbSet<ProblemTag> ProblemTags => Set<ProblemTag>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<SubmissionTestResult> SubmissionTestResults => Set<SubmissionTestResult>();
    public DbSet<StudentCourseProgress> StudentCourseProgresses => Set<StudentCourseProgress>();
    public DbSet<StudentBlockProgress> StudentBlockProgresses => Set<StudentBlockProgress>();
    public DbSet<StudentQuizAttempt> StudentQuizAttempts => Set<StudentQuizAttempt>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Certificate> Certificates => Set<Certificate>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Tables for the MassTransit transactional outbox, which publishes messages saved with the same transaction
        builder.AddInboxStateEntity();
        builder.AddOutboxMessageEntity();
        builder.AddOutboxStateEntity();

        builder.ConfigureIdentityTables();
        builder.ConfigureTimestamps();
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries<BaseEntity>();

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = DateTimeOffset.UtcNow;
                entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
    }
}
