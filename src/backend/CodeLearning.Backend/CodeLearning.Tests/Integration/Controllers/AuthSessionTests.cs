using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Application.Services;
using CodeLearning.Core.Enums;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace CodeLearning.Tests.Integration.Controllers;

// Revocation, cookie-only auth and multi-session behaviour of the auth endpoints
public class AuthSessionTests : IClassFixture<IntegrationTestWebAppFactory>, IAsyncLifetime
{
    private readonly IntegrationTestWebAppFactory _factory;
    private readonly HttpClient _client;

    public AuthSessionTests(IntegrationTestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAnonymousClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    #region Blacklist bypass

    [Fact]
    public async Task RevokedCookieToken_WithNonBearerAuthorizationHeader_ShouldReturn401()
    {
        // Arrange - the old middleware read the junk header, found no JWT and let the cookie through
        var session = await RegisterAsync("bypass-header@test.com");
        await LogoutWithBearerAsync(session.AccessToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me")
            .WithCookie("access_token", session.AccessToken);
        request.Headers.TryAddWithoutValidation("Authorization", "x");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Bearer  {0}")]
    [InlineData("bearer {0}")]
    [InlineData("Bearer {0}  ")]
    public async Task RevokedToken_WithUnusualBearerFormatting_ShouldReturn401(string headerFormat)
    {
        // Arrange - "Bearer  x".Replace("Bearer ", "") left a leading space, so the old check could not parse it
        var session = await RegisterAsync($"bypass-{Guid.NewGuid():N}@test.com");
        await LogoutWithBearerAsync(session.AccessToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.TryAddWithoutValidation("Authorization", string.Format(headerFormat, session.AccessToken));

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RevokedToken_ShouldReturnProblemDetails()
    {
        // Arrange
        var session = await RegisterAsync("revoked-problem@test.com");
        await LogoutWithBearerAsync(session.AccessToken);

        // Act
        var response = await GetMeAsync(session.AccessToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":401", content);
        Assert.Contains("revoked", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingToken_ShouldReturnProblemDetails()
    {
        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    #endregion

    #region Cookie-only auth

    [Fact]
    public async Task CookieOnlyAuth_ShouldAuthenticateAndLogoutShouldEndIt()
    {
        // Arrange - a browser-like client: no Authorization header, only the HttpOnly cookies
        var client = _factory.CreateCookieAuthClient();
        var registerDto = TestDataBuilder.CreateValidRegisterDto(email: "cookie-only@test.com");
        await client.PostAsJsonAsync("/api/auth/register", registerDto);

        // Act & Assert - authenticated by cookie
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(registerDto.Email, (await me.Content.ReadFromJsonAsync<CurrentUserDto>())!.Email);

        // Refresh works by cookie too
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        // Logout clears the cookies
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task CookieOnlyLogout_ShouldRevokeTheAccessTokenInTheCookie()
    {
        // Arrange
        var client = _factory.CreateCookieAuthClient();
        await client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto(email: "cookie-logout@test.com"));
        var refresh = await client.PostAsync("/api/auth/refresh", null);
        var currentAccessToken = refresh.GetAccessToken();

        // Act
        await client.PostAsync("/api/auth/logout", null);

        // Assert
        using var replay = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me").WithCookie("access_token", currentAccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(replay)).StatusCode);
    }

    #endregion

    #region Multiple sessions

    [Fact]
    public async Task TwoSessionsOfOneUser_ShouldBothKeepWorking()
    {
        // Arrange - the same account signed in on two devices
        var email = "two-sessions@test.com";
        var deviceA = _factory.CreateCookieAuthClient();
        var deviceB = _factory.CreateCookieAuthClient();
        await deviceA.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto(email: email));
        await deviceB.PostAsJsonAsync("/api/auth/login", TestDataBuilder.CreateValidLoginDto(email));

        // Act - each refreshes in turn several times; the old single-session store let B evict A
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await deviceA.PostAsync("/api/auth/refresh", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await deviceB.PostAsync("/api/auth/refresh", null)).StatusCode);
        }

        // Assert
        Assert.Equal(HttpStatusCode.OK, (await deviceA.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await deviceB.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_ShouldEndOnlyItsOwnSession()
    {
        // Arrange
        var email = "logout-one@test.com";
        var deviceA = _factory.CreateCookieAuthClient();
        var deviceB = _factory.CreateCookieAuthClient();
        await deviceA.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto(email: email));
        var loginA = await deviceA.PostAsJsonAsync("/api/auth/login", TestDataBuilder.CreateValidLoginDto(email));
        var refreshTokenA = loginA.GetSetCookie("refresh_token").Value;
        await deviceB.PostAsJsonAsync("/api/auth/login", TestDataBuilder.CreateValidLoginDto(email));

        // Act
        Assert.Equal(HttpStatusCode.OK, (await deviceA.PostAsync("/api/auth/logout", null)).StatusCode);

        // Assert - A's refresh token is dead, B is untouched
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendRefreshAsync(refreshTokenA)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await deviceB.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await deviceB.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task LogoutWithBearerOnly_ShouldEndOnlyThatSession()
    {
        // Arrange - session A logs out with just its access token (no refresh cookie)
        var email = "logout-bearer-one@test.com";
        var sessionA = await RegisterAsync(email);
        var loginB = await _client.PostAsJsonAsync("/api/auth/login", TestDataBuilder.CreateValidLoginDto(email));
        var refreshTokenB = loginB.GetSetCookie("refresh_token").Value;

        // Act
        await LogoutWithBearerAsync(sessionA.AccessToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendRefreshAsync(sessionA.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendRefreshAsync(refreshTokenB)).StatusCode);
    }

    [Fact]
    public async Task RevokeAllSessions_ShouldInvalidateEveryRefreshToken()
    {
        // Arrange
        var email = "revoke-all@test.com";
        var sessionA = await RegisterAsync(email);
        var loginB = await _client.PostAsJsonAsync("/api/auth/login", TestDataBuilder.CreateValidLoginDto(email));
        var refreshTokenB = loginB.GetSetCookie("refresh_token").Value;

        // Act - what a password or security-stamp change does
        using (var scope = _factory.Services.CreateScope())
        {
            var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
            await authService.RevokeAllSessionsAsync(Guid.Parse(sessionA.UserId));
        }

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendRefreshAsync(sessionA.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendRefreshAsync(refreshTokenB)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentRefreshWithSameToken_ShouldSucceedOnlyOnce()
    {
        // Arrange
        var session = await RegisterAsync("refresh-race@test.com");

        // Act
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => SendRefreshAsync(session.RefreshToken)));

        // Assert - rotation is atomic, so exactly one request gets the new token
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
    }

    #endregion

    #region Refresh token expiry and replay

    [Fact]
    public async Task Refresh_WithExpiredRefreshToken_ShouldReturn401()
    {
        // Arrange - let the stored token expire in Redis, as it would after RefreshTokenExpirationDays
        var session = await RegisterAsync("refresh-expired@test.com");

        var redis = _factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        var key = $"refresh:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(session.RefreshToken)))}";
        Assert.True(await redis.KeyExistsAsync(key), "the refresh token should be stored under its hash");
        await redis.KeyExpireAsync(key, TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);

        // Act
        var response = await SendRefreshAsync(session.RefreshToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReplayingOldTokenAfterRotation_ShouldNotAffectRotatedSession()
    {
        // Arrange
        var session = await RegisterAsync("refresh-replay-session@test.com");
        var rotated = await SendRefreshAsync(session.RefreshToken);
        var newRefreshToken = rotated.GetSetCookie("refresh_token").Value;

        // Act
        var replay = await SendRefreshAsync(session.RefreshToken);

        // Assert - the replay fails; the legitimate holder of the new token carries on
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendRefreshAsync(newRefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Refresh_ShouldKeepTheSameSession()
    {
        // Arrange
        var session = await RegisterAsync("refresh-same-session@test.com");
        var rotated = await SendRefreshAsync(session.RefreshToken);

        // Act - logging out with the new access token only (no cookie) must end the rotated session
        await LogoutWithBearerAsync(rotated.GetAccessToken());

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendRefreshAsync(rotated.GetSetCookie("refresh_token").Value)).StatusCode);
    }

    #endregion

    #region Redis unavailable

    [Fact]
    public async Task RequestWithToken_WhenRedisIsDown_ShouldFailClosed()
    {
        // Arrange - Redis points at a closed port; commands fail immediately instead of queueing
        await using var redisDownFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IConnectionMultiplexer>();
                services.AddSingleton<IConnectionMultiplexer>(_ =>
                {
                    var options = ConfigurationOptions.Parse("127.0.0.1:1");
                    options.AbortOnConnectFail = false;
                    options.ConnectTimeout = 200;
                    options.BacklogPolicy = BacklogPolicy.FailFast;
                    return ConnectionMultiplexer.Connect(options);
                });
            }));

        // A valid token signed by this host (issuing one does not touch Redis)
        string accessToken;
        using (var scope = redisDownFactory.Services.CreateScope())
        {
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            accessToken = tokenService.GenerateAccessToken(TestDataBuilder.CreateValidUser(UserRole.Student), Guid.NewGuid()).Token;
        }

        var client = redisDownFactory.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("could not be verified", await response.Content.ReadAsStringAsync());
    }

    #endregion

    private sealed record Session(string UserId, string AccessToken, string RefreshToken);

    private async Task<Session> RegisterAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto(email: email));
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        return new Session(body!.UserId, response.GetAccessToken(), response.GetSetCookie("refresh_token").Value);
    }

    private async Task LogoutWithBearerAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> GetMeAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendRefreshAsync(string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
            .WithCookie("refresh_token", refreshToken);
        return await _client.SendAsync(request);
    }
}
