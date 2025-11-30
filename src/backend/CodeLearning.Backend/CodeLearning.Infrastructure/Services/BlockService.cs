using CodeLearning.Application.DTOs.Block;
using CodeLearning.Application.Extensions;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class BlockService : IBlockService
{
    private readonly ApplicationDbContext _context;
    private readonly ISanitizationService _sanitizationService;

    public BlockService(ApplicationDbContext context, ISanitizationService sanitizationService)
    {
        _context = context;
        _sanitizationService = sanitizationService;
    }

    public async Task<Guid> CreateTheoryBlockAsync(Guid subchapterId, CreateTheoryBlockDto dto, Guid instructorId)
    {
        await VerifyOwnershipAsync(subchapterId, instructorId);
        var nextOrderIndex = await GetNextOrderIndexAsync(subchapterId);

        var sanitizedContent = _sanitizationService.SanitizeMarkdown(dto.Content);
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

        _context.CourseBlocks.Add(block);
        await _context.SaveChangesAsync();

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

        _context.CourseBlocks.Add(block);
        await _context.SaveChangesAsync();

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
                Content = _sanitizationService.SanitizeMarkdown(q.QuestionText),
                Type = Enum.Parse<QuestionType>(q.Type),
                OrderIndex = qIndex + 1,
                Quiz = null!,
                Answers = q.Answers.Select((a, aIndex) => new QuizAnswer
                {
                    Text = _sanitizationService.SanitizeText(a.AnswerText),
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

        _context.CourseBlocks.Add(block);
        await _context.SaveChangesAsync();

        return block.Id;
    }

    public async Task<Guid> CreateProblemBlockAsync(Guid subchapterId, CreateProblemBlockDto dto, Guid instructorId)
    {
        await VerifyOwnershipAsync(subchapterId, instructorId);

        var problemExists = await _context.Problems.AnyAsync(p => p.Id == dto.ProblemId);
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

        _context.CourseBlocks.Add(block);
        await _context.SaveChangesAsync();

        return block.Id;
    }

    public async Task DeleteBlockAsync(Guid blockId, Guid instructorId)
    {
        var block = await _context.CourseBlocks
            .Include(b => b.Subchapter)
                .ThenInclude(s => s.Chapter)
                    .ThenInclude(ch => ch.Course)
            .FirstOrDefaultAsync(b => b.Id == blockId)
            ?? throw new KeyNotFoundException($"Block with ID {blockId} not found");

        if (block.Subchapter.Chapter.Course.InstructorId != instructorId)
        {
            throw new UnauthorizedAccessException("You can only delete blocks from your own courses");
        }

        if (block.Subchapter.Chapter.Course.Status == CourseStatus.Published)
        {
            throw new InvalidOperationException("Cannot delete blocks from a published course");
        }

        _context.CourseBlocks.Remove(block);
        await _context.SaveChangesAsync();
    }

    public async Task<BlockResponseDto> GetBlockByIdAsync(Guid blockId)
    {
        var block = await _context.CourseBlocks
            .Include(b => b.TheoryContent)
            .Include(b => b.VideoContent)
            .Include(b => b.Quiz)
                .ThenInclude(q => q!.Questions.OrderBy(qq => qq.OrderIndex))
                    .ThenInclude(qq => qq.Answers.OrderBy(a => a.OrderIndex))
            .Include(b => b.Problem)
            .FirstOrDefaultAsync(b => b.Id == blockId)
            ?? throw new KeyNotFoundException($"Block with ID {blockId} not found");

        return block.ToResponseDto();
    }

    public async Task<IEnumerable<BlockResponseDto>> GetSubchapterBlocksAsync(Guid subchapterId)
    {
        var blocks = await _context.CourseBlocks
            .Include(b => b.TheoryContent)
            .Include(b => b.VideoContent)
            .Include(b => b.Quiz)
                .ThenInclude(q => q!.Questions.OrderBy(qq => qq.OrderIndex))
                    .ThenInclude(qq => qq.Answers.OrderBy(a => a.OrderIndex))
            .Include(b => b.Problem)
            .Where(b => b.SubchapterId == subchapterId)
            .OrderBy(b => b.OrderIndex)
            .ToListAsync();

        return blocks.ToResponseDtos();
    }

    #region Helper Methods

    private async Task VerifyOwnershipAsync(Guid subchapterId, Guid instructorId)
    {
        var subchapter = await _context.Subchapters
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

    private async Task<int> GetNextOrderIndexAsync(Guid subchapterId)
    {
        var maxOrder = await _context.CourseBlocks
            .Where(b => b.SubchapterId == subchapterId)
            .MaxAsync(b => (int?)b.OrderIndex);

        return (maxOrder ?? 0) + 1;
    }

    #endregion
}
