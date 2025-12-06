using CodeLearning.Application.DTOs.Subchapter;
using CodeLearning.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/chapters/{chapterId}/[controller]")]
public class SubchaptersController(
    ISubchapterService subchapterService,
    IValidator<CreateSubchapterDto> createSubchapterValidator) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> AddSubchapter(Guid chapterId, [FromBody] CreateSubchapterDto dto)
    {
        await createSubchapterValidator.ValidateAndThrowAsync(dto);

        var userId = GetCurrentUserId();
        var subchapter = await subchapterService.AddSubchapterAsync(chapterId, dto, userId);
        return CreatedAtAction(nameof(GetChapterSubchapters), new { chapterId }, subchapter);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetChapterSubchapters(Guid chapterId)
    {
        var subchapters = await subchapterService.GetChapterSubchaptersAsync(chapterId);
        return Ok(subchapters);
    }

    [HttpPatch("{subchapterId}/order")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> UpdateSubchapterOrder(Guid subchapterId, [FromBody] int newOrderIndex)
    {
        var userId = GetCurrentUserId();
        var subchapter = await subchapterService.UpdateSubchapterOrderAsync(subchapterId, newOrderIndex, userId);
        return Ok(subchapter);
    }

    [HttpDelete("{subchapterId}")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> DeleteSubchapter(Guid subchapterId)
    {
        var userId = GetCurrentUserId();
        await subchapterService.DeleteSubchapterAsync(subchapterId, userId);
        return Ok(new { message = "Subchapter deleted successfully" });
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User not authenticated");
        return Guid.Parse(userIdClaim);
    }
}
