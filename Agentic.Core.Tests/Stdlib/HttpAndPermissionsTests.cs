using Agentic.Core.Execution;
using Agentic.Core.Stdlib;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Stdlib;

public sealed class HttpModuleTests
{
    [Fact]
    public void HttpGet_DuringVerification_ShouldFail()
    {
        // HTTP ops are not available during verification
        const string source = @"(do (def resp (http.get ""http://example.com"")))";
        var compiler = new Compiler(emitBinary: false, permissions: Permissions.All);

        var result = compiler.Compile(source);

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Message.Contains("not available during verification"));
    }

    [Fact]
    public void HttpGet_WithoutPermission_ShouldFail()
    {
        const string source = @"(do (def resp (http.get ""http://example.com"")))";
        var compiler = new Compiler(emitBinary: false, permissions: Permissions.None);

        var result = compiler.Compile(source);

        // Fails at verifier first (HTTP not available during verification)
        result.Success.Should().BeFalse();
    }
}

public sealed class PermissionsTests
{
    [Fact]
    public void None_DeniesEverything()
    {
        var p = Permissions.None;
        p.AllowFileRead.Should().BeFalse();
        p.AllowFileWrite.Should().BeFalse();
        p.AllowHttp.Should().BeFalse();
    }

    [Fact]
    public void All_GrantsEverything()
    {
        var p = Permissions.All;
        p.AllowFileRead.Should().BeTrue();
        p.AllowFileWrite.Should().BeTrue();
        p.AllowHttp.Should().BeTrue();
    }

    [Fact]
    public void Require_GrantedCapability_ShouldNotThrow()
    {
        var p = new Permissions { AllowFileRead = true };
        var act = () => p.Require("file.read");
        act.Should().NotThrow();
    }

    [Fact]
    public void Require_DeniedCapability_ShouldThrow()
    {
        var p = Permissions.None;
        var act = () => p.Require("file.write");
        act.Should().Throw<PermissionDeniedException>()
            .WithMessage("*file.write*not granted*");
    }

    [Fact]
    public void PermissionDeniedException_ContainsCapability()
    {
        var ex = new PermissionDeniedException("http");
        ex.Capability.Should().Be("http");
        ex.Message.Should().Contain("http");
        ex.Message.Should().Contain("--allow-http");
    }
}
