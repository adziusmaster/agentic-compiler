using System;
using Agentic.Core.Execution;
using Agentic.Core.Syntax;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Execution;

public sealed class DefStructTests
{
    [Fact]
    public void Verifier_ConstructAndReadField_ShouldReturnStoredValue()
    {
        // Arrange
        const string program = @"
            (do
              (defstruct Point (x y))
              (def p (Point.new 3 4))
              (sys.stdout.write (Point.x p)))";
        var ast = Compile(program);
        var verifier = new Verifier();

        // Act
        verifier.Evaluate(ast);

        // Assert
        verifier.CapturedOutput.Should().Be("3");
    }

    [Fact]
    public void Verifier_SetFieldFunctional_ShouldReturnNewRecordWithoutMutatingOriginal()
    {
        // Arrange — (Point.set-x p 99) must return a new record; p must still read as 3.
        const string program =
            "(do " +
            "  (defstruct Point (x y)) " +
            "  (def p (Point.new 3 4)) " +
            "  (def q (Point.set-x p 99)) " +
            "  (sys.stdout.write (Point.x p)) " +
            "  (sys.stdout.write \",\") " +
            "  (sys.stdout.write (Point.x q)))";
        var ast = Compile(program);
        var verifier = new Verifier();

        // Act
        verifier.Evaluate(ast);

        // Assert
        verifier.CapturedOutput.Should().Be("3,99");
    }

    [Fact]
    public void Verifier_RectangleAreaFromStruct_ShouldComputeCorrectly()
    {
        // Arrange
        const string program = @"
            (do
              (defstruct Rect (w h))
              (def r (Rect.new 10 5))
              (sys.stdout.write (* (Rect.w r) (Rect.h r))))";
        var ast = Compile(program);
        var verifier = new Verifier();

        // Act
        verifier.Evaluate(ast);

        // Assert
        verifier.CapturedOutput.Should().Be("50");
    }

    [Fact]
    public void Verifier_ConstructorWithWrongArity_ShouldThrowReadableError()
    {
        // Arrange — Point has 2 fields; passing 1 should fail cleanly.
        const string program = @"
            (do
              (defstruct Point (x y))
              (def p (Point.new 3)))";
        var ast = Compile(program);
        var verifier = new Verifier();

        // Act
        Action act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Point.new*expected 2*got 1*");
    }

    [Fact]
    public void Verifier_UnknownFieldRead_ShouldThrowReadableError()
    {
        // Arrange — reading a field that doesn't exist.
        const string program = @"
            (do
              (defstruct Point (x y))
              (def p (Point.new 3 4))
              (sys.stdout.write (Point.z p)))";
        var ast = Compile(program);
        var verifier = new Verifier();

        // Act
        Action act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Field 'z' not found*");
    }

    [Fact]
    public void Transpiler_DefStruct_ShouldHoistCSharpRecordStructAboveProgram()
    {
        // Arrange
        const string program = @"
            (do
              (defstruct Point (x y))
              (def p (Point.new 3 4))
              (sys.stdout.write (Point.x p)))";
        var ast = Compile(program);

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert
        csharp.Should().Contain("public record struct Point(double x, double y);");
        // Struct declaration must precede the `class Program` line
        int structPos  = csharp.IndexOf("record struct Point", StringComparison.Ordinal);
        int programPos = csharp.IndexOf("class Program",       StringComparison.Ordinal);
        structPos.Should().BeLessThan(programPos);
    }

    [Fact]
    public void Transpiler_StructConstructor_ShouldEmitNewExpression()
    {
        // Arrange
        var ast = Compile("(do (defstruct Rect (w h)) (def r (Rect.new 10 5)))");

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert
        csharp.Should().Contain("new Rect(10.0, 5.0)");
    }

    [Fact]
    public void Transpiler_StructFieldRead_ShouldEmitDotAccess()
    {
        // Arrange
        const string program = @"
            (do
              (defstruct Rect (w h))
              (def r (Rect.new 10 5))
              (sys.stdout.write (Rect.w r)))";
        var ast = Compile(program);

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert
        csharp.Should().Contain("Console.Write(r.w)");
    }

    [Fact]
    public void Transpiler_StructSetFieldWither_ShouldEmitWithExpression()
    {
        // Arrange
        const string program = @"
            (do
              (defstruct Rect (w h))
              (def r (Rect.new 10 5))
              (def r2 (Rect.set-w r 20)))";
        var ast = Compile(program);

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert
        csharp.Should().Contain("r with { w = 20.0 }");
    }

    private static AstNode Compile(string source) =>
        new Parser(new Lexer(source).Tokenize()).Parse();
}
