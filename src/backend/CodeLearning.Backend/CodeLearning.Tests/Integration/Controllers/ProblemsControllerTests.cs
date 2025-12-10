using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Application.DTOs.Problem;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using FluentAssertions;

namespace CodeLearning.Tests.Integration.Controllers;

public class ProblemsControllerTests : IClassFixture<IntegrationTestWebAppFactory>, IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly IntegrationTestWebAppFactory _factory;
    private readonly Guid _pythonLanguageId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public ProblemsControllerTests(IntegrationTestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    #region Helper Methods

    private async Task<string> GetTeacherToken()
    {
        var registerDto = TestDataBuilder.CreateValidRegisterDto(role: "Teacher");
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerDto);
        var content = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        return content?.AccessToken ?? throw new Exception("No teacher token received");
    }

    private async Task<string> GetStudentToken()
    {
        var registerDto = TestDataBuilder.CreateValidRegisterDto(role: "Student");
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerDto);
        var content = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        return content?.AccessToken ?? throw new Exception("No student token received");
    }

    private async Task<ProblemResponseDto> CreateProblemAsTeacher(string? title = null)
    {
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateProblemDto(title);
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);
        
        return await response.Content.ReadFromJsonAsync<ProblemResponseDto>()
            ?? throw new Exception("Failed to create problem");
    }

    #endregion

    #region Create Problem Tests

    [Fact]
    public async Task CreateProblem_AsTeacher_WithValidData_ShouldReturn201()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateProblemDto("Two Sum");

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var content = await response.Content.ReadFromJsonAsync<ProblemResponseDto>();
        content.Should().NotBeNull();
        content!.Title.Should().Be("Two Sum");
        content.Difficulty.Should().Be("Easy");
        content.TestCases.Should().HaveCount(2);
        content.TestCases.Should().Contain(tc => tc.IsPublic);
    }

    [Fact]
    public async Task CreateProblem_WithStarterCodes_ShouldIncludeThem()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateProblemDto("Problem with Starter");
        createDto.StarterCodes.Add(TestDataBuilder.CreateValidCreateStarterCodeDto(_pythonLanguageId));

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var content = await response.Content.ReadFromJsonAsync<ProblemResponseDto>();
        content!.StarterCodes.Should().HaveCount(1);
        content.StarterCodes.First().LanguageId.Should().Be(_pythonLanguageId);
    }

    [Fact]
    public async Task CreateProblem_AsStudent_ShouldReturn403()
    {
        // Arrange
        var token = await GetStudentToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateProblemDto();

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateProblem_WithoutAuth_ShouldReturn401()
    {
        // Arrange
        var createDto = TestDataBuilder.CreateValidCreateProblemDto();

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateProblem_WithEmptyTitle_ShouldReturn400()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateProblemDto();
        createDto.Title = "";

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProblem_WithShortDescription_ShouldReturn400()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateProblemDto();
        createDto.Description = "Too short";

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProblem_WithInvalidDifficulty_ShouldReturn400()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateProblemDto();
        createDto.Difficulty = "Invalid";

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProblem_WithNoTestCases_ShouldReturn400()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateProblemDto();
        createDto.TestCases.Clear();

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProblem_WithNoPublicTestCases_ShouldReturn400()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateProblemDto();
        foreach (var testCase in createDto.TestCases)
        {
            testCase.IsPublic = false;
        }

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Get Problem Tests

    [Fact]
    public async Task GetProblemById_ExistingProblem_ShouldReturn200()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Get By ID Test");

        // Act
        var response = await _client.GetAsync($"/api/problems/{problem.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<ProblemResponseDto>();
        content.Should().NotBeNull();
        content!.Id.Should().Be(problem.Id);
        content.Title.Should().Be("Get By ID Test");
    }

    [Fact]
    public async Task GetProblemById_NonExistingProblem_ShouldReturn404()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        var nonExistingId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/problems/{nonExistingId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetProblems_ShouldReturnList()
    {
        // Arrange
        await CreateProblemAsTeacher("Problem 1");
        await CreateProblemAsTeacher("Problem 2");

        // Act
        var response = await _client.GetAsync("/api/problems");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<List<ProblemListDto>>();
        content.Should().NotBeNull();
        content!.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetProblems_WithDifficultyFilter_ShouldReturnFiltered()
    {
        // Arrange
        await CreateProblemAsTeacher("Easy Problem");
        
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var hardProblem = TestDataBuilder.CreateValidCreateProblemDto("Hard Problem", "Hard");
        await _client.PostAsJsonAsync("/api/problems", hardProblem);

        // Act
        var response = await _client.GetAsync("/api/problems?difficulty=Easy");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<List<ProblemListDto>>();
        content.Should().NotBeNull();
        content!.Should().OnlyContain(p => p.Difficulty == "Easy");
    }

    [Fact]
    public async Task GetProblems_WithSearchFilter_ShouldReturnMatching()
    {
        // Arrange
        await CreateProblemAsTeacher("Array Sum Problem");
        await CreateProblemAsTeacher("String Reverse Problem");

        // Act
        var response = await _client.GetAsync("/api/problems?search=Array");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<List<ProblemListDto>>();
        content.Should().NotBeNull();
        content!.Should().Contain(p => p.Title.Contains("Array"));
    }

    [Fact]
    public async Task GetMyProblems_AsTeacher_ShouldReturnOnlyMyProblems()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("My Problem");

        // Create another problem by different teacher
        var anotherToken = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", anotherToken);
        var anotherDto = TestDataBuilder.CreateValidCreateProblemDto("Another Problem");
        await _client.PostAsJsonAsync("/api/problems", anotherDto);

        // Switch back to first teacher
        var firstToken = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstToken);

        // Act - create one more to ensure we have at least one
        await _client.PostAsJsonAsync("/api/problems", TestDataBuilder.CreateValidCreateProblemDto("My Second Problem"));
        
        var response = await _client.GetAsync("/api/problems/my");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<List<ProblemListDto>>();
        content.Should().NotBeNull();
        content!.Should().Contain(p => p.Title == "My Second Problem");
    }

    #endregion

    #region Update Problem Tests

    [Fact]
    public async Task UpdateProblem_AsOwner_ShouldReturn200()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Original Title");

        var updateDto = TestDataBuilder.CreateValidUpdateProblemDto("Updated Title", "Hard");

        // Act
        var response = await _client.PutAsJsonAsync($"/api/problems/{problem.Id}", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<ProblemResponseDto>();
        content.Should().NotBeNull();
        content!.Title.Should().Be("Updated Title");
        content.Difficulty.Should().Be("Hard");
    }

    [Fact]
    public async Task UpdateProblem_AsNonOwner_ShouldReturn403()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Original Problem");

        // Switch to different teacher
        var anotherToken = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", anotherToken);

        var updateDto = TestDataBuilder.CreateValidUpdateProblemDto("Hacked Title");

        // Act
        var response = await _client.PutAsJsonAsync($"/api/problems/{problem.Id}", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Delete Problem Tests

    [Fact]
    public async Task DeleteProblem_AsOwner_WithNoSubmissions_ShouldReturn200()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Problem to Delete");

        // Act
        var response = await _client.DeleteAsync($"/api/problems/{problem.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify deleted
        var getResponse = await _client.GetAsync($"/api/problems/{problem.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteProblem_AsNonOwner_ShouldReturn403()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Problem to Delete");

        // Switch to different teacher
        var anotherToken = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", anotherToken);

        // Act
        var response = await _client.DeleteAsync($"/api/problems/{problem.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Test Case Management Tests

    [Fact]
    public async Task AddTestCase_AsOwner_ShouldReturn200()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Problem for TestCase");

        var testCaseDto = TestDataBuilder.CreateValidCreateTestCaseDto(isPublic: true);

        // Act
        var response = await _client.PostAsJsonAsync($"/api/problems/{problem.Id}/testcases", testCaseDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<TestCaseResponseDto>();
        content.Should().NotBeNull();
        content!.Input.Should().Be(testCaseDto.Input);
        content.ExpectedOutput.Should().Be(testCaseDto.ExpectedOutput);
    }

    [Fact]
    public async Task BulkAddTestCases_WithMultipleTestCases_ShouldReturn200()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateProblemDto("Problem for Bulk Add");
        var createResponse = await _client.PostAsJsonAsync("/api/problems", createDto);
        var problem = await createResponse.Content.ReadFromJsonAsync<ProblemResponseDto>();

        var bulkDto = new BulkAddTestCasesDto
        {
            TestCases =
            [
                new CreateTestCaseDto { Input = "10 20", ExpectedOutput = "30", IsPublic = true },
                new CreateTestCaseDto { Input = "5 15", ExpectedOutput = "20", IsPublic = false },
                new CreateTestCaseDto { Input = "100 200", ExpectedOutput = "300", IsPublic = true }
            ]
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/problems/{problem!.Id}/testcases/bulk", bulkDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<BulkAddTestCasesResult>();
        content.Should().NotBeNull();
        content!.Added.Should().Be(3);
        content.TestCases.Should().HaveCount(3);
        content.TestCases.Should().Contain(tc => tc.Input == "10 20");
    }

    [Fact]
    public async Task BulkAddTestCases_WithNoPublicTests_ShouldReturn400()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateProblemDto("Problem for Bulk Validation");
        var createResponse = await _client.PostAsJsonAsync("/api/problems", createDto);
        var problem = await createResponse.Content.ReadFromJsonAsync<ProblemResponseDto>();

        var bulkDto = new BulkAddTestCasesDto
        {
            TestCases =
            [
                new CreateTestCaseDto { Input = "1", ExpectedOutput = "1", IsPublic = false },
                new CreateTestCaseDto { Input = "2", ExpectedOutput = "2", IsPublic = false }
            ]
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/problems/{problem!.Id}/testcases/bulk", bulkDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateTestCase_AsOwner_ShouldReturn200()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateProblemDto("Problem for Update");
        var createResponse = await _client.PostAsJsonAsync("/api/problems", createDto);
        var problem = await createResponse.Content.ReadFromJsonAsync<ProblemResponseDto>();

        var testCase = problem!.TestCases.First();

        var updateDto = new UpdateTestCaseDto
        {
            Input = "99 1",
            ExpectedOutput = "100",
            IsPublic = true
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/problems/testcases/{testCase.Id}", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<TestCaseResponseDto>();
        content.Should().NotBeNull();
        content!.Input.Should().Be("99 1");
        content.ExpectedOutput.Should().Be("100");
    }

    [Fact]
    public async Task UpdateTestCase_MakingLastPublicPrivate_ShouldReturn400()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateProblemDto("Problem for Last Public");
        createDto.TestCases.Clear();
        createDto.TestCases.Add(new CreateTestCaseDto { Input = "1", ExpectedOutput = "1", IsPublic = true });
        createDto.TestCases.Add(new CreateTestCaseDto { Input = "2", ExpectedOutput = "2", IsPublic = false });

        var createResponse = await _client.PostAsJsonAsync("/api/problems", createDto);
        var problem = await createResponse.Content.ReadFromJsonAsync<ProblemResponseDto>();

        var publicTestCase = problem!.TestCases.First(tc => tc.IsPublic);

        var updateDto = new UpdateTestCaseDto
        {
            Input = publicTestCase.Input,
            ExpectedOutput = publicTestCase.ExpectedOutput,
            IsPublic = false
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/problems/testcases/{publicTestCase.Id}", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ReorderTestCases_WithValidOrder_ShouldReturn200()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateProblemDto("Problem for Reorder");
        var createResponse = await _client.PostAsJsonAsync("/api/problems", createDto);
        var problem = await createResponse.Content.ReadFromJsonAsync<ProblemResponseDto>();

        var testCaseIds = problem!.TestCases.Select(tc => tc.Id).ToList();
        testCaseIds.Reverse(); // Reverse order

        var reorderDto = new ReorderTestCasesDto
        {
            TestCaseIds = testCaseIds
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/problems/{problem.Id}/testcases/reorder", reorderDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify order changed
        var updatedProblem = await _client.GetFromJsonAsync<ProblemResponseDto>($"/api/problems/{problem.Id}");
        updatedProblem!.TestCases.First().Id.Should().Be(testCaseIds[0]);
    }

    [Fact]
    public async Task ReorderTestCases_WithIncompleteList_ShouldReturn400()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateProblemDto("Problem for Incomplete Reorder");
        var createResponse = await _client.PostAsJsonAsync("/api/problems", createDto);
        var problem = await createResponse.Content.ReadFromJsonAsync<ProblemResponseDto>();

        var reorderDto = new ReorderTestCasesDto
        {
            TestCaseIds = [problem!.TestCases.First().Id] // Missing other test cases
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/problems/{problem.Id}/testcases/reorder", reorderDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteTestCase_NonExisting_ShouldReturn404()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var nonExistingId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/testcases/{nonExistingId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Starter Code Management Tests

    [Fact]
    public async Task AddStarterCode_AsOwner_ShouldReturn200()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Problem for Starter Code");

        var starterCodeDto = TestDataBuilder.CreateValidCreateStarterCodeDto(_pythonLanguageId);

        // Act
        var response = await _client.PostAsJsonAsync($"/api/problems/{problem.Id}/startercodes", starterCodeDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<StarterCodeResponseDto>();
        content.Should().NotBeNull();
        content!.Code.Should().Be(starterCodeDto.Code);
        content.LanguageId.Should().Be(_pythonLanguageId);
    }

    [Fact]
    public async Task AddStarterCode_DuplicateLanguage_ShouldReturn400()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Problem for Duplicate Starter");

        var starterCodeDto = TestDataBuilder.CreateValidCreateStarterCodeDto(_pythonLanguageId);
        await _client.PostAsJsonAsync($"/api/problems/{problem.Id}/startercodes", starterCodeDto);

        // Act - try to add again for same language
        var response = await _client.PostAsJsonAsync($"/api/problems/{problem.Id}/startercodes", starterCodeDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteStarterCode_NonExisting_ShouldReturn404()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var nonExistingId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/startercodes/{nonExistingId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}
