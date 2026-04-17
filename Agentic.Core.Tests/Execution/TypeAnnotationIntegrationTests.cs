using Agentic.Core.Execution;
using Agentic.Core.Syntax;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Execution;

/// <summary>
/// End-to-end tests verifying that typed and untyped defun/def syntax
/// works correctly through the full Verifier → Transpiler pipeline.
/// </summary>
public sealed class TypeAnnotationIntegrationTests
{
    [Fact]
    public void Verifier_TypedDefun_ShouldBindParamsCorrectly()
    {
        // Arrange — typed defun with Num params
        const string program = @"
            (do
              (defun double-it ((x : Num)) : Num
                (return (* x 2)))
              (sys.stdout.write (str.from_num (double-it 7))))";
        var ast = Compile(program);
        var verifier = new Verifier();

        // Act
        verifier.Evaluate(ast);

        // Assert
        verifier.CapturedOutput.Should().Be("14");
    }

    [Fact]
    public void Verifier_TypedDefWithNum_ShouldResolveValue()
    {
        // Arrange — (def x : Num 42)
        const string program = "(do (def x : Num 42) (sys.stdout.write (str.from_num x)))";
        var ast = Compile(program);
        var verifier = new Verifier();

        // Act
        verifier.Evaluate(ast);

        // Assert
        verifier.CapturedOutput.Should().Be("42");
    }

    [Fact]
    public void Verifier_TypedDefWithStr_ShouldResolveValue()
    {
        // Arrange — (def name : Str "world")
        const string program = @"(do (def name : Str ""world"") (sys.stdout.write (str.concat ""hello "" name)))";
        var ast = Compile(program);
        var verifier = new Verifier();

        // Act
        verifier.Evaluate(ast);

        // Assert
        verifier.CapturedOutput.Should().Be("hello world");
    }

    [Fact]
    public void Transpiler_TypedDefun_ShouldEmitTypedCSharpSignature()
    {
        // Arrange
        const string program = @"
            (do
              (defun add ((a : Num) (b : Num)) : Num
                (return (+ a b))))";
        var ast = Compile(program);

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert
        csharp.Should().Contain("double add(double a, double b)");
    }

    [Fact]
    public void Transpiler_TypedDefunWithStr_ShouldEmitStringReturn()
    {
        // Arrange
        const string program = @"
            (do
              (defun greet ((name : Str)) : Str
                (return (str.concat ""Hi "" name))))";
        var ast = Compile(program);

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert
        csharp.Should().Contain("string greet(string name)");
    }

    [Fact]
    public void Transpiler_TypedDef_ShouldEmitExplicitType()
    {
        // Arrange
        const string program = "(do (def x : Num 10))";
        var ast = Compile(program);

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert
        csharp.Should().Contain("double x =");
    }

    [Fact]
    public void Transpiler_UntypedDefun_ShouldStillEmitDouble()
    {
        // Arrange — backward compatibility: untyped defun defaults to double
        const string program = @"
            (do
              (defun square (n)
                (return (* n n))))";
        var ast = Compile(program);

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert
        csharp.Should().Contain("double square(double n)");
    }

    [Fact]
    public void Transpiler_UntypedDef_ShouldStillEmitVar()
    {
        // Arrange — backward compatibility
        const string program = "(do (def x 5))";
        var ast = Compile(program);

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert
        csharp.Should().Contain("var x = 5.0;");
    }

    private static AstNode Compile(string source) =>
        new Parser(new Lexer(source).Tokenize()).Parse();
}
