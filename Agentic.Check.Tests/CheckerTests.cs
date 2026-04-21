using System.Text;
using System.Text.Json;
using Agentic.Check;
using FluentAssertions;
using Xunit;

namespace Agentic.Check.Tests;

// C7 acceptance suite — five scenarios required by ROADMAP Arc C7:
//   1. Happy path        → Accept
//   2. Binary tamper     → Reject (binary-tampered)
//   3. Source mismatch   → Reject (source-mismatch)
//   4. Capability undecl.→ Reject (capability-undeclared)
//   5. Test-count mismatch → Reject (test-count-mismatch)
//
// Fixtures are synthesized at runtime — no Agentic.Core dependency. A
// "binary" is just bytes on disk; the checker hashes it with SHA256 and
// substring-scans for capability signatures. Tests that need a capability
// present embed its emit-signature string into the fixture bytes.

public sealed class CheckerTests : IDisposable
{
    private readonly string _tmpDir;

    public CheckerTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "agc-check-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, recursive: true);
    }

    private (string binPath, string srcPath, CheckManifest manifest) BuildFixture(
        byte[] binaryBytes,
        string sourceText,
        List<string> capabilities,
        List<CheckTest> tests)
    {
        string binPath = Path.Combine(_tmpDir, "prog");
        string srcPath = Path.Combine(_tmpDir, "prog.ag");

        File.WriteAllBytes(binPath, binaryBytes);
        File.WriteAllText(srcPath, sourceText);

        string binHash = ManifestLoader.Sha256HexOfFile(binPath);
        string srcHash = ManifestLoader.Sha256HexOfString(sourceText);

        var manifest = new CheckManifest(
            SchemaVersion: "1.0",
            SourceHash: srcHash,
            Capabilities: capabilities,
            Permissions: new List<string>(),
            Tests: tests,
            Contracts: new List<CheckContract>(),
            BuiltAt: DateTime.UtcNow,
            BinaryHash: binHash);

        File.WriteAllText(ManifestLoader.SidecarPath(binPath),
            JsonSerializer.Serialize(manifest));

        return (binPath, srcPath, manifest);
    }

    [Fact]
    public void Run_HappyPath_NoCapsNoTests_ReturnsAccept()
    {
        // Arrange
        var (binPath, _, _) = BuildFixture(
            binaryBytes: Encoding.UTF8.GetBytes("inert bytes, no capability signatures"),
            sourceText: "(module M)",
            capabilities: new List<string>(),
            tests: new List<CheckTest>());

        // Act
        var result = Checker.Run(binPath);

        // Assert
        result.Verdict.Should().Be(Verdict.Accept);
        result.Code.Should().Be("accept");
    }

    [Fact]
    public void Run_HappyPath_WithPassingTest_ReturnsAccept()
    {
        // Arrange — (test trivial (assert-eq 1 1)) runs stand-alone with no source.
        const string snippet = "(test trivial (assert-eq 1 1))";
        var tests = new List<CheckTest> { new("trivial", snippet, ExpectedPasses: 1) };
        var (binPath, _, _) = BuildFixture(
            binaryBytes: Encoding.UTF8.GetBytes("inert bytes"),
            sourceText: "(module M)",
            capabilities: new List<string>(),
            tests: tests);

        // Act
        var result = Checker.Run(binPath);

        // Assert
        result.Verdict.Should().Be(Verdict.Accept);
        result.TestsPassed.Should().Be(1);
        result.TestsTotal.Should().Be(1);
    }

    [Fact]
    public void Run_BinaryTamper_ReturnsBinaryTamperedReject()
    {
        // Arrange
        var (binPath, _, _) = BuildFixture(
            binaryBytes: Encoding.UTF8.GetBytes("original binary content"),
            sourceText: "(module M)",
            capabilities: new List<string>(),
            tests: new List<CheckTest>());

        // Flip a byte — SHA256 will no longer match the sidecar's BinaryHash.
        byte[] tampered = File.ReadAllBytes(binPath);
        tampered[0] ^= 0xFF;
        File.WriteAllBytes(binPath, tampered);

        // Act
        var result = Checker.Run(binPath);

        // Assert
        result.Verdict.Should().Be(Verdict.Reject);
        result.Code.Should().Be("binary-tampered");
    }

    [Fact]
    public void Run_SourceMismatch_ReturnsSourceMismatchReject()
    {
        // Arrange — build fixture, then replace source content so SHA256(σ) drifts.
        var (binPath, srcPath, _) = BuildFixture(
            binaryBytes: Encoding.UTF8.GetBytes("inert"),
            sourceText: "(module OriginalSource)",
            capabilities: new List<string>(),
            tests: new List<CheckTest>());

        File.WriteAllText(srcPath, "(module TamperedSource)");

        // Act
        var result = Checker.Run(binPath, sourcePath: srcPath);

        // Assert
        result.Verdict.Should().Be(Verdict.Reject);
        result.Code.Should().Be("source-mismatch");
    }

    [Fact]
    public void Run_CapabilityUndeclared_ReturnsCapabilityUndeclaredReject()
    {
        // Arrange — binary contains the file.read emit signature but the
        // manifest declares no capabilities. CS must reject.
        var binaryBytes = Encoding.UTF8.GetBytes(
            "...padding...System.IO.File.ReadAllText(...)...more padding...");
        var (binPath, _, _) = BuildFixture(
            binaryBytes: binaryBytes,
            sourceText: "(module M)",
            capabilities: new List<string>(),        // declares nothing
            tests: new List<CheckTest>());

        // Act
        var result = Checker.Run(binPath);

        // Assert
        result.Verdict.Should().Be(Verdict.Reject);
        result.Code.Should().Be("capability-undeclared");
        result.ObservedCapabilities.Should().Contain("file.read");
    }

    [Fact]
    public void Run_TestCountMismatch_ReturnsTestCountMismatchReject()
    {
        // Arrange — 2 tests in the list but ExpectedPasses claims 5.
        const string snippet = "(test t (assert-eq 1 1))";
        var tests = new List<CheckTest>
        {
            new("t1", snippet, ExpectedPasses: 5), // first element's field is the manifest claim
            new("t2", snippet, ExpectedPasses: 5),
        };
        var (binPath, _, _) = BuildFixture(
            binaryBytes: Encoding.UTF8.GetBytes("inert"),
            sourceText: "(module M)",
            capabilities: new List<string>(),
            tests: tests);

        // Act
        var result = Checker.Run(binPath);

        // Assert
        result.Verdict.Should().Be(Verdict.Reject);
        result.Code.Should().Be("test-count-mismatch");
    }

    [Fact]
    public void Run_MissingBinary_ReturnsIoErrorReject()
    {
        // Act
        var result = Checker.Run(Path.Combine(_tmpDir, "does-not-exist"));

        // Assert
        result.Verdict.Should().Be(Verdict.Reject);
        result.Code.Should().Be("io-error");
    }

    [Fact]
    public void Run_MissingSidecar_ReturnsNoManifestReject()
    {
        // Arrange — write a binary but no sidecar.
        string binPath = Path.Combine(_tmpDir, "orphan");
        File.WriteAllBytes(binPath, Encoding.UTF8.GetBytes("x"));

        // Act
        var result = Checker.Run(binPath);

        // Assert
        result.Verdict.Should().Be(Verdict.Reject);
        result.Code.Should().Be("no-manifest");
    }
}
