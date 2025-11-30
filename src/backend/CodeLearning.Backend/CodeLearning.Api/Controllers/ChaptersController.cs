using CodeLearning.Application.DTOs.Chapter;
using CodeLearning.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/courses/{courseId}/[controller]")]
public class ChaptersController : ControllerBase
{
    private readonly IChapterService _chapterService;
    private readonly IValidator<CreateChapterDto> _createChapterValidator;

    public ChaptersController(
        IChapterService chapterService,
        IValidator<CreateChapterDto> createChapterValidator)
    {
        _chapterService = chapterService;
        _createChapterValidator = createChapterValidator;
    }

    [HttpPost]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> AddChapter(Guid courseId, [FromBody] CreateChapterDto dto)
    {
        var validationResult = await _createChapterValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var userId = GetCurrentUserId();
            var chapter = await _chapterService.AddChapterAsync(courseId, dto, userId);
            return CreatedAtAction(nameof(GetCourseChapters), new { courseId }, chapter);
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
    public async Task<IActionResult> GetCourseChapters(Guid courseId)
    {
        var chapters = await _chapterService.GetCourseChaptersAsync(courseId);
        return Ok(chapters);
    }

    [HttpPatch("{chapterId}/order")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> UpdateChapterOrder(Guid courseId, Guid chapterId, [FromBody] int newOrderIndex)
    {
        try
        {
            var userId = GetCurrentUserId();
            var chapter = await _chapterService.UpdateChapterOrderAsync(chapterId, newOrderIndex, userId);
            return Ok(chapter);
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

    [HttpDelete("{chapterId}")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> DeleteChapter(Guid courseId, Guid chapterId)
    {
        try
        {
            var userId = GetCurrentUserId();
            await _chapterService.DeleteChapterAsync(chapterId, userId);
            return Ok(new { message = "Chapter deleted successfully" });
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
