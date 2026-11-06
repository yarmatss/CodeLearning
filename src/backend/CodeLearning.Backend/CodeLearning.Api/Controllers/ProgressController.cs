using CodeLearning.Api.Extensions;
using CodeLearning.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/progress")]
[Authorize(Roles = "Student")]
public class ProgressController(IProgressService progressService) : ControllerBase
{
    [HttpPost("blocks/{blockId}/complete")]
    public async Task<IActionResult> CompleteBlock(Guid blockId)
    {
        var studentId = User.GetRequiredUserId();
        var result = await progressService.CompleteBlockAsync(blockId, studentId);
        return Ok(result);
    }

    [HttpGet("courses/{courseId}")]
    public async Task<IActionResult> GetCourseProgress(Guid courseId)
    {
        var studentId = User.GetRequiredUserId();
        var progress = await progressService.GetCourseProgressAsync(courseId, studentId);
        return Ok(progress);
    }

    [HttpGet("courses/{courseId}/next-block")]
    public async Task<IActionResult> GetNextBlock(Guid courseId)
    {
        var studentId = User.GetRequiredUserId();
        var nextBlockId = await progressService.GetNextBlockAsync(courseId, studentId);

        if (nextBlockId == null)
        {
            return Ok(new { message = "Course completed!", nextBlockId = (Guid?)null });
        }

        return Ok(new { nextBlockId });
    }
}
