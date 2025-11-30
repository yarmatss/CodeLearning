using CodeLearning.Core.Enums;
using Microsoft.AspNetCore.Identity;

namespace CodeLearning.Core.Entities;

public class User : IdentityUser<Guid>
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public UserRole Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Course> CoursesAsInstructor { get; init; } = [];
    public ICollection<Problem> ProblemsAsAuthor { get; init; } = [];
    public ICollection<Submission> Submissions { get; init; } = [];
    public ICollection<StudentCourseProgress> CourseProgress { get; init; } = [];
    public ICollection<StudentBlockProgress> BlockProgress { get; init; } = [];
    public ICollection<StudentQuizAttempt> QuizAttempts { get; init; } = [];
    public ICollection<Comment> Comments { get; init; } = [];
    public ICollection<Certificate> Certificates { get; init; } = [];
}
