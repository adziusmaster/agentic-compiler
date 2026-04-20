using Agentic.Core.Execution;
using Agentic.Core.Capabilities;
using Agentic.Core.Stdlib;
using Agentic.Core.Syntax;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Capabilities;

public sealed class CapabilityFfiTests
{
    [Fact]
    public void ExternDefun_WithRegisteredCapability_ShouldParseAndRegister()
    {
        const string src = @"
            (do
              (extern defun fetch ((url : Str)) : Str @capability ""http.fetch"")
              (test demo
                (mocks (http.fetch ""https://example.com"" ""hello""))
                (assert-eq (fetch ""https://example.com"") ""hello"")))";
        var ast = new Parser(new Lexer(src).Tokenize()).Parse();

        var verifier = new Verifier();
        verifier.Evaluate(ast);

        verifier.TestsPassed.Should().Be(1);
        verifier.TestsFailed.Should().Be(0);
        verifier.DeclaredCapabilities.Should().Contain("http.fetch");
    }

    [Fact]
    public void ExternDefun_UnregisteredCapability_ShouldThrow()
    {
        const string src = @"
            (extern defun steal ((x : Str)) : Str @capability ""exec.shell"")";
        var ast = new Parser(new Lexer(src).Tokenize()).Parse();

        var act = () => new Verifier().Evaluate(ast);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*exec.shell*");
    }

    [Fact]
    public void CapabilityCall_InsideTestWithoutMock_ShouldThrow()
    {
        const string src = @"
            (do
              (extern defun fetch ((url : Str)) : Str @capability ""http.fetch"")
              (test unmocked
                (assert-eq (fetch ""https://example.com"") ""anything"")))";
        var ast = new Parser(new Lexer(src).Tokenize()).Parse();

        var verifier = new Verifier { CollectAllErrors = true };
        verifier.Evaluate(ast);

        verifier.TestsFailed.Should().Be(1);
        verifier.TestFailures[0].Message.Should().Contain("Capability 'http.fetch'");
    }

    [Fact]
    public void Transpile_CapabilityCall_ShouldInlineEmitExpr()
    {
        const string src = @"
            (do
              (extern defun fetch ((url : Str)) : Str @capability ""http.fetch"")
              (sys.stdout.write (fetch ""https://example.com"")))";
        var ast = new Parser(new Lexer(src).Tokenize()).Parse();

        var permissions = new Permissions { AllowHttp = true };
        string csharp = new Transpiler(permissions).Transpile(ast);

        csharp.Should().Contain("_httpClient.GetStringAsync(\"https://example.com\").GetAwaiter().GetResult()");
    }

    [Fact]
    public void ExternDefun_InsideModuleWithMainBody_ShouldRunTests()
    {
        const string src = @"
            (module Demo
              (extern defun http-fetch ((url : Str)) : Str @capability ""http.fetch"")
              (defun extract ((body : Str)) : Str (return body))
              (test fetch
                (mocks (http-fetch ""u"" ""hello""))
                (assert-eq (extract (http-fetch ""u"")) ""hello""))
              (sys.stdout.write (extract (http-fetch ""u""))))";

        var result = new Compiler(emitBinary: false, permissions: new Permissions { AllowHttp = true })
            .Compile(src, "demo");

        result.Success.Should().BeTrue(because: string.Join("; ",
            result.Diagnostics.Select(d => $"{d.Type}:{d.Message}")));
        result.TestsPassed.Should().Be(1);
    }

    [Fact]
    public void WeatherFetcherSample_ShouldReportTestPassed()
    {
        string src = System.IO.File.ReadAllText(
            System.IO.Path.Combine(System.AppContext.BaseDirectory,
                "..", "..", "..", "..", "Agentic.Cli", "samples", "WeatherFetcher.ag"));

        var ast = new Parser(new Lexer(src).Tokenize()).Parse();
        var verifier = new Verifier(null, null, null) { CollectAllErrors = true };
        try { verifier.Evaluate(ast); } catch { }

        verifier.TestFailures.Should().BeEmpty(because:
            string.Join("; ", verifier.TestFailures.Select(f => f.TestName + "->" + f.Message)));
        verifier.TestsPassed.Should().Be(1);
    }

    [Fact]
    public void Transpile_CapabilityWithoutPermission_ShouldThrow()
    {
        const string src = @"
            (do
              (extern defun fetch ((url : Str)) : Str @capability ""http.fetch"")
              (sys.stdout.write (fetch ""https://example.com"")))";
        var ast = new Parser(new Lexer(src).Tokenize()).Parse();

        var act = () => new Transpiler(Permissions.None).Transpile(ast);

        act.Should().Throw<PermissionDeniedException>()
            .Where(e => e.Capability == "http");
    }
}
