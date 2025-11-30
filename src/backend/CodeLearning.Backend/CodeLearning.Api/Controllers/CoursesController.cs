using CodeLearning.Application.DTOs.Course;
using CodeLearning.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CoursesController : ControllerBase
{
    private readonly ICourseService _courseService;
    private readonly IValidator<CreateCourseDto> _createCourseValidator;
    private readonly IValidator<UpdateCourseDto> _updateCourseValidator;

    public CoursesController(
        ICourseService courseService,
        IValidator<CreateCourseDto> createCourseValidator,
        IValidator<UpdateCourseDto> updateCourseValidator)
    {
        _courseService = courseService;
        _createCourseValidator = createCourseValidator;
        _updateCourseValidator = updateCourseValidator;
    }

    [HttpPost]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> CreateCourse([FromBody] CreateCourseDto dto)
    {
        var validationResult = await _createCourseValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var userId = GetCurrentUserId();
        var course = await _courseService.CreateCourseAsync(dto, userId);

        return CreatedAtAction(nameof(GetCourseById), new { id = course.Id }, course);
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCourseById(Guid id)
    {
        try
        {
            var course = await _courseService.GetCourseByIdAsync(id);
            return Ok(course);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("my-courses")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> GetMyCourses()
    {
        var userId = GetCurrentUserId();
        var courses = await _courseService.GetInstructorCoursesAsync(userId);
        return Ok(courses);
    }

    [HttpGet("published")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublishedCourses()
    {
        var courses = await _courseService.GetPublishedCoursesAsync();
        return Ok(courses);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> UpdateCourse(Guid id, [FromBody] UpdateCourseDto dto)
    {
        var validationResult = await _updateCourseValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var userId = GetCurrentUserId();
            var course = await _courseService.UpdateCourseAsync(id, dto, userId);
            return Ok(course);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id}/publish")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> PublishCourse(Guid id)
    {
        try
        {
            var userId = GetCurrentUserId();
            var course = await _courseService.PublishCourseAsync(id, userId);
            return Ok(new { message = "Course published successfully", course });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> DeleteCourse(Guid id)
    {
        try
        {
            var userId = GetCurrentUserId();
            await _courseService.DeleteCourseAsync(id, userId);
            return Ok(new { message = "Course deleted successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User not authenticated");
        return Guid.Parse(userIdClaim);
    }
}
