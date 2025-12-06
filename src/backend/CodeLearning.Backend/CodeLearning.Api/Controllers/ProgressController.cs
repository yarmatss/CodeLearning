using CodeLearning.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/progress")]
[Authorize(Roles = "Student")]
public class ProgressController(IProgressService progressService) : ControllerBase
{
    [HttpPost("blocks/{blockId}/complete")]
    public async Task<IActionResult> CompleteBlock(Guid blockId)
    {
        var studentId = GetCurrentUserId();
        var result = await progressService.CompleteBlockAsync(blockId, studentId);
        return Ok(result);
    }

    [HttpGet("courses/{courseId}")]
    public async Task<IActionResult> GetCourseProgress(Guid courseId)
    {
        var studentId = GetCurrentUserId();
        var progress = await progressService.GetCourseProgressAsync(courseId, studentId);
        return Ok(progress);
    }

    [HttpGet("courses/{courseId}/next-block")]
    public async Task<IActionResult> GetNextBlock(Guid courseId)
    {
        var studentId = GetCurrentUserId();
        var nextBlockId = await progressService.GetNextBlockAsync(courseId, studentId);

        if (nextBlockId == null)
        {
            return Ok(new { message = "Course completed!", nextBlockId = (Guid?)null });
        }

        return Ok(new { nextBlockId });
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User not authenticated");
        return Guid.Parse(userIdClaim);
    }
}
