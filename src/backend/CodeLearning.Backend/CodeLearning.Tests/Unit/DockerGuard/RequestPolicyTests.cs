extern alias DockerGuard;

using DockerGuard::CodeLearning.DockerGuard;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace CodeLearning.Tests.Unit.DockerGuard;

public class RequestPolicyTests
{
    private static readonly GuardOptions Options = new()
    {
        AllowedImages = ["codelearning/python:3.12-slim"],
        WorkspacePath = "/app"
    };

    [Theory]
    [InlineData("GET", "/_ping")]
    [InlineData("HEAD", "/_ping")]
    [InlineData("GET", "/version")]
    [InlineData("GET", "/images/codelearning/python:3.12-slim/json")]
    [InlineData("GET", "/v1.47/images/codelearning/python:3.12-slim/json")]
    [InlineData("POST", "/images/create?fromImage=codelearning/python:3.12-slim")]
    [InlineData("POST", "/images/create?fromImage=codelearning/python&tag=3.12-slim")]
    public void Evaluate_ReadOnlyAndAllowedPull_ShouldForward(string method, string url)
    {
        Evaluate(method, url).Kind.Should().Be(RouteKind.Forward);
    }

    [Theory]
    [InlineData("POST", "/containers/create")]
    [InlineData("POST", "/v1.47/containers/create")]
    public void Evaluate_ContainerCreate_ShouldRequireBodyValidation(string method, string url)
    {
        Evaluate(method, url).Kind.Should().Be(RouteKind.CreateContainer);
    }

    [Theory]
    [InlineData("POST", "/containers/abc123/start")]
    [InlineData("POST", "/containers/abc123/wait")]
    [InlineData("POST", "/containers/abc123/kill")]
    [InlineData("GET", "/containers/abc123/logs?stdout=1&stderr=1")]
    [InlineData("PUT", "/containers/abc123/archive?path=/app")]
    [InlineData("DELETE", "/containers/abc123?v=1&force=1")]
    public void Evaluate_ContainerLifecycle_ShouldRequireSandboxContainer(string method, string url)
    {
        var decision = Evaluate(method, url);

        decision.Kind.Should().Be(RouteKind.SandboxContainer);
        decision.ContainerId.Should().Be("abc123");
    }

    [Theory]
    [InlineData("POST", "/containers/abc123/exec")]
    [InlineData("POST", "/exec/abc/start")]
    [InlineData("GET", "/containers/json")]
    [InlineData("GET", "/containers/abc123/json")]
    [InlineData("POST", "/containers/abc123/attach")]
    [InlineData("POST", "/containers/abc123/update")]
    [InlineData("PUT", "/containers/abc123/archive?path=/")]
    [InlineData("PUT", "/containers/abc123/archive?path=/etc")]
    [InlineData("GET", "/containers/abc123/logs?follow=1")]
    [InlineData("POST", "/images/create?fromImage=alpine:latest")]
    [InlineData("POST", "/images/create?fromSrc=-&repo=codelearning/python:3.12-slim")]
    [InlineData("POST", "/build")]
    [InlineData("POST", "/volumes/create")]
    [InlineData("POST", "/networks/create")]
    [InlineData("GET", "/info")]
    [InlineData("POST", "/containers/prune")]
    [InlineData("DELETE", "/images/codelearning/python:3.12-slim")]
    [InlineData("POST", "/containers/abc123/../../build")]
    [InlineData("POST", "/containers/abc%2F..%2Fexec")]
    public void Evaluate_EverythingElse_ShouldDeny(string method, string url)
    {
        Evaluate(method, url).Kind.Should().Be(RouteKind.Deny);
    }

    private static RouteDecision Evaluate(string method, string url)
    {
        var parts = url.Split('?', 2);
        var query = new QueryCollection(parts.Length > 1 ? QueryHelpers.ParseQuery(parts[1]) : []);
        return RequestPolicy.Evaluate(method, parts[0], query, Options);
    }
}
