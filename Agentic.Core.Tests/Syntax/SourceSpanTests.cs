using Agentic.Core.Syntax;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Syntax;

public sealed class SourceSpanTests
{
    [Fact]
    public void Parser_ListNode_ShouldCaptureSpanFromOpeningToClosingParen()
    {
        // Arrange
        var tokens = new Lexer("(+ 1 2)").Tokenize();

        // Act
        var ast = new Parser(tokens).Parse();

        // Assert
        ast.Should().BeOfType<ListNode>();
        ast.Span.Should().NotBeNull();
        ast.Span!.Value.StartLine.Should().Be(1);
        ast.Span!.Value.StartColumn.Should().Be(1);
        ast.Span!.Value.EndLine.Should().Be(1);
    }

    [Fact]
    public void Parser_AtomNode_ShouldCaptureSpanAtTokenPosition()
    {
        // Arrange — a list with a single atom "hello" at column 2.
        var tokens = new Lexer("(hello)").Tokenize();

        // Act
        var list = (ListNode)new Parser(tokens).Parse();
        var atom = (AtomNode)list.Elements[0];

        // Assert
        atom.Span.Should().NotBeNull();
        atom.Span!.Value.StartLine.Should().Be(1);
        atom.Span!.Value.StartColumn.Should().Be(2);
    }

    [Fact]
    public void Parser_MultilineList_ShouldSpanAcrossLines()
    {
        // Arrange
        var tokens = new Lexer("(do\n  (+ 1 2)\n)").Tokenize();

        // Act
        var ast = (ListNode)new Parser(tokens).Parse();

        // Assert
        ast.Span.Should().NotBeNull();
        ast.Span!.Value.StartLine.Should().Be(1);
        ast.Span!.Value.EndLine.Should().Be(3);
    }
}
