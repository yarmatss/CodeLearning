using CodeLearning.Application.DTOs.Course;
using CodeLearning.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CoursesController(
    ICourseService courseService,
    IValidator<CreateCourseDto> createCourseValidator,
    IValidator<UpdateCourseDto> updateCourseValidator) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> CreateCourse([FromBody] CreateCourseDto dto)
    {
        await createCourseValidator.ValidateAndThrowAsync(dto);

        var userId = GetCurrentUserId();
        var course = await courseService.CreateCourseAsync(dto, userId);

        return CreatedAtAction(nameof(GetCourseById), new { id = course.Id }, course);
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCourseById(Guid id)
    {
        var course = await courseService.GetCourseByIdAsync(id);
        return Ok(course);
    }

    [HttpGet("my-courses")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> GetMyCourses()
    {
        var userId = GetCurrentUserId();
        var courses = await courseService.GetInstructorCoursesAsync(userId);
        return Ok(courses);
    }

    [HttpGet("published")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublishedCourses()
    {
        var courses = await courseService.GetPublishedCoursesAsync();
        return Ok(courses);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> UpdateCourse(Guid id, [FromBody] UpdateCourseDto dto)
    {
        await updateCourseValidator.ValidateAndThrowAsync(dto);

        var userId = GetCurrentUserId();
        var course = await courseService.UpdateCourseAsync(id, dto, userId);
        return Ok(course);
    }

    [HttpPost("{id}/publish")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> PublishCourse(Guid id)
    {
        var userId = GetCurrentUserId();
        var course = await courseService.PublishCourseAsync(id, userId);
        return Ok(new { message = "Course published successfully", course });
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> DeleteCourse(Guid id)
    {
        var userId = GetCurrentUserId();
        await courseService.DeleteCourseAsync(id, userId);
        return Ok(new { message = "Course deleted successfully" });
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User not authenticated");
        return Guid.Parse(userIdClaim);
    }
}
