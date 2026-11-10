using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
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
        _client = factory.CreateAnonymousClient();
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
    [InlineData("PASSWORD123", "Password must contain at least one lowercase letter")]
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

    [Fact]
    public async Task Login_ToLockedAccount_ShouldReturnSameGeneric401AsWrongPassword()
    {
        // Arrange - Identity locks the account after 5 failed attempts
        var email = "locked@test.com";
        await _client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto(email: email));

        for (var i = 0; i < 5; i++)
        {
            await _client.PostAsJsonAsync("/api/auth/login", TestDataBuilder.CreateValidLoginDto(email, "WrongPassword123"));
        }

        // Act - the correct password, but the account is locked
        var response = await _client.PostAsJsonAsync("/api/auth/login", TestDataBuilder.CreateValidLoginDto(email));

        // Assert - nothing tells an attacker the account exists or is locked
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid email or password", content);
        Assert.DoesNotContain("lock", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_AccessCookieShouldExpireWithAccessToken_NotWithRefreshToken()
    {
        // Arrange
        var email = "cookie-expiry@test.com";
        await _client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto(email: email));

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", TestDataBuilder.CreateValidLoginDto(email));

        // Assert
        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        var accessExpires = ParseCookieExpires(response.GetSetCookie("access_token").Attributes);
        var refreshExpires = ParseCookieExpires(response.GetSetCookie("refresh_token").Attributes);

        Assert.Equal(body!.AccessTokenExpiresAt.ToUnixTimeSeconds(), accessExpires.ToUnixTimeSeconds());
        Assert.True(refreshExpires - accessExpires > TimeSpan.FromDays(6), "refresh cookie should outlive the access cookie by days");
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

    [Fact]
    public async Task GetMe_ShouldReturnTheAccessTokenExpiryFromLogin()
    {
        // Arrange
        var email = "me-expiry@test.com";
        await _client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto(email: email));
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", TestDataBuilder.CreateValidLoginDto(email));
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginResponse.GetAccessToken());

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"accessTokenExpiresAt\":", json);

        var content = await response.Content.ReadFromJsonAsync<CurrentUserDto>();
        Assert.NotNull(content?.AccessTokenExpiresAt);
        Assert.Equal(login!.AccessTokenExpiresAt, content.AccessTokenExpiresAt);
        Assert.Equal(TimeSpan.Zero, content.AccessTokenExpiresAt!.Value.Offset);
        Assert.True(content.AccessTokenExpiresAt > DateTimeOffset.UtcNow);
    }

    #endregion

    #region Logout Tests

    [Fact]
    public async Task Logout_WithoutAnyTokens_ShouldReturn200AndClearCookies()
    {
        // Act
        var response = await _client.PostAsync("/api/auth/logout", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("expires=", response.GetSetCookie("refresh_token").Attributes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Logout_WithOnlyRefreshCookie_ShouldRevokeRefreshToken()
    {
        // Arrange - the access token has expired, so only the refresh cookie identifies the session
        var client = _factory.CreateAnonymousClient();

        var email = "logout-expired@test.com";
        await client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto(email: email));
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", TestDataBuilder.CreateValidLoginDto(email));
        var refreshToken = loginResponse.GetSetCookie("refresh_token").Value;

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Add("Cookie", $"refresh_token={Uri.EscapeDataString(refreshToken)}");

        // Act
        var logoutResponse = await client.SendAsync(logoutRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendRefreshAsync(client, refreshToken)).StatusCode);
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
        var client = _factory.CreateAnonymousClient();

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

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendRefreshAsync(client, refreshToken)).StatusCode);
    }

    #endregion

    #region Refresh Tests

    [Fact]
    public async Task Refresh_WithValidRefreshToken_ShouldRotateCookiesWithoutExposingTokensInBody()
    {
        // Arrange - cookies are sent by hand, so the client must not manage them itself
        var client = _factory.CreateAnonymousClient();

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
        var client = _factory.CreateAnonymousClient();

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

    [Fact]
    public async Task Refresh_WithoutAccessToken_ShouldSucceed()
    {
        // Arrange - the browser may no longer send an access token at all
        var client = _factory.CreateAnonymousClient();

        var email = "refresh-no-access@test.com";
        await client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto(email: email));
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", TestDataBuilder.CreateValidLoginDto(email));

        // Act
        var response = await SendRefreshAsync(client, loginResponse.GetSetCookie("refresh_token").Value);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReusingRotatedRefreshToken_ShouldReturn401()
    {
        // Arrange
        var client = _factory.CreateAnonymousClient();

        var email = "refresh-replay@test.com";
        await client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto(email: email));
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", TestDataBuilder.CreateValidLoginDto(email));
        var refreshToken = loginResponse.GetSetCookie("refresh_token").Value;

        Assert.Equal(HttpStatusCode.OK, (await SendRefreshAsync(client, refreshToken)).StatusCode);

        // Act
        var replay = await SendRefreshAsync(client, refreshToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task Refresh_ShouldIssueTokensForRefreshTokenOwner_IgnoringAccessTokenIdentity()
    {
        // Arrange - another user's access token must not decide whose tokens are issued
        var client = _factory.CreateAnonymousClient();

        var ownerEmail = "refresh-owner@test.com";
        await client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto(email: ownerEmail));
        var ownerLogin = await client.PostAsJsonAsync("/api/auth/login", TestDataBuilder.CreateValidLoginDto(ownerEmail));

        var otherRegister = await client.PostAsJsonAsync("/api/auth/register",
            TestDataBuilder.CreateValidRegisterDto(email: "refresh-other@test.com"));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", otherRegister.GetAccessToken());
        request.Headers.Add("Cookie", $"refresh_token={Uri.EscapeDataString(ownerLogin.GetSetCookie("refresh_token").Value)}");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", response.GetAccessToken());
        var me = await (await client.SendAsync(meRequest)).Content.ReadFromJsonAsync<CurrentUserDto>();
        Assert.Equal(ownerEmail, me!.Email);
    }

    #endregion

    #region Rate Limiting Tests

    [Fact]
    public async Task Login_ExceedingAuthRateLimit_ShouldReturn429()
    {
        // Arrange - a host with a low limit, so the test does not depend on the configured value
        await using var limitedFactory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("AuthRateLimit:PermitLimit", "3"));
        var client = limitedFactory.CreateAnonymousClient();
        var loginDto = TestDataBuilder.CreateValidLoginDto("rate-limit@test.com");

        for (var i = 0; i < 3; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", loginDto);
        }

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", loginDto);

        // Assert
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Contains("Too many login attempts", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Refresh_ShouldNotCountTowardsAuthRateLimit()
    {
        // Arrange - every signed-in client refreshes periodically, so a shared limit must not block it
        await using var limitedFactory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("AuthRateLimit:PermitLimit", "2"));
        var client = limitedFactory.CreateAnonymousClient();

        var email = "refresh-unthrottled@test.com";
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto(email: email));
        var refreshToken = registerResponse.GetSetCookie("refresh_token").Value;

        // Act - each refresh rotates the token, so the next request sends the new one
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            var response = await SendRefreshAsync(client, refreshToken);
            statuses.Add(response.StatusCode);
            if (response.IsSuccessStatusCode)
            {
                refreshToken = response.GetSetCookie("refresh_token").Value;
            }
        }

        // Assert
        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.OK, status));
    }

    [Fact]
    public async Task Login_RateLimit_ShouldBePerEmail_SoOtherAccountsFromSameIpAreNotBlocked()
    {
        // Arrange
        await using var limitedFactory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("AuthRateLimit:PermitLimit", "2"));
        var client = limitedFactory.CreateAnonymousClient();

        var targeted = TestDataBuilder.CreateValidLoginDto("targeted@test.com", "WrongPassword123");
        for (var i = 0; i < 2; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", targeted);
        }

        // Act - same email, differently cased, then a different account from the same IP
        var sameAccount = await client.PostAsJsonAsync("/api/auth/login",
            TestDataBuilder.CreateValidLoginDto(" TARGETED@test.com", "WrongPassword123"));
        var otherAccount = await client.PostAsJsonAsync("/api/auth/login",
            TestDataBuilder.CreateValidLoginDto("someone-else@test.com", "WrongPassword123"));

        // Assert
        Assert.Equal(HttpStatusCode.TooManyRequests, sameAccount.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otherAccount.StatusCode);
    }

    [Fact]
    public async Task Login_ManyEmailsFromOneIp_ShouldHitPerIpCeiling()
    {
        // Arrange - password spraying: one attempt per account, many accounts
        await using var limitedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AuthRateLimit:PermitLimit", "5");
            builder.UseSetting("AuthRateLimit:LoginPerIpPermitLimit", "3");
        });
        var client = limitedFactory.CreateAnonymousClient();

        for (var i = 0; i < 3; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login",
                TestDataBuilder.CreateValidLoginDto($"spray{i}@test.com", "WrongPassword123"));
        }

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login",
            TestDataBuilder.CreateValidLoginDto("spray-last@test.com", "WrongPassword123"));

        // Assert
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Contains("Too many login attempts", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Register_ExceedingAuthRateLimit_ShouldReturn429WithRegistrationMessage()
    {
        // Arrange
        await using var limitedFactory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("AuthRateLimit:PermitLimit", "1"));
        var client = limitedFactory.CreateAnonymousClient();

        await client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto());

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto());

        // Assert
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Contains("Too many registration attempts", await response.Content.ReadAsStringAsync());
    }

    #endregion

    private static DateTimeOffset ParseCookieExpires(string attributes)
    {
        var expires = attributes.Split(';')
            .Select(a => a.Trim())
            .Single(a => a.StartsWith("expires=", StringComparison.OrdinalIgnoreCase));

        return DateTimeOffset.Parse(expires["expires=".Length..], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<HttpResponseMessage> SendRefreshAsync(HttpClient client, string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"refresh_token={Uri.EscapeDataString(refreshToken)}");
        return await client.SendAsync(request);
    }
}
