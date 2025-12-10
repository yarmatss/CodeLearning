using CodeLearning.Application.DTOs.Problem;
using CodeLearning.Application.Services;
using CodeLearning.Core.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProblemsController(
    IProblemService problemService,
    IValidator<CreateProblemDto> createValidator,
    IValidator<UpdateProblemDto> updateValidator,
    IValidator<UpdateTestCaseDto> updateTestCaseValidator,
    IValidator<BulkAddTestCasesDto> bulkAddTestCasesValidator,
    IValidator<ReorderTestCasesDto> reorderTestCasesValidator) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    public async Task<IActionResult> CreateProblem([FromBody] CreateProblemDto dto)
    {
        await createValidator.ValidateAndThrowAsync(dto);

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await problemService.CreateProblemAsync(dto, userId);

        return CreatedAtAction(nameof(GetProblem), new { id = result.Id }, result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetProblem(Guid id)
    {
        var result = await problemService.GetProblemByIdAsync(id);
        return Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetProblems(
        [FromQuery] string? difficulty = null,
        [FromQuery] Guid? tagId = null,
        [FromQuery] string? search = null)
    {
        var results = await problemService.GetProblemsAsync(difficulty, tagId, search);
        return Ok(results);
    }

    [HttpGet("my")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    public async Task<IActionResult> GetMyProblems()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var results = await problemService.GetMyProblemsAsync(userId);
        return Ok(results);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    public async Task<IActionResult> UpdateProblem(Guid id, [FromBody] UpdateProblemDto dto)
    {
        await updateValidator.ValidateAndThrowAsync(dto);

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await problemService.UpdateProblemAsync(id, dto, userId);

        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    public async Task<IActionResult> DeleteProblem(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await problemService.DeleteProblemAsync(id, userId);

        return Ok(new { message = "Problem deleted successfully" });
    }

    #region Test Cases Management

    [HttpPost("{id:guid}/testcases")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    public async Task<IActionResult> AddTestCase(Guid id, [FromBody] CreateTestCaseDto dto)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await problemService.AddTestCaseAsync(id, dto, userId);

        return Ok(result);
    }

    [HttpPost("{id:guid}/testcases/bulk")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    public async Task<IActionResult> AddTestCasesBulk(Guid id, [FromBody] BulkAddTestCasesDto dto)
    {
        await bulkAddTestCasesValidator.ValidateAndThrowAsync(dto);

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await problemService.AddTestCasesBulkAsync(id, dto, userId);

        return Ok(result);
    }

    [HttpPut("testcases/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    public async Task<IActionResult> UpdateTestCase(Guid id, [FromBody] UpdateTestCaseDto dto)
    {
        await updateTestCaseValidator.ValidateAndThrowAsync(dto);

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await problemService.UpdateTestCaseAsync(id, dto, userId);

        return Ok(result);
    }

    [HttpDelete("testcases/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    public async Task<IActionResult> DeleteTestCase(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await problemService.DeleteTestCaseAsync(id, userId);

        return Ok(new { message = "Test case deleted successfully" });
    }

    [HttpPut("{id:guid}/testcases/reorder")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    public async Task<IActionResult> ReorderTestCases(Guid id, [FromBody] ReorderTestCasesDto dto)
    {
        await reorderTestCasesValidator.ValidateAndThrowAsync(dto);

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await problemService.ReorderTestCasesAsync(id, dto, userId);

        return Ok(new { message = "Test cases reordered successfully" });
    }

    #endregion

    #region Starter Codes Management

    [HttpPost("{id:guid}/startercodes")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    public async Task<IActionResult> AddStarterCode(Guid id, [FromBody] CreateStarterCodeDto dto)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await problemService.AddStarterCodeAsync(id, dto, userId);

        return Ok(result);
    }

    [HttpDelete("startercodes/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    public async Task<IActionResult> DeleteStarterCode(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await problemService.DeleteStarterCodeAsync(id, userId);

        return Ok(new { message = "Starter code deleted successfully" });
    }

    #endregion
}
