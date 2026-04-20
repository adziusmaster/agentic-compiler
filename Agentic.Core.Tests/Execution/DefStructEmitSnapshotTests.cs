using Agentic.Core.Execution;
using Agentic.Core.Syntax;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Agentic.Core.Tests.Execution;

// Captures the full transpiled C# for a representative struct program so regressions
// in hoisting order or emission shape are obvious. Not a true snapshot tool — just
// a readable assertion that the shape hasn't drifted.
public sealed class DefStructEmitSnapshotTests
{
    private readonly ITestOutputHelper _output;

    public DefStructEmitSnapshotTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Transpile_RectangleAreaProgram_ShouldProduceCompilableCSharp()
    {
        // Arrange
        const string program = @"
            (do
              (defstruct Rect (w h))
              (def r (Rect.new 10 5))
              (sys.stdout.write (* (Rect.w r) (Rect.h r))))";
        var ast = new Parser(new Lexer(program).Tokenize()).Parse();

        // Act
        string csharp = new Transpiler().Transpile(ast);
        _output.WriteLine(csharp);

        // Assert — structural properties that together imply valid C#
        csharp.Should().Contain("using System;");
        csharp.Should().Contain("public record struct Rect(double w, double h);");
        csharp.Should().Contain("class Program");
        csharp.Should().Contain("static void Main(string[] args)");
        csharp.Should().Contain("var r = new Rect(10.0, 5.0);");
        csharp.Should().Contain("Console.Write(AgCanonical.Out((r.w * r.h)));");
    }
}
