using CodeLearning.Application.DTOs.Quiz;

namespace CodeLearning.Application.Services;

public interface IQuizService
{
    Task<QuizAttemptResultDto> SubmitQuizAsync(Guid quizId, SubmitQuizDto dto, Guid studentId);
    Task<QuizAttemptResultDto?> GetQuizAttemptAsync(Guid quizId, Guid studentId);
}
