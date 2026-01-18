using CodeLearning.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureIdentity(modelBuilder);
        ConfigureEntities(modelBuilder);
        ConfigureRelationships(modelBuilder);
        ConfigureIndexes(modelBuilder);
        SeedData(modelBuilder);
    }

    private void ConfigureIdentity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
        });

        modelBuilder.Entity<IdentityRole<Guid>>(entity =>
        {
            entity.ToTable("Roles");
        });

        modelBuilder.Entity<IdentityUserRole<Guid>>(entity =>
        {
            entity.ToTable("UserRoles");
        });

        modelBuilder.Entity<IdentityUserClaim<Guid>>(entity =>
        {
            entity.ToTable("UserClaims");
        });

        modelBuilder.Entity<IdentityUserLogin<Guid>>(entity =>
        {
            entity.ToTable("UserLogins");
        });

        modelBuilder.Entity<IdentityUserToken<Guid>>(entity =>
        {
            entity.ToTable("UserTokens");
        });

        modelBuilder.Entity<IdentityRoleClaim<Guid>>(entity =>
        {
            entity.ToTable("RoleClaims");
        });
    }

    private void ConfigureEntities(ModelBuilder modelBuilder)
    {
        // BaseEntity configuration (CreatedAt, UpdatedAt auto-set)
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property<DateTimeOffset>("CreatedAt")
                    .HasDefaultValueSql("NOW()")
                    .ValueGeneratedOnAdd();

                modelBuilder.Entity(entityType.ClrType)
                    .Property<DateTimeOffset>("UpdatedAt")
                    .HasDefaultValueSql("NOW()")
                    .ValueGeneratedOnAddOrUpdate();
            }
        }

        // ProblemTag - composite key
        modelBuilder.Entity<ProblemTag>()
            .HasKey(pt => new { pt.ProblemId, pt.TagId });

        // StudentQuizAttempt - JSON column for Answers
        modelBuilder.Entity<StudentQuizAttempt>()
            .Property(e => e.Answers)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<QuizAnswerData>>(v, (JsonSerializerOptions?)null) ?? new List<QuizAnswerData>()
            )
            .Metadata.SetValueComparer(CreateJsonValueComparer<QuizAnswerData>());

        // Language unique constraint
        modelBuilder.Entity<Language>()
            .HasIndex(l => new { l.Name, l.Version })
            .IsUnique();

        // Tag unique name
        modelBuilder.Entity<Tag>()
            .HasIndex(t => t.Name)
            .IsUnique();

        // Certificate unique verification code
        modelBuilder.Entity<Certificate>()
            .HasIndex(c => c.VerificationCode)
            .IsUnique();
    }

    private void ConfigureRelationships(ModelBuilder modelBuilder)
    {
        // Course -> Instructor (User)
        modelBuilder.Entity<Course>()
            .HasOne(c => c.Instructor)
            .WithMany(u => u.CoursesAsInstructor)
            .HasForeignKey(c => c.InstructorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Course -> Chapters (cascade)
        modelBuilder.Entity<Chapter>()
            .HasOne(ch => ch.Course)
            .WithMany(c => c.Chapters)
            .HasForeignKey(ch => ch.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        // Chapter -> Subchapters (cascade)
        modelBuilder.Entity<Subchapter>()
            .HasOne(s => s.Chapter)
            .WithMany(ch => ch.Subchapters)
            .HasForeignKey(s => s.ChapterId)
            .OnDelete(DeleteBehavior.Cascade);

        // Subchapter -> CourseBlocks (cascade)
        modelBuilder.Entity<CourseBlock>()
            .HasOne(b => b.Subchapter)
            .WithMany(s => s.Blocks)
            .HasForeignKey(b => b.SubchapterId)
            .OnDelete(DeleteBehavior.Cascade);

        // CourseBlock -> TheoryContent (1:1, optional, no cascade)
        modelBuilder.Entity<CourseBlock>()
            .HasOne(b => b.TheoryContent)
            .WithOne(t => t.Block)
            .HasForeignKey<CourseBlock>(b => b.TheoryContentId)
            .OnDelete(DeleteBehavior.Restrict);

        // CourseBlock -> VideoContent (1:1, optional, no cascade)
        modelBuilder.Entity<CourseBlock>()
            .HasOne(b => b.VideoContent)
            .WithOne(v => v.Block)
            .HasForeignKey<CourseBlock>(b => b.VideoContentId)
            .OnDelete(DeleteBehavior.Restrict);

        // CourseBlock -> Quiz (1:1, optional, no cascade)
        modelBuilder.Entity<CourseBlock>()
            .HasOne(b => b.Quiz)
            .WithOne(q => q.Block)
            .HasForeignKey<CourseBlock>(b => b.QuizId)
            .OnDelete(DeleteBehavior.Restrict);

        // CourseBlock -> Problem (1:N, optional, no cascade - Problem stays in pool)
        modelBuilder.Entity<CourseBlock>()
            .HasOne(b => b.Problem)
            .WithMany(p => p.Blocks)
            .HasForeignKey(b => b.ProblemId)
            .OnDelete(DeleteBehavior.Restrict);

        // Quiz -> Questions (cascade)
        modelBuilder.Entity<QuizQuestion>()
            .HasOne(q => q.Quiz)
            .WithMany(qz => qz.Questions)
            .HasForeignKey(q => q.QuizId)
            .OnDelete(DeleteBehavior.Cascade);

        // QuizQuestion -> Answers (cascade)
        modelBuilder.Entity<QuizAnswer>()
            .HasOne(a => a.Question)
            .WithMany(q => q.Answers)
            .HasForeignKey(a => a.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Problem -> Author (User)
        modelBuilder.Entity<Problem>()
            .HasOne(p => p.Author)
            .WithMany(u => u.ProblemsAsAuthor)
            .HasForeignKey(p => p.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Problem -> TestCases (cascade)
        modelBuilder.Entity<TestCase>()
            .HasOne(t => t.Problem)
            .WithMany(p => p.TestCases)
            .HasForeignKey(t => t.ProblemId)
            .OnDelete(DeleteBehavior.Cascade);

        // Problem -> StarterCodes (cascade)
        modelBuilder.Entity<StarterCode>()
            .HasOne(s => s.Problem)
            .WithMany(p => p.StarterCodes)
            .HasForeignKey(s => s.ProblemId)
            .OnDelete(DeleteBehavior.Cascade);

        // StarterCode -> Language
        modelBuilder.Entity<StarterCode>()
            .HasOne(s => s.Language)
            .WithMany(l => l.StarterCodes)
            .HasForeignKey(s => s.LanguageId)
            .OnDelete(DeleteBehavior.Restrict);

        // ProblemTag - Many-to-Many
        modelBuilder.Entity<ProblemTag>()
            .HasOne(pt => pt.Problem)
            .WithMany(p => p.ProblemTags)
            .HasForeignKey(pt => pt.ProblemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProblemTag>()
            .HasOne(pt => pt.Tag)
            .WithMany(t => t.ProblemTags)
            .HasForeignKey(pt => pt.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        // Submission -> Problem, Student, Language
        modelBuilder.Entity<Submission>()
            .HasOne(s => s.Problem)
            .WithMany(p => p.Submissions)
            .HasForeignKey(s => s.ProblemId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Submission>()
            .HasOne(s => s.Student)
            .WithMany(u => u.Submissions)
            .HasForeignKey(s => s.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Submission>()
            .HasOne(s => s.Language)
            .WithMany(l => l.Submissions)
            .HasForeignKey(s => s.LanguageId)
            .OnDelete(DeleteBehavior.Restrict);

        // SubmissionTestResult -> Submission, TestCase (cascade)
        modelBuilder.Entity<SubmissionTestResult>()
            .HasOne(r => r.Submission)
            .WithMany(s => s.TestResults)
            .HasForeignKey(r => r.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SubmissionTestResult>()
            .HasOne(r => r.TestCase)
            .WithMany(t => t.TestResults)
            .HasForeignKey(r => r.TestCaseId)
            .OnDelete(DeleteBehavior.Restrict);

        // StudentCourseProgress
        modelBuilder.Entity<StudentCourseProgress>()
            .HasOne(p => p.Student)
            .WithMany(u => u.CourseProgress)
            .HasForeignKey(p => p.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StudentCourseProgress>()
            .HasOne(p => p.Course)
            .WithMany(c => c.StudentProgress)
            .HasForeignKey(p => p.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        // StudentBlockProgress
        modelBuilder.Entity<StudentBlockProgress>()
            .HasOne(p => p.Student)
            .WithMany(u => u.BlockProgress)
            .HasForeignKey(p => p.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StudentBlockProgress>()
            .HasOne(p => p.Block)
            .WithMany(b => b.StudentProgress)
            .HasForeignKey(p => p.BlockId)
            .OnDelete(DeleteBehavior.Cascade);

        // StudentQuizAttempt
        modelBuilder.Entity<StudentQuizAttempt>()
            .HasOne(a => a.Student)
            .WithMany(u => u.QuizAttempts)
            .HasForeignKey(a => a.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StudentQuizAttempt>()
            .HasOne(a => a.Quiz)
            .WithMany(q => q.StudentAttempts)
            .HasForeignKey(a => a.QuizId)
            .OnDelete(DeleteBehavior.Restrict);

        // Comment (self-referencing)
        modelBuilder.Entity<Comment>()
            .HasOne(c => c.ParentComment)
            .WithMany(c => c.Replies)
            .HasForeignKey(c => c.ParentCommentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Comment>()
            .HasOne(c => c.Block)
            .WithMany(b => b.Comments)
            .HasForeignKey(c => c.BlockId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Comment>()
            .HasOne(c => c.User)
            .WithMany(u => u.Comments)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Certificate
        modelBuilder.Entity<Certificate>()
            .HasOne(c => c.Student)
            .WithMany(u => u.Certificates)
            .HasForeignKey(c => c.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Certificate>()
            .HasOne(c => c.Course)
            .WithMany(co => co.Certificates)
            .HasForeignKey(c => c.CourseId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private void ConfigureIndexes(ModelBuilder modelBuilder)
    {
        // Unique constraints
        modelBuilder.Entity<StudentCourseProgress>()
            .HasIndex(p => new { p.StudentId, p.CourseId })
            .IsUnique();

        modelBuilder.Entity<StudentBlockProgress>()
            .HasIndex(p => new { p.StudentId, p.BlockId })
            .IsUnique();

        modelBuilder.Entity<StudentQuizAttempt>()
            .HasIndex(a => new { a.StudentId, a.QuizId })
            .IsUnique();

        // Performance indexes
        modelBuilder.Entity<CourseBlock>()
            .HasIndex(b => b.SubchapterId);

        modelBuilder.Entity<Submission>()
            .HasIndex(s => s.StudentId);

        modelBuilder.Entity<Submission>()
            .HasIndex(s => s.ProblemId);

        modelBuilder.Entity<Comment>()
            .HasIndex(c => c.BlockId);

        modelBuilder.Entity<Course>()
            .HasIndex(c => c.Status);
    }

    private void SeedData(ModelBuilder modelBuilder)
    {
        // Fixed timestamp for seed data
        var seedDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Seed programming languages
        var languages = new[]
        {
            new Language
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Python",
                Version = "3.14.2",
                DockerImage = "codelearning/python:3.14.2-alpine",
                FileExtension = ".py",
                RunCommand = "/bin/bash /app/run_tests.sh",
                ExecutableCommand = "python3",
                CompileCommand = null,
                TimeoutSeconds = 5,
                MemoryLimitMB = 256,
                CpuLimit = 0.5m,
                IsEnabled = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate
            },
            new Language
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "JavaScript",
                Version = "25.2.1",
                DockerImage = "codelearning/node:25.2.1-alpine",
                FileExtension = ".js",
                RunCommand = "/bin/bash /app/run_tests.sh",
                ExecutableCommand = "node",
                CompileCommand = null,
                TimeoutSeconds = 5,
                MemoryLimitMB = 256,
                CpuLimit = 0.5m,
                IsEnabled = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate
            },
            new Language
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Name = "C#",
                Version = "14",
                DockerImage = "codelearning/dotnet:10.0",
                FileExtension = ".cs",
                RunCommand = "/bin/bash /app/run_tests.sh",
                ExecutableCommand = "dotnet run --no-build",
                CompileCommand = "csharp",  // Special marker - handled in shell script
                TimeoutSeconds = 10,
                MemoryLimitMB = 512,
                CpuLimit = 1.0m,
                IsEnabled = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate
            },
            new Language
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Name = "Java",
                Version = "21",
                DockerImage = "codelearning/java:21-jdk",
                FileExtension = ".java",
                RunCommand = "/bin/bash /app/run_tests.sh",
                ExecutableCommand = "java Solution",
                CompileCommand = "javac Solution.java",  // Must match capitalized filename
                TimeoutSeconds = 10,
                MemoryLimitMB = 512,
                CpuLimit = 1.0m,
                IsEnabled = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate
            }
        };

        modelBuilder.Entity<Language>().HasData(languages);

        // Seed tags with fixed GUIDs
        var tags = new[]
        {
            new Tag { Id = Guid.Parse("a1111111-1111-1111-1111-111111111111"), Name = "Algorithms", CreatedAt = seedDate, UpdatedAt = seedDate },
            new Tag { Id = Guid.Parse("a2222222-2222-2222-2222-222222222222"), Name = "Data Structures", CreatedAt = seedDate, UpdatedAt = seedDate },
            new Tag { Id = Guid.Parse("a3333333-3333-3333-3333-333333333333"), Name = "OOP", CreatedAt = seedDate, UpdatedAt = seedDate },
            new Tag { Id = Guid.Parse("a4444444-4444-4444-4444-444444444444"), Name = "Arrays", CreatedAt = seedDate, UpdatedAt = seedDate },
            new Tag { Id = Guid.Parse("a5555555-5555-5555-5555-555555555555"), Name = "Strings", CreatedAt = seedDate, UpdatedAt = seedDate },
            new Tag { Id = Guid.Parse("a6666666-6666-6666-6666-666666666666"), Name = "Sorting", CreatedAt = seedDate, UpdatedAt = seedDate },
            new Tag { Id = Guid.Parse("a7777777-7777-7777-7777-777777777777"), Name = "Recursion", CreatedAt = seedDate, UpdatedAt = seedDate },
            new Tag { Id = Guid.Parse("a8888888-8888-8888-8888-888888888888"), Name = "Dynamic Programming", CreatedAt = seedDate, UpdatedAt = seedDate }
        };

        modelBuilder.Entity<Tag>().HasData(tags);
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

    private static ValueComparer<List<T>> CreateJsonValueComparer<T>()
    {
        return new ValueComparer<List<T>>(
            (c1, c2) => JsonSerializer.Serialize(c1) == JsonSerializer.Serialize(c2),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v!.GetHashCode())),
            c => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(c))!
        );
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
