using CodeLearning.Application.DTOs.Problem;

namespace CodeLearning.Application.Services;

public interface IProblemService
{
    Task<ProblemResponseDto> CreateProblemAsync(CreateProblemDto dto, Guid authorId);
    Task<ProblemResponseDto> GetProblemByIdAsync(Guid problemId);
    Task<IEnumerable<ProblemListDto>> GetProblemsAsync(string? difficulty = null, Guid? tagId = null, string? search = null);
    Task<IEnumerable<ProblemListDto>> GetMyProblemsAsync(Guid authorId);
    Task<ProblemResponseDto> UpdateProblemAsync(Guid problemId, UpdateProblemDto dto, Guid authorId);
    Task DeleteProblemAsync(Guid problemId, Guid authorId);
    Task<IEnumerable<TagResponseDto>> GetAllTagsAsync();
    
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
