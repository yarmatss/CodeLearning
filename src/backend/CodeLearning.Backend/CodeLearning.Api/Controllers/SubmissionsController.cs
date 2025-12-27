using CodeLearning.Application.DTOs.Submission;
using CodeLearning.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SubmissionsController(
    ISubmissionService submissionService,
    IValidator<SubmitCodeDto> submitValidator,
    ILogger<SubmissionsController> logger) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(SubmissionResponseDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SubmitCode([FromBody] SubmitCodeDto dto)
    {
        await submitValidator.ValidateAndThrowAsync(dto);
        
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var studentId))
        {
            return Unauthorized();
        }

        try
        {
            var submission = await submissionService.SubmitCodeAsync(dto, studentId);
            
            logger.LogInformation(
                "Submission {SubmissionId} created by student {StudentId} for problem {ProblemId}",
                submission.Id,
                studentId,
                dto.ProblemId);

            return Accepted(submission);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Invalid submission by student {StudentId}", studentId);
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(SubmissionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSubmission(Guid id)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var studentId))
        {
            return Unauthorized();
        }

        var submission = await submissionService.GetSubmissionAsync(id, studentId);

        if (submission == null)
        {
            return NotFound();
        }

        return Ok(submission);
    }

    [HttpGet("problem/{problemId}")]
    [ProducesResponseType(typeof(List<SubmissionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMySubmissions(Guid problemId)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var studentId))
        {
            return Unauthorized();
        }

        var submissions = await submissionService.GetMySubmissionsAsync(problemId, studentId);

        return Ok(submissions);
    }
}
