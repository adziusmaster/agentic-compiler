using Agentic.Core.Execution;
using Agentic.Core.Syntax;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Execution;

/// <summary>
/// Tests for the <see cref="Compiler"/> facade and <see cref="CompileResult"/> structured output.
/// </summary>
public sealed class CompilerFacadeTests
{
    [Fact]
    public void Compile_ValidProgram_ShouldSucceed()
    {
        // Arrange
        var compiler = new Compiler(emitBinary: false);

        // Act
        var result = compiler.Compile("(do (sys.stdout.write \"hello\"))");

        // Assert
        result.Success.Should().BeTrue();
        result.GeneratedSource.Should().Contain("Console.Write(AgCanonical.Out(\"hello\"))");
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Compile_WithPassingTests_ShouldReportTestCount()
    {
        // Arrange
        const string source = @"
            (do
              (defun add ((a : Num) (b : Num)) : Num (return (+ a b)))
              (test add
                (assert-eq (add 1 2) 3)
                (assert-eq (add 0 0) 0)))";
        var compiler = new Compiler(emitBinary: false);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
        result.TestsPassed.Should().Be(1);
        result.TestsFailed.Should().Be(0);
    }

    [Fact]
    public void Compile_WithFailingTest_ShouldReturnError()
    {
        // Arrange
        const string source = @"
            (do
              (defun add ((a : Num) (b : Num)) : Num (return (+ a b)))
              (test add
                (assert-eq (add 1 2) 999)))";
        var compiler = new Compiler(emitBinary: false);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle();
        result.Diagnostics[0].Type.Should().Be("test-failure");
        result.Diagnostics[0].InContext.Should().Be("add");
    }

    [Fact]
    public void Compile_WithContractViolation_ShouldReturnError()
    {
        // Arrange
        const string source = @"
            (do
              (defun safe-div ((a : Num) (b : Num)) : Num
                (do (require (> b 0)) (return (/ a b))))
              (safe-div 10 0))";
        var compiler = new Compiler(emitBinary: false);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle();
        result.Diagnostics[0].Type.Should().Be("contract-violation");
    }

    [Fact]
    public void Compile_WithParseError_ShouldReturnError()
    {
        // Arrange — unmatched paren
        var compiler = new Compiler(emitBinary: false);

        // Act
        var result = compiler.Compile("(do (+ 1 2)");

        // Assert
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle();
        result.Diagnostics[0].Type.Should().Be("parse-error");
    }

    [Fact]
    public void CompileResult_ToSExpr_Success_ShouldFormatCorrectly()
    {
        // Arrange
        var result = new CompileResult
        {
            Success = true,
            BinaryPath = "./output",
            TestsPassed = 3,
            TestsFailed = 0
        };

        // Act
        string sexpr = result.ToSExpr();

        // Assert
        sexpr.Should().Contain("(ok");
        sexpr.Should().Contain("(binary \"./output\")");
        sexpr.Should().Contain("(tests-passed 3/3)");
    }

    [Fact]
    public void CompileResult_ToSExpr_Failure_ShouldIncludeDiagnostics()
    {
        // Arrange
        var result = new CompileResult
        {
            Success = false,
            TestsPassed = 1,
            TestsFailed = 1,
            Diagnostics = new[]
            {
                new CompileDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Type = "test-failure",
                    Message = "expected 6, got 9",
                    InContext = "double-it",
                    FixHint = "Fix the multiplication"
                }
            }
        };

        // Act
        string sexpr = result.ToSExpr();

        // Assert
        sexpr.Should().Contain("(error");
        sexpr.Should().Contain("(type \"test-failure\")");
        sexpr.Should().Contain("(in-context \"double-it\")");
        sexpr.Should().Contain("(fix-hint \"Fix the multiplication\")");
    }

    [Fact]
    public void CompileResult_ToJson_ShouldSerializeCorrectly()
    {
        // Arrange
        var result = new CompileResult
        {
            Success = true,
            TestsPassed = 2,
            TestsFailed = 0,
            BinaryPath = "./out"
        };

        // Act
        string json = result.ToJson();

        // Assert
        json.Should().Contain("\"success\": true");
        json.Should().Contain("\"testsPassed\": 2");
        json.Should().Contain("\"binaryPath\": \"./out\"");
    }

