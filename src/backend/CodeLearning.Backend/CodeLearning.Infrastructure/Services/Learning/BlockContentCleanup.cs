using CodeLearning.Core.Entities;
using CodeLearning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace CodeLearning.Infrastructure.Services.Learning;

// A block holds the foreign keys to its theory, video and quiz rows, so deleting a block (or the course,
// chapter or subchapter above it) never reaches that content. Every delete path goes through here so no
// content row is left behind. Questions and answers cascade from the quiz.
internal static class BlockContentCleanup
{
    // Deletes the matched blocks, their content, the quiz attempts that would block it and the enrollment
    // bookmarks that point at it. Saves, so call it inside a CourseEditScope: the caller's later changes and
    // the commit complete the operation, and a failure rolls all of it back
    public static async Task RemoveForBlocksAsync(ApplicationDbContext context, Expression<Func<CourseBlock, bool>> blockFilter)
    {
        var blocks = await context.CourseBlocks
            .Include(b => b.TheoryContent)
            .Include(b => b.VideoContent)
            .Include(b => b.Quiz)
            .Where(blockFilter)
            .ToListAsync();

        if (blocks.Count == 0)
        {
            return;
        }

        var blockIds = blocks.Select(b => b.Id).ToList();
        var quizIds = blocks.Where(b => b.QuizId != null).Select(b => b.QuizId!.Value).ToList();

        // A course can be unpublished and edited after students attempted its quizzes; the attempt rows
        // restrict deleting the quiz, so they go with it
        if (quizIds.Count > 0)
        {
            await context.StudentQuizAttempts
                .Where(a => quizIds.Contains(a.QuizId))
                .ExecuteDeleteAsync();
        }

        // A bookmark on a removed block falls back to "continue from the first incomplete block"
        await context.StudentCourseProgresses
            .Where(p => p.CurrentBlockId != null && blockIds.Contains(p.CurrentBlockId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CurrentBlockId, (Guid?)null));

        // Blocks are removed explicitly as well, so EF deletes each block before the content it references
        context.CourseBlocks.RemoveRange(blocks);

        foreach (var block in blocks)
        {
            if (block.TheoryContent != null) context.TheoryContents.Remove(block.TheoryContent);
            if (block.VideoContent != null) context.VideoContents.Remove(block.VideoContent);
            if (block.Quiz != null) context.Quizzes.Remove(block.Quiz);
        }

        // Flushed now, inside the caller's transaction: if the caller then deletes a chapter or course, the
        // database cascade would otherwise remove these blocks first and EF's own deletes would find no rows
        await context.SaveChangesAsync();
    }
}
