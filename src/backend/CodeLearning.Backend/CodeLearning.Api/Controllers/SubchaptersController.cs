using CodeLearning.Application.DTOs.Subchapter;
using CodeLearning.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/chapters/{chapterId}/[controller]")]
public class SubchaptersController : ControllerBase
{
    private readonly ISubchapterService _subchapterService;
    private readonly IValidator<CreateSubchapterDto> _createSubchapterValidator;

    public SubchaptersController(
        ISubchapterService subchapterService,
        IValidator<CreateSubchapterDto> createSubchapterValidator)
    {
        _subchapterService = subchapterService;
        _createSubchapterValidator = createSubchapterValidator;
    }

    [HttpPost]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> AddSubchapter(Guid chapterId, [FromBody] CreateSubchapterDto dto)
    {
        var validationResult = await _createSubchapterValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var userId = GetCurrentUserId();
            var subchapter = await _subchapterService.AddSubchapterAsync(chapterId, dto, userId);
            return CreatedAtAction(nameof(GetChapterSubchapters), new { chapterId }, subchapter);
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

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetChapterSubchapters(Guid chapterId)
    {
        var subchapters = await _subchapterService.GetChapterSubchaptersAsync(chapterId);
        return Ok(subchapters);
    }

    [HttpPatch("{subchapterId}/order")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> UpdateSubchapterOrder(Guid chapterId, Guid subchapterId, [FromBody] int newOrderIndex)
    {
        try
        {
            var userId = GetCurrentUserId();
            var subchapter = await _subchapterService.UpdateSubchapterOrderAsync(subchapterId, newOrderIndex, userId);
            return Ok(subchapter);
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

    [HttpDelete("{subchapterId}")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> DeleteSubchapter(Guid chapterId, Guid subchapterId)
    {
        try
        {
            var userId = GetCurrentUserId();
            await _subchapterService.DeleteSubchapterAsync(subchapterId, userId);
            return Ok(new { message = "Subchapter deleted successfully" });
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

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User not authenticated");
        return Guid.Parse(userIdClaim);
    }
}
