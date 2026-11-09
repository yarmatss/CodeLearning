using CodeLearning.Application.DTOs.Common;
using CodeLearning.Application.DTOs.Problem;

namespace CodeLearning.Application.Services;

public interface IProblemService
{
    Task<ProblemResponseDto> CreateProblemAsync(CreateProblemDto dto, Guid authorId);

    // Visible to the author, or to users enrolled in a published course that uses the problem
    Task<ProblemResponseDto> GetProblemByIdAsync(Guid problemId, Guid userId);
    Task<PagedResult<ProblemListDto>> GetProblemsAsync(ProblemListQuery query, Guid userId);
    Task<PagedResult<ProblemListDto>> GetMyProblemsAsync(ProblemListQuery query, Guid authorId);

    Task<ProblemResponseDto> UpdateProblemAsync(Guid problemId, UpdateProblemDto dto, Guid authorId);
    Task DeleteProblemAsync(Guid problemId, Guid authorId);
    Task<IEnumerable<TagResponseDto>> GetAllTagsAsync();

    // A problem used by a published course is locked: it, its test cases and starter codes can't change
    Task<bool> IsUsedByPublishedCourseAsync(Guid problemId);

    // Test Case Management
    Task<TestCaseResponseDto> AddTestCaseAsync(Guid problemId, CreateTestCaseDto dto, Guid authorId);
    Task<BulkAddTestCasesResult> AddTestCasesBulkAsync(Guid problemId, BulkAddTestCasesDto dto, Guid authorId);
    Task<TestCaseResponseDto> UpdateTestCaseAsync(Guid testCaseId, UpdateTestCaseDto dto, Guid authorId);
    Task DeleteTestCaseAsync(Guid testCaseId, Guid authorId);
    Task ReorderTestCasesAsync(Guid problemId, ReorderTestCasesDto dto, Guid authorId);

    // Starter Code Management
    Task<StarterCodeResponseDto> AddStarterCodeAsync(Guid problemId, CreateStarterCodeDto dto, Guid authorId);
    Task DeleteStarterCodeAsync(Guid starterCodeId, Guid authorId);
}
