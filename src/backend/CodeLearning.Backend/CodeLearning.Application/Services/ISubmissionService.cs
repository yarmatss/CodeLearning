using CodeLearning.Application.DTOs.Submission;

namespace CodeLearning.Application.Services;

public interface ISubmissionService
{
    Task<SubmissionResponseDto> SubmitCodeAsync(SubmitCodeDto dto, Guid studentId);
    Task<SubmissionResponseDto?> GetSubmissionAsync(Guid submissionId, Guid studentId);
    Task<List<SubmissionResponseDto>> GetMySubmissionsAsync(Guid problemId, Guid studentId);
}
