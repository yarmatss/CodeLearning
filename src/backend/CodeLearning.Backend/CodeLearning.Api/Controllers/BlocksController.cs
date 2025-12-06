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
public class BlocksController(
    IBlockService blockService,
    IValidator<CreateTheoryBlockDto> createTheoryBlockValidator,
    IValidator<CreateVideoBlockDto> createVideoBlockValidator,
    IValidator<CreateQuizBlockDto> createQuizBlockValidator,
    IValidator<CreateProblemBlockDto> problemBlockValidator,
    IValidator<UpdateTheoryBlockDto> updateTheoryBlockValidator,
    IValidator<UpdateVideoBlockDto> updateVideoBlockValidator,
    IValidator<UpdateQuizBlockDto> updateQuizBlockValidator) : ControllerBase
{
    #region Create Methods

    [HttpPost("theory")]
    public async Task<IActionResult> CreateTheoryBlock(Guid subchapterId, [FromBody] CreateTheoryBlockDto dto)
    {
        await createTheoryBlockValidator.ValidateAndThrowAsync(dto);

        var instructorId = GetCurrentUserId();
        var blockId = await blockService.CreateTheoryBlockAsync(subchapterId, dto, instructorId);

        return CreatedAtAction(nameof(GetBlockById), new { blockId },
            new BlockCreatedResponseDto
            {
                Id = blockId,
                Message = "Theory block created successfully"
            });
    }

    [HttpPost("video")]
    public async Task<IActionResult> CreateVideoBlock(Guid subchapterId, [FromBody] CreateVideoBlockDto dto)
    {
        await createVideoBlockValidator.ValidateAndThrowAsync(dto);

        var instructorId = GetCurrentUserId();
        var blockId = await blockService.CreateVideoBlockAsync(subchapterId, dto, instructorId);

        return CreatedAtAction(nameof(GetBlockById), new { blockId },
            new BlockCreatedResponseDto
            {
                Id = blockId,
                Message = "Video block created successfully"
            });
    }

    [HttpPost("quiz")]
    public async Task<IActionResult> CreateQuizBlock(Guid subchapterId, [FromBody] CreateQuizBlockDto dto)
    {
        await createQuizBlockValidator.ValidateAndThrowAsync(dto);

        var instructorId = GetCurrentUserId();
        var blockId = await blockService.CreateQuizBlockAsync(subchapterId, dto, instructorId);

        return CreatedAtAction(nameof(GetBlockById), new { blockId },
            new BlockCreatedResponseDto
            {
                Id = blockId,
                Message = "Quiz block created successfully"
            });
    }

    [HttpPost("problem")]
    public async Task<IActionResult> CreateProblemBlock(Guid subchapterId, [FromBody] CreateProblemBlockDto dto)
    {
        await problemBlockValidator.ValidateAndThrowAsync(dto);

        var instructorId = GetCurrentUserId();
        var blockId = await blockService.CreateProblemBlockAsync(subchapterId, dto, instructorId);

        return CreatedAtAction(nameof(GetBlockById), new { blockId },
            new BlockCreatedResponseDto
            {
                Id = blockId,
                Message = "Problem block created successfully"
            });
    }

    #endregion

    #region Update Methods

    [HttpPut("{blockId}/theory")]
    public async Task<IActionResult> UpdateTheoryBlock(Guid blockId, [FromBody] UpdateTheoryBlockDto dto)
    {
        await updateTheoryBlockValidator.ValidateAndThrowAsync(dto);

        var instructorId = GetCurrentUserId();
        await blockService.UpdateTheoryBlockAsync(blockId, dto, instructorId);

        return Ok(new { message = "Theory block updated successfully" });
    }

    [HttpPut("{blockId}/video")]
    public async Task<IActionResult> UpdateVideoBlock(Guid blockId, [FromBody] UpdateVideoBlockDto dto)
    {
        await updateVideoBlockValidator.ValidateAndThrowAsync(dto);

        var instructorId = GetCurrentUserId();
        await blockService.UpdateVideoBlockAsync(blockId, dto, instructorId);

        return Ok(new { message = "Video block updated successfully" });
    }

    [HttpPut("{blockId}/quiz")]
    public async Task<IActionResult> UpdateQuizBlock(Guid blockId, [FromBody] UpdateQuizBlockDto dto)
    {
        await updateQuizBlockValidator.ValidateAndThrowAsync(dto);

        var instructorId = GetCurrentUserId();
        await blockService.UpdateQuizBlockAsync(blockId, dto, instructorId);

        return Ok(new { message = "Quiz block updated successfully" });
    }

    [HttpPatch("{blockId}/order")]
    public async Task<IActionResult> UpdateBlockOrder(Guid blockId, [FromBody] UpdateBlockOrderDto dto)
    {
        var instructorId = GetCurrentUserId();
        await blockService.UpdateBlockOrderAsync(blockId, dto.NewOrderIndex, instructorId);

        return Ok(new { message = "Block order updated successfully" });
    }

    #endregion

    #region Delete & Get Methods

    [HttpDelete("{blockId}")]
    public async Task<IActionResult> DeleteBlock(Guid blockId)
    {
        var instructorId = GetCurrentUserId();
        await blockService.DeleteBlockAsync(blockId, instructorId);

        return Ok(new { message = "Block deleted successfully" });
    }

    [HttpGet("/api/blocks/{blockId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetBlockById(Guid blockId)
    {
        var block = await blockService.GetBlockByIdAsync(blockId);
        return Ok(block);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetSubchapterBlocks(Guid subchapterId)
    {
        var blocks = await blockService.GetSubchapterBlocksAsync(subchapterId);
        return Ok(blocks);
    }

    #endregion

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User not authenticated");
        return Guid.Parse(userIdClaim);
    }
}
