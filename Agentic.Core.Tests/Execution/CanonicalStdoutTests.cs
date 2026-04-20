using Agentic.Core.Execution;
using Agentic.Core.Syntax;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Execution;

/// <summary>
/// Ensures <c>(sys.stdout.write …)</c> produces canonical, round-trippable text
/// for composite values — the substrate the Implementer's micro-test probes
/// depend on to verify non-numeric helpers.
/// </summary>
public sealed class CanonicalStdoutTests
{
    [Fact]
    public void StdoutWrite_Primitive_ShouldMatchLegacyFormat()
    {
        // Arrange — existing samples depend on unquoted string output.
        var verifier = new Verifier();
        var ast = ParseOf("(do (sys.stdout.write \"hello\"))");

        // Act
        verifier.Evaluate(ast);

        // Assert
        verifier.CapturedOutput.Should().Be("hello");
    }

    [Fact]
    public void StdoutWrite_IntegerDouble_ShouldOmitTrailingZero()
    {
        // Arrange
        var verifier = new Verifier();
        var ast = ParseOf("(do (sys.stdout.write 42))");

        // Act
        verifier.Evaluate(ast);

        // Assert
        verifier.CapturedOutput.Should().Be("42");
    }

    [Fact]
    public void StdoutWrite_Map_ShouldRenderCanonicalForm()
    {
        // Arrange — previously this printed "System.Collections.Generic.Dictionary`2…".
        var verifier = new Verifier();
        var ast = ParseOf(@"
            (do
              (def m : (Map Str Num) (map.new))
              (map.set m ""b"" 2)
              (map.set m ""a"" 1)
              (sys.stdout.write m))");

        // Act
        verifier.Evaluate(ast);

        // Assert — keys sorted lexicographically, values inline.
        verifier.CapturedOutput.Should().Be("{\"a\": 1, \"b\": 2}");
    }

    [Fact]
    public void StdoutWrite_Array_ShouldRenderBracketedList()
    {
        // Arrange
        var verifier = new Verifier();
        var ast = ParseOf(@"
            (do
              (def a : (Array Num) (arr.new 3))
              (arr.set a 0 10)
              (arr.set a 1 20)
              (arr.set a 2 30)
              (sys.stdout.write a))");

        // Act
        verifier.Evaluate(ast);

        // Assert
        verifier.CapturedOutput.Should().Be("[10, 20, 30]");
    }

    [Fact]
    public void StdoutWrite_Record_ShouldRenderTypeAndFields()
    {
        // Arrange
        var verifier = new Verifier();
        var ast = ParseOf(@"
            (do
              (defstruct Point (x y))
              (def p (Point.new 3 4))
              (sys.stdout.write p))");

        // Act
        verifier.Evaluate(ast);

        // Assert
        verifier.CapturedOutput.Should().Be("Point{x: 3, y: 4}");
    }

    private static AstNode ParseOf(string src) =>
        new Parser(new Lexer(src).Tokenize()).Parse();
}
