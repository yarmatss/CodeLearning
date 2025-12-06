using CodeLearning.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/enrollments")]
[Authorize(Roles = "Student")]
public class EnrollmentController(IEnrollmentService enrollmentService) : ControllerBase
{
    [HttpGet("my-courses")]
    public async Task<IActionResult> GetMyCourses()
    {
        var studentId = GetCurrentUserId();
        var courses = await enrollmentService.GetEnrolledCoursesAsync(studentId);
        return Ok(courses);
    }

    [HttpPost("courses/{courseId}")]
    public async Task<IActionResult> Enroll(Guid courseId)
    {
        var studentId = GetCurrentUserId();
        var result = await enrollmentService.EnrollAsync(courseId, studentId);
        return Ok(result);
    }

    [HttpDelete("courses/{courseId}")]
    public async Task<IActionResult> Unenroll(Guid courseId)
    {
        var studentId = GetCurrentUserId();
        await enrollmentService.UnenrollAsync(courseId, studentId);
        return Ok(new { message = "Successfully unenrolled from course" });
    }

    [HttpGet("courses/{courseId}/status")]
    public async Task<IActionResult> GetEnrollmentStatus(Guid courseId)
    {
        var studentId = GetCurrentUserId();
        var isEnrolled = await enrollmentService.IsEnrolledAsync(courseId, studentId);
        return Ok(new { isEnrolled });
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User not authenticated");
        return Guid.Parse(userIdClaim);
    }
}
