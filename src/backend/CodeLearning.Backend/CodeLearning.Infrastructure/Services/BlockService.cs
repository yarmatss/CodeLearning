using CodeLearning.Application.DTOs.Block;
using CodeLearning.Application.Extensions;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
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
            throw new ArgumentException("Invalid YouTube URL");
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
            Block = null!,
            Questions = dto.Questions.Select((q, qIndex) => new QuizQuestion
            {
                Content = sanitizationService.SanitizeMarkdown(q.QuestionText),
                Type = Enum.Parse<QuestionType>(q.Type),
                Points = q.Points,
                Explanation = string.IsNullOrWhiteSpace(q.Explanation) 
                    ? null 
                    : sanitizationService.SanitizeMarkdown(q.Explanation),
                OrderIndex = qIndex + 1,
                Quiz = null!,
                Answers = q.Answers.Select((a, aIndex) => new QuizAnswer
                {
                    Text = sanitizationService.SanitizeText(a.AnswerText),
                    IsCorrect = a.IsCorrect,
                    OrderIndex = aIndex + 1,
                    Question = null!
                }).ToList()
            }).ToList()
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
        await context.SaveChangesAsync();

        return block.Id;
    }

    public async Task<Guid> CreateProblemBlockAsync(Guid subchapterId, CreateProblemBlockDto dto, Guid instructorId)
    {
        await VerifyOwnershipAsync(subchapterId, instructorId);

        var problemExists = await context.Problems.AnyAsync(p => p.Id == dto.ProblemId);
        if (!problemExists)
        {
            throw new KeyNotFoundException($"Problem with ID {dto.ProblemId} not found");
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
            .Include(b => b.Subchapter)
                .ThenInclude(s => s.Chapter)
                    .ThenInclude(ch => ch.Course)
            .FirstOrDefaultAsync(b => b.Id == blockId)
            ?? throw new KeyNotFoundException($"Block with ID {blockId} not found");

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

    public async Task<BlockResponseDto> GetBlockByIdAsync(Guid blockId, bool includeCorrectAnswers = false)
    {
        var block = await GetBlockWithIncludesAsync(blockId)
            ?? throw new KeyNotFoundException($"Block with ID {blockId} not found");

        return block.ToResponseDto(includeCorrectAnswers);
    }

    public async Task<IEnumerable<BlockResponseDto>> GetSubchapterBlocksAsync(Guid subchapterId, bool includeCorrectAnswers = false)
    {
        var blocks = await context.CourseBlocks
            .Include(b => b.TheoryContent)
            .Include(b => b.VideoContent)
            .Include(b => b.Quiz)
                .ThenInclude(q => q!.Questions.OrderBy(qq => qq.OrderIndex))
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
            throw new ArgumentException("Invalid YouTube URL");
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

        var newQuestions = dto.Questions.Select((q, qIndex) => new QuizQuestion
        {
            Content = sanitizationService.SanitizeMarkdown(q.QuestionText),
            Type = Enum.Parse<QuestionType>(q.Type),
            OrderIndex = qIndex + 1,
            QuizId = block.Quiz!.Id,
            Quiz = block.Quiz,
            Answers = q.Answers.Select((a, aIndex) => new QuizAnswer
            {
                Text = sanitizationService.SanitizeText(a.AnswerText),
                IsCorrect = a.IsCorrect,
                OrderIndex = aIndex + 1,
                Question = null!
            }).ToList()
        }).ToList();

        context.QuizQuestions.AddRange(newQuestions);
        await context.SaveChangesAsync();
    }

    public async Task UpdateBlockOrderAsync(Guid blockId, int newOrderIndex, Guid instructorId)
    {
        var block = await context.CourseBlocks
            .Include(b => b.Subchapter)
                .ThenInclude(s => s.Chapter)
                    .ThenInclude(ch => ch.Course)
            .FirstOrDefaultAsync(b => b.Id == blockId)
            ?? throw new KeyNotFoundException($"Block with ID {blockId} not found");

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

    private async Task VerifyOwnershipAsync(Guid subchapterId, Guid instructorId)
    {
        var subchapter = await context.Subchapters
            .Include(s => s.Chapter)
                .ThenInclude(ch => ch.Course)
            .FirstOrDefaultAsync(s => s.Id == subchapterId)
            ?? throw new KeyNotFoundException($"Subchapter with ID {subchapterId} not found");

        if (subchapter.Chapter.Course.InstructorId != instructorId)
        {
            throw new UnauthorizedAccessException("You can only add blocks to your own courses");
        }

        if (subchapter.Chapter.Course.Status == CourseStatus.Published)
        {
            throw new InvalidOperationException("Cannot add blocks to a published course");
        }
    }

    private static void VerifyBlockOwnership(CourseBlock block, Guid instructorId)
    {
        if (block.Subchapter.Chapter.Course.InstructorId != instructorId)
        {
            throw new UnauthorizedAccessException("You can only modify blocks in your own courses");
        }

        if (block.Subchapter.Chapter.Course.Status == CourseStatus.Published)
        {
            throw new InvalidOperationException("Cannot modify blocks in a published course");
        }
    }

    private async Task<CourseBlock> GetBlockForUpdateAsync(Guid blockId, BlockType expectedType, Guid instructorId)
    {
        var block = await context.CourseBlocks
            .Include(b => b.TheoryContent)
            .Include(b => b.VideoContent)
            .Include(b => b.Quiz)
                .ThenInclude(q => q!.Questions)
                    .ThenInclude(qq => qq.Answers)
            .Include(b => b.Subchapter)
                .ThenInclude(s => s.Chapter)
                    .ThenInclude(ch => ch.Course)
            .FirstOrDefaultAsync(b => b.Id == blockId)
            ?? throw new KeyNotFoundException($"Block with ID {blockId} not found");

        VerifyBlockOwnership(block, instructorId);

        if (block.Type != expectedType)
        {
            throw new InvalidOperationException($"Block is not a {expectedType.ToString().ToLower()} block");
        }

        return block;
    }

    private async Task<CourseBlock?> GetBlockWithIncludesAsync(Guid blockId)
    {
        return await context.CourseBlocks
            .Include(b => b.TheoryContent)
            .Include(b => b.VideoContent)
            .Include(b => b.Quiz)
                .ThenInclude(q => q!.Questions.OrderBy(qq => qq.OrderIndex))
                    .ThenInclude(qq => qq.Answers.OrderBy(a => a.OrderIndex))
            .Include(b => b.Problem)
            .FirstOrDefaultAsync(b => b.Id == blockId);
    }

    private async Task ReorderBlocks(Guid subchapterId, Guid excludeBlockId, int oldOrderIndex, int newOrderIndex)
    {
        var otherBlocks = await context.CourseBlocks
            .Where(b => b.SubchapterId == subchapterId && b.Id != excludeBlockId)
            .ToListAsync();

        foreach (var other in otherBlocks)
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

    private async Task<int> GetNextOrderIndexAsync(Guid subchapterId)
    {
        var maxOrder = await context.CourseBlocks
            .Where(b => b.SubchapterId == subchapterId)
            .MaxAsync(b => (int?)b.OrderIndex);

        return (maxOrder ?? 0) + 1;
    }
}
