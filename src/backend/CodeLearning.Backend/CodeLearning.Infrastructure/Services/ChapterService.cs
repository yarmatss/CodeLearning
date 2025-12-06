using CodeLearning.Application.DTOs.Chapter;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class ChapterService(ApplicationDbContext context) : IChapterService
{
    public async Task<ChapterResponseDto> AddChapterAsync(Guid courseId, CreateChapterDto dto, Guid instructorId)
    {
        var course = await context.Courses
            .Include(c => c.Chapters)
            .FirstOrDefaultAsync(c => c.Id == courseId)
            ?? throw new KeyNotFoundException($"Course with ID {courseId} not found");

        if (course.InstructorId != instructorId)
        {
            throw new UnauthorizedAccessException("You can only add chapters to your own courses");
        }

        if (course.Status == CourseStatus.Published)
        {
            throw new InvalidOperationException("Cannot modify a published course");
        }

        var maxOrder = course.Chapters.Count > 0 ? course.Chapters.Max(c => c.OrderIndex) : 0;

        var chapter = new Chapter
        {
            Title = dto.Title,
            OrderIndex = maxOrder + 1,
            CourseId = courseId,
            Course = course
        };

        context.Chapters.Add(chapter);
        await context.SaveChangesAsync();

        return MapToChapterResponseDto(chapter);
    }

    public async Task<IEnumerable<ChapterResponseDto>> GetCourseChaptersAsync(Guid courseId)
    {
        var chapters = await context.Chapters
            .Include(c => c.Subchapters)
            .Where(c => c.CourseId == courseId)
            .OrderBy(c => c.OrderIndex)
            .ToListAsync();

        return chapters.Select(MapToChapterResponseDto);
    }

    public async Task<ChapterResponseDto> UpdateChapterOrderAsync(Guid chapterId, int newOrderIndex, Guid instructorId)
    {
        var chapter = await context.Chapters
            .Include(c => c.Course)
            .Include(c => c.Subchapters)
            .FirstOrDefaultAsync(c => c.Id == chapterId)
            ?? throw new KeyNotFoundException($"Chapter with ID {chapterId} not found");

        if (chapter.Course.InstructorId != instructorId)
        {
            throw new UnauthorizedAccessException("You can only modify your own course chapters");
        }

        if (chapter.Course.Status == CourseStatus.Published)
        {
            throw new InvalidOperationException("Cannot modify a published course");
        }

        await ReorderChapters(chapter, newOrderIndex);
        await context.SaveChangesAsync();

        return MapToChapterResponseDto(chapter);
    }

    public async Task DeleteChapterAsync(Guid chapterId, Guid instructorId)
    {
        var chapter = await context.Chapters
            .Include(c => c.Course)
            .FirstOrDefaultAsync(c => c.Id == chapterId)
            ?? throw new KeyNotFoundException($"Chapter with ID {chapterId} not found");

        if (chapter.Course.InstructorId != instructorId)
        {
            throw new UnauthorizedAccessException("You can only delete chapters from your own courses");
        }

        if (chapter.Course.Status == CourseStatus.Published)
        {
            throw new InvalidOperationException("Cannot delete chapters from a published course");
        }

        context.Chapters.Remove(chapter);

        await ReorderAfterDeletion(chapter);
        await context.SaveChangesAsync();
    }

    private async Task ReorderChapters(Chapter chapter, int newOrderIndex)
    {
        var oldOrderIndex = chapter.OrderIndex;
        chapter.OrderIndex = newOrderIndex;

        var otherChapters = await context.Chapters
            .Where(c => c.CourseId == chapter.CourseId && c.Id != chapter.Id)
            .ToListAsync();

        foreach (var other in otherChapters)
        {
            if (newOrderIndex < oldOrderIndex)
            {
                if (other.OrderIndex >= newOrderIndex && other.OrderIndex < oldOrderIndex)
                {
                    other.OrderIndex++;
                }
            }
            else
            {
                if (other.OrderIndex > oldOrderIndex && other.OrderIndex <= newOrderIndex)
                {
                    other.OrderIndex--;
                }
            }
        }
    }

    private async Task ReorderAfterDeletion(Chapter deletedChapter)
    {
        var remainingChapters = await context.Chapters
            .Where(c => c.CourseId == deletedChapter.CourseId && c.OrderIndex > deletedChapter.OrderIndex)
            .ToListAsync();

        foreach (var chapter in remainingChapters)
        {
            chapter.OrderIndex--;
        }
    }

    private static ChapterResponseDto MapToChapterResponseDto(Chapter chapter) => new()
    {
        Id = chapter.Id,
        Title = chapter.Title,
        OrderIndex = chapter.OrderIndex,
        CourseId = chapter.CourseId,
        SubchaptersCount = chapter.Subchapters?.Count ?? 0
    };
}
