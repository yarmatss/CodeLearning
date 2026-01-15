using CodeLearning.Application.DTOs.Course;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class CourseService(
    ApplicationDbContext context,
    ISanitizationService sanitizationService) : ICourseService
{
    public async Task<CourseResponseDto> CreateCourseAsync(CreateCourseDto dto, Guid instructorId)
    {
        var instructor = await context.Users.FindAsync(instructorId)
            ?? throw new InvalidOperationException("Instructor not found");

        var course = new Course
        {
            Title = dto.Title,
            Description = sanitizationService.SanitizeMarkdown(dto.Description),
            Status = CourseStatus.Draft,
            InstructorId = instructorId,
            Instructor = instructor
        };

        context.Courses.Add(course);
        await context.SaveChangesAsync();

        return await MapToCourseResponseDto(course);
    }

    public async Task<CourseResponseDto> GetCourseByIdAsync(Guid courseId)
    {
        var course = await context.Courses
            .Include(c => c.Instructor)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.Subchapters)
                    .ThenInclude(s => s.Blocks)
            .FirstOrDefaultAsync(c => c.Id == courseId)
            ?? throw new KeyNotFoundException($"Course with ID {courseId} not found");

        return await MapToCourseResponseDto(course);
    }

    public async Task<CourseStructureDto> GetCourseStructureAsync(Guid courseId)
    {
        var course = await context.Courses
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.Subchapters)
                    .ThenInclude(s => s.Blocks)
            .FirstOrDefaultAsync(c => c.Id == courseId)
            ?? throw new KeyNotFoundException($"Course with ID {courseId} not found");

        var totalBlocks = course.Chapters
            .SelectMany(ch => ch.Subchapters)
            .SelectMany(s => s.Blocks)
            .Count();

        return new CourseStructureDto
        {
            CourseId = course.Id,
            CourseTitle = course.Title,
            CourseDescription = course.Description,
            TotalBlocksCount = totalBlocks,
            Chapters = course.Chapters
                .OrderBy(ch => ch.OrderIndex)
                .Select(ch => new ChapterStructureDto
                {
                    ChapterId = ch.Id,
                    Title = ch.Title,
                    OrderIndex = ch.OrderIndex,
                    Subchapters = ch.Subchapters
                        .OrderBy(s => s.OrderIndex)
                        .Select(s => new SubchapterStructureDto
                        {
                            SubchapterId = s.Id,
                            Title = s.Title,
                            OrderIndex = s.OrderIndex,
                            BlocksCount = s.Blocks.Count
                        })
                        .ToList()
                })
                .ToList()
        };
    }

    public async Task<IEnumerable<CourseResponseDto>> GetInstructorCoursesAsync(Guid instructorId)
    {
        var courses = await context.Courses
            .Include(c => c.Instructor)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.Subchapters)
                    .ThenInclude(s => s.Blocks)
            .Where(c => c.InstructorId == instructorId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return await Task.WhenAll(courses.Select(MapToCourseResponseDto));
    }

    public async Task<IEnumerable<CourseResponseDto>> GetPublishedCoursesAsync()
    {
        var courses = await context.Courses
            .Include(c => c.Instructor)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.Subchapters)
                    .ThenInclude(s => s.Blocks)
            .Where(c => c.Status == CourseStatus.Published)
            .OrderByDescending(c => c.PublishedAt)
            .ToListAsync();

        return await Task.WhenAll(courses.Select(MapToCourseResponseDto));
    }

    public async Task<CourseResponseDto> UpdateCourseAsync(Guid courseId, UpdateCourseDto dto, Guid instructorId)
    {
        var course = await context.Courses
            .Include(c => c.Instructor)
            .FirstOrDefaultAsync(c => c.Id == courseId)
            ?? throw new KeyNotFoundException($"Course with ID {courseId} not found");

        if (course.InstructorId != instructorId)
        {
            throw new UnauthorizedAccessException("You can only update your own courses");
        }

        if (course.Status == CourseStatus.Published)
        {
            throw new InvalidOperationException("Cannot update a published course");
        }

        course.Title = dto.Title;
        course.Description = sanitizationService.SanitizeMarkdown(dto.Description);

        await context.SaveChangesAsync();

        return await MapToCourseResponseDto(course);
    }

    public async Task<CourseResponseDto> PublishCourseAsync(Guid courseId, Guid instructorId)
    {
        var course = await context.Courses
            .Include(c => c.Instructor)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.Subchapters)
                    .ThenInclude(s => s.Blocks)
            .FirstOrDefaultAsync(c => c.Id == courseId)
            ?? throw new KeyNotFoundException($"Course with ID {courseId} not found");

        if (course.InstructorId != instructorId)
        {
            throw new UnauthorizedAccessException("You can only publish your own courses");
        }

        if (course.Status == CourseStatus.Published)
        {
            throw new InvalidOperationException("Course is already published");
        }

        ValidateCourseForPublishing(course);

        course.Status = CourseStatus.Published;
        course.PublishedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync();

        return await MapToCourseResponseDto(course);
    }

    public async Task DeleteCourseAsync(Guid courseId, Guid instructorId)
    {
        var course = await context.Courses.FindAsync(courseId)
            ?? throw new KeyNotFoundException($"Course with ID {courseId} not found");

        if (course.InstructorId != instructorId)
        {
            throw new UnauthorizedAccessException("You can only delete your own courses");
        }

        if (course.Status == CourseStatus.Published)
        {
            throw new InvalidOperationException("Cannot delete a published course");
        }

        context.Courses.Remove(course);
        await context.SaveChangesAsync();
    }

    private static void ValidateCourseForPublishing(Course course)
    {
        var validationErrors = new List<string>();

        if (!course.Chapters.Any())
        {
            validationErrors.Add("Course must have at least one chapter");
        }

        foreach (var chapter in course.Chapters)
        {
            if (!chapter.Subchapters.Any())
            {
                validationErrors.Add($"Chapter '{chapter.Title}' must have at least one subchapter");
            }

            foreach (var subchapter in chapter.Subchapters)
            {
                if (!subchapter.Blocks.Any())
                {
                    validationErrors.Add($"Subchapter '{subchapter.Title}' in chapter '{chapter.Title}' must have at least one block");
                }
            }
        }

        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException($"Cannot publish course: {string.Join(", ", validationErrors)}");
        }
    }

    private async Task<CourseResponseDto> MapToCourseResponseDto(Course course)
    {
        await EnsureNavigationPropertiesLoaded(course);

        var totalBlocks = course.Chapters
            .SelectMany(ch => ch.Subchapters)
            .Sum(s => s.Blocks.Count);

        return new CourseResponseDto
        {
            Id = course.Id,
            Title = course.Title,
            Description = course.Description,
            Status = course.Status,
            InstructorId = course.InstructorId,
            InstructorName = $"{course.Instructor.FirstName} {course.Instructor.LastName}",
            CreatedAt = course.CreatedAt,
            PublishedAt = course.PublishedAt,
            ChaptersCount = course.Chapters.Count,
            TotalBlocks = totalBlocks
        };
    }

    private async Task EnsureNavigationPropertiesLoaded(Course course)
    {
        if (!context.Entry(course).Collection(c => c.Chapters).IsLoaded)
        {
            await context.Entry(course).Collection(c => c.Chapters).LoadAsync();
        }

        foreach (var chapter in course.Chapters)
        {
            if (!context.Entry(chapter).Collection(ch => ch.Subchapters).IsLoaded)
            {
                await context.Entry(chapter).Collection(ch => ch.Subchapters).LoadAsync();
            }

            foreach (var subchapter in chapter.Subchapters)
            {
                if (!context.Entry(subchapter).Collection(s => s.Blocks).IsLoaded)
                {
                    await context.Entry(subchapter).Collection(s => s.Blocks).LoadAsync();
                }
            }
        }
    }
}
