using CodeLearning.Application.Exceptions;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services.Learning;

// The single place that records block completion. Every path that completes a block (viewing, quiz
// submission, an accepted submission, re-enrolling) updates the same three things: the block's
// StudentBlockProgress row, the enrollment's CurrentBlockId (the next incomplete block) and LastActivityAt.
public static class ProgressWriter
{
    public sealed record BlockCompletion(
        Guid BlockId,
        DateTimeOffset CompletedAt,
        bool WasAlreadyCompleted,
        Guid? NextBlockId,
        bool CourseCompleted);

    public sealed record CourseProgressSnapshot(Guid? NextBlockId, int TotalBlocks, int CompletedBlocks)
    {
        public bool CourseCompleted => TotalBlocks > 0 && CompletedBlocks >= TotalBlocks;
    }

    // Stages the completion of one block for an enrolled student; the caller saves, so the completion
    // commits together with whatever produced it (e.g. the quiz attempt)
    public static async Task<BlockCompletion> MarkBlockCompletedAsync(
        ApplicationDbContext context,
        Guid userId,
        Guid courseId,
        Guid blockId,
        CancellationToken cancellationToken = default)
    {
        var enrollment = await context.StudentCourseProgresses
            .FirstOrDefaultAsync(p => p.CourseId == courseId && p.StudentId == userId, cancellationToken)
            ?? throw new ForbiddenException("You must be enrolled in this course to complete its blocks");

        var now = DateTimeOffset.UtcNow;
        var (progress, wasAlreadyCompleted) = await StageCompletionAsync(context, userId, blockId, now, cancellationToken);

        enrollment.LastActivityAt = now;
        var snapshot = await RefreshCurrentBlockAsync(context, enrollment, [blockId], cancellationToken);

        return new BlockCompletion(
            blockId,
            progress.CompletedAt ?? now,
            wasAlreadyCompleted,
            snapshot.NextBlockId,
            snapshot.CourseCompleted);
    }

    // Marks complete every Problem block that uses the problem, in every course the user is enrolled in,
    // and saves. Call it once a submission for the problem has been stored as Completed with score 100.
    // Usable from any process that has an ApplicationDbContext (the Runner calls it after grading).
    // Note: it saves the whole context, including changes the caller staged before.
    public static async Task<int> CompleteSolvedProblemBlocksAsync(
        ApplicationDbContext context,
        Guid userId,
        Guid problemId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await StageSolvedProblemBlocksAndSaveAsync(context, userId, problemId, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // A concurrent completion (the student clicking "complete" while the runner grades) inserted the
            // same progress row first: forget the rows staged here and redo the work against stored state
            foreach (var entry in context.ChangeTracker.Entries<StudentBlockProgress>()
                         .Where(e => e.State == EntityState.Added && e.Entity.StudentId == userId)
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }

            return await StageSolvedProblemBlocksAndSaveAsync(context, userId, problemId, cancellationToken);
        }
    }

    // Re-enrolling restores the work that outlives an enrollment: quizzes attempted (one attempt per quiz)
    // and problems solved (accepted submission, score 100). Stages only; the caller saves
    internal static async Task RestoreCompletedWorkAsync(
        ApplicationDbContext context,
        StudentCourseProgress enrollment,
        CancellationToken cancellationToken = default)
    {
        var studentId = enrollment.StudentId;
        var courseId = enrollment.CourseId;

        var attemptedQuizBlocks = await context.CourseBlocks
            .Where(b => b.Subchapter.Chapter.CourseId == courseId && b.Type == BlockType.Quiz)
            .SelectMany(b => b.Quiz!.StudentAttempts
                .Where(a => a.StudentId == studentId)
                .Select(a => new { BlockId = b.Id, CompletedAt = a.AttemptedAt }))
            .ToListAsync(cancellationToken);

        var solvedProblemBlocks = await context.CourseBlocks
            .Where(b => b.Subchapter.Chapter.CourseId == courseId && b.Type == BlockType.Problem)
            .Select(b => new
            {
                BlockId = b.Id,
                CompletedAt = context.Submissions
                    .Where(s => s.ProblemId == b.ProblemId
                                && s.StudentId == studentId
                                && s.Status == SubmissionStatus.Completed
                                && s.Score == 100)
                    .Min(s => (DateTimeOffset?)(s.CompletedAt ?? s.CreatedAt))
            })
            .Where(x => x.CompletedAt != null)
            .ToListAsync(cancellationToken);

        var restored = attemptedQuizBlocks
            .Select(a => (a.BlockId, CompletedAt: a.CompletedAt))
            .Concat(solvedProblemBlocks.Select(p => (p.BlockId, CompletedAt: p.CompletedAt!.Value)))
            .ToList();

        foreach (var (blockId, completedAt) in restored)
        {
            await StageCompletionAsync(context, studentId, blockId, completedAt, cancellationToken);
        }

        await RefreshCurrentBlockAsync(context, enrollment, restored.Select(r => r.BlockId).ToList(), cancellationToken);
    }

