using Agentic.Core.Execution;
using Agentic.Core.Syntax;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Execution;

public sealed class TranspilerTests
{
    [Fact]
    public void Transpile_HelloWorld_ShouldEmitConsoleWrite()
    {
        // Arrange
        var ast = Compile("(do (sys.stdout.write \"Hello\"))");

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert
        csharp.Should().Contain("Console.Write(AgCanonical.Out(\"Hello\"))");
    }

    [Fact]
    public void Transpile_RecursiveFactorial_ShouldEmitLocalFunctionThatCallsItself()
    {
        // Arrange
        const string program = @"
            (do
              (defun fact (n)
                (if (<= n 1)
                    (return 1)
                    (return (* n (fact (- n 1))))))
              (sys.stdout.write (fact 5)))";
        var ast = Compile(program);

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert — the emitted C# must declare the function AND call it recursively
        csharp.Should().Contain("double fact(double n)");
        csharp.Should().Contain("fact((n - 1.0))");
    }

    [Fact]
    public void Transpile_ArrayNewInDef_ShouldEmitDoubleArrayDeclaration()
    {
        // Arrange
        var ast = Compile("(do (def a (arr.new 3)))");

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert
        csharp.Should().Contain("double[] a = new double[(int)(3.0)]");
    }

    [Fact]
    public void Transpile_StringLiteralInDef_ShouldEmitStringDeclaration()
    {
        // Arrange
        var ast = Compile("(do (def msg \"hi\"))");

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert
        csharp.Should().Contain("string msg = \"hi\"");
    }

    private static AstNode Compile(string source) =>
        new Parser(new Lexer(source).Tokenize()).Parse();
}
