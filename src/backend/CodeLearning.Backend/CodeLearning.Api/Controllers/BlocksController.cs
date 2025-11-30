using CodeLearning.Application.DTOs.Block;
using CodeLearning.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/subchapters/{subchapterId}/blocks")]
[Authorize(Roles = "Teacher")]
public class BlocksController : ControllerBase
{
    private readonly IBlockService _blockService;
    private readonly IValidator<CreateTheoryBlockDto> _theoryBlockValidator;
    private readonly IValidator<CreateVideoBlockDto> _videoBlockValidator;
    private readonly IValidator<CreateQuizBlockDto> _quizBlockValidator;
    private readonly IValidator<CreateProblemBlockDto> _problemBlockValidator;

    public BlocksController(
        IBlockService blockService,
        IValidator<CreateTheoryBlockDto> theoryBlockValidator,
        IValidator<CreateVideoBlockDto> videoBlockValidator,
        IValidator<CreateQuizBlockDto> quizBlockValidator,
        IValidator<CreateProblemBlockDto> problemBlockValidator)
    {
        _blockService = blockService;
        _theoryBlockValidator = theoryBlockValidator;
        _videoBlockValidator = videoBlockValidator;
        _quizBlockValidator = quizBlockValidator;
        _problemBlockValidator = problemBlockValidator;
    }

    [HttpPost("theory")]
    public async Task<IActionResult> CreateTheoryBlock(Guid subchapterId, [FromBody] CreateTheoryBlockDto dto)
    {
        var validationResult = await _theoryBlockValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var instructorId = GetCurrentUserId();
            var blockId = await _blockService.CreateTheoryBlockAsync(subchapterId, dto, instructorId);

            return CreatedAtAction(nameof(CreateTheoryBlock), new { blockId }, new BlockCreatedResponseDto 
            { 
                Id = blockId, 
                Message = "Theory block created successfully" 
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("video")]
    public async Task<IActionResult> CreateVideoBlock(Guid subchapterId, [FromBody] CreateVideoBlockDto dto)
    {
        var validationResult = await _videoBlockValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var instructorId = GetCurrentUserId();
            var blockId = await _blockService.CreateVideoBlockAsync(subchapterId, dto, instructorId);

            return CreatedAtAction(nameof(CreateVideoBlock), new { blockId }, new BlockCreatedResponseDto 
            { 
                Id = blockId, 
                Message = "Video block created successfully" 
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("quiz")]
    public async Task<IActionResult> CreateQuizBlock(Guid subchapterId, [FromBody] CreateQuizBlockDto dto)
    {
        var validationResult = await _quizBlockValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var instructorId = GetCurrentUserId();
            var blockId = await _blockService.CreateQuizBlockAsync(subchapterId, dto, instructorId);

            return CreatedAtAction(nameof(CreateQuizBlock), new { blockId }, new BlockCreatedResponseDto 
            { 
                Id = blockId, 
                Message = "Quiz block created successfully" 
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("problem")]
    public async Task<IActionResult> CreateProblemBlock(Guid subchapterId, [FromBody] CreateProblemBlockDto dto)
    {
        var validationResult = await _problemBlockValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var instructorId = GetCurrentUserId();
            var blockId = await _blockService.CreateProblemBlockAsync(subchapterId, dto, instructorId);

            return CreatedAtAction(nameof(CreateProblemBlock), new { blockId }, new BlockCreatedResponseDto 
            { 
                Id = blockId, 
                Message = "Problem block created successfully" 
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{blockId}")]
    public async Task<IActionResult> DeleteBlock(Guid subchapterId, Guid blockId)
    {
        try
        {
            var instructorId = GetCurrentUserId();
            await _blockService.DeleteBlockAsync(blockId, instructorId);

            return Ok(new { message = "Block deleted successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("/api/blocks/{blockId}")]
    [AllowAnonymous] // Public for published courses
    public async Task<IActionResult> GetBlockById(Guid blockId)
    {
        try
        {
            var block = await _blockService.GetBlockByIdAsync(blockId);
            return Ok(block);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetSubchapterBlocks(Guid subchapterId)
    {
        var blocks = await _blockService.GetSubchapterBlocksAsync(subchapterId);
        return Ok(blocks);
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User not authenticated");

        return Guid.Parse(userIdClaim);
    }
}
