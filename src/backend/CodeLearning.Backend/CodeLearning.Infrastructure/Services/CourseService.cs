using CodeLearning.Application.DTOs.Course;
using CodeLearning.Application.Exceptions;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class CourseService(
    ApplicationDbContext context,
    ISanitizationService sanitizationService) : ICourseService
{
    public async Task<CourseResponseDto> CreateCourseAsync(CreateCourseDto dto, Guid instructorId)
    {
        var instructor = await context.Users.FindAsync(instructorId)
            ?? throw new BusinessRuleException("Instructor not found");

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

        return MapToCourseResponseDto(course);
    }

    public async Task<CourseResponseDto> GetCourseByIdAsync(Guid courseId, Guid? viewerId = null)
    {
        var course = await CoursesWithStructure()
            .VisibleTo(viewerId)
            .FirstOrDefaultAsync(c => c.Id == courseId)
            ?? throw new NotFoundException($"Course with ID {courseId} not found");

        return MapToCourseResponseDto(course);
    }

    public async Task<CourseStructureDto> GetCourseStructureAsync(Guid courseId, Guid? viewerId = null)
    {
        var course = await context.Courses
            .VisibleTo(viewerId)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.Subchapters)
                    .ThenInclude(s => s.Blocks)
            .FirstOrDefaultAsync(c => c.Id == courseId)
            ?? throw new NotFoundException($"Course with ID {courseId} not found");

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
        var courses = await CoursesWithStructure()
            .Where(c => c.InstructorId == instructorId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return courses.Select(MapToCourseResponseDto);
    }

    public async Task<IEnumerable<CourseResponseDto>> GetPublishedCoursesAsync()
    {
        var courses = await CoursesWithStructure()
            .Where(c => c.Status == CourseStatus.Published)
            .OrderByDescending(c => c.PublishedAt)
            .ToListAsync();

        return courses.Select(MapToCourseResponseDto);
    }

    public async Task<CourseResponseDto> UpdateCourseAsync(Guid courseId, UpdateCourseDto dto, Guid instructorId)
    {
        var course = await CoursesWithStructure()
            .FirstOrDefaultAsync(c => c.Id == courseId)
            ?? throw new NotFoundException($"Course with ID {courseId} not found");

        if (course.InstructorId != instructorId)
        {
            throw new ForbiddenException("You can only update your own courses");
        }

        if (course.Status == CourseStatus.Published)
        {
            throw new BusinessRuleException("Cannot update a published course");
        }

        course.Title = dto.Title;
        course.Description = sanitizationService.SanitizeMarkdown(dto.Description);

        await context.SaveChangesAsync();

        return MapToCourseResponseDto(course);
    }

    public async Task<CourseResponseDto> PublishCourseAsync(Guid courseId, Guid instructorId)
    {
        var course = await CoursesWithStructure()
            .FirstOrDefaultAsync(c => c.Id == courseId)
            ?? throw new NotFoundException($"Course with ID {courseId} not found");

        if (course.InstructorId != instructorId)
        {
            throw new ForbiddenException("You can only publish your own courses");
        }

        if (course.Status == CourseStatus.Published)
        {
            throw new BusinessRuleException("Course is already published");
        }

        ValidateCourseForPublishing(course);

        course.Status = CourseStatus.Published;
        course.PublishedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync();

        return MapToCourseResponseDto(course);
    }

    public async Task DeleteCourseAsync(Guid courseId, Guid instructorId)
    {
        var course = await context.Courses.FindAsync(courseId)
            ?? throw new NotFoundException($"Course with ID {courseId} not found");

        if (course.InstructorId != instructorId)
        {
            throw new ForbiddenException("You can only delete your own courses");
        }

        if (course.Status == CourseStatus.Published)
        {
            throw new BusinessRuleException("Cannot delete a published course");
        }

        context.Courses.Remove(course);
        await context.SaveChangesAsync();
    }

    private static void ValidateCourseForPublishing(Course course)
    {
        var validationErrors = new List<string>();

        if (course.Chapters.Count == 0)
        {
            validationErrors.Add("Course must have at least one chapter");
        }

        foreach (var chapter in course.Chapters)
        {
            if (chapter.Subchapters.Count == 0)
            {
                validationErrors.Add($"Chapter '{chapter.Title}' must have at least one subchapter");
            }

            validationErrors.AddRange(chapter.Subchapters
                .Where(subchapter => subchapter.Blocks.Count == 0)
                .Select(subchapter => $"Subchapter '{subchapter.Title}' in chapter '{chapter.Title}' must have at least one block"));
        }

        if (validationErrors.Count > 0)
        {
            throw new BusinessRuleException($"Cannot publish course: {string.Join(", ", validationErrors)}");
        }
    }

    // Loads everything MapToCourseResponseDto reads, so mapping never queries per course
    private IQueryable<Course> CoursesWithStructure() =>
        context.Courses
            .Include(c => c.Instructor)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.Subchapters)
                    .ThenInclude(s => s.Blocks);

    private static CourseResponseDto MapToCourseResponseDto(Course course)
    {
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
}
