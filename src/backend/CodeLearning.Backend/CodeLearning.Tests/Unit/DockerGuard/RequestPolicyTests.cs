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
        AllowedImages = ["codelearning/python:3.12-slim"]
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
    [InlineData("GET", "/containers/abc123/logs?stdout=1&follow=0")]
    [InlineData("GET", "/containers/abc123/logs?stdout=1&follow=false")]
    [InlineData("GET", "/containers/abc123/logs?stdout=1&follow=False")]
    [InlineData("GET", "/containers/abc123/logs?stdout=1&follow=")]
    [InlineData("DELETE", "/containers/abc123?v=1&force=1")]
    [InlineData("GET", "/containers/abc123/json")]
    public void Evaluate_ContainerLifecycle_ShouldRequireSandboxContainer(string method, string url)
    {
        var decision = Evaluate(method, url);

        decision.Kind.Should().Be(RouteKind.SandboxContainer);
        decision.ContainerId.Should().Be("abc123");
    }

    [Theory]
    [InlineData("POST", "/containers/abc123/exec")]
    [InlineData("POST", "/exec/abc/start")]
    [InlineData("POST", "/containers/json")]
    [InlineData("POST", "/containers/abc123/json")]
    [InlineData("POST", "/containers/abc123/attach")]
    [InlineData("POST", "/containers/abc123/attach?stream=1&stdout=1")]
    [InlineData("POST", "/containers/abc123/attach?stdin=0")]
    [InlineData("GET", "/containers/abc123/attach/ws?stdin=1")]
    [InlineData("POST", "/containers/abc123/update")]
    [InlineData("PUT", "/containers/abc123/archive?path=/app")]
    [InlineData("PUT", "/containers/abc123/archive?path=/")]
    [InlineData("GET", "/containers/abc123/archive?path=/app/out")]
    [InlineData("GET", "/containers/abc123/logs?follow=1")]
    [InlineData("GET", "/containers/abc123/logs?follow=true")]
    [InlineData("GET", "/containers/abc123/logs?follow=TRUE")]
    [InlineData("GET", "/containers/abc123/logs?follow=yes")]
    [InlineData("GET", "/containers/abc123/logs?follow=2")]
    [InlineData("GET", "/containers/abc123/logs?follow=0&follow=1")]
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

    [Theory]
    [InlineData("/containers/abc123/attach?stream=1&stdin=1")]
    [InlineData("/v1.47/containers/abc123/attach?stream=true&stdin=true")]
    public void Evaluate_StdinAttach_ShouldBeTunnelledForSandboxContainer(string url)
    {
        var decision = Evaluate("POST", url);

        decision.Kind.Should().Be(RouteKind.AttachSandboxContainer);
        decision.ContainerId.Should().Be("abc123");
    }

    [Theory]
    [InlineData("/containers/json")]
    [InlineData("/v1.47/containers/json")]
    public void Evaluate_ContainerList_ShouldBeRewrittenToSandboxOnly(string url)
    {
        Evaluate("GET", url).Kind.Should().Be(RouteKind.ListSandboxContainers);
    }

    [Theory]
    [InlineData("all=1&filters=%7B%22label%22%3A%7B%22other%3Dtrue%22%3Atrue%7D%7D", true)]
    [InlineData("filters=%7B%22name%22%3A%7B%22codelearning-postgres%22%3Atrue%7D%7D", false)]
    [InlineData("", false)]
    public void SandboxListQuery_ShouldForceTheSandboxLabelFilter(string query, bool all)
    {
        var rewritten = RequestPolicy.SandboxListQuery(new QueryCollection(QueryHelpers.ParseQuery(query)));

        var parsed = QueryHelpers.ParseQuery(rewritten.Value);
        parsed["filters"].ToString().Should().Be("{\"label\":{\"codelearning.sandbox=true\":true}}");
        parsed.ContainsKey("all").Should().Be(all);
        parsed.Keys.Should().BeSubsetOf(["filters", "all"]);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    [InlineData("0,1", true)]
    public void IsTrue_ShouldMatchDockerBooleanParsing(string value, bool expected)
    {
        RequestPolicy.IsTrue(value).Should().Be(expected);
    }

    private static RouteDecision Evaluate(string method, string url)
    {
        var parts = url.Split('?', 2);
        var query = new QueryCollection(parts.Length > 1 ? QueryHelpers.ParseQuery(parts[1]) : []);
        return RequestPolicy.Evaluate(method, parts[0], query, Options);
    }
}