    [Fact]
    public void CompileDiagnostic_ToSExpr_WithSpan_ShouldIncludeLocation()
    {
        // Arrange
        var diag = new CompileDiagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Type = "type-mismatch",
            Message = "expected Num, got Str",
            Span = new SourceSpan(5, 10, 5, 25),
            Expected = "Num",
            Actual = "Str",
            FixHint = "use (str.to_num x)"
        };

        // Act
        string sexpr = diag.ToSExpr();

        // Assert
        sexpr.Should().Contain("(span (line 5) (col 10)");
        sexpr.Should().Contain("(expected \"Num\")");
        sexpr.Should().Contain("(actual \"Str\")");
        sexpr.Should().Contain("(fix-hint \"use (str.to_num x)\")");
    }

    [Fact]
    public void Compile_MultiplePassingTestBlocks_ShouldCountAll()
    {
        // Arrange
        const string source = @"
            (do
              (defun square ((n : Num)) : Num (return (* n n)))
              (defun cube ((n : Num)) : Num (return (* n (* n n))))
              (test square
                (assert-eq (square 3) 9)
                (assert-eq (square 0) 0))
              (test cube
                (assert-eq (cube 2) 8)))";
        var compiler = new Compiler(emitBinary: false);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
        result.TestsPassed.Should().Be(2);
    }

    [Fact]
    public void Compile_MultipleFailingTests_ShouldCollectAllErrors()
    {
        // Arrange — two test blocks, both failing
        const string source = @"
            (do
              (defun add ((a : Num) (b : Num)) : Num (return (+ a b)))
              (defun mul ((a : Num) (b : Num)) : Num (return (+ a b)))
              (test add
                (assert-eq (add 1 2) 999))
              (test mul
                (assert-eq (mul 3 4) 999)))";
        var compiler = new Compiler(emitBinary: false);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().HaveCount(2);
        result.Diagnostics[0].Type.Should().Be("test-failure");
        result.Diagnostics[0].InContext.Should().Be("add");
        result.Diagnostics[1].Type.Should().Be("test-failure");
        result.Diagnostics[1].InContext.Should().Be("mul");
        result.TestsFailed.Should().Be(2);
    }

    [Fact]
    public void Compile_MixedPassingAndFailingTests_ShouldReportBoth()
    {
        // Arrange — 3 tests: 2 pass, 1 fails
        const string source = @"
            (do
              (defun add ((a : Num) (b : Num)) : Num (return (+ a b)))
              (defun sub ((a : Num) (b : Num)) : Num (return (- a b)))
              (defun bad ((a : Num)) : Num (return 0))
              (test add (assert-eq (add 1 2) 3))
              (test sub (assert-eq (sub 5 3) 2))
              (test bad (assert-eq (bad 1) 999)))";
        var compiler = new Compiler(emitBinary: false);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeFalse();
        result.TestsPassed.Should().Be(2);
        result.TestsFailed.Should().Be(1);
        result.Diagnostics.Should().ContainSingle();
        result.Diagnostics[0].InContext.Should().Be("bad");
    }

    [Fact]
    public void Compile_AllErrorsInSExpr_ShouldListMultipleDiagnostics()
    {
        // Arrange
        const string source = @"
            (do
              (defun f ((x : Num)) : Num (return 0))
              (defun g ((x : Num)) : Num (return 0))
              (test f (assert-eq (f 1) 999))
              (test g (assert-eq (g 2) 888)))";
        var compiler = new Compiler(emitBinary: false);

        // Act
        var result = compiler.Compile(source);
        string sexpr = result.ToSExpr();

        // Assert — both diagnostics should appear in the output
        sexpr.Should().Contain("(error");
        sexpr.Should().Contain("(in-context \"f\")");
        sexpr.Should().Contain("(in-context \"g\")");
        sexpr.Should().Contain("(tests-passed 0/2)");
    }
}
