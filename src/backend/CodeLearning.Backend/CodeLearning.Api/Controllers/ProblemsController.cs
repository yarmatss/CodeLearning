using CodeLearning.Api.Extensions;
using CodeLearning.Application.DTOs.Common;
using CodeLearning.Application.DTOs.Problem;
using CodeLearning.Application.Services;
using CodeLearning.Core.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProblemsController(
    IProblemService problemService,
    IValidator<CreateProblemDto> createValidator,
    IValidator<UpdateProblemDto> updateValidator,
    IValidator<ProblemListQuery> listQueryValidator,
    IValidator<CreateTestCaseDto> createTestCaseValidator,
    IValidator<UpdateTestCaseDto> updateTestCaseValidator,
    IValidator<BulkAddTestCasesDto> bulkAddTestCasesValidator,
    IValidator<ReorderTestCasesDto> reorderTestCasesValidator,
    IValidator<CreateStarterCodeDto> createStarterCodeValidator) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    [ProducesResponseType(typeof(ProblemResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateProblem([FromBody] CreateProblemDto dto)
    {
        await createValidator.ValidateAndThrowAsync(dto);

        var userId = User.GetRequiredUserId();
        var result = await problemService.CreateProblemAsync(dto, userId);

        return CreatedAtAction(nameof(GetProblem), new { id = result.Id }, result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProblemResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProblem(Guid id)
    {
        var userId = User.GetRequiredUserId();
        var result = await problemService.GetProblemByIdAsync(id, userId);
        return Ok(result);
    }

    // The caller's own problems plus problems in published courses the caller is enrolled in
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProblemListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetProblems([FromQuery] ProblemListQuery query)
    {
        await listQueryValidator.ValidateAndThrowAsync(query);

        var userId = User.GetRequiredUserId();
        var results = await problemService.GetProblemsAsync(query, userId);
        return Ok(results);
    }

    [HttpGet("my")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    [ProducesResponseType(typeof(PagedResult<ProblemListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMyProblems([FromQuery] ProblemListQuery query)
    {
        await listQueryValidator.ValidateAndThrowAsync(query);

        var userId = User.GetRequiredUserId();
        var results = await problemService.GetMyProblemsAsync(query, userId);
        return Ok(results);
    }

    [HttpGet("tags")]
    public async Task<IActionResult> GetTags()
    {
        var tags = await problemService.GetAllTagsAsync();
        return Ok(tags);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    [ProducesResponseType(typeof(ProblemResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateProblem(Guid id, [FromBody] UpdateProblemDto dto)
    {
        await updateValidator.ValidateAndThrowAsync(dto);

        var userId = User.GetRequiredUserId();
        var result = await problemService.UpdateProblemAsync(id, dto, userId);

        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteProblem(Guid id)
    {
        var userId = User.GetRequiredUserId();
        await problemService.DeleteProblemAsync(id, userId);

        return Ok(new { message = "Problem deleted successfully" });
    }

    #region Test Cases Management

    [HttpPost("{id:guid}/testcases")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    [ProducesResponseType(typeof(TestCaseResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddTestCase(Guid id, [FromBody] CreateTestCaseDto dto)
    {
        await createTestCaseValidator.ValidateAndThrowAsync(dto);

        var userId = User.GetRequiredUserId();
        var result = await problemService.AddTestCaseAsync(id, dto, userId);

        return Ok(result);
    }

    [HttpPost("{id:guid}/testcases/bulk")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    [ProducesResponseType(typeof(BulkAddTestCasesResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddTestCasesBulk(Guid id, [FromBody] BulkAddTestCasesDto dto)
    {
        await bulkAddTestCasesValidator.ValidateAndThrowAsync(dto);

        var userId = User.GetRequiredUserId();
        var result = await problemService.AddTestCasesBulkAsync(id, dto, userId);

        return Ok(result);
    }

    [HttpPut("testcases/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    [ProducesResponseType(typeof(TestCaseResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateTestCase(Guid id, [FromBody] UpdateTestCaseDto dto)
    {
        await updateTestCaseValidator.ValidateAndThrowAsync(dto);

        var userId = User.GetRequiredUserId();
        var result = await problemService.UpdateTestCaseAsync(id, dto, userId);

        return Ok(result);
    }

    [HttpDelete("testcases/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteTestCase(Guid id)
    {
        var userId = User.GetRequiredUserId();
        await problemService.DeleteTestCaseAsync(id, userId);

        return Ok(new { message = "Test case deleted successfully" });
    }

    [HttpPut("{id:guid}/testcases/reorder")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReorderTestCases(Guid id, [FromBody] ReorderTestCasesDto dto)
    {
        await reorderTestCasesValidator.ValidateAndThrowAsync(dto);

        var userId = User.GetRequiredUserId();
        await problemService.ReorderTestCasesAsync(id, dto, userId);

        return Ok(new { message = "Test cases reordered successfully" });
    }

    #endregion

    #region Starter Codes Management

    [HttpPost("{id:guid}/startercodes")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    [ProducesResponseType(typeof(StarterCodeResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddStarterCode(Guid id, [FromBody] CreateStarterCodeDto dto)
    {
        await createStarterCodeValidator.ValidateAndThrowAsync(dto);

        var userId = User.GetRequiredUserId();
        var result = await problemService.AddStarterCodeAsync(id, dto, userId);

        return Ok(result);
    }

    [HttpDelete("startercodes/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Teacher))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteStarterCode(Guid id)
    {
        var userId = User.GetRequiredUserId();
        await problemService.DeleteStarterCodeAsync(id, userId);

        return Ok(new { message = "Starter code deleted successfully" });
    }

    #endregion
}
