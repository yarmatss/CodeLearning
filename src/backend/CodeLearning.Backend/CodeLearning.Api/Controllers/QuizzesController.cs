using CodeLearning.Application.DTOs.Quiz;
using CodeLearning.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/quizzes")]
[Authorize(Roles = "Student")]
public class QuizzesController(
    IQuizService quizService,
    ILogger<QuizzesController> logger) : ControllerBase
{
    [HttpPost("{quizId}/submit")]
    [ProducesResponseType(typeof(QuizAttemptResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitQuiz(Guid quizId, [FromBody] SubmitQuizDto dto)
    {
        var studentId = GetCurrentUserId();

        try
        {
            var result = await quizService.SubmitQuizAsync(quizId, dto, studentId);

            logger.LogInformation(
                "Student {StudentId} submitted quiz {QuizId} with score {Score}/{MaxScore} ({Percentage}%)",
                studentId,
                quizId,
                result.Score,
                result.MaxScore,
                result.Percentage);

            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            logger.LogWarning(ex, "Quiz {QuizId} not found", quizId);
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Invalid quiz submission by student {StudentId}", studentId);
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{quizId}/attempt")]
    [ProducesResponseType(typeof(QuizAttemptResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetQuizAttempt(Guid quizId)
    {
        var studentId = GetCurrentUserId();

        var attempt = await quizService.GetQuizAttemptAsync(quizId, studentId);

        if (attempt == null)
        {
            return NotFound(new { message = "No attempt found for this quiz" });
        }

        return Ok(attempt);
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User not authenticated");
        return Guid.Parse(userIdClaim);
    }
}