    // The next block in course order that the student has not completed, plus the counts behind it.
    // pendingCompletedBlockIds are completions staged but not saved yet
    public static async Task<CourseProgressSnapshot> GetProgressSnapshotAsync(
        ApplicationDbContext context,
        Guid userId,
        Guid courseId,
        IReadOnlyCollection<Guid>? pendingCompletedBlockIds = null,
        CancellationToken cancellationToken = default)
    {
        var orderedBlockIds = await context.CourseBlocks
            .Where(b => b.Subchapter.Chapter.CourseId == courseId)
            .OrderBy(b => b.Subchapter.Chapter.OrderIndex)
                .ThenBy(b => b.Subchapter.OrderIndex)
                .ThenBy(b => b.OrderIndex)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        var completed = (await context.StudentBlockProgresses
                .Where(bp => bp.StudentId == userId
                             && bp.IsCompleted
                             && bp.Block.Subchapter.Chapter.CourseId == courseId)
                .Select(bp => bp.BlockId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        if (pendingCompletedBlockIds is not null)
        {
            completed.UnionWith(pendingCompletedBlockIds);
        }

        Guid? nextBlockId = null;
        var completedCount = 0;

        foreach (var blockId in orderedBlockIds)
        {
            if (completed.Contains(blockId))
            {
                completedCount++;
            }
            else
            {
                nextBlockId ??= blockId;
            }
        }

        return new CourseProgressSnapshot(nextBlockId, orderedBlockIds.Count, completedCount);
    }

    private static async Task<int> StageSolvedProblemBlocksAndSaveAsync(
        ApplicationDbContext context,
        Guid userId,
        Guid problemId,
        CancellationToken cancellationToken)
    {
        var blocksByCourse = (await context.CourseBlocks
                .Where(b => b.ProblemId == problemId && b.Type == BlockType.Problem)
                .Where(b => context.StudentCourseProgresses.Any(p =>
                    p.StudentId == userId && p.CourseId == b.Subchapter.Chapter.CourseId))
                .Select(b => new { b.Id, b.Subchapter.Chapter.CourseId })
                .ToListAsync(cancellationToken))
            .GroupBy(b => b.CourseId, b => b.Id)
            .ToList();

        if (blocksByCourse.Count == 0)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        var newlyCompleted = 0;

        foreach (var course in blocksByCourse)
        {
            var enrollment = await context.StudentCourseProgresses
                .FirstAsync(p => p.CourseId == course.Key && p.StudentId == userId, cancellationToken);

            foreach (var blockId in course)
            {
                var (_, wasAlreadyCompleted) = await StageCompletionAsync(context, userId, blockId, now, cancellationToken);
                if (!wasAlreadyCompleted)
                {
                    newlyCompleted++;
                }
            }

            enrollment.LastActivityAt = now;
            await RefreshCurrentBlockAsync(context, enrollment, course.ToList(), cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        return newlyCompleted;
    }

    private static async Task<(StudentBlockProgress Progress, bool WasAlreadyCompleted)> StageCompletionAsync(
        ApplicationDbContext context,
        Guid userId,
        Guid blockId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var progress = context.StudentBlockProgresses.Local
                           .FirstOrDefault(bp => bp.StudentId == userId && bp.BlockId == blockId)
                       ?? await context.StudentBlockProgresses
                           .FirstOrDefaultAsync(bp => bp.StudentId == userId && bp.BlockId == blockId, cancellationToken);

        if (progress is null)
        {
            progress = new StudentBlockProgress
            {
                StudentId = userId,
                BlockId = blockId,
                IsCompleted = true,
                CompletedAt = completedAt,
                Student = null!,
                Block = null!
            };
            context.StudentBlockProgresses.Add(progress);
            return (progress, false);
        }

        if (progress.IsCompleted)
        {
            return (progress, true);
        }

        progress.IsCompleted = true;
        progress.CompletedAt = completedAt;
        return (progress, false);
    }

    private static async Task<CourseProgressSnapshot> RefreshCurrentBlockAsync(
        ApplicationDbContext context,
        StudentCourseProgress enrollment,
        IReadOnlyCollection<Guid> pendingCompletedBlockIds,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetProgressSnapshotAsync(
            context, enrollment.StudentId, enrollment.CourseId, pendingCompletedBlockIds, cancellationToken);

        enrollment.CurrentBlockId = snapshot.NextBlockId;
        return snapshot;
    }
}
