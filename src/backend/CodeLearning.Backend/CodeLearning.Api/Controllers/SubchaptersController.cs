using CodeLearning.Api.Extensions;
using CodeLearning.Application.DTOs.Subchapter;
using CodeLearning.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/chapters/{chapterId:guid}/[controller]")]
public class SubchaptersController(
    ISubchapterService subchapterService,
    IValidator<CreateSubchapterDto> createSubchapterValidator,
    IValidator<UpdateSubchapterDto> updateSubchapterValidator) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> AddSubchapter(Guid chapterId, [FromBody] CreateSubchapterDto dto)
    {
        await createSubchapterValidator.ValidateAndThrowAsync(dto);

        var subchapter = await subchapterService.AddSubchapterAsync(chapterId, dto, User.GetRequiredUserId());
        return CreatedAtAction(nameof(GetChapterSubchapters), new { chapterId }, subchapter);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetChapterSubchapters(Guid chapterId)
    {
        var subchapters = await subchapterService.GetChapterSubchaptersAsync(chapterId, User.GetUserIdOrNull());
        return Ok(subchapters);
    }

    [HttpPut("{subchapterId:guid}")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> RenameSubchapter(Guid chapterId, Guid subchapterId, [FromBody] UpdateSubchapterDto dto)
    {
        await updateSubchapterValidator.ValidateAndThrowAsync(dto);

        var subchapter = await subchapterService.RenameSubchapterAsync(chapterId, subchapterId, dto, User.GetRequiredUserId());
        return Ok(subchapter);
    }

    [HttpPatch("{subchapterId:guid}/order")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> UpdateSubchapterOrder(Guid chapterId, Guid subchapterId, [FromBody] int newOrderIndex)
    {
        var subchapter = await subchapterService.UpdateSubchapterOrderAsync(chapterId, subchapterId, newOrderIndex, User.GetRequiredUserId());
        return Ok(subchapter);
    }

    [HttpDelete("{subchapterId:guid}")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> DeleteSubchapter(Guid chapterId, Guid subchapterId)
    {
        await subchapterService.DeleteSubchapterAsync(chapterId, subchapterId, User.GetRequiredUserId());
        return Ok(new { message = "Subchapter deleted successfully" });
    }
}
