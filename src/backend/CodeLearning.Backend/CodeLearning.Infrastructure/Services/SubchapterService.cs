using CodeLearning.Application.DTOs.Subchapter;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class SubchapterService : ISubchapterService
{
    private readonly ApplicationDbContext _context;

    public SubchapterService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<SubchapterResponseDto> AddSubchapterAsync(Guid chapterId, CreateSubchapterDto dto, Guid instructorId)
    {
        var chapter = await _context.Chapters
            .Include(c => c.Course)
            .Include(c => c.Subchapters)
            .FirstOrDefaultAsync(c => c.Id == chapterId);

        if (chapter == null)
            throw new KeyNotFoundException($"Chapter with ID {chapterId} not found");

        if (chapter.Course.InstructorId != instructorId)
            throw new UnauthorizedAccessException("You can only add subchapters to your own courses");

        if (chapter.Course.Status == CourseStatus.Published)
            throw new InvalidOperationException("Cannot modify a published course");

        var maxOrder = chapter.Subchapters.Any() ? chapter.Subchapters.Max(s => s.OrderIndex) : 0;

        var subchapter = new Subchapter
        {
            Title = dto.Title,
            OrderIndex = maxOrder + 1,
            ChapterId = chapterId,
            Chapter = chapter
        };

        _context.Subchapters.Add(subchapter);
        await _context.SaveChangesAsync();

        return MapToSubchapterResponseDto(subchapter);
    }

    public async Task<IEnumerable<SubchapterResponseDto>> GetChapterSubchaptersAsync(Guid chapterId)
    {
        var subchapters = await _context.Subchapters
            .Include(s => s.Blocks)
            .Where(s => s.ChapterId == chapterId)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();

        return subchapters.Select(MapToSubchapterResponseDto);
    }

    public async Task<SubchapterResponseDto> UpdateSubchapterOrderAsync(Guid subchapterId, int newOrderIndex, Guid instructorId)
    {
        var subchapter = await _context.Subchapters
            .Include(s => s.Chapter)
                .ThenInclude(c => c.Course)
            .Include(s => s.Blocks)
            .FirstOrDefaultAsync(s => s.Id == subchapterId);

        if (subchapter == null)
            throw new KeyNotFoundException($"Subchapter with ID {subchapterId} not found");

        if (subchapter.Chapter.Course.InstructorId != instructorId)
            throw new UnauthorizedAccessException("You can only modify your own course subchapters");

        if (subchapter.Chapter.Course.Status == CourseStatus.Published)
            throw new InvalidOperationException("Cannot modify a published course");

        var oldOrderIndex = subchapter.OrderIndex;
        subchapter.OrderIndex = newOrderIndex;

        // Reorder other subchapters
        var otherSubchapters = await _context.Subchapters
            .Where(s => s.ChapterId == subchapter.ChapterId && s.Id != subchapterId)
            .ToListAsync();

        foreach (var other in otherSubchapters)
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

        return MapToSubchapterResponseDto(subchapter);
    }

    public async Task DeleteSubchapterAsync(Guid subchapterId, Guid instructorId)
    {
        var subchapter = await _context.Subchapters
            .Include(s => s.Chapter)
                .ThenInclude(c => c.Course)
            .FirstOrDefaultAsync(s => s.Id == subchapterId);

        if (subchapter == null)
            throw new KeyNotFoundException($"Subchapter with ID {subchapterId} not found");

        if (subchapter.Chapter.Course.InstructorId != instructorId)
            throw new UnauthorizedAccessException("You can only delete subchapters from your own courses");

        if (subchapter.Chapter.Course.Status == CourseStatus.Published)
            throw new InvalidOperationException("Cannot delete subchapters from a published course");

        _context.Subchapters.Remove(subchapter);

        // Reorder remaining subchapters
        var remainingSubchapters = await _context.Subchapters
            .Where(s => s.ChapterId == subchapter.ChapterId && s.OrderIndex > subchapter.OrderIndex)
            .ToListAsync();

        foreach (var remaining in remainingSubchapters)
        {
            remaining.OrderIndex--;
        }

        await _context.SaveChangesAsync();
    }

    private SubchapterResponseDto MapToSubchapterResponseDto(Subchapter subchapter)
    {
        return new SubchapterResponseDto
        {
            Id = subchapter.Id,
            Title = subchapter.Title,
            OrderIndex = subchapter.OrderIndex,
            ChapterId = subchapter.ChapterId,
            BlocksCount = subchapter.Blocks?.Count ?? 0
        };
    }
}
