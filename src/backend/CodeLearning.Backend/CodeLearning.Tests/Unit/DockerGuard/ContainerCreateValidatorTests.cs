extern alias DockerGuard;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DockerGuard::CodeLearning.DockerGuard;
using FluentAssertions;

namespace CodeLearning.Tests.Unit.DockerGuard;

public class ContainerCreateValidatorTests
{
    // The shape Docker.DotNet 3.125 sends for the parameters DockerRunner builds
    private const string RunnerBody = """
        {"User":"0:0","Cmd":["/bin/bash","-c","bootstrap"],"Image":"codelearning/python:3.12-slim","WorkingDir":"/",
         "AttachStdin":true,"OpenStdin":true,"StdinOnce":true,"AttachStdout":false,"Tty":false,
         "Labels":{"submission-id":"x","codelearning.sandbox":"true"},
         "HostConfig":{"NetworkMode":"none","CapDrop":["ALL"],"CapAdd":["SETUID","SETGID","KILL"],
           "SecurityOpt":["no-new-privileges"],"ReadonlyRootfs":true,
           "Tmpfs":{"/sandbox":"size=64m,uid=65534,gid=65534,mode=0755","/tmp":"size=32m,mode=1777","/app/out":"size=32m,mode=0700"},
           "Memory":268435456,"NanoCpus":1000000000,"MemorySwap":268435456,
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
        var body = RunnerBody.Replace("\"User\":\"0:0\"", "\"User\":\"0:0\",\"User\":\"65534:65534\"");

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
    [InlineData("0:0")]
    [InlineData("00")]
    [InlineData("000:0")]
    public void Validate_NumericRootWithOnlyDropCapabilities_ShouldAccept(string user)
    {
        Validate(Modify(body => body["User"] = user)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("00")]
    [InlineData("0:0")]
    public void Validate_RootWithExtraCapability_ShouldReject(string user)
    {
        var result = Validate(Modify(body =>
        {
            body["User"] = user;
            body["HostConfig"]!["CapAdd"] = new JsonArray("SETUID", "SYS_ADMIN");
        }));

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("SETUID, SETGID and KILL");
    }

    [Fact]
    public void Validate_RootWithCapPrefixedCapabilities_ShouldAccept()
    {
        Validate(Modify(body => body["HostConfig"]!["CapAdd"] = new JsonArray("CAP_SETUID", "cap_setgid")))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("-0")]
    [InlineData("+0")]
    [InlineData(" 0")]
    [InlineData("0x0")]
    [InlineData("root")]
    [InlineData("root:root")]
    [InlineData("nobody")]
    [InlineData("65534:00")]
    [InlineData("65534:0")]
    [InlineData("65534")]
    [InlineData("0:65534")]
    [InlineData("1000:1000")]
    [InlineData("0:0:0")]
    [InlineData("99999999999999999999")]
    [InlineData("")]
    public void Validate_UnexpectedUser_ShouldReject(string user)
    {
        Validate(Modify(body => body["User"] = user)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_MissingUser_ShouldReject()
    {
        Validate(Modify(body => body.AsObject().Remove("User"))).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_SandboxUserWithoutCapabilities_ShouldAccept()
    {
        Validate(Modify(body =>
        {
            body["User"] = "65534:65534";
            body["HostConfig"]!.AsObject().Remove("CapAdd");
        })).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_SandboxUserWithCapabilities_ShouldReject()
    {
        Validate(Modify(body => body["User"] = "65534:65534")).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void Validate_WritableRootfs_ShouldReject(bool? readonlyRootfs)
    {
        var result = Validate(Modify(body =>
        {
            if (readonlyRootfs is { } value)
                body["HostConfig"]!["ReadonlyRootfs"] = value;
            else
                body["HostConfig"]!.AsObject().Remove("ReadonlyRootfs");
        }));

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("ReadonlyRootfs");
    }

    [Fact]
    public void Validate_NoTmpfs_ShouldReject()
    {
        Validate(Modify(body => body["HostConfig"]!.AsObject().Remove("Tmpfs"))).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("size=32m,exec")]
    [InlineData("size=32m,suid")]
    [InlineData("size=32m,nr_inodes=0")]
    public void Validate_TmpfsWithUnexpectedOption_ShouldReject(string tmpfsOptions)
    {
        Validate(Modify(body => body["HostConfig"]!["Tmpfs"] = new JsonObject { ["/tmp"] = tmpfsOptions }))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_Tty_ShouldReject()
    {
        Validate(Modify(body => body["Tty"] = true)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_RunnerBody_ShouldForwardStdinAndRootfsSettings()
    {
        var forwarded = JsonNode.Parse(Validate(RunnerBody).Body)!;

        forwarded["OpenStdin"]!.GetValue<bool>().Should().BeTrue();
        forwarded["StdinOnce"]!.GetValue<bool>().Should().BeTrue();
        forwarded["HostConfig"]!["ReadonlyRootfs"]!.GetValue<bool>().Should().BeTrue();
        forwarded["HostConfig"]!["CapAdd"]!.AsArray().Select(c => c!.GetValue<string>())
            .Should().BeEquivalentTo("SETUID", "SETGID", "KILL");
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
    public void Validate_MemoryAboveCeiling_ShouldReject()
    {
        const long tooMuch = 8L * 1024 * 1024 * 1024;
        Validate(Modify(body =>
        {
            body["HostConfig"]!["Memory"] = tooMuch;
            body["HostConfig"]!["MemorySwap"] = tooMuch;
        })).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_PidsLimitAboveCeiling_ShouldReject()
    {
        Validate(Modify(body => body["HostConfig"]!["PidsLimit"] = 100_000)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_UlimitAboveCeiling_ShouldReject()
    {
        Validate(Modify(body => body["HostConfig"]!["Ulimits"]![0]!["Hard"] = long.MaxValue)).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("size=4g,mode=1777")]
    [InlineData("mode=1777")]
    public void Validate_TmpfsWithoutBoundedSize_ShouldReject(string tmpfsOptions)
    {
        Validate(Modify(body => body["HostConfig"]!["Tmpfs"] = new JsonObject { ["/tmp"] = tmpfsOptions }))
            .IsValid.Should().BeFalse();
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
