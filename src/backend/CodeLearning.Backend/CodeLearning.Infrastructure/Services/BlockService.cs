using CodeLearning.Application.DTOs.Block;
using CodeLearning.Application.Exceptions;
using CodeLearning.Application.Extensions;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Infrastructure.Services.Learning;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

// Text is stored as written; the frontend sanitizes rendered markdown (DOMPurify) and escapes plain text.
// Every mutation runs in a CourseEditScope (course row locked) and saves once
public class BlockService(ApplicationDbContext context) : IBlockService
{
    public async Task<Guid> CreateTheoryBlockAsync(Guid subchapterId, CreateTheoryBlockDto dto, Guid instructorId)
    {
        return await CreateBlockAsync(subchapterId, instructorId, BlockType.Theory, dto.Title, block =>
        {
            block.TheoryContent = new TheoryContent { Content = dto.Content, Block = null! };
            return Task.CompletedTask;
        });
    }

    public async Task<Guid> CreateVideoBlockAsync(Guid subchapterId, CreateVideoBlockDto dto, Guid instructorId)
    {
        var videoId = RequireVideoId(dto.VideoUrl);

        return await CreateBlockAsync(subchapterId, instructorId, BlockType.Video, dto.Title, block =>
        {
            block.VideoContent = new VideoContent
            {
                VideoUrl = videoId.ToYouTubeWatchUrl(),
                VideoId = videoId,
                Block = null!
            };
            return Task.CompletedTask;
        });
    }

    public async Task<Guid> CreateQuizBlockAsync(Guid subchapterId, CreateQuizBlockDto dto, Guid instructorId)
    {
        return await CreateBlockAsync(subchapterId, instructorId, BlockType.Quiz, dto.Title, block =>
        {
            var quiz = new Quiz { Block = null! };
            block.Quiz = quiz;
            context.QuizQuestions.AddRange(BuildQuizQuestions(dto.Questions, quiz));
            return Task.CompletedTask;
        });
    }

    public async Task<Guid> CreateProblemBlockAsync(Guid subchapterId, CreateProblemBlockDto dto, Guid instructorId)
    {
        return await CreateBlockAsync(subchapterId, instructorId, BlockType.Problem, dto.Title, async block =>
        {
            await EnsureProblemAttachableAsync(dto.ProblemId, instructorId);
            block.ProblemId = dto.ProblemId;
        });
    }

    public async Task DeleteBlockAsync(Guid subchapterId, Guid blockId, Guid instructorId)
    {
        await using var scope = await BeginEditAsync(subchapterId);

        var block = await LoadEditableBlockAsync(subchapterId, blockId, instructorId);
        var deletedOrderIndex = block.OrderIndex;

        await BlockContentCleanup.RemoveForBlocksAsync(context, b => b.Id == blockId);

        var following = await context.CourseBlocks
            .Where(b => b.SubchapterId == subchapterId && b.OrderIndex > deletedOrderIndex)
            .ToListAsync();

        foreach (var sibling in following)
        {
            sibling.OrderIndex--;
        }

        await context.SaveChangesAsync();
        await scope.CommitAsync();
    }

    public async Task<BlockResponseDto> GetBlockByIdAsync(Guid blockId, Guid viewerId)
    {
        var subchapterId = await context.CourseBlocks
            .Where(b => b.Id == blockId)
            .Select(b => (Guid?)b.SubchapterId)
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException($"Block with ID {blockId} not found");

        var isInstructor = await AuthorizeContentAccessAsync(subchapterId, viewerId, $"Block with ID {blockId} not found");

        var block = await BlocksWithContent()
            .FirstAsync(b => b.Id == blockId);

        return block.ToResponseDto(includeCorrectAnswers: isInstructor);
    }

    public async Task<IEnumerable<BlockResponseDto>> GetSubchapterBlocksAsync(Guid subchapterId, Guid viewerId)
    {
        var isInstructor = await AuthorizeContentAccessAsync(subchapterId, viewerId, $"Subchapter with ID {subchapterId} not found");

        var blocks = await BlocksWithContent()
            .Where(b => b.SubchapterId == subchapterId)
            .OrderBy(b => b.OrderIndex)
            .ToListAsync();

        return blocks.ToResponseDtos(includeCorrectAnswers: isInstructor);
    }

    public async Task UpdateTheoryBlockAsync(Guid subchapterId, Guid blockId, UpdateTheoryBlockDto dto, Guid instructorId)
    {
        await UpdateBlockAsync(subchapterId, blockId, instructorId, BlockType.Theory, block =>
        {
            block.Title = dto.Title;

            if (block.TheoryContent != null)
            {
                block.TheoryContent.Content = dto.Content;
            }

            return Task.CompletedTask;
        });
    }

