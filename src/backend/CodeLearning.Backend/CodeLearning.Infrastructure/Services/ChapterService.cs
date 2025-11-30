using CodeLearning.Application.DTOs.Chapter;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class ChapterService : IChapterService
{
    private readonly ApplicationDbContext _context;

    public ChapterService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ChapterResponseDto> AddChapterAsync(Guid courseId, CreateChapterDto dto, Guid instructorId)
    {
        var course = await _context.Courses
            .Include(c => c.Chapters)
            .FirstOrDefaultAsync(c => c.Id == courseId);

        if (course == null)
            throw new KeyNotFoundException($"Course with ID {courseId} not found");

        if (course.InstructorId != instructorId)
            throw new UnauthorizedAccessException("You can only add chapters to your own courses");

        if (course.Status == CourseStatus.Published)
            throw new InvalidOperationException("Cannot modify a published course");

        var maxOrder = course.Chapters.Any() ? course.Chapters.Max(c => c.OrderIndex) : 0;

        var chapter = new Chapter
        {
            Title = dto.Title,
            OrderIndex = maxOrder + 1,
            CourseId = courseId,
            Course = course
        };

        _context.Chapters.Add(chapter);
        await _context.SaveChangesAsync();

        return MapToChapterResponseDto(chapter);
    }

    public async Task<IEnumerable<ChapterResponseDto>> GetCourseChaptersAsync(Guid courseId)
    {
        var chapters = await _context.Chapters
            .Include(c => c.Subchapters)
            .Where(c => c.CourseId == courseId)
            .OrderBy(c => c.OrderIndex)
            .ToListAsync();

        return chapters.Select(MapToChapterResponseDto);
    }

    public async Task<ChapterResponseDto> UpdateChapterOrderAsync(Guid chapterId, int newOrderIndex, Guid instructorId)
    {
        var chapter = await _context.Chapters
            .Include(c => c.Course)
            .Include(c => c.Subchapters)
            .FirstOrDefaultAsync(c => c.Id == chapterId);

        if (chapter == null)
            throw new KeyNotFoundException($"Chapter with ID {chapterId} not found");

        if (chapter.Course.InstructorId != instructorId)
            throw new UnauthorizedAccessException("You can only modify your own course chapters");

        if (chapter.Course.Status == CourseStatus.Published)
            throw new InvalidOperationException("Cannot modify a published course");

        var oldOrderIndex = chapter.OrderIndex;
        chapter.OrderIndex = newOrderIndex;

        // Reorder other chapters
        var otherChapters = await _context.Chapters
            .Where(c => c.CourseId == chapter.CourseId && c.Id != chapterId)
            .ToListAsync();

        foreach (var other in otherChapters)
        {
            if (newOrderIndex < oldOrderIndex)
            {
                if (other.OrderIndex >= newOrderIndex && other.OrderIndex < oldOrderIndex)
                    other.OrderIndex++;
            }
            else
            {
                if (other.OrderIndex > oldOrderIndex && other.OrderIndex <= newOrderIndex)
                    other.OrderIndex--;
            }
        }

        await _context.SaveChangesAsync();

        return MapToChapterResponseDto(chapter);
    }

    public async Task DeleteChapterAsync(Guid chapterId, Guid instructorId)
    {
        var chapter = await _context.Chapters
            .Include(c => c.Course)
            .FirstOrDefaultAsync(c => c.Id == chapterId);

        if (chapter == null)
            throw new KeyNotFoundException($"Chapter with ID {chapterId} not found");

        if (chapter.Course.InstructorId != instructorId)
            throw new UnauthorizedAccessException("You can only delete chapters from your own courses");

        if (chapter.Course.Status == CourseStatus.Published)
            throw new InvalidOperationException("Cannot delete chapters from a published course");

        _context.Chapters.Remove(chapter);

        // Reorder remaining chapters
        var remainingChapters = await _context.Chapters
            .Where(c => c.CourseId == chapter.CourseId && c.OrderIndex > chapter.OrderIndex)
            .ToListAsync();

        foreach (var remaining in remainingChapters)
        {
            remaining.OrderIndex--;
        }

        await _context.SaveChangesAsync();
    }

    private ChapterResponseDto MapToChapterResponseDto(Chapter chapter)
    {
        return new ChapterResponseDto
        {
            Id = chapter.Id,
            Title = chapter.Title,
            OrderIndex = chapter.OrderIndex,
            CourseId = chapter.CourseId,
            SubchaptersCount = chapter.Subchapters?.Count ?? 0
        };
    }
}
