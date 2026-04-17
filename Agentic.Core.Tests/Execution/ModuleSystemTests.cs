using Agentic.Core.Execution;
using Agentic.Core.Syntax;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Execution;

/// <summary>
/// Tests for the module system: <c>(module …)</c>, <c>(import …)</c>, <c>(export …)</c>.
/// </summary>
public sealed class ModuleSystemTests
{
    [Fact]
    public void Module_BasicProgram_ShouldEvaluateBody()
    {
        // Arrange
        const string source = @"
            (module Calculator
              (import std.math)
              (defun add ((a : Num) (b : Num)) : Num (return (+ a b)))
              (sys.stdout.write (str.from_num (add 3 4))))";
        var ast = Compile(source);
        var verifier = new Verifier();

        // Act
        verifier.Evaluate(ast);

        // Assert
        verifier.CapturedOutput.Should().Be("7");
    }

    [Fact]
    public void Module_WithTestBlock_ShouldRunTests()
    {
        // Arrange
        const string source = @"
            (module MathLib
              (import std.math)
              (defun square ((n : Num)) : Num (return (* n n)))
              (test square
                (assert-eq (square 3) 9)
                (assert-eq (square 5) 25))
              (export square))";
        var ast = Compile(source);
        var verifier = new Verifier();

        // Act
        verifier.Evaluate(ast);

        // Assert
        verifier.TestsPassed.Should().Be(1);
    }

    [Fact]
    public void Module_WithContractInDefun_ShouldEnforceContract()
    {
        // Arrange
        const string source = @"
            (module SafeMath
              (defun safe-div ((a : Num) (b : Num)) : Num
                (do
                  (require (> b 0))
                  (return (/ a b))))
              (test safe-div
                (assert-eq (safe-div 10 2) 5)))";
        var ast = Compile(source);
        var verifier = new Verifier();

        // Act
        verifier.Evaluate(ast);

        // Assert
        verifier.TestsPassed.Should().Be(1);
    }

    [Fact]
    public void Module_Transpile_ShouldEmitValidCSharp()
    {
        // Arrange
        const string source = @"
            (module Greeter
              (import std.string)
              (defun greet ((name : Str)) : Str
                (return (str.concat ""Hello "" name)))
              (sys.stdout.write (greet ""World"")))";
        var ast = Compile(source);

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert
        csharp.Should().Contain("string greet(string name)");
        csharp.Should().Contain("Console.Write");
        csharp.Should().NotContain("module");
        csharp.Should().NotContain("import");
    }

    [Fact]
    public void Import_StdModule_ShouldBeAccepted()
    {
        // Arrange
        const string source = "(module Foo (import std.math) (import std.string))";
        var ast = Compile(source);
        var verifier = new Verifier();

        // Act
        var act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Export_ShouldBeRecognized()
    {
        // Arrange
        const string source = @"
            (module Lib
              (defun helper ((x : Num)) : Num (return (* x 2)))
              (export helper))";
        var ast = Compile(source);
        var verifier = new Verifier();

        // Act
        var act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Compiler_ModuleWithTests_ShouldProduceStructuredResult()
    {
        // Arrange
        const string source = @"
            (module Stats
              (defun mean ((a : Num) (b : Num)) : Num
                (return (/ (+ a b) 2.0)))
              (test mean
                (assert-eq (mean 10 20) 15)
                (assert-near (mean 1 2) 1.5 0.001)))";
        var compiler = new Compiler(emitBinary: false);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
        result.TestsPassed.Should().Be(1);
        var sexpr = result.ToSExpr();
        sexpr.Should().Contain("(ok");
        sexpr.Should().Contain("(tests-passed 1/1)");
    }

    private static AstNode Compile(string source) =>
        new Parser(new Lexer(source).Tokenize()).Parse();
}