    public async Task UpdateVideoBlockAsync(Guid subchapterId, Guid blockId, UpdateVideoBlockDto dto, Guid instructorId)
    {
        var videoId = RequireVideoId(dto.VideoUrl);

        await UpdateBlockAsync(subchapterId, blockId, instructorId, BlockType.Video, block =>
        {
            block.Title = dto.Title;

            if (block.VideoContent != null)
            {
                block.VideoContent.VideoUrl = videoId.ToYouTubeWatchUrl();
                block.VideoContent.VideoId = videoId;
            }

            return Task.CompletedTask;
        });
    }

    // Questions are replaced, not merged: the old ones (answers cascade) are removed and the new ones added
    // in the same SaveChanges, so a failure leaves the quiz as it was
    public async Task UpdateQuizBlockAsync(Guid subchapterId, Guid blockId, UpdateQuizBlockDto dto, Guid instructorId)
    {
        await UpdateBlockAsync(subchapterId, blockId, instructorId, BlockType.Quiz, async block =>
        {
            block.Title = dto.Title;

            var quiz = block.Quiz ?? throw new BusinessRuleException("Quiz block has no quiz");

            var oldQuestions = await context.QuizQuestions
                .Include(q => q.Answers)
                .Where(q => q.QuizId == quiz.Id)
                .ToListAsync();

            context.QuizAnswers.RemoveRange(oldQuestions.SelectMany(q => q.Answers));
            context.QuizQuestions.RemoveRange(oldQuestions);
            context.QuizQuestions.AddRange(BuildQuizQuestions(dto.Questions, quiz));
        });
    }

    public async Task UpdateProblemBlockAsync(Guid subchapterId, Guid blockId, UpdateProblemBlockDto dto, Guid instructorId)
    {
        await UpdateBlockAsync(subchapterId, blockId, instructorId, BlockType.Problem, async block =>
        {
            if (block.ProblemId != dto.ProblemId)
            {
                await EnsureProblemAttachableAsync(dto.ProblemId, instructorId);
                block.ProblemId = dto.ProblemId;
            }

            block.Title = dto.Title;
        });
    }

    public async Task UpdateBlockOrderAsync(Guid subchapterId, Guid blockId, int newOrderIndex, Guid instructorId)
    {
        await using var scope = await BeginEditAsync(subchapterId);

        var block = await LoadEditableBlockAsync(subchapterId, blockId, instructorId);

        var siblings = await context.CourseBlocks
            .Where(b => b.SubchapterId == subchapterId && b.Id != blockId)
            .ToListAsync();

        OrderIndexRules.EnsureWithinRange(newOrderIndex, siblings.Count + 1);
        OrderIndexRules.Move(block, siblings, b => b.OrderIndex, (b, index) => b.OrderIndex = index, newOrderIndex);

        await context.SaveChangesAsync();
        await scope.CommitAsync();
    }

    private async Task<Guid> CreateBlockAsync(
        Guid subchapterId,
        Guid instructorId,
        BlockType type,
        string title,
        Func<CourseBlock, Task> attachContent)
    {
        await using var scope = await BeginEditAsync(subchapterId);

        var subchapter = await context.Subchapters
            .Include(s => s.Chapter.Course)
            .FirstAsync(s => s.Id == subchapterId);

        EnsureEditable(subchapter.Chapter.Course, instructorId);

        // Read under the course lock, so two concurrent creates cannot both take the same index
        var maxOrder = await context.CourseBlocks
            .Where(b => b.SubchapterId == subchapterId)
            .MaxAsync(b => (int?)b.OrderIndex) ?? 0;

        var block = new CourseBlock
        {
            Title = title,
            Type = type,
            OrderIndex = maxOrder + 1,
            SubchapterId = subchapterId,
            Subchapter = subchapter
        };

        await attachContent(block);

        context.CourseBlocks.Add(block);
        await context.SaveChangesAsync();
        await scope.CommitAsync();

        return block.Id;
    }

    private async Task UpdateBlockAsync(
        Guid subchapterId,
        Guid blockId,
        Guid instructorId,
        BlockType expectedType,
        Func<CourseBlock, Task> apply)
    {
        await using var scope = await BeginEditAsync(subchapterId);

        var block = await LoadEditableBlockAsync(subchapterId, blockId, instructorId);

        if (block.Type != expectedType)
        {
            throw new BusinessRuleException($"Block is not a {expectedType.ToString().ToLowerInvariant()} block");
        }

        await apply(block);

        await context.SaveChangesAsync();
        await scope.CommitAsync();
    }

