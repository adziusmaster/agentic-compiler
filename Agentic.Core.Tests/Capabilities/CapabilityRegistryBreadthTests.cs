using Agentic.Core.Capabilities;
using Agentic.Core.Execution;
using Agentic.Core.Stdlib;
using Agentic.Core.Syntax;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Capabilities;

/// <summary>
/// A4: the FFI capability registry covers the real-world I/O surface the
/// evaluation relies on (file / env / db / process). Every capability must:
/// (1) verify hermetically under a (mocks …) clause;
/// (2) refuse to run unmocked without --allow-real-io;
/// (3) permission-gate at transpile time.
/// </summary>
public sealed class CapabilityRegistryBreadthTests
{
    [Fact]
    public void FileRead_UnderMock_ShouldReturnStubbedContent()
    {
        const string src = @"
            (do
              (extern defun f-read ((p : Str)) : Str @capability ""file.read"")
              (test mocked
                (mocks (file.read ""/tmp/x"" ""alpha""))
                (assert-eq (f-read ""/tmp/x"") ""alpha"")))";

        var verifier = new Verifier();
        verifier.Evaluate(new Parser(new Lexer(src).Tokenize()).Parse());

        verifier.TestsPassed.Should().Be(1);
        verifier.DeclaredCapabilities.Should().Contain("file.read");
    }

    [Fact]
    public void FileWrite_UnderMock_ShouldReturnStubbedResult()
    {
        const string src = @"
            (do
              (extern defun f-write ((p : Str) (c : Str)) : Num @capability ""file.write"")
              (test mocked
                (mocks (file.write ""/tmp/x"" 1))
                (assert-eq (f-write ""/tmp/x"" ""hi"") 1)))";

        var verifier = new Verifier();
        verifier.Evaluate(new Parser(new Lexer(src).Tokenize()).Parse());

        verifier.TestsPassed.Should().Be(1);
        verifier.DeclaredCapabilities.Should().Contain("file.write");
    }

    [Fact]
    public void EnvGet_UnderMock_ShouldReturnStubbedValue()
    {
        const string src = @"
            (do
              (extern defun get-env ((k : Str)) : Str @capability ""env.get"")
              (test mocked
                (mocks (env.get ""HOME"" ""/root""))
                (assert-eq (get-env ""HOME"") ""/root"")))";

        var verifier = new Verifier();
        verifier.Evaluate(new Parser(new Lexer(src).Tokenize()).Parse());

        verifier.TestsPassed.Should().Be(1);
        verifier.DeclaredCapabilities.Should().Contain("env.get");
    }

    [Fact]
    public void DbQuery_UnderMock_ShouldReturnStubbedRow()
    {
        // db.query is keyed by first arg (connection string) in the mock frame.
        const string src = @"
            (do
              (extern defun q ((conn : Str) (sql : Str)) : Str @capability ""db.query"")
              (test mocked
                (mocks (db.query ""Data Source=:memory:"" ""42""))
                (assert-eq (q ""Data Source=:memory:"" ""SELECT 42"") ""42"")))";

        var verifier = new Verifier();
        verifier.Evaluate(new Parser(new Lexer(src).Tokenize()).Parse());

        verifier.TestsPassed.Should().Be(1);
        verifier.DeclaredCapabilities.Should().Contain("db.query");
    }

    [Fact]
    public void ProcessSpawn_UnderMock_ShouldReturnStubbedStdout()
    {
        const string src = @"
            (do
              (extern defun run ((c : Str)) : Str @capability ""process.spawn"")
              (test mocked
                (mocks (process.spawn ""echo hi"" ""hi\n""))
                (assert-eq (run ""echo hi"") ""hi\n"")))";

        var verifier = new Verifier();
        verifier.Evaluate(new Parser(new Lexer(src).Tokenize()).Parse());

        verifier.TestsPassed.Should().Be(1);
        verifier.DeclaredCapabilities.Should().Contain("process.spawn");
    }

    [Theory]
    [InlineData("file.read",    "file.read")]
    [InlineData("file.write",   "file.write")]
    [InlineData("env.get",      "env")]
    [InlineData("db.query",     "db")]
    [InlineData("process.spawn","process")]
    public void Capability_WithoutMatchingPermission_ShouldRejectAtTranspile(
        string capabilityName, string expectedPermission)
    {
        string src = $@"
            (do
              (extern defun use-it ((x : Str)) : Str @capability ""{capabilityName}"")
              (sys.stdout.write (use-it ""x"")))";
        var ast = new Parser(new Lexer(src).Tokenize()).Parse();

        var act = () => new Transpiler(Permissions.None).Transpile(ast);

        act.Should().Throw<PermissionDeniedException>()
            .Where(e => e.Capability == expectedPermission);
    }

    [Fact]
    public void UnmockedCapabilityCall_InsideTest_ShouldFail()
    {
        const string src = @"
            (do
              (extern defun f-read ((p : Str)) : Str @capability ""file.read"")
              (test unmocked
                (assert-eq (f-read ""/etc/passwd"") ""anything"")))";

        var verifier = new Verifier { CollectAllErrors = true };
        verifier.Evaluate(new Parser(new Lexer(src).Tokenize()).Parse());

        verifier.TestsFailed.Should().Be(1);
        verifier.TestFailures[0].Message.Should().Contain("file.read");
    }
}
