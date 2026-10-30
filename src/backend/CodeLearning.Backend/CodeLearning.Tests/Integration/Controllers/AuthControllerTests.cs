using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Application.Services;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

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
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(content);
        Assert.Equal(registerDto.Email, content.Email);
        Assert.Equal("Student", content.Role);
        Assert.Equal(registerDto.FirstName, content.FirstName);
        Assert.Equal(registerDto.LastName, content.LastName);
        Assert.Equal("Registration successful", content.Message);
    }

    [Fact]
    public async Task Register_WithValidTeacherData_ShouldReturn200AndCorrectRole()
    {
        // Arrange
        var registerDto = TestDataBuilder.CreateValidRegisterDto(role: "Teacher");

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerDto);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.Equal("Teacher", content!.Role);
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
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedError, content, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("already exists", content, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("do not match", content, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Login Tests

    [Fact]
    public async Task Login_WithValidCredentials_ShouldSetHttpOnlyTokenCookiesWithoutExposingTokensInBody()
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
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var accessCookie = response.GetSetCookie("access_token");
        var refreshCookie = response.GetSetCookie("refresh_token");
        Assert.False(string.IsNullOrEmpty(accessCookie.Value), "access token cookie should be set");
        Assert.False(string.IsNullOrEmpty(refreshCookie.Value), "refresh token cookie should be set");
        Assert.Contains("httponly", accessCookie.Attributes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", refreshCookie.Attributes, StringComparison.OrdinalIgnoreCase);

        // Tokens readable from the body would defeat HttpOnly: any XSS could exfiltrate them
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(accessCookie.Value, body);
        Assert.DoesNotContain(refreshCookie.Value, body);

        var content = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(content);
        Assert.Equal(email, content.Email);
        Assert.True(content.AccessTokenExpiresAt > DateTimeOffset.UtcNow, "access token expiry should be in the future");
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
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid email or password", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WithNonExistentUser_ShouldReturn401()
    {
        // Arrange
        var loginDto = TestDataBuilder.CreateValidLoginDto("nonexistent@test.com");

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginDto);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region GetMe Tests

    [Fact]
    public async Task GetMe_WithoutAuthentication_ShouldReturn401()
    {
        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

        var accessToken = loginResponse.GetAccessToken();

        // Add Authorization header
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<CurrentUserDto>();
        Assert.NotNull(content);
        Assert.Equal(email, content.Email);
        Assert.Equal("Student", content.Role);
        Assert.Equal(registerDto.FirstName, content.FirstName);
        Assert.Equal(registerDto.LastName, content.LastName);
    }

    #endregion

    #region Logout Tests

    [Fact]
    public async Task Logout_WithoutAuthentication_ShouldReturn401()
    {
        // Act
        var response = await _client.PostAsync("/api/auth/logout", null);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
        var accessToken = loginResponse.GetAccessToken();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        // Act
        var response = await _client.PostAsync("/api/auth/logout", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Logout successful", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Logout_WithoutRefreshCookie_ShouldStillRevokeTokens()
    {
        // Arrange - only the bearer header is sent, as by a client that lost its refresh cookie
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var email = "logout-no-cookie@test.com";
        await client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto(email: email));

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", TestDataBuilder.CreateValidLoginDto(email));
        var accessToken = loginResponse.GetAccessToken();
        var refreshToken = loginResponse.GetSetCookie("refresh_token").Value;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // Act
        var logoutResponse = await client.PostAsync("/api/auth/logout", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);

        // Checked directly: a refresh request would already be rejected for its revoked access token
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        Assert.False(await tokenService.ValidateRefreshTokenAsync(refreshToken, Guid.Parse(loginBody!.UserId)));
    }

    #endregion

    #region Refresh Tests

    [Fact]
    public async Task Refresh_WithValidRefreshToken_ShouldRotateCookiesWithoutExposingTokensInBody()
    {
        // Arrange - cookies are sent by hand, so the client must not manage them itself
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var email = "refresh@test.com";
        await client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto(email: email));

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", TestDataBuilder.CreateValidLoginDto(email));
        var accessToken = loginResponse.GetAccessToken();
        var refreshToken = loginResponse.GetSetCookie("refresh_token").Value;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Cookie", $"refresh_token={Uri.EscapeDataString(refreshToken)}");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var newAccessToken = response.GetAccessToken();
        var newRefreshToken = response.GetSetCookie("refresh_token").Value;
        Assert.NotEqual(refreshToken, newRefreshToken);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(newAccessToken, body);
        Assert.DoesNotContain(newRefreshToken, body);
        Assert.Contains("accessTokenExpiresAt", body);
    }

    [Fact]
    public async Task Refresh_WithInvalidRefreshToken_ShouldReturn401()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var email = "refresh-invalid@test.com";
        await client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto(email: email));

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", TestDataBuilder.CreateValidLoginDto(email));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginResponse.GetAccessToken());
        request.Headers.Add("Cookie", "refresh_token=not-the-stored-token");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion
}