    private async Task<CourseEditScope> BeginEditAsync(Guid subchapterId)
    {
        var courseId = await context.Subchapters
            .Where(s => s.Id == subchapterId)
            .Select(s => (Guid?)s.Chapter.CourseId)
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException($"Subchapter with ID {subchapterId} not found");

        return await CourseEditScope.BeginAsync(context, courseId);
    }

    // A block id under another subchapter's route is answered as not found
    private async Task<CourseBlock> LoadEditableBlockAsync(Guid subchapterId, Guid blockId, Guid instructorId)
    {
        var block = await context.CourseBlocks
            .Include(b => b.TheoryContent)
            .Include(b => b.VideoContent)
            .Include(b => b.Quiz)
            .Include(b => b.Subchapter.Chapter.Course)
            .FirstOrDefaultAsync(b => b.Id == blockId && b.SubchapterId == subchapterId)
            ?? throw new NotFoundException($"Block with ID {blockId} not found");

        EnsureEditable(block.Subchapter.Chapter.Course, instructorId);

        return block;
    }

    private static void EnsureEditable(Course course, Guid instructorId)
    {
        if (course.InstructorId != instructorId)
        {
            throw new ForbiddenException("You can only modify blocks in your own courses");
        }

        if (course.Status == CourseStatus.Published)
        {
            throw new BusinessRuleException("Cannot modify blocks in a published course");
        }
    }

    // Teachers attach only problems they authored: the course must not depend on a problem someone else
    // can change or lock
    private async Task EnsureProblemAttachableAsync(Guid problemId, Guid instructorId)
    {
        var authorId = await context.Problems
            .Where(p => p.Id == problemId)
            .Select(p => (Guid?)p.AuthorId)
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException($"Problem with ID {problemId} not found");

        if (authorId != instructorId)
        {
            throw new ForbiddenException("You can only attach problems you authored");
        }
    }

    // Returns whether the viewer is the course instructor (who also sees quiz answers). Students need an
    // enrollment in a published course; a draft course stays hidden (404) from everyone else
    private async Task<bool> AuthorizeContentAccessAsync(Guid subchapterId, Guid viewerId, string notFoundMessage)
    {
        var course = await context.Subchapters
            .Where(s => s.Id == subchapterId)
            .Select(s => new { s.Chapter.CourseId, s.Chapter.Course.InstructorId, s.Chapter.Course.Status })
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException(notFoundMessage);

        if (course.InstructorId == viewerId)
        {
            return true;
        }

        var isEnrolled = await context.StudentCourseProgresses
            .AnyAsync(p => p.CourseId == course.CourseId && p.StudentId == viewerId);

        return (course.Status, isEnrolled) switch
        {
            (CourseStatus.Published, true) => false,
            (CourseStatus.Published, false) => throw new ForbiddenException("Enroll in this course to view its content"),
            (_, true) => throw new ForbiddenException("This course is not published at the moment"),
            _ => throw new NotFoundException(notFoundMessage)
        };
    }

    private static string RequireVideoId(string videoUrl) =>
        videoUrl.ExtractYouTubeVideoId() ?? throw new BusinessRuleException("Invalid YouTube URL");

    // Shared by create and update so both persist every question field
    private static List<QuizQuestion> BuildQuizQuestions(IEnumerable<CreateQuizQuestionDto> questions, Quiz quiz) =>
        questions.Select((q, qIndex) => new QuizQuestion
        {
            Content = q.QuestionText,
            Type = Enum.Parse<QuestionType>(q.Type),
            Points = q.Points,
            Explanation = string.IsNullOrWhiteSpace(q.Explanation)
                ? null
                : q.Explanation,
            OrderIndex = qIndex + 1,
            Quiz = quiz,
            Answers = q.Answers.Select((a, aIndex) => new QuizAnswer
            {
                Text = a.AnswerText,
                IsCorrect = a.IsCorrect,
                OrderIndex = aIndex + 1,
                Question = null!
            }).ToList()
        }).ToList();

    private IQueryable<CourseBlock> BlocksWithContent() =>
        context.CourseBlocks
            .AsNoTracking()
            .Include(b => b.TheoryContent)
            .Include(b => b.VideoContent)
            .Include(b => b.Quiz!.Questions.OrderBy(qq => qq.OrderIndex))
                .ThenInclude(qq => qq.Answers.OrderBy(a => a.OrderIndex))
            .Include(b => b.Problem)
            .AsSplitQuery();
}
