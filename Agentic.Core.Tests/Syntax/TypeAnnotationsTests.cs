using Agentic.Core.Syntax;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Syntax;

public sealed class TypeAnnotationsTests
{
    [Fact]
    public void ParseAnnotation_Num_ShouldReturnNumType()
    {
        // Arrange
        var atom = new AtomNode(new Token(TokenType.Identifier, "Num", 0, 0));

        // Act
        var result = TypeAnnotations.ParseAnnotation(atom);

        // Assert
        result.Should().Be(AgType.Num);
    }

    [Fact]
    public void ParseAnnotation_Str_ShouldReturnStrType()
    {
        var atom = new AtomNode(new Token(TokenType.Identifier, "Str", 0, 0));
        TypeAnnotations.ParseAnnotation(atom).Should().Be(AgType.Str);
    }

    [Fact]
    public void ParseAnnotation_Bool_ShouldReturnBoolType()
    {
        var atom = new AtomNode(new Token(TokenType.Identifier, "Bool", 0, 0));
        TypeAnnotations.ParseAnnotation(atom).Should().Be(AgType.Bool);
    }

    [Fact]
    public void ParseAnnotation_ArrayNum_ShouldReturnArrayOfNum()
    {
        // Arrange — (Array Num)
        var list = new ListNode(new[]
        {
            (AstNode)new AtomNode(new Token(TokenType.Identifier, "Array", 0, 0)),
            new AtomNode(new Token(TokenType.Identifier, "Num", 0, 0))
        });

        // Act
        var result = TypeAnnotations.ParseAnnotation(list);

        // Assert
        result.Should().Be(AgType.ArrayOf(AgType.Num));
    }

    [Fact]
    public void ParseAnnotation_LowercaseUnknownIdentifier_ShouldReturnUnknown()
    {
        // Lowercase identifiers are not treated as struct references.
        var atom = new AtomNode(new Token(TokenType.Identifier, "foo", 0, 0));
        TypeAnnotations.ParseAnnotation(atom).Should().Be(AgType.Unknown);
    }

    [Fact]
    public void ParseDefun_UntypedSyntax_ShouldExtractParamsAndBody()
    {
        // Arrange — (defun add (a b) (+ a b))
        var ast = Parse("(defun add (a b) (+ a b))");

        // Act
        var sig = TypeAnnotations.ParseDefun((ListNode)ast);

        // Assert
        sig.Name.Should().Be("add");
        sig.Parameters.Should().HaveCount(2);
        sig.Parameters[0].Param.Should().Be("a");
        sig.Parameters[0].Type.Should().Be(AgType.Num);
        sig.Parameters[1].Param.Should().Be("b");
        sig.Parameters[1].Type.Should().Be(AgType.Num);
        sig.ReturnType.Should().Be(AgType.Num);
        sig.Body.Should().BeOfType<ListNode>();
    }

    [Fact]
    public void ParseDefun_TypedSyntax_ShouldExtractExplicitTypes()
    {
        // Arrange — (defun greet ((name : Str) (count : Num)) : Str (str.concat name "!"))
        var ast = Parse("(defun greet ((name : Str) (count : Num)) : Str (str.concat name \"!\"))");

        // Act
        var sig = TypeAnnotations.ParseDefun((ListNode)ast);

        // Assert
        sig.Name.Should().Be("greet");
        sig.Parameters.Should().HaveCount(2);
        sig.Parameters[0].Param.Should().Be("name");
        sig.Parameters[0].Type.Should().Be(AgType.Str);
        sig.Parameters[1].Param.Should().Be("count");
        sig.Parameters[1].Type.Should().Be(AgType.Num);
        sig.ReturnType.Should().Be(AgType.Str);
    }

    [Fact]
    public void ParseDef_UntypedSyntax_ShouldReturnNameAndValue()
    {
        // Arrange — (def x 5)
        var ast = Parse("(def x 5)");

        // Act
        var (name, type, valueNode) = TypeAnnotations.ParseDef((ListNode)ast);

        // Assert
        name.Should().Be("x");
        type.Should().BeNull();
        valueNode.Should().BeOfType<AtomNode>();
        ((AtomNode)valueNode).Token.Value.Should().Be("5");
    }

    [Fact]
    public void ParseDef_TypedSyntax_ShouldReturnNameTypeAndValue()
    {
        // Arrange — (def x : Num 5)
        var ast = Parse("(def x : Num 5)");

        // Act
        var (name, type, valueNode) = TypeAnnotations.ParseDef((ListNode)ast);

        // Assert
        name.Should().Be("x");
        type.Should().Be(AgType.Num);
        ((AtomNode)valueNode).Token.Value.Should().Be("5");
    }

    [Fact]
    public void ParseDef_TypedStr_ShouldReturnStrType()
    {
        // Arrange — (def greeting : Str "hello")
        var ast = Parse("(def greeting : Str \"hello\")");

        // Act
        var (name, type, _) = TypeAnnotations.ParseDef((ListNode)ast);

        // Assert
        name.Should().Be("greeting");
        type.Should().Be(AgType.Str);
    }

    private static AstNode Parse(string source) =>
        new Parser(new Lexer(source).Tokenize()).Parse();
}
