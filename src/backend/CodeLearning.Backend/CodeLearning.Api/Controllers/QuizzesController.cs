using CodeLearning.Api.Extensions;
using CodeLearning.Application.DTOs.Quiz;
using CodeLearning.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/quizzes")]
[Authorize(Roles = "Student")]
public class QuizzesController(
    IQuizService quizService,
    IValidator<SubmitQuizDto> submitQuizValidator,
    ILogger<QuizzesController> logger) : ControllerBase
{
    [HttpPost("{quizId}/submit")]
    [ProducesResponseType(typeof(QuizAttemptResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitQuiz(Guid quizId, [FromBody] SubmitQuizDto dto)
    {
        await submitQuizValidator.ValidateAndThrowAsync(dto);

        var studentId = User.GetRequiredUserId();

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

    [HttpGet("{quizId}/attempt")]
    [ProducesResponseType(typeof(QuizAttemptResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetQuizAttempt(Guid quizId)
    {
        var studentId = User.GetRequiredUserId();

        var attempt = await quizService.GetQuizAttemptAsync(quizId, studentId);

        if (attempt == null)
        {
            return NotFound(new { message = "No attempt found for this quiz" });
        }

        return Ok(attempt);
    }
}
