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
        List<CheckTest> tests,
        List<CheckDef>? defs = null,
        List<CheckContract>? contracts = null)
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
            Contracts: contracts ?? new List<CheckContract>(),
            BuiltAt: DateTime.UtcNow,
            BinaryHash: binHash,
            Defs: defs);

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

    // ─── C8: VC emission — checker accepts from (binary, manifest) alone ───

    [Fact]
    public void Run_NoSource_WithEmbeddedDefs_AcceptsTestsThatCallUserFunctions()
    {
        // Arrange — manifest embeds the `add` defun; the test snippet calls it.
        // Without C8 this would fail "Unbound variable: add" because the
        // test snippet alone has no way to resolve the reference.
        var defs = new List<CheckDef>
        {
            new("defun", "add",
                "(defun add ((a : Num) (b : Num)) : Num (return (+ a b)))"),
        };
        var tests = new List<CheckTest>
        {
            new("add", "(test add (assert-eq (add 1 2) 3))", ExpectedPasses: 1),
        };
        var (binPath, _, _) = BuildFixture(
            binaryBytes: Encoding.UTF8.GetBytes("inert"),
            sourceText: "(module M)",
            capabilities: new List<string>(),
            tests: tests,
            defs: defs);

        // Act — no sourcePath passed.
        var result = Checker.Run(binPath);

        // Assert — manifest alone is sufficient; no source path was passed.
        result.Verdict.Should().Be(Verdict.Accept);
        result.TestsPassed.Should().Be(1);
    }

    [Fact]
    public void Run_NoSource_WithContractsInEmbeddedDefs_EnforcesRequireAtCallSite()
    {
        // Arrange — defun with a (require …). The test calls it with a
        // satisfying argument so the contract passes. This exercises the
        // multi-form defun body path (require + return) which needs the
        // implicit-do synthesis in EvalDefun.
        var defs = new List<CheckDef>
        {
            new("defun", "add_nonneg",
                "(defun add_nonneg ((a : Num) (b : Num)) : Num " +
                "(require (>= a 0)) (require (>= b 0)) (return (+ a b)))"),
        };
        var contracts = new List<CheckContract>
        {
            new("add_nonneg", "require", "(require (>= a 0))"),
            new("add_nonneg", "require", "(require (>= b 0))"),
        };
        var tests = new List<CheckTest>
        {
            new("add_nonneg_ok",
                "(test add_nonneg_ok (assert-eq (add_nonneg 5 3) 8))",
                ExpectedPasses: 1),
        };
        var (binPath, _, _) = BuildFixture(
            binaryBytes: Encoding.UTF8.GetBytes("inert"),
            sourceText: "(module M)",
            capabilities: new List<string>(),
            tests: tests,
            defs: defs,
            contracts: contracts);

        // Act
        var result = Checker.Run(binPath);

        // Assert
        result.Verdict.Should().Be(Verdict.Accept);
        result.TestsPassed.Should().Be(1);
    }

    [Fact]
    public void Run_NoSource_ContractViolation_RejectsAsTestFail()
    {
        // Arrange — same defun, but test passes a negative argument that
        // violates the (require (>= a 0)) precondition. Expected: the
        // contract aborts, test status = fail, verdict = reject.
        var defs = new List<CheckDef>
        {
            new("defun", "square",
                "(defun square ((n : Num)) : Num " +
                "(require (>= n 0)) (return (* n n)))"),
        };
        var tests = new List<CheckTest>
        {
            new("square_neg",
                "(test square_neg (assert-eq (square -2) 4))",
                ExpectedPasses: 1),
        };
        var (binPath, _, _) = BuildFixture(
            binaryBytes: Encoding.UTF8.GetBytes("inert"),
            sourceText: "(module M)",
            capabilities: new List<string>(),
            tests: tests,
            defs: defs);

        // Act
        var result = Checker.Run(binPath);

        // Assert
        result.Verdict.Should().Be(Verdict.Reject);
        result.Code.Should().Be("test-fail");
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
