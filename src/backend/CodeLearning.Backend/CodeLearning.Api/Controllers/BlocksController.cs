using CodeLearning.Api.Extensions;
using CodeLearning.Application.DTOs.Block;
using CodeLearning.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CodeLearning.Api.Controllers;

// Mutations are for the course instructor; reading content needs a signed-in user who owns the course or is
// enrolled in it while it is published (401 anonymous, 403 not enrolled, 404 for someone else's draft)
[ApiController]
[Route("api/subchapters/{subchapterId:guid}/blocks")]
public class BlocksController(
    IBlockService blockService,
    IValidator<CreateTheoryBlockDto> createTheoryBlockValidator,
    IValidator<CreateVideoBlockDto> createVideoBlockValidator,
    IValidator<CreateQuizBlockDto> createQuizBlockValidator,
    IValidator<CreateProblemBlockDto> problemBlockValidator,
    IValidator<UpdateTheoryBlockDto> updateTheoryBlockValidator,
    IValidator<UpdateVideoBlockDto> updateVideoBlockValidator,
    IValidator<UpdateQuizBlockDto> updateQuizBlockValidator,
    IValidator<UpdateProblemBlockDto> updateProblemBlockValidator,
    IValidator<UpdateBlockOrderDto> updateBlockOrderValidator) : ControllerBase
{
    #region Create Methods

    [HttpPost("theory")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> CreateTheoryBlock(Guid subchapterId, [FromBody] CreateTheoryBlockDto dto)
    {
        await createTheoryBlockValidator.ValidateAndThrowAsync(dto);

        var blockId = await blockService.CreateTheoryBlockAsync(subchapterId, dto, User.GetRequiredUserId());
        return Created(blockId, "Theory block created successfully");
    }

    [HttpPost("video")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> CreateVideoBlock(Guid subchapterId, [FromBody] CreateVideoBlockDto dto)
    {
        await createVideoBlockValidator.ValidateAndThrowAsync(dto);

        var blockId = await blockService.CreateVideoBlockAsync(subchapterId, dto, User.GetRequiredUserId());
        return Created(blockId, "Video block created successfully");
    }

    [HttpPost("quiz")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> CreateQuizBlock(Guid subchapterId, [FromBody] CreateQuizBlockDto dto)
    {
        await createQuizBlockValidator.ValidateAndThrowAsync(dto);

        var blockId = await blockService.CreateQuizBlockAsync(subchapterId, dto, User.GetRequiredUserId());
        return Created(blockId, "Quiz block created successfully");
    }

    [HttpPost("problem")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> CreateProblemBlock(Guid subchapterId, [FromBody] CreateProblemBlockDto dto)
    {
        await problemBlockValidator.ValidateAndThrowAsync(dto);

        var blockId = await blockService.CreateProblemBlockAsync(subchapterId, dto, User.GetRequiredUserId());
        return Created(blockId, "Problem block created successfully");
    }

    #endregion

    #region Update Methods

    [HttpPut("{blockId:guid}/theory")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> UpdateTheoryBlock(Guid subchapterId, Guid blockId, [FromBody] UpdateTheoryBlockDto dto)
    {
        await updateTheoryBlockValidator.ValidateAndThrowAsync(dto);

        await blockService.UpdateTheoryBlockAsync(subchapterId, blockId, dto, User.GetRequiredUserId());
        return Ok(new { message = "Theory block updated successfully" });
    }

    [HttpPut("{blockId:guid}/video")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> UpdateVideoBlock(Guid subchapterId, Guid blockId, [FromBody] UpdateVideoBlockDto dto)
    {
        await updateVideoBlockValidator.ValidateAndThrowAsync(dto);

        await blockService.UpdateVideoBlockAsync(subchapterId, blockId, dto, User.GetRequiredUserId());
        return Ok(new { message = "Video block updated successfully" });
    }

    [HttpPut("{blockId:guid}/quiz")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> UpdateQuizBlock(Guid subchapterId, Guid blockId, [FromBody] UpdateQuizBlockDto dto)
    {
        await updateQuizBlockValidator.ValidateAndThrowAsync(dto);

        await blockService.UpdateQuizBlockAsync(subchapterId, blockId, dto, User.GetRequiredUserId());
        return Ok(new { message = "Quiz block updated successfully" });
    }

    [HttpPut("{blockId:guid}/problem")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> UpdateProblemBlock(Guid subchapterId, Guid blockId, [FromBody] UpdateProblemBlockDto dto)
    {
        await updateProblemBlockValidator.ValidateAndThrowAsync(dto);

        await blockService.UpdateProblemBlockAsync(subchapterId, blockId, dto, User.GetRequiredUserId());
        return Ok(new { message = "Problem block updated successfully" });
    }

    [HttpPatch("{blockId:guid}/order")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> UpdateBlockOrder(Guid subchapterId, Guid blockId, [FromBody] UpdateBlockOrderDto dto)
    {
        await updateBlockOrderValidator.ValidateAndThrowAsync(dto);

        await blockService.UpdateBlockOrderAsync(subchapterId, blockId, dto.NewOrderIndex, User.GetRequiredUserId());
        return Ok(new { message = "Block order updated successfully" });
    }

    #endregion

    #region Delete & Get Methods

    [HttpDelete("{blockId:guid}")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> DeleteBlock(Guid subchapterId, Guid blockId)
    {
        await blockService.DeleteBlockAsync(subchapterId, blockId, User.GetRequiredUserId());
        return Ok(new { message = "Block deleted successfully" });
    }

    [HttpGet("/api/blocks/{blockId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetBlockById(Guid blockId)
    {
        var block = await blockService.GetBlockByIdAsync(blockId, User.GetRequiredUserId());
        return Ok(block);
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetSubchapterBlocks(Guid subchapterId)
    {
        var blocks = await blockService.GetSubchapterBlocksAsync(subchapterId, User.GetRequiredUserId());
        return Ok(blocks);
    }

    #endregion

    private CreatedAtActionResult Created(Guid blockId, string message) =>
        CreatedAtAction(nameof(GetBlockById), new { blockId },
            new BlockCreatedResponseDto
            {
                Id = blockId,
                Message = message
            });
}
