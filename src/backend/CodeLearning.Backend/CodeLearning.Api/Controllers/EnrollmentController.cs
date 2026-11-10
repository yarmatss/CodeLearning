using CodeLearning.Api.Extensions;
using CodeLearning.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/enrollments")]
[Authorize(Roles = "Student")]
public class EnrollmentController(IEnrollmentService enrollmentService) : ControllerBase
{
    [HttpGet("my-courses")]
    public async Task<IActionResult> GetMyCourses([FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var courses = await enrollmentService.GetEnrolledCoursesAsync(User.GetRequiredUserId(), page, pageSize);
        return Ok(courses);
    }

    [HttpPost("courses/{courseId:guid}")]
    public async Task<IActionResult> Enroll(Guid courseId)
    {
        var result = await enrollmentService.EnrollAsync(courseId, User.GetRequiredUserId());
        return Ok(result);
    }

    [HttpDelete("courses/{courseId:guid}")]
    public async Task<IActionResult> Unenroll(Guid courseId)
    {
        await enrollmentService.UnenrollAsync(courseId, User.GetRequiredUserId());
        return Ok(new { message = "Successfully unenrolled from course" });
    }

    [HttpGet("courses/{courseId:guid}/status")]
    public async Task<IActionResult> GetEnrollmentStatus(Guid courseId)
    {
        var isEnrolled = await enrollmentService.IsEnrolledAsync(courseId, User.GetRequiredUserId());
        return Ok(new { isEnrolled });
    }
}
