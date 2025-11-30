using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using FluentAssertions;

namespace CodeLearning.Tests.Integration.Controllers;

public class AuthControllerTests : IClassFixture<IntegrationTestWebAppFactory>, IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly IntegrationTestWebAppFactory _factory;

    public AuthControllerTests(IntegrationTestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    #region Registration Tests

    [Fact]
    public async Task Register_WithValidStudentData_ShouldReturn200AndToken()
    {
        // Arrange
        var registerDto = TestDataBuilder.CreateValidRegisterDto(role: "Student");

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        content.Should().NotBeNull();
        content!.Email.Should().Be(registerDto.Email);
        content.Role.Should().Be("Student");
        content.FirstName.Should().Be(registerDto.FirstName);
        content.LastName.Should().Be(registerDto.LastName);
        content.Message.Should().Be("Registration successful");
    }

    [Fact]
    public async Task Register_WithValidTeacherData_ShouldReturn200AndCorrectRole()
    {
        // Arrange
        var registerDto = TestDataBuilder.CreateValidRegisterDto(role: "Teacher");

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        content!.Role.Should().Be("Teacher");
    }

    [Theory]
    [InlineData("", "Password must contain at least one uppercase")]
    [InlineData("weak", "Password must be at least 8 characters")]
    [InlineData("password", "Password must contain at least one uppercase")]
    [InlineData("PASSWORD", "Password must contain at least one digit")]
    [InlineData("Pass123", "Password must be at least 8 characters")]
    public async Task Register_WithWeakPassword_ShouldReturn400WithValidationError(string password, string expectedError)
    {
        // Arrange
        var registerDto = TestDataBuilder.CreateValidRegisterDto();
        registerDto.Password = password;
        registerDto.ConfirmPassword = password;

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().ContainEquivalentOf(expectedError);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ShouldReturn400()
    {
        // Arrange
        var email = "duplicate@test.com";
        var firstDto = TestDataBuilder.CreateValidRegisterDto(email: email);
        var secondDto = TestDataBuilder.CreateValidRegisterDto(email: email);

        await _client.PostAsJsonAsync("/api/auth/register", firstDto);

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/register", secondDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().ContainEquivalentOf("already exists");
    }

    [Fact]
    public async Task Register_WithPasswordMismatch_ShouldReturn400()
    {
        // Arrange
        var registerDto = TestDataBuilder.CreateValidRegisterDto();
        registerDto.ConfirmPassword = "DifferentPassword123";

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().ContainEquivalentOf("do not match");
    }

    #endregion

    #region Login Tests

    [Fact]
    public async Task Login_WithValidCredentials_ShouldReturn200WithToken()
    {
        // Arrange - Register first
        var email = "login@test.com";
        var password = "ValidPassword123";
        var registerDto = TestDataBuilder.CreateValidRegisterDto(email: email);
        registerDto.Password = password;
        registerDto.ConfirmPassword = password;
        
        await _client.PostAsJsonAsync("/api/auth/register", registerDto);

        var loginDto = TestDataBuilder.CreateValidLoginDto(email, password);

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        content.Should().NotBeNull();
        content!.AccessToken.Should().NotBeNullOrEmpty("response should contain access token");
        content.RefreshToken.Should().NotBeNullOrEmpty("response should contain refresh token");
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ShouldReturn401()
    {
        // Arrange
        var email = "invalid@test.com";
        var registerDto = TestDataBuilder.CreateValidRegisterDto(email: email);
        await _client.PostAsJsonAsync("/api/auth/register", registerDto);

        var loginDto = TestDataBuilder.CreateValidLoginDto(email, "WrongPassword123");

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().ContainEquivalentOf("Invalid email or password");
    }

    [Fact]
    public async Task Login_WithNonExistentUser_ShouldReturn401()
    {
        // Arrange
        var loginDto = TestDataBuilder.CreateValidLoginDto("nonexistent@test.com");

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region GetMe Tests

    [Fact]
    public async Task GetMe_WithoutAuthentication_ShouldReturn401()
    {
        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_WithValidToken_ShouldReturn200WithUserData()
    {
        // Arrange - Register and login
        var email = "me@test.com";
        var registerDto = TestDataBuilder.CreateValidRegisterDto(email: email, role: "Student");
        await _client.PostAsJsonAsync("/api/auth/register", registerDto);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", 
            TestDataBuilder.CreateValidLoginDto(email));
        var loginContent = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        
        var accessToken = loginContent?.AccessToken ?? throw new Exception("No token received");

        // Add Authorization header
        _client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", accessToken);

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<CurrentUserDto>();
        content.Should().NotBeNull();
        content!.Email.Should().Be(email);
        content.Role.Should().Be("Student");
        content.FirstName.Should().Be(registerDto.FirstName);
        content.LastName.Should().Be(registerDto.LastName);
    }

    #endregion

    #region Logout Tests

    [Fact]
    public async Task Logout_WithoutAuthentication_ShouldReturn401()
    {
        // Act
        var response = await _client.PostAsync("/api/auth/logout", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithValidToken_ShouldReturn200()
    {
        // Arrange
        var email = "logout@test.com";
        var registerDto = TestDataBuilder.CreateValidRegisterDto(email: email);
        await _client.PostAsJsonAsync("/api/auth/register", registerDto);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", 
            TestDataBuilder.CreateValidLoginDto(email));
        var loginContent = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        var accessToken = loginContent?.AccessToken ?? throw new Exception("No token");

        _client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", accessToken);

        // Act
        var response = await _client.PostAsync("/api/auth/logout", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().ContainEquivalentOf("Logout successful");
    }

    #endregion
}
