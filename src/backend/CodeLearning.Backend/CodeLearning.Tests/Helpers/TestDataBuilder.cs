using Bogus;
using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Application.DTOs.Course;
using CodeLearning.Application.DTOs.Problem;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;

namespace CodeLearning.Tests.Helpers;

public static class TestDataBuilder
{
    private static readonly Faker _faker = new();

    #region DTOs

    public static RegisterDto CreateValidRegisterDto(string? email = null, string? role = "Student")
    {
        return new RegisterDto
        {
            Email = email ?? _faker.Internet.Email(),
            Password = "ValidPassword123",
            ConfirmPassword = "ValidPassword123",
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
            Role = role
        };
    }

    public static LoginDto CreateValidLoginDto(string email, string password = "ValidPassword123")
    {
        return new LoginDto
        {
            Email = email,
            Password = password
        };
    }

    public static CreateCourseDto CreateValidCreateCourseDto(string? title = null)
    {
        return new CreateCourseDto
        {
            Title = title ?? _faker.Lorem.Sentence(3, 5),
            Description = _faker.Lorem.Paragraph()
        };
    }

    public static CreateProblemDto CreateValidCreateProblemDto(string? title = null, string difficulty = "Easy")
    {
        return new CreateProblemDto
        {
            Title = title ?? _faker.Lorem.Sentence(3, 5),
            Description = _faker.Lorem.Paragraph(3),
            Difficulty = difficulty,
            TestCases =
            [
                new CreateTestCaseDto
                {
                    Input = "1 2",
                    ExpectedOutput = "3",
                    IsPublic = true
                },
                new CreateTestCaseDto
                {
                    Input = "5 10",
                    ExpectedOutput = "15",
                    IsPublic = false
                }
            ],
            StarterCodes = [],
            TagIds = []
        };
    }

    public static UpdateProblemDto CreateValidUpdateProblemDto(string? title = null, string difficulty = "Medium")
    {
        return new UpdateProblemDto
        {
            Title = title ?? _faker.Lorem.Sentence(3, 5),
            Description = _faker.Lorem.Paragraph(3),
            Difficulty = difficulty,
            TagIds = []
        };
    }

    public static CreateTestCaseDto CreateValidCreateTestCaseDto(bool isPublic = true)
    {
        return new CreateTestCaseDto
        {
            Input = _faker.Random.Number(1, 100).ToString(),
            ExpectedOutput = _faker.Random.Number(1, 100).ToString(),
            IsPublic = isPublic
        };
    }

    public static CreateStarterCodeDto CreateValidCreateStarterCodeDto(Guid languageId)
    {
        return new CreateStarterCodeDto
        {
            Code = "def solution():\n    pass",
            LanguageId = languageId
        };
    }

    #endregion

    #region Entities

    public static User CreateValidUser(UserRole role = UserRole.Student, string? email = null)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            UserName = email ?? _faker.Internet.Email(),
            Email = email ?? _faker.Internet.Email(),
            NormalizedEmail = (email ?? _faker.Internet.Email()).ToUpper(),
            NormalizedUserName = (email ?? _faker.Internet.Email()).ToUpper(),
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow,
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };
    }

    public static Course CreateValidCourse(User instructor, CourseStatus status = CourseStatus.Draft)
    {
        return new Course
        {
            Id = Guid.NewGuid(),
            Title = _faker.Lorem.Sentence(3, 5),
            Description = _faker.Lorem.Paragraph(),
            Status = status,
            InstructorId = instructor.Id,
            Instructor = instructor,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            PublishedAt = status == CourseStatus.Published ? DateTimeOffset.UtcNow : null
        };
    }

    public static Chapter CreateValidChapter(Course course, int orderIndex = 1)
    {
        return new Chapter
        {
            Id = Guid.NewGuid(),
            Title = _faker.Lorem.Sentence(2, 4),
            OrderIndex = orderIndex,
            CourseId = course.Id,
            Course = course,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public static Subchapter CreateValidSubchapter(Chapter chapter, int orderIndex = 1)
    {
        return new Subchapter
        {
            Id = Guid.NewGuid(),
            Title = _faker.Lorem.Sentence(2, 4),
            OrderIndex = orderIndex,
            ChapterId = chapter.Id,
            Chapter = chapter,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public static Problem CreateValidProblem(User author, DifficultyLevel difficulty = DifficultyLevel.Easy)
    {
        return new Problem
        {
            Id = Guid.NewGuid(),
            Title = _faker.Lorem.Sentence(3, 5),
            Description = _faker.Lorem.Paragraph(3),
            Difficulty = difficulty,
            AuthorId = author.Id,
            Author = author,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public static TestCase CreateValidTestCase(Problem problem, int orderIndex = 1, bool isPublic = true)
    {
        return new TestCase
        {
            Id = Guid.NewGuid(),
            Input = _faker.Random.Number(1, 100).ToString(),
            ExpectedOutput = _faker.Random.Number(1, 100).ToString(),
            IsPublic = isPublic,
            OrderIndex = orderIndex,
            ProblemId = problem.Id,
            Problem = problem,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    #endregion

    #region Helper Methods

    public static (Course course, Chapter chapter, Subchapter subchapter) CreateCourseHierarchy(User instructor)
    {
        var course = CreateValidCourse(instructor);
        var chapter = CreateValidChapter(course);
        var subchapter = CreateValidSubchapter(chapter);

        return (course, chapter, subchapter);
    }

    #endregion
}
