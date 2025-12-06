using CodeLearning.Application.DTOs.Subchapter;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class SubchapterService(ApplicationDbContext context) : ISubchapterService
{
    public async Task<SubchapterResponseDto> AddSubchapterAsync(Guid chapterId, CreateSubchapterDto dto, Guid instructorId)
    {
        var chapter = await context.Chapters
            .Include(c => c.Course)
            .Include(c => c.Subchapters)
            .FirstOrDefaultAsync(c => c.Id == chapterId)
            ?? throw new KeyNotFoundException($"Chapter with ID {chapterId} not found");

        if (chapter.Course.InstructorId != instructorId)
        {
            throw new UnauthorizedAccessException("You can only add subchapters to your own courses");
        }

        if (chapter.Course.Status == CourseStatus.Published)
        {
            throw new InvalidOperationException("Cannot modify a published course");
        }

        var maxOrder = chapter.Subchapters.Count > 0 ? chapter.Subchapters.Max(s => s.OrderIndex) : 0;

        var subchapter = new Subchapter
        {
            Title = dto.Title,
            OrderIndex = maxOrder + 1,
            ChapterId = chapterId,
            Chapter = chapter
        };

        context.Subchapters.Add(subchapter);
        await context.SaveChangesAsync();

        return MapToSubchapterResponseDto(subchapter);
    }

    public async Task<IEnumerable<SubchapterResponseDto>> GetChapterSubchaptersAsync(Guid chapterId)
    {
        var subchapters = await context.Subchapters
            .Include(s => s.Blocks)
            .Where(s => s.ChapterId == chapterId)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();

        return subchapters.Select(MapToSubchapterResponseDto);
    }

    public async Task<SubchapterResponseDto> UpdateSubchapterOrderAsync(Guid subchapterId, int newOrderIndex, Guid instructorId)
    {
        var subchapter = await context.Subchapters
            .Include(s => s.Chapter)
                .ThenInclude(c => c.Course)
            .Include(s => s.Blocks)
            .FirstOrDefaultAsync(s => s.Id == subchapterId)
            ?? throw new KeyNotFoundException($"Subchapter with ID {subchapterId} not found");

        if (subchapter.Chapter.Course.InstructorId != instructorId)
        {
            throw new UnauthorizedAccessException("You can only modify your own course subchapters");
        }

        if (subchapter.Chapter.Course.Status == CourseStatus.Published)
        {
            throw new InvalidOperationException("Cannot modify a published course");
        }

        await ReorderSubchapters(subchapter, newOrderIndex);
        await context.SaveChangesAsync();

        return MapToSubchapterResponseDto(subchapter);
    }

    public async Task DeleteSubchapterAsync(Guid subchapterId, Guid instructorId)
    {
        var subchapter = await context.Subchapters
            .Include(s => s.Chapter)
                .ThenInclude(c => c.Course)
            .FirstOrDefaultAsync(s => s.Id == subchapterId)
            ?? throw new KeyNotFoundException($"Subchapter with ID {subchapterId} not found");

        if (subchapter.Chapter.Course.InstructorId != instructorId)
        {
            throw new UnauthorizedAccessException("You can only delete subchapters from your own courses");
        }

        if (subchapter.Chapter.Course.Status == CourseStatus.Published)
        {
            throw new InvalidOperationException("Cannot delete subchapters from a published course");
        }

        context.Subchapters.Remove(subchapter);

        await ReorderAfterDeletion(subchapter);
        await context.SaveChangesAsync();
    }

    private async Task ReorderSubchapters(Subchapter subchapter, int newOrderIndex)
    {
        var oldOrderIndex = subchapter.OrderIndex;
        subchapter.OrderIndex = newOrderIndex;

        var otherSubchapters = await context.Subchapters
            .Where(s => s.ChapterId == subchapter.ChapterId && s.Id != subchapter.Id)
            .ToListAsync();

        foreach (var other in otherSubchapters)
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

    private async Task ReorderAfterDeletion(Subchapter deletedSubchapter)
    {
        var remainingSubchapters = await context.Subchapters
            .Where(s => s.ChapterId == deletedSubchapter.ChapterId && s.OrderIndex > deletedSubchapter.OrderIndex)
            .ToListAsync();

        foreach (var subchapter in remainingSubchapters)
        {
            subchapter.OrderIndex--;
        }
    }

    private static SubchapterResponseDto MapToSubchapterResponseDto(Subchapter subchapter) => new()
    {
        Id = subchapter.Id,
        Title = subchapter.Title,
        OrderIndex = subchapter.OrderIndex,
        ChapterId = subchapter.ChapterId,
        BlocksCount = subchapter.Blocks?.Count ?? 0
    };
}
