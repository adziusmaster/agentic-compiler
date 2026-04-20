using Agentic.Core.Execution;
using Agentic.Core.Syntax;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Syntax;

public sealed class StructTypeAnnotationTests
{
    [Fact]
    public void ParseAnnotation_UppercaseIdentifier_ShouldProduceStructType()
    {
        // Arrange
        var node = new AtomNode(new Token(TokenType.Identifier, "Point", 0, 0));

        // Act
        var result = TypeAnnotations.ParseAnnotation(node);

        // Assert
        result.Should().BeOfType<StructType>().Which.Name.Should().Be("Point");
    }

    [Fact]
    public void ParseDefun_RecordParam_ShouldResolveToStructType()
    {
        // Arrange
        const string src = "(do (defun midpoint ((a : Point) (b : Point)) : Point 0))";
        var ast = (ListNode)new Parser(new Lexer(src).Tokenize()).Parse();
        var defun = (ListNode)ast.Elements[1];

        // Act
        var sig = TypeAnnotations.ParseDefun(defun);

        // Assert
        sig.Parameters.Should().HaveCount(2);
        sig.Parameters[0].Type.Should().BeOfType<StructType>().Which.Name.Should().Be("Point");
        sig.ReturnType.Should().BeOfType<StructType>().Which.Name.Should().Be("Point");
        AgType.ToCSharp(sig.Parameters[0].Type).Should().Be("Point");
    }

    [Fact]
    public void Transpile_RecordParamAndReturn_ShouldEmitStructTypeNotVar()
    {
        // Arrange
        const string src = @"
            (do
              (defstruct Point (x y))
              (defun midpoint ((a : Point) (b : Point)) : Point
                (return (Point.new (Point.x a) (Point.y b))))
              (sys.stdout.write (midpoint (Point.new 0 0) (Point.new 4 6))))";
        var ast = new Parser(new Lexer(src).Tokenize()).Parse();

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert — no `var` leaks for param / return position
        csharp.Should().Contain("Point midpoint(Point a, Point b)");
    }

    [Fact]
    public void Transpile_TypedStructFields_WithHyphenatedFunctionName()
    {
        // Arrange — ShoppingCart-shape: typed fields + hyphenated defun name.
        const string src = @"
            (module ShoppingCart
              (defstruct Item ((name : Str) (qty : Num) (price : Num)))
              (defun line-total ((it : Item)) : Num
                (return (* (Item.qty it) (Item.price it)))))";
        var ast = new Parser(new Lexer(src).Tokenize()).Parse();

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert
        csharp.Should().Contain("public record struct Item(string name, double qty, double price);");
        csharp.Should().Contain("double line_total(Item it)");
    }

    [Fact]
    public void Transpile_FullShoppingCartSample_ShouldEmitItemTypedParams()
    {
        // Arrange — the full ShoppingCart.ag source (with tests and main stdout).
        const string src = @"
            (module ShoppingCart
              (defstruct Item ((name : Str) (qty : Num) (price : Num)))

              (defun line-total ((it : Item)) : Num
                (return (* (Item.qty it) (Item.price it))))

              (defun discounted ((it : Item) (pct : Num)) : Item
                (return (Item.set-price it (* (Item.price it) (- 1 pct)))))

              (test line-total
                (def apple : Item (Item.new ""apple"" 3 2))
                (assert-eq (line-total apple) 6))

              (test discount-returns-new-item
                (def apple : Item (Item.new ""apple"" 3 2))
                (def half : Item (discounted apple 0.5))
                (assert-eq (Item.price half) 1)
                (assert-eq (Item.price apple) 2))

              (sys.stdout.write (discounted (Item.new ""apple"" 3 2) 0.1)))";
        var ast = new Parser(new Lexer(src).Tokenize()).Parse();

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert
        csharp.Should().Contain("double line_total(Item it)");
        csharp.Should().Contain("Item discounted(Item it, double pct)");
    }
}
