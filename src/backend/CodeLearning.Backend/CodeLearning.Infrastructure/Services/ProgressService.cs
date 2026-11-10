using CodeLearning.Application.DTOs.Progress;
using CodeLearning.Application.Exceptions;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Infrastructure.Extensions;
using CodeLearning.Infrastructure.Services.Learning;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class ProgressService(ApplicationDbContext context) : IProgressService
{
    public async Task<CompleteBlockResponseDto> CompleteBlockAsync(Guid blockId, Guid studentId)
    {
        var block = await context.CourseBlocks
            .Include(b => b.Subchapter.Chapter.Course)
            .FirstOrDefaultAsync(b => b.Id == blockId)
            ?? throw new NotFoundException($"Block with ID {blockId} not found");

        var course = block.Subchapter.Chapter.Course;
        await EnsureEnrolledAsync(course.Id, studentId, "You must be enrolled in this course to mark blocks as completed");

        if (course.Status != CourseStatus.Published)
        {
            throw new ForbiddenException("This course is not published at the moment");
        }

        var isAlreadyCompleted = await context.StudentBlockProgresses
            .AnyAsync(bp => bp.BlockId == blockId && bp.StudentId == studentId && bp.IsCompleted);

        if (!isAlreadyCompleted)
        {
            await EnsureBlockRequirementsMetAsync(block, studentId);
        }

        var completion = await ProgressWriter.MarkBlockCompletedAsync(context, studentId, course.Id, blockId);

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // A concurrent request (e.g. a double click) completed the block first: answer as for a
            // repeated completion, which now finds the stored progress
            context.ChangeTracker.Clear();
            return await CompleteBlockAsync(blockId, studentId);
        }

        var message = completion switch
        {
            { WasAlreadyCompleted: true } => "Block already completed",
            { CourseCompleted: true } => "Congratulations! You completed the course!",
            _ => "Block completed successfully"
        };

        return new CompleteBlockResponseDto
        {
            BlockId = blockId,
            CompletedAt = completion.CompletedAt,
            NextBlockId = completion.NextBlockId,
            Message = message,
            CourseCompleted = completion.CourseCompleted
        };
    }

    public async Task<CourseProgressDto> GetCourseProgressAsync(Guid courseId, Guid studentId)
    {
        var enrollment = await context.StudentCourseProgresses
            .AsNoTracking()
            .Include(p => p.Course)
            .FirstOrDefaultAsync(p => p.CourseId == courseId && p.StudentId == studentId)
            ?? throw new ForbiddenException("You are not enrolled in this course");

        var blocks = await context.CourseBlocks
            .AsNoTracking()
            .Include(b => b.Subchapter.Chapter)
            .Where(b => b.Subchapter.Chapter.CourseId == courseId)
            .OrderBy(b => b.Subchapter.Chapter.OrderIndex)
                .ThenBy(b => b.Subchapter.OrderIndex)
                .ThenBy(b => b.OrderIndex)
            .ToListAsync();

        var blockProgress = await context.StudentBlockProgresses
            .AsNoTracking()
            .Where(bp => bp.StudentId == studentId &&
                         bp.Block.Subchapter.Chapter.CourseId == courseId)
            .ToDictionaryAsync(bp => bp.BlockId);

        var completedCount = blocks.Count(b => blockProgress.TryGetValue(b.Id, out var p) && p.IsCompleted);
        var progressPercentage = blocks.Count > 0 ? (double)completedCount / blocks.Count * 100 : 0;

        return new CourseProgressDto
        {
            CourseId = courseId,
            CourseTitle = enrollment.Course.Title,
            EnrolledAt = enrollment.EnrolledAt,
            LastActivityAt = enrollment.LastActivityAt,
            CurrentBlockId = enrollment.CurrentBlockId,
            CompletedBlocksCount = completedCount,
            TotalBlocksCount = blocks.Count,
            ProgressPercentage = Math.Round(progressPercentage, 2),
            Chapters = BuildChapterProgress(blocks, blockProgress)
        };
    }

    public async Task<Guid?> GetNextBlockAsync(Guid courseId, Guid studentId)
    {
        await EnsureEnrolledAsync(courseId, studentId, "You are not enrolled in this course");

        var snapshot = await ProgressWriter.GetProgressSnapshotAsync(context, studentId, courseId);
        return snapshot.NextBlockId;
    }

    private async Task EnsureEnrolledAsync(Guid courseId, Guid studentId, string message)
    {
        var isEnrolled = await context.StudentCourseProgresses
            .AnyAsync(p => p.CourseId == courseId && p.StudentId == studentId);

        if (!isEnrolled)
        {
            throw new ForbiddenException(message);
        }
    }

    // Theory and video blocks complete on viewing; quiz and problem blocks need evidence of the work
    private async Task EnsureBlockRequirementsMetAsync(CourseBlock block, Guid studentId)
    {
        switch (block.Type)
        {
            case BlockType.Quiz:
                var hasQuizAttempt = await context.StudentQuizAttempts
                    .AnyAsync(a => a.QuizId == block.QuizId && a.StudentId == studentId);

                if (!hasQuizAttempt)
                {
                    throw new BusinessRuleException("Submit the quiz to complete this block");
                }
                break;

            case BlockType.Problem:
                var hasAcceptedSubmission = await context.Submissions
                    .AnyAsync(s => s.ProblemId == block.ProblemId &&
                                   s.StudentId == studentId &&
                                   s.Status == SubmissionStatus.Completed &&
                                   s.Score == 100);

                if (!hasAcceptedSubmission)
                {
                    throw new BusinessRuleException("Pass all test cases of the problem to complete this block");
                }
                break;
        }
    }

    private static List<ChapterProgressDto> BuildChapterProgress(
        List<CourseBlock> blocks,
        Dictionary<Guid, StudentBlockProgress> blockProgress)
    {
        return blocks
            .GroupBy(b => b.Subchapter.Chapter.Id)
            .Select(chGroup =>
            {
                var chapter = chGroup.First().Subchapter.Chapter;
                return new ChapterProgressDto
                {
                    ChapterId = chapter.Id,
                    Title = chapter.Title,
                    OrderIndex = chapter.OrderIndex,
                    Subchapters = chGroup
                        .GroupBy(b => b.SubchapterId)
                        .Select(subGroup =>
                        {
                            var subchapter = subGroup.First().Subchapter;
                            return new SubchapterProgressDto
                            {
                                SubchapterId = subchapter.Id,
                                Title = subchapter.Title,
                                OrderIndex = subchapter.OrderIndex,
                                Blocks = subGroup.Select(b =>
                                {
                                    blockProgress.TryGetValue(b.Id, out var progress);
                                    return new BlockProgressDto
                                    {
                                        BlockId = b.Id,
                                        Title = b.Title,
                                        Type = b.Type,
                                        OrderIndex = b.OrderIndex,
                                        IsCompleted = progress?.IsCompleted ?? false,
                                        CompletedAt = progress?.CompletedAt
                                    };
                                }).ToList()
                            };
                        })
                        .OrderBy(s => s.OrderIndex)
                        .ToList()
                };
            })
            .OrderBy(ch => ch.OrderIndex)
            .ToList();
    }
}
