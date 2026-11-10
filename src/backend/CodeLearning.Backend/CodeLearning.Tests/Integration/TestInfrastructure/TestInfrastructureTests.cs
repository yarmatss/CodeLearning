using CodeLearning.Infrastructure.Data;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace CodeLearning.Tests.Integration.TestInfrastructure;

public class TestInfrastructureTests(IntegrationTestWebAppFactory factory)
    : IClassFixture<IntegrationTestWebAppFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CookieAuthClient_AfterRegister_ShouldSendSecureAuthCookie()
    {
        // Arrange
        var client = factory.CreateCookieAuthClient();
        var register = await client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto());
        register.EnsureSuccessStatusCode();

        // Act
        var response = await client.GetAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnonymousClient_AfterRegister_ShouldNotSendCookies()
    {
        // Arrange
        var client = factory.CreateAnonymousClient();
        var register = await client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto());
        register.EnsureSuccessStatusCode();

        // Act
        var response = await client.GetAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnonymousClient_WithCookieHeader_ShouldAuthenticate()
    {
        // Arrange
        var client = factory.CreateAnonymousClient();
        var register = await client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto());
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me")
            .WithCookie("access_token", register.GetAccessToken());

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResetDatabaseAsync_ShouldClearApplicationTablesButKeepSeedData()
    {
        // Arrange
        var client = factory.CreateAnonymousClient();
        (await client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto()))
            .EnsureSuccessStatusCode();

        // Act
        await factory.ResetDatabaseAsync();

        // Assert
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Users.AnyAsync()).Should().BeFalse();
        (await db.Languages.AnyAsync()).Should().BeTrue();
        (await db.Tags.AnyAsync()).Should().BeTrue();
    }

    [Fact]
    public void CreateValidUser_ShouldUseOneEmailForAllFields()
    {
        // Act
        var user = TestDataBuilder.CreateValidUser();

        // Assert
        user.UserName.Should().Be(user.Email);
        user.NormalizedEmail.Should().Be(user.Email!.ToUpperInvariant());
        user.NormalizedUserName.Should().Be(user.NormalizedEmail);
    }
}
