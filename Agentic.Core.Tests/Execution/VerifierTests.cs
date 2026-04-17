using System;
using Agentic.Core.Execution;
using Agentic.Core.Syntax;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Execution;

public sealed class VerifierTests
{
    [Fact]
    public void Evaluate_HelloWorldProgram_ShouldCaptureStdoutLiteral()
    {
        // Arrange
        var ast = Compile("(do (sys.stdout.write \"Hello, Agentic World!\"))");
        var verifier = new Verifier();

        // Act
        verifier.Evaluate(ast);

        // Assert
        verifier.CapturedOutput.Should().Be("Hello, Agentic World!");
    }

    [Fact]
    public void Evaluate_RecursiveFactorial_ShouldComputeCorrectly()
    {
        // Arrange — classic recursion. This test protects the recursion enablement in Stage 1B.
        const string program = @"
            (do
              (defun fact (n)
                (if (<= n 1)
                    (return 1)
                    (return (* n (fact (- n 1))))))
              (sys.stdout.write (fact (sys.input.get 0))))";
        var ast = Compile(program);
        var verifier = new Verifier(new[] { "5" });

        // Act
        verifier.Evaluate(ast);

        // Assert
        verifier.CapturedOutput.Should().Be("120");
    }

    [Fact]
    public void Evaluate_RecursiveGcd_ShouldComputeCorrectly()
    {
        // Arrange
        const string program = @"
            (do
              (defun gcd (a b)
                (if (= b 0)
                    (return a)
                    (return (gcd b (math.mod a b)))))
              (sys.stdout.write (gcd (sys.input.get 0) (sys.input.get 1))))";
        var ast = Compile(program);
        var verifier = new Verifier(new[] { "48", "18" });

        // Act
        verifier.Evaluate(ast);

        // Assert
        verifier.CapturedOutput.Should().Be("6");
    }

    [Fact]
    public void Evaluate_UnboundedRecursion_ShouldSurfaceStackDiagnostic()
    {
        // Arrange — a recursive function missing its base case. Stage 1B must catch this
        // with a readable diagnostic instead of crashing the host with StackOverflowException.
        const string program = @"
            (do
              (defun forever (n) (return (forever (+ n 1))))
              (forever 0))";
        var ast = Compile(program);
        var verifier = new Verifier();

        // Act
        Action act = () => verifier.Evaluate(ast);

        // Assert — either the logical cap or the host-stack guard may fire first,
        // depending on host stack size; both surface a readable diagnostic.
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Stack overflow*base case*");
    }

    [Fact]
    public void Evaluate_WhenOperationHasMissingArgument_ShouldReportArityError()
    {
        // Arrange — (arr.new) with no size — LLM arity slip.
        var ast = Compile("(do (def a (arr.new)))");
        var verifier = new Verifier();

        // Act
        Action act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Arity error*arr.new*");
    }

    private static AstNode Compile(string source) =>
        new Parser(new Lexer(source).Tokenize()).Parse();
}
