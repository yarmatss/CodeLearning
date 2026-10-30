using CodeLearning.Application.DTOs.Block;
using CodeLearning.Application.Exceptions;
using CodeLearning.Application.Extensions;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class BlockService(
    ApplicationDbContext context,
    ISanitizationService sanitizationService) : IBlockService
{
    public async Task<Guid> CreateTheoryBlockAsync(Guid subchapterId, CreateTheoryBlockDto dto, Guid instructorId)
    {
        await VerifyOwnershipAsync(subchapterId, instructorId);
        var nextOrderIndex = await GetNextOrderIndexAsync(subchapterId);

        var sanitizedContent = sanitizationService.SanitizeMarkdown(dto.Content);
        var theoryContent = new TheoryContent
        {
            Content = sanitizedContent,
            Block = null!
        };

        var block = new CourseBlock
        {
            Title = dto.Title,
            Type = BlockType.Theory,
            OrderIndex = nextOrderIndex,
            SubchapterId = subchapterId,
            Subchapter = null!,
            TheoryContent = theoryContent
        };

        context.CourseBlocks.Add(block);
        await context.SaveChangesAsync();

        return block.Id;
    }

    public async Task<Guid> CreateVideoBlockAsync(Guid subchapterId, CreateVideoBlockDto dto, Guid instructorId)
    {
        await VerifyOwnershipAsync(subchapterId, instructorId);

        var videoId = dto.VideoUrl.ExtractYouTubeVideoId();
        if (string.IsNullOrEmpty(videoId))
        {
            throw new BusinessRuleException("Invalid YouTube URL");
        }

        var nextOrderIndex = await GetNextOrderIndexAsync(subchapterId);
        var videoContent = new VideoContent
        {
            VideoUrl = dto.VideoUrl,
            VideoId = videoId,
            Block = null!
        };

        var block = new CourseBlock
        {
            Title = dto.Title,
            Type = BlockType.Video,
            OrderIndex = nextOrderIndex,
            SubchapterId = subchapterId,
            Subchapter = null!,
            VideoContent = videoContent
        };

        context.CourseBlocks.Add(block);
        await context.SaveChangesAsync();

        return block.Id;
    }

    public async Task<Guid> CreateQuizBlockAsync(Guid subchapterId, CreateQuizBlockDto dto, Guid instructorId)
    {
        await VerifyOwnershipAsync(subchapterId, instructorId);

        var nextOrderIndex = await GetNextOrderIndexAsync(subchapterId);
        var quiz = new Quiz
        {
            Block = null!
        };

        var block = new CourseBlock
        {
            Title = dto.Title,
            Type = BlockType.Quiz,
            OrderIndex = nextOrderIndex,
            SubchapterId = subchapterId,
            Subchapter = null!,
            Quiz = quiz
        };

        context.CourseBlocks.Add(block);
        context.QuizQuestions.AddRange(BuildQuizQuestions(dto.Questions, quiz));
        await context.SaveChangesAsync();

        return block.Id;
    }

    public async Task<Guid> CreateProblemBlockAsync(Guid subchapterId, CreateProblemBlockDto dto, Guid instructorId)
    {
        await VerifyOwnershipAsync(subchapterId, instructorId);

        var problemExists = await context.Problems.AnyAsync(p => p.Id == dto.ProblemId);
        if (!problemExists)
        {
            throw new NotFoundException($"Problem with ID {dto.ProblemId} not found");
        }

        var nextOrderIndex = await GetNextOrderIndexAsync(subchapterId);
        var block = new CourseBlock
        {
            Title = dto.Title,
            Type = BlockType.Problem,
            OrderIndex = nextOrderIndex,
            SubchapterId = subchapterId,
            Subchapter = null!,
            ProblemId = dto.ProblemId
        };

        context.CourseBlocks.Add(block);
        await context.SaveChangesAsync();

        return block.Id;
    }

    public async Task DeleteBlockAsync(Guid blockId, Guid instructorId)
    {
        var block = await context.CourseBlocks
            .Include(b => b.Subchapter.Chapter.Course)
            .FirstOrDefaultAsync(b => b.Id == blockId)
            ?? throw new NotFoundException($"Block with ID {blockId} not found");

        VerifyBlockOwnership(block, instructorId);

        var deletedOrderIndex = block.OrderIndex;
        var subchapterId = block.SubchapterId;

        context.CourseBlocks.Remove(block);
        await context.SaveChangesAsync();

        // Reindex remaining blocks: all blocks with orderIndex > deletedOrderIndex should decrease by 1
        var blocksToReindex = await context.CourseBlocks
            .Where(b => b.SubchapterId == subchapterId && b.OrderIndex > deletedOrderIndex)
            .ToListAsync();

        foreach (var blockToReindex in blocksToReindex)
        {
            blockToReindex.OrderIndex--;
        }

        await context.SaveChangesAsync();
    }

    public async Task<BlockResponseDto> GetBlockByIdAsync(Guid blockId, Guid? viewerId = null)
    {
        var block = await GetBlockWithIncludesAsync(blockId)
            ?? throw new NotFoundException($"Block with ID {blockId} not found");

        var instructorId = await GetVisibleCourseInstructorIdAsync(block.SubchapterId, viewerId)
            ?? throw new NotFoundException($"Block with ID {blockId} not found");

        var includeCorrectAnswers = instructorId == viewerId;

        return block.ToResponseDto(includeCorrectAnswers);
    }

    public async Task<IEnumerable<BlockResponseDto>> GetSubchapterBlocksAsync(Guid subchapterId, Guid? viewerId = null)
    {
        var instructorId = await GetVisibleCourseInstructorIdAsync(subchapterId, viewerId)
            ?? throw new NotFoundException($"Subchapter with ID {subchapterId} not found");

        var includeCorrectAnswers = instructorId == viewerId;

        var blocks = await context.CourseBlocks
            .Include(b => b.TheoryContent)
            .Include(b => b.VideoContent)
            .Include(b => b.Quiz!.Questions.OrderBy(qq => qq.OrderIndex))
                .ThenInclude(qq => qq.Answers.OrderBy(a => a.OrderIndex))
            .Include(b => b.Problem)
            .Where(b => b.SubchapterId == subchapterId)
            .OrderBy(b => b.OrderIndex)
            .ToListAsync();

        return blocks.ToResponseDtos(includeCorrectAnswers);
    }

    public async Task UpdateTheoryBlockAsync(Guid blockId, UpdateTheoryBlockDto dto, Guid instructorId)
    {
        var block = await GetBlockForUpdateAsync(blockId, BlockType.Theory, instructorId);

        block.Title = dto.Title;

        if (block.TheoryContent != null)
        {
            block.TheoryContent.Content = sanitizationService.SanitizeMarkdown(dto.Content);
        }

        await context.SaveChangesAsync();
    }

    public async Task UpdateVideoBlockAsync(Guid blockId, UpdateVideoBlockDto dto, Guid instructorId)
    {
        var block = await GetBlockForUpdateAsync(blockId, BlockType.Video, instructorId);

        var videoId = dto.VideoUrl.ExtractYouTubeVideoId();
        if (string.IsNullOrEmpty(videoId))
        {
            throw new BusinessRuleException("Invalid YouTube URL");
        }

        block.Title = dto.Title;

        if (block.VideoContent != null)
        {
            block.VideoContent.VideoUrl = dto.VideoUrl;
            block.VideoContent.VideoId = videoId;
        }

        await context.SaveChangesAsync();
    }

    public async Task UpdateQuizBlockAsync(Guid blockId, UpdateQuizBlockDto dto, Guid instructorId)
    {
        var block = await GetBlockForUpdateAsync(blockId, BlockType.Quiz, instructorId);

        block.Title = dto.Title;

        if (block.Quiz?.Questions != null)
        {
            foreach (var question in block.Quiz.Questions.ToList())
            {
                context.QuizAnswers.RemoveRange(question.Answers);
            }
            context.QuizQuestions.RemoveRange(block.Quiz.Questions);
        }

        await context.SaveChangesAsync();

        context.QuizQuestions.AddRange(BuildQuizQuestions(dto.Questions, block.Quiz!));
        await context.SaveChangesAsync();
    }

    public async Task UpdateBlockOrderAsync(Guid blockId, int newOrderIndex, Guid instructorId)
    {
        var block = await context.CourseBlocks
            .Include(b => b.Subchapter.Chapter.Course)
            .FirstOrDefaultAsync(b => b.Id == blockId)
            ?? throw new NotFoundException($"Block with ID {blockId} not found");

        VerifyBlockOwnership(block, instructorId);

        var oldOrderIndex = block.OrderIndex;

        if (oldOrderIndex == newOrderIndex)
        {
            return;
        }

        block.OrderIndex = newOrderIndex;

        await ReorderBlocks(block.SubchapterId, blockId, oldOrderIndex, newOrderIndex);
        await context.SaveChangesAsync();
    }

    // Shared by create and update so both persist every question field
    private List<QuizQuestion> BuildQuizQuestions(IEnumerable<CreateQuizQuestionDto> questions, Quiz quiz) =>
        questions.Select((q, qIndex) => new QuizQuestion
        {
            Content = sanitizationService.SanitizeMarkdown(q.QuestionText),
            Type = Enum.Parse<QuestionType>(q.Type),
            Points = q.Points,
            Explanation = string.IsNullOrWhiteSpace(q.Explanation)
                ? null
                : sanitizationService.SanitizeMarkdown(q.Explanation),
            OrderIndex = qIndex + 1,
            Quiz = quiz,
            Answers = q.Answers.Select((a, aIndex) => new QuizAnswer
            {
                Text = sanitizationService.SanitizeText(a.AnswerText),
                IsCorrect = a.IsCorrect,
                OrderIndex = aIndex + 1,
                Question = null!
            }).ToList()
        }).ToList();

    // Null when the subchapter does not exist or belongs to a draft course the viewer does not own;
    // the course instructor is also the only viewer who sees quiz answers
    private async Task<Guid?> GetVisibleCourseInstructorIdAsync(Guid subchapterId, Guid? viewerId) =>
        await context.Courses
            .VisibleTo(viewerId)
            .Where(c => c.Chapters.Any(ch => ch.Subchapters.Any(s => s.Id == subchapterId)))
            .Select(c => (Guid?)c.InstructorId)
            .FirstOrDefaultAsync();

    private async Task VerifyOwnershipAsync(Guid subchapterId, Guid instructorId)
    {
        var subchapter = await context.Subchapters
            .Include(s => s.Chapter.Course)
            .FirstOrDefaultAsync(s => s.Id == subchapterId)
            ?? throw new NotFoundException($"Subchapter with ID {subchapterId} not found");

        if (subchapter.Chapter.Course.InstructorId != instructorId)
        {
            throw new ForbiddenException("You can only add blocks to your own courses");
        }

        if (subchapter.Chapter.Course.Status == CourseStatus.Published)
        {
            throw new BusinessRuleException("Cannot add blocks to a published course");
        }
    }

    private static void VerifyBlockOwnership(CourseBlock block, Guid instructorId)
    {
        if (block.Subchapter.Chapter.Course.InstructorId != instructorId)
        {
            throw new ForbiddenException("You can only modify blocks in your own courses");
        }

        if (block.Subchapter.Chapter.Course.Status == CourseStatus.Published)
        {
            throw new BusinessRuleException("Cannot modify blocks in a published course");
        }
    }

    private async Task<CourseBlock> GetBlockForUpdateAsync(Guid blockId, BlockType expectedType, Guid instructorId)
    {
        var block = await context.CourseBlocks
            .Include(b => b.TheoryContent)
            .Include(b => b.VideoContent)
            .Include(b => b.Quiz!.Questions)
                .ThenInclude(qq => qq.Answers)
            .Include(b => b.Subchapter.Chapter.Course)
            .FirstOrDefaultAsync(b => b.Id == blockId)
            ?? throw new NotFoundException($"Block with ID {blockId} not found");

        VerifyBlockOwnership(block, instructorId);

        if (block.Type != expectedType)
        {
            throw new BusinessRuleException($"Block is not a {expectedType.ToString().ToLower()} block");
        }

        return block;
    }

    private async Task<CourseBlock?> GetBlockWithIncludesAsync(Guid blockId)
    {
        return await context.CourseBlocks
            .Include(b => b.TheoryContent)
            .Include(b => b.VideoContent)
            .Include(b => b.Quiz!.Questions.OrderBy(qq => qq.OrderIndex))
                .ThenInclude(qq => qq.Answers.OrderBy(a => a.OrderIndex))
            .Include(b => b.Problem)
            .FirstOrDefaultAsync(b => b.Id == blockId);
    }

    private async Task ReorderBlocks(Guid subchapterId, Guid excludeBlockId, int oldOrderIndex, int newOrderIndex)
    {
        var otherBlocks = await context.CourseBlocks
            .Where(b => b.SubchapterId == subchapterId && b.Id != excludeBlockId)
            .ToListAsync();

        if (newOrderIndex < oldOrderIndex)
        {
            foreach (var other in otherBlocks.Where(o => o.OrderIndex >= newOrderIndex && o.OrderIndex < oldOrderIndex))
            {
                other.OrderIndex++;
            }
        }
        else
        {
            foreach (var other in otherBlocks.Where(o => o.OrderIndex > oldOrderIndex && o.OrderIndex <= newOrderIndex))
            {
                other.OrderIndex--;
            }
        }
    }

    private async Task<int> GetNextOrderIndexAsync(Guid subchapterId)
    {
        var maxOrder = await context.CourseBlocks
            .Where(b => b.SubchapterId == subchapterId)
            .MaxAsync(b => (int?)b.OrderIndex);

        return (maxOrder ?? 0) + 1;
    }
}
