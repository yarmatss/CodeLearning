using CodeLearning.Application.DTOs.Course;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class CourseService : ICourseService
{
    private readonly ApplicationDbContext _context;
    private readonly ISanitizationService _sanitizationService;

    public CourseService(ApplicationDbContext context, ISanitizationService sanitizationService)
    {
        _context = context;
        _sanitizationService = sanitizationService;
    }

    public async Task<CourseResponseDto> CreateCourseAsync(CreateCourseDto dto, Guid instructorId)
    {
        var sanitizedDescription = _sanitizationService.SanitizeMarkdown(dto.Description);

        var course = new Course
        {
            Title = dto.Title,
            Description = sanitizedDescription,
            Status = CourseStatus.Draft,
            InstructorId = instructorId,
            Instructor = await _context.Users.FindAsync(instructorId) 
                ?? throw new InvalidOperationException("Instructor not found")
        };

        _context.Courses.Add(course);
        await _context.SaveChangesAsync();

        return await MapToCourseResponseDto(course);
    }

    public async Task<CourseResponseDto> GetCourseByIdAsync(Guid courseId)
    {
        var course = await _context.Courses
            .Include(c => c.Instructor)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.Subchapters)
                    .ThenInclude(s => s.Blocks)
            .FirstOrDefaultAsync(c => c.Id == courseId);

        if (course == null)
            throw new KeyNotFoundException($"Course with ID {courseId} not found");

        return await MapToCourseResponseDto(course);
    }

    public async Task<IEnumerable<CourseResponseDto>> GetInstructorCoursesAsync(Guid instructorId)
    {
        var courses = await _context.Courses
            .Include(c => c.Instructor)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.Subchapters)
                    .ThenInclude(s => s.Blocks)
            .Where(c => c.InstructorId == instructorId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var result = new List<CourseResponseDto>();
        foreach (var course in courses)
        {
            result.Add(await MapToCourseResponseDto(course));
        }
        return result;
    }

    public async Task<IEnumerable<CourseResponseDto>> GetPublishedCoursesAsync()
    {
        var courses = await _context.Courses
            .Include(c => c.Instructor)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.Subchapters)
                    .ThenInclude(s => s.Blocks)
            .Where(c => c.Status == CourseStatus.Published)
            .OrderByDescending(c => c.PublishedAt)
            .ToListAsync();

        var result = new List<CourseResponseDto>();
        foreach (var course in courses)
        {
            result.Add(await MapToCourseResponseDto(course));
        }
        return result;
    }

    public async Task<CourseResponseDto> UpdateCourseAsync(Guid courseId, UpdateCourseDto dto, Guid instructorId)
    {
        var course = await _context.Courses
            .Include(c => c.Instructor)
            .FirstOrDefaultAsync(c => c.Id == courseId);

        if (course == null)
            throw new KeyNotFoundException($"Course with ID {courseId} not found");

        if (course.InstructorId != instructorId)
            throw new UnauthorizedAccessException("You can only update your own courses");

        if (course.Status == CourseStatus.Published)
            throw new InvalidOperationException("Cannot update a published course");

        course.Title = dto.Title;
        course.Description = _sanitizationService.SanitizeMarkdown(dto.Description);

        await _context.SaveChangesAsync();

        return await MapToCourseResponseDto(course);
    }

    public async Task<CourseResponseDto> PublishCourseAsync(Guid courseId, Guid instructorId)
    {
        var course = await _context.Courses
            .Include(c => c.Instructor)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.Subchapters)
                    .ThenInclude(s => s.Blocks)
            .FirstOrDefaultAsync(c => c.Id == courseId);

        if (course == null)
            throw new KeyNotFoundException($"Course with ID {courseId} not found");

        if (course.InstructorId != instructorId)
            throw new UnauthorizedAccessException("You can only publish your own courses");

        if (course.Status == CourseStatus.Published)
            throw new InvalidOperationException("Course is already published");

        var validationErrors = new List<string>();

        if (!course.Chapters.Any())
            validationErrors.Add("Course must have at least one chapter");

        foreach (var chapter in course.Chapters)
        {
            if (!chapter.Subchapters.Any())
                validationErrors.Add($"Chapter '{chapter.Title}' must have at least one subchapter");

            foreach (var subchapter in chapter.Subchapters)
            {
                if (!subchapter.Blocks.Any())
                    validationErrors.Add($"Subchapter '{subchapter.Title}' in chapter '{chapter.Title}' must have at least one block");
            }
        }

        if (validationErrors.Any())
            throw new InvalidOperationException($"Cannot publish course: {string.Join(", ", validationErrors)}");

        course.Status = CourseStatus.Published;
        course.PublishedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync();

        return await MapToCourseResponseDto(course);
    }

    public async Task DeleteCourseAsync(Guid courseId, Guid instructorId)
    {
        var course = await _context.Courses
            .FirstOrDefaultAsync(c => c.Id == courseId);

        if (course == null)
            throw new KeyNotFoundException($"Course with ID {courseId} not found");

        if (course.InstructorId != instructorId)
            throw new UnauthorizedAccessException("You can only delete your own courses");

        if (course.Status == CourseStatus.Published)
            throw new InvalidOperationException("Cannot delete a published course");

        _context.Courses.Remove(course);
        await _context.SaveChangesAsync();
    }

    private async Task<CourseResponseDto> MapToCourseResponseDto(Course course)
    {
        // Ensure navigation properties are loaded
        if (!_context.Entry(course).Collection(c => c.Chapters).IsLoaded)
        {
            await _context.Entry(course).Collection(c => c.Chapters).LoadAsync();
        }

        var totalBlocks = 0;
        foreach (var chapter in course.Chapters)
        {
            if (!_context.Entry(chapter).Collection(ch => ch.Subchapters).IsLoaded)
            {
                await _context.Entry(chapter).Collection(ch => ch.Subchapters).LoadAsync();
            }

            foreach (var subchapter in chapter.Subchapters)
            {
                if (!_context.Entry(subchapter).Collection(s => s.Blocks).IsLoaded)
                {
                    await _context.Entry(subchapter).Collection(s => s.Blocks).LoadAsync();
                }
                totalBlocks += subchapter.Blocks.Count;
            }
        }

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
