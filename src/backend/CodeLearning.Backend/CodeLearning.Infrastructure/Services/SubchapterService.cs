using CodeLearning.Application.DTOs.Subchapter;
using CodeLearning.Application.Exceptions;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Infrastructure.Extensions;
using CodeLearning.Infrastructure.Services.Learning;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class SubchapterService(ApplicationDbContext context) : ISubchapterService
{
    public async Task<SubchapterResponseDto> AddSubchapterAsync(Guid chapterId, CreateSubchapterDto dto, Guid instructorId)
    {
        await using var scope = await BeginEditAsync(chapterId);

        var chapter = await context.Chapters
            .Include(c => c.Course)
            .FirstOrDefaultAsync(c => c.Id == chapterId)
            ?? throw new NotFoundException($"Chapter with ID {chapterId} not found");

        EnsureEditable(chapter.Course, instructorId, "You can only add subchapters to your own courses");

        // Read under the course lock, so two concurrent adds cannot both take the same index
        var maxOrder = await context.Subchapters
            .Where(s => s.ChapterId == chapterId)
            .MaxAsync(s => (int?)s.OrderIndex) ?? 0;

        var subchapter = new Subchapter
        {
            Title = dto.Title,
            OrderIndex = maxOrder + 1,
            ChapterId = chapterId,
            Chapter = chapter
        };

        context.Subchapters.Add(subchapter);
        await context.SaveChangesAsync();
        await scope.CommitAsync();

        return MapToSubchapterResponseDto(subchapter, blocksCount: 0);
    }

    public async Task<IEnumerable<SubchapterResponseDto>> GetChapterSubchaptersAsync(Guid chapterId, Guid? viewerId = null)
    {
        if (!await context.Courses.VisibleTo(viewerId).AnyAsync(c => c.Chapters.Any(ch => ch.Id == chapterId)))
        {
            throw new NotFoundException($"Chapter with ID {chapterId} not found");
        }

        return await context.Subchapters
            .Where(s => s.ChapterId == chapterId)
            .OrderBy(s => s.OrderIndex)
            .Select(s => new SubchapterResponseDto
            {
                Id = s.Id,
                Title = s.Title,
                OrderIndex = s.OrderIndex,
                ChapterId = s.ChapterId,
                BlocksCount = s.Blocks.Count
            })
            .ToListAsync();
    }

    public async Task<SubchapterResponseDto> RenameSubchapterAsync(Guid chapterId, Guid subchapterId, UpdateSubchapterDto dto, Guid instructorId)
    {
        await using var scope = await BeginEditAsync(chapterId);

        var subchapter = await LoadEditableSubchapterAsync(chapterId, subchapterId, instructorId);
        subchapter.Title = dto.Title;

        await context.SaveChangesAsync();
        await scope.CommitAsync();

        return await GetResponseAsync(subchapter);
    }

    public async Task<SubchapterResponseDto> UpdateSubchapterOrderAsync(Guid chapterId, Guid subchapterId, int newOrderIndex, Guid instructorId)
    {
        await using var scope = await BeginEditAsync(chapterId);

        var subchapter = await LoadEditableSubchapterAsync(chapterId, subchapterId, instructorId);

        var siblings = await context.Subchapters
            .Where(s => s.ChapterId == chapterId && s.Id != subchapterId)
            .ToListAsync();

        OrderIndexRules.EnsureWithinRange(newOrderIndex, siblings.Count + 1);
        OrderIndexRules.Move(subchapter, siblings, s => s.OrderIndex, (s, index) => s.OrderIndex = index, newOrderIndex);

        await context.SaveChangesAsync();
        await scope.CommitAsync();

        return await GetResponseAsync(subchapter);
    }

    public async Task DeleteSubchapterAsync(Guid chapterId, Guid subchapterId, Guid instructorId)
    {
        await using var scope = await BeginEditAsync(chapterId);

        var subchapter = await LoadEditableSubchapterAsync(chapterId, subchapterId, instructorId);

        await BlockContentCleanup.RemoveForBlocksAsync(context, b => b.SubchapterId == subchapterId);

        var following = await context.Subchapters
            .Where(s => s.ChapterId == chapterId && s.OrderIndex > subchapter.OrderIndex)
            .ToListAsync();

        foreach (var sibling in following)
        {
            sibling.OrderIndex--;
        }

        context.Subchapters.Remove(subchapter);
        await context.SaveChangesAsync();
        await scope.CommitAsync();
    }

    private async Task<CourseEditScope> BeginEditAsync(Guid chapterId)
    {
        var courseId = await context.Chapters
            .Where(c => c.Id == chapterId)
            .Select(c => (Guid?)c.CourseId)
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException($"Chapter with ID {chapterId} not found");

        return await CourseEditScope.BeginAsync(context, courseId);
    }

    // A subchapter id under another chapter's route is answered as not found
    private async Task<Subchapter> LoadEditableSubchapterAsync(Guid chapterId, Guid subchapterId, Guid instructorId)
    {
        var subchapter = await context.Subchapters
            .Include(s => s.Chapter.Course)
            .FirstOrDefaultAsync(s => s.Id == subchapterId && s.ChapterId == chapterId)
            ?? throw new NotFoundException($"Subchapter with ID {subchapterId} not found");

        EnsureEditable(subchapter.Chapter.Course, instructorId, "You can only modify subchapters of your own courses");

        return subchapter;
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

    private async Task<SubchapterResponseDto> GetResponseAsync(Subchapter subchapter)
    {
        var blocksCount = await context.CourseBlocks.CountAsync(b => b.SubchapterId == subchapter.Id);
        return MapToSubchapterResponseDto(subchapter, blocksCount);
    }

    private static SubchapterResponseDto MapToSubchapterResponseDto(Subchapter subchapter, int blocksCount) => new()
    {
        Id = subchapter.Id,
        Title = subchapter.Title,
        OrderIndex = subchapter.OrderIndex,
        ChapterId = subchapter.ChapterId,
        BlocksCount = blocksCount
    };
}
