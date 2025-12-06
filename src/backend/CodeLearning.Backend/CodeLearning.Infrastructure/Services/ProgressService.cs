using CodeLearning.Application.DTOs.Progress;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class ProgressService(ApplicationDbContext context) : IProgressService
{
    public async Task<CompleteBlockResponseDto> CompleteBlockAsync(Guid blockId, Guid studentId)
    {
        var block = await context.CourseBlocks
            .Include(b => b.Subchapter)
                .ThenInclude(s => s.Chapter)
                    .ThenInclude(ch => ch.Course)
            .FirstOrDefaultAsync(b => b.Id == blockId)
            ?? throw new KeyNotFoundException($"Block with ID {blockId} not found");

        var courseId = block.Subchapter.Chapter.CourseId;

        var enrollment = await context.StudentCourseProgresses
            .FirstOrDefaultAsync(p => p.CourseId == courseId && p.StudentId == studentId)
            ?? throw new InvalidOperationException("You must be enrolled in this course to mark blocks as completed");

        var existingProgress = await context.StudentBlockProgresses
            .FirstOrDefaultAsync(bp => bp.BlockId == blockId && bp.StudentId == studentId);

        if (existingProgress?.IsCompleted == true)
        {
            var nextBlock = await GetNextBlockAsync(courseId, studentId);
            return new CompleteBlockResponseDto
            {
                BlockId = blockId,
                CompletedAt = existingProgress.CompletedAt ?? DateTimeOffset.UtcNow,
                NextBlockId = nextBlock,
                Message = "Block already completed",
                CourseCompleted = nextBlock == null
            };
        }

        if (existingProgress == null)
        {
            existingProgress = new StudentBlockProgress
            {
                StudentId = studentId,
                BlockId = blockId,
                IsCompleted = true,
                CompletedAt = DateTimeOffset.UtcNow,
                Student = null!,
                Block = null!
            };
            context.StudentBlockProgresses.Add(existingProgress);
        }
        else
        {
            existingProgress.IsCompleted = true;
            existingProgress.CompletedAt = DateTimeOffset.UtcNow;
        }

        enrollment.LastActivityAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();

        var nextBlockId = await GetNextBlockAsync(courseId, studentId);
        enrollment.CurrentBlockId = nextBlockId;
        await context.SaveChangesAsync();

        var (totalBlocks, completedBlocks) = await GetBlockCounts(courseId, studentId);
        var courseCompleted = totalBlocks > 0 && completedBlocks == totalBlocks;

        return new CompleteBlockResponseDto
        {
            BlockId = blockId,
            CompletedAt = existingProgress.CompletedAt ?? DateTimeOffset.UtcNow,
            NextBlockId = nextBlockId,
            Message = courseCompleted ? "Congratulations! You completed the course!" : "Block completed successfully",
            CourseCompleted = courseCompleted
        };
    }

    public async Task<CourseProgressDto> GetCourseProgressAsync(Guid courseId, Guid studentId)
    {
        var enrollment = await context.StudentCourseProgresses
            .Include(p => p.Course)
            .FirstOrDefaultAsync(p => p.CourseId == courseId && p.StudentId == studentId)
            ?? throw new InvalidOperationException("You are not enrolled in this course");

        var blocks = await context.CourseBlocks
            .Include(b => b.Subchapter)
                .ThenInclude(s => s.Chapter)
            .Where(b => b.Subchapter.Chapter.CourseId == courseId)
            .OrderBy(b => b.Subchapter.Chapter.OrderIndex)
                .ThenBy(b => b.Subchapter.OrderIndex)
                .ThenBy(b => b.OrderIndex)
            .ToListAsync();

        var blockProgress = await context.StudentBlockProgresses
            .Where(bp => bp.StudentId == studentId &&
                         bp.Block.Subchapter.Chapter.CourseId == courseId)
            .ToListAsync();

        var chapters = BuildChapterProgress(blocks, blockProgress);
        var (totalBlocks, completedCount) = CalculateProgress(blocks, blockProgress);
        var progressPercentage = totalBlocks > 0 ? (double)completedCount / totalBlocks * 100 : 0;

        return new CourseProgressDto
        {
            CourseId = courseId,
            CourseTitle = enrollment.Course.Title,
            EnrolledAt = enrollment.EnrolledAt,
            LastActivityAt = enrollment.LastActivityAt,
            CurrentBlockId = enrollment.CurrentBlockId,
            CompletedBlocksCount = completedCount,
            TotalBlocksCount = totalBlocks,
            ProgressPercentage = Math.Round(progressPercentage, 2),
            Chapters = chapters
        };
    }

    public async Task<Guid?> GetNextBlockAsync(Guid courseId, Guid studentId)
    {
        var blocks = await context.CourseBlocks
            .Include(b => b.Subchapter)
                .ThenInclude(s => s.Chapter)
            .Where(b => b.Subchapter.Chapter.CourseId == courseId)
            .OrderBy(b => b.Subchapter.Chapter.OrderIndex)
                .ThenBy(b => b.Subchapter.OrderIndex)
                .ThenBy(b => b.OrderIndex)
            .ToListAsync();

        var completedBlockIds = await context.StudentBlockProgresses
            .Where(bp => bp.StudentId == studentId &&
                         bp.IsCompleted &&
                         bp.Block.Subchapter.Chapter.CourseId == courseId)
            .Select(bp => bp.BlockId)
            .ToListAsync();

        return blocks.FirstOrDefault(b => !completedBlockIds.Contains(b.Id))?.Id;
    }

    private async Task<(int TotalBlocks, int CompletedBlocks)> GetBlockCounts(Guid courseId, Guid studentId)
    {
        var totalBlocks = await context.CourseBlocks
            .Where(b => b.Subchapter.Chapter.CourseId == courseId)
            .CountAsync();

        var completedBlocks = await context.StudentBlockProgresses
            .Where(bp => bp.StudentId == studentId &&
                         bp.IsCompleted &&
                         bp.Block.Subchapter.Chapter.CourseId == courseId)
            .CountAsync();

        return (totalBlocks, completedBlocks);
    }

    private static List<ChapterProgressDto> BuildChapterProgress(
        List<CourseBlock> blocks,
        List<StudentBlockProgress> blockProgress)
    {
        return blocks
            .GroupBy(b => b.Subchapter.Chapter)
            .Select(chGroup => new ChapterProgressDto
            {
                ChapterId = chGroup.Key.Id,
                Title = chGroup.Key.Title,
                OrderIndex = chGroup.Key.OrderIndex,
                Subchapters = chGroup
                    .GroupBy(b => b.Subchapter)
                    .Select(subGroup => new SubchapterProgressDto
                    {
                        SubchapterId = subGroup.Key.Id,
                        Title = subGroup.Key.Title,
                        OrderIndex = subGroup.Key.OrderIndex,
                        Blocks = subGroup.Select(b =>
                        {
                            var progress = blockProgress.FirstOrDefault(bp => bp.BlockId == b.Id);
                            return new BlockProgressDto
                            {
                                BlockId = b.Id,
                                Title = b.Title,
                                Type = b.Type.ToString(),
                                OrderIndex = b.OrderIndex,
                                IsCompleted = progress?.IsCompleted ?? false,
                                CompletedAt = progress?.CompletedAt
                            };
                        }).ToList()
                    })
                    .OrderBy(s => s.OrderIndex)
                    .ToList()
            })
            .OrderBy(ch => ch.OrderIndex)
            .ToList();
    }

    private static (int TotalBlocks, int CompletedCount) CalculateProgress(
        List<CourseBlock> blocks,
        List<StudentBlockProgress> blockProgress)
    {
        return (blocks.Count, blockProgress.Count(bp => bp.IsCompleted));
    }
}
