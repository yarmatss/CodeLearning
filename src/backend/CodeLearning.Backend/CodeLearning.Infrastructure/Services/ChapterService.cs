using CodeLearning.Application.DTOs.Chapter;
using CodeLearning.Application.Exceptions;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Infrastructure.Extensions;
using CodeLearning.Infrastructure.Services.Learning;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class ChapterService(ApplicationDbContext context) : IChapterService
{
    public async Task<ChapterResponseDto> AddChapterAsync(Guid courseId, CreateChapterDto dto, Guid instructorId)
    {
        await using var scope = await CourseEditScope.BeginAsync(context, courseId);

        var course = await context.Courses.FirstOrDefaultAsync(c => c.Id == courseId)
            ?? throw new NotFoundException($"Course with ID {courseId} not found");

        EnsureEditable(course, instructorId, "You can only add chapters to your own courses");

        // Read under the course lock, so two concurrent adds cannot both take the same index
        var maxOrder = await context.Chapters
            .Where(c => c.CourseId == courseId)
            .MaxAsync(c => (int?)c.OrderIndex) ?? 0;

        var chapter = new Chapter
        {
            Title = dto.Title,
            OrderIndex = maxOrder + 1,
            CourseId = courseId,
            Course = course
        };

        context.Chapters.Add(chapter);
        await context.SaveChangesAsync();
        await scope.CommitAsync();

        return MapToChapterResponseDto(chapter, subchaptersCount: 0);
    }

    public async Task<IEnumerable<ChapterResponseDto>> GetCourseChaptersAsync(Guid courseId, Guid? viewerId = null)
    {
        if (!await context.Courses.VisibleTo(viewerId).AnyAsync(c => c.Id == courseId))
        {
            throw new NotFoundException($"Course with ID {courseId} not found");
        }

        return await context.Chapters
            .Where(c => c.CourseId == courseId)
            .OrderBy(c => c.OrderIndex)
            .Select(c => new ChapterResponseDto
            {
                Id = c.Id,
                Title = c.Title,
                OrderIndex = c.OrderIndex,
                CourseId = c.CourseId,
                SubchaptersCount = c.Subchapters.Count
            })
            .ToListAsync();
    }

    public async Task<ChapterResponseDto> RenameChapterAsync(Guid courseId, Guid chapterId, UpdateChapterDto dto, Guid instructorId)
    {
        await using var scope = await CourseEditScope.BeginAsync(context, courseId);

        var chapter = await LoadEditableChapterAsync(courseId, chapterId, instructorId);
        chapter.Title = dto.Title;

        await context.SaveChangesAsync();
        await scope.CommitAsync();

        return await GetResponseAsync(chapter);
    }

    public async Task<ChapterResponseDto> UpdateChapterOrderAsync(Guid courseId, Guid chapterId, int newOrderIndex, Guid instructorId)
    {
        await using var scope = await CourseEditScope.BeginAsync(context, courseId);

        var chapter = await LoadEditableChapterAsync(courseId, chapterId, instructorId);

        var siblings = await context.Chapters
            .Where(c => c.CourseId == courseId && c.Id != chapterId)
            .ToListAsync();

        OrderIndexRules.EnsureWithinRange(newOrderIndex, siblings.Count + 1);
        OrderIndexRules.Move(chapter, siblings, c => c.OrderIndex, (c, index) => c.OrderIndex = index, newOrderIndex);

        await context.SaveChangesAsync();
        await scope.CommitAsync();

        return await GetResponseAsync(chapter);
    }

    public async Task DeleteChapterAsync(Guid courseId, Guid chapterId, Guid instructorId)
    {
        await using var scope = await CourseEditScope.BeginAsync(context, courseId);

        var chapter = await LoadEditableChapterAsync(courseId, chapterId, instructorId);

        await BlockContentCleanup.RemoveForBlocksAsync(context, b => b.Subchapter.ChapterId == chapterId);

        var following = await context.Chapters
            .Where(c => c.CourseId == courseId && c.OrderIndex > chapter.OrderIndex)
            .ToListAsync();

        foreach (var sibling in following)
        {
            sibling.OrderIndex--;
        }

        context.Chapters.Remove(chapter);
        await context.SaveChangesAsync();
        await scope.CommitAsync();
    }

    // A chapter id under another course's route is answered as not found
    private async Task<Chapter> LoadEditableChapterAsync(Guid courseId, Guid chapterId, Guid instructorId)
    {
        var chapter = await context.Chapters
            .Include(c => c.Course)
            .FirstOrDefaultAsync(c => c.Id == chapterId && c.CourseId == courseId)
            ?? throw new NotFoundException($"Chapter with ID {chapterId} not found");

        EnsureEditable(chapter.Course, instructorId, "You can only modify chapters of your own courses");

        return chapter;
    }

    private static void EnsureEditable(Course course, Guid instructorId, string forbiddenMessage)
    {
        if (course.InstructorId != instructorId)
        {
            throw new ForbiddenException(forbiddenMessage);
        }

        if (course.Status == CourseStatus.Published)
        {
            throw new BusinessRuleException("Cannot modify a published course");
        }
    }

    private async Task<ChapterResponseDto> GetResponseAsync(Chapter chapter)
    {
        var subchaptersCount = await context.Subchapters.CountAsync(s => s.ChapterId == chapter.Id);
        return MapToChapterResponseDto(chapter, subchaptersCount);
    }

    private static ChapterResponseDto MapToChapterResponseDto(Chapter chapter, int subchaptersCount) => new()
    {
        Id = chapter.Id,
        Title = chapter.Title,
        OrderIndex = chapter.OrderIndex,
        CourseId = chapter.CourseId,
        SubchaptersCount = subchaptersCount
    };
}
