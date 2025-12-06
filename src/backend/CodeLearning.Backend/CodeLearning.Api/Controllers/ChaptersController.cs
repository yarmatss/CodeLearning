using CodeLearning.Application.DTOs.Chapter;
using CodeLearning.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/courses/{courseId}/[controller]")]
public class ChaptersController(
    IChapterService chapterService,
    IValidator<CreateChapterDto> createChapterValidator) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> AddChapter(Guid courseId, [FromBody] CreateChapterDto dto)
    {
        await createChapterValidator.ValidateAndThrowAsync(dto);

        var userId = GetCurrentUserId();
        var chapter = await chapterService.AddChapterAsync(courseId, dto, userId);
        return CreatedAtAction(nameof(GetCourseChapters), new { courseId }, chapter);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetCourseChapters(Guid courseId)
    {
        var chapters = await chapterService.GetCourseChaptersAsync(courseId);
        return Ok(chapters);
    }

    [HttpPatch("{chapterId}/order")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> UpdateChapterOrder(Guid chapterId, [FromBody] int newOrderIndex)
    {
        var userId = GetCurrentUserId();
        var chapter = await chapterService.UpdateChapterOrderAsync(chapterId, newOrderIndex, userId);
        return Ok(chapter);
    }

    [HttpDelete("{chapterId}")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> DeleteChapter(Guid chapterId)
    {
        var userId = GetCurrentUserId();
        await chapterService.DeleteChapterAsync(chapterId, userId);
        return Ok(new { message = "Chapter deleted successfully" });
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User not authenticated");
        return Guid.Parse(userIdClaim);
    }
}
