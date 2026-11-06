extern alias DockerGuard;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DockerGuard::CodeLearning.DockerGuard;
using FluentAssertions;

namespace CodeLearning.Tests.Unit.DockerGuard;

public class ContainerCreateValidatorTests
{
    // Captured from Docker.DotNet 3.125 for the parameters DockerRunner builds
    private const string RunnerBody = """
        {"User":"65534:65534","Cmd":["/app/runner"],"Image":"codelearning/python:3.12-slim","WorkingDir":"/app",
         "Labels":{"submission-id":"x","codelearning.sandbox":"true"},
         "HostConfig":{"NetworkMode":"none","CapDrop":["ALL"],"SecurityOpt":["no-new-privileges"],
           "Tmpfs":{"/tmp":"size=50m,mode=1777"},"Memory":268435456,"NanoCpus":1000000000,"MemorySwap":268435456,
           "PidsLimit":100,"Ulimits":[{"Name":"fsize","Hard":67108864,"Soft":67108864}]}}
        """;

    private static readonly GuardOptions Options = new()
    {
        AllowedImages = ["codelearning/python:3.12-slim"],
        AllowedRuntimes = ["runsc"]
    };

    [Fact]
    public void Validate_RunnerBody_ShouldAcceptAndKeepCheckedFields()
    {
        var result = Validate(RunnerBody);

        result.IsValid.Should().BeTrue(result.Error);
        var forwarded = JsonNode.Parse(result.Body)!;
        forwarded["Image"]!.GetValue<string>().Should().Be("codelearning/python:3.12-slim");
        forwarded["HostConfig"]!["NetworkMode"]!.GetValue<string>().Should().Be("none");
        forwarded["HostConfig"]!["PidsLimit"]!.GetValue<long>().Should().Be(100);
        forwarded["HostConfig"]!["Ulimits"]![0]!["Hard"]!.GetValue<long>().Should().Be(67108864);
    }

    [Fact]
    public void Validate_GvisorRuntime_ShouldAccept()
    {
        var result = Validate(Modify(body => body["HostConfig"]!["Runtime"] = "runsc"));

        result.IsValid.Should().BeTrue(result.Error);
        JsonNode.Parse(result.Body)!["HostConfig"]!["Runtime"]!.GetValue<string>().Should().Be("runsc");
    }

    [Theory]
    [InlineData("Privileged")]
    [InlineData("privileged")]
    [InlineData("PRIVILEGED")]
    public void Validate_Privileged_ShouldReject(string field)
    {
        var result = Validate(Modify(body => body["HostConfig"]![field] = true));

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain(field);
    }

    [Fact]
    public void Validate_PrivilegedFalse_ShouldAcceptAndDropIt()
    {
        var result = Validate(Modify(body => body["HostConfig"]!["Privileged"] = false));

        result.IsValid.Should().BeTrue(result.Error);
        Encoding.UTF8.GetString(result.Body!).Should().NotContain("Privileged");
    }

    [Fact]
    public void Validate_HostBindMount_ShouldReject()
    {
        var result = Validate(Modify(body => body["HostConfig"]!["Binds"] = new JsonArray("/:/host")));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_HostMount_ShouldReject()
    {
        var result = Validate(Modify(body => body["HostConfig"]!["Mounts"] =
            new JsonArray(new JsonObject { ["Type"] = "bind", ["Source"] = "/", ["Target"] = "/host" })));

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("CapAdd", "SYS_ADMIN")]
    [InlineData("Devices", "/dev/sda")]
    public void Validate_ExtraPrivileges_ShouldReject(string field, string value)
    {
        var result = Validate(Modify(body => body["HostConfig"]![field] = new JsonArray(value)));

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("PidMode")]
    [InlineData("IpcMode")]
    [InlineData("UsernsMode")]
    public void Validate_HostNamespace_ShouldReject(string field)
    {
        var result = Validate(Modify(body => body["HostConfig"]![field] = "host"));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_CaseVariantDuplicate_ShouldReject()
    {
        // Docker would read the second, lowercase field
        var body = RunnerBody.Replace("\"NetworkMode\":\"none\"", "\"NetworkMode\":\"none\",\"networkmode\":\"host\"");

        var result = Validate(body);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("Duplicate");
    }

    [Fact]
    public void Validate_ExactDuplicate_ShouldReject()
    {
        var body = RunnerBody.Replace("\"User\":\"65534:65534\"", "\"User\":\"65534:65534\",\"User\":\"0\"");

        Validate(body).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ForeignImage_ShouldReject()
    {
        Validate(Modify(body => body["Image"] = "alpine:latest")).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("bridge")]
    [InlineData("host")]
    [InlineData("container:codelearning-postgres")]
    public void Validate_NetworkEnabled_ShouldReject(string networkMode)
    {
        Validate(Modify(body => body["HostConfig"]!["NetworkMode"] = networkMode)).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("root")]
    [InlineData("0:0")]
    [InlineData("65534:0")]
    [InlineData("")]
    public void Validate_RootOrMissingUser_ShouldReject(string user)
    {
        Validate(Modify(body => body["User"] = user)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_CapabilitiesNotDropped_ShouldReject()
    {
        Validate(Modify(body => body["HostConfig"]!["CapDrop"] = new JsonArray("NET_RAW"))).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("seccomp=unconfined")]
    [InlineData("apparmor=unconfined")]
    public void Validate_UnconfinedSecurityProfile_ShouldReject(string option)
    {
        var result = Validate(Modify(body => body["HostConfig"]!["SecurityOpt"] = new JsonArray("no-new-privileges", option)));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_UnknownRuntime_ShouldReject()
    {
        Validate(Modify(body => body["HostConfig"]!["Runtime"] = "nvidia")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_MissingSandboxLabel_ShouldReject()
    {
        Validate(Modify(body => body["Labels"] = new JsonObject { ["submission-id"] = "x" })).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_NoMemoryLimit_ShouldReject()
    {
        Validate(Modify(body => body["HostConfig"]!.AsObject().Remove("Memory"))).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_UnexpectedTopLevelField_ShouldReject()
    {
        Validate(Modify(body => body["Entrypoint"] = new JsonArray("/bin/sh"))).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_InvalidJson_ShouldReject()
    {
        Validate("{not json").IsValid.Should().BeFalse();
    }

    private static ValidationResult Validate(string body) =>
        ContainerCreateValidator.Validate(Encoding.UTF8.GetBytes(body), Options);

    private static string Modify(Action<JsonNode> change)
    {
        var body = JsonNode.Parse(RunnerBody)!;
        change(body);
        return body.ToJsonString(new JsonSerializerOptions());
    }
}
