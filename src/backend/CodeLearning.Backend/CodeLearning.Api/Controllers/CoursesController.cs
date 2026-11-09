using CodeLearning.Api.Extensions;
using CodeLearning.Application.DTOs.Course;
using CodeLearning.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

        var course = await courseService.CreateCourseAsync(dto, User.GetRequiredUserId());
        return CreatedAtAction(nameof(GetCourseById), new { id = course.Id }, course);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCourseById(Guid id)
    {
        var course = await courseService.GetCourseByIdAsync(id, User.GetUserIdOrNull());
        return Ok(course);
    }

    // Public outline (titles, block types, order); no block content
    [HttpGet("{id:guid}/structure")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCourseStructure(Guid id)
    {
        var structure = await courseService.GetCourseStructureAsync(id, User.GetUserIdOrNull());
        return Ok(structure);
    }

    [HttpGet("my-courses")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> GetMyCourses()
    {
        var courses = await courseService.GetInstructorCoursesAsync(User.GetRequiredUserId());
        return Ok(courses);
    }

    [HttpGet("my/stats")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> GetMyStats()
    {
        var stats = await courseService.GetInstructorStatsAsync(User.GetRequiredUserId());
        return Ok(stats);
    }

    [HttpGet("published")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublishedCourses([FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var courses = await courseService.GetPublishedCoursesAsync(page, pageSize);
        return Ok(courses);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> UpdateCourse(Guid id, [FromBody] UpdateCourseDto dto)
    {
        await updateCourseValidator.ValidateAndThrowAsync(dto);

        var course = await courseService.UpdateCourseAsync(id, dto, User.GetRequiredUserId());
        return Ok(course);
    }

    [HttpPost("{id:guid}/publish")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> PublishCourse(Guid id)
    {
        var course = await courseService.PublishCourseAsync(id, User.GetRequiredUserId());
        return Ok(new { message = "Course published successfully", course });
    }

    [HttpPost("{id:guid}/unpublish")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> UnpublishCourse(Guid id)
    {
        var course = await courseService.UnpublishCourseAsync(id, User.GetRequiredUserId());
        return Ok(new { message = "Course unpublished successfully", course });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> DeleteCourse(Guid id)
    {
        await courseService.DeleteCourseAsync(id, User.GetRequiredUserId());
        return Ok(new { message = "Course deleted successfully" });
    }
}
