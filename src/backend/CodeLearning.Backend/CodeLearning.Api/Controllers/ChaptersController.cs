using CodeLearning.Api.Extensions;
using CodeLearning.Application.DTOs.Chapter;
using CodeLearning.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/courses/{courseId:guid}/[controller]")]
public class ChaptersController(
    IChapterService chapterService,
    IValidator<CreateChapterDto> createChapterValidator,
    IValidator<UpdateChapterDto> updateChapterValidator) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> AddChapter(Guid courseId, [FromBody] CreateChapterDto dto)
    {
        await createChapterValidator.ValidateAndThrowAsync(dto);

        var chapter = await chapterService.AddChapterAsync(courseId, dto, User.GetRequiredUserId());
        return CreatedAtAction(nameof(GetCourseChapters), new { courseId }, chapter);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetCourseChapters(Guid courseId)
    {
        var chapters = await chapterService.GetCourseChaptersAsync(courseId, User.GetUserIdOrNull());
        return Ok(chapters);
    }

    [HttpPut("{chapterId:guid}")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> RenameChapter(Guid courseId, Guid chapterId, [FromBody] UpdateChapterDto dto)
    {
        await updateChapterValidator.ValidateAndThrowAsync(dto);

        var chapter = await chapterService.RenameChapterAsync(courseId, chapterId, dto, User.GetRequiredUserId());
        return Ok(chapter);
    }

    [HttpPatch("{chapterId:guid}/order")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> UpdateChapterOrder(Guid courseId, Guid chapterId, [FromBody] int newOrderIndex)
    {
        var chapter = await chapterService.UpdateChapterOrderAsync(courseId, chapterId, newOrderIndex, User.GetRequiredUserId());
        return Ok(chapter);
    }

    [HttpDelete("{chapterId:guid}")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> DeleteChapter(Guid courseId, Guid chapterId)
    {
        await chapterService.DeleteChapterAsync(courseId, chapterId, User.GetRequiredUserId());
        return Ok(new { message = "Chapter deleted successfully" });
    }
}
