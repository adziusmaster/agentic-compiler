using Agentic.Core.Execution;
using Agentic.Core.Syntax;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Execution;

public sealed class TypeInferencePassTests
{
    [Fact]
    public void Scan_WhenDefAssignsArrayNew_ShouldClassifyVariableAsArray()
    {
        // Arrange
        var ast = Compile("(do (def a (arr.new 3)))");
        var pass = new TypeInferencePass();

        // Act
        pass.Scan(ast);

        // Assert
        pass.GetVarType("a").Should().BeOfType<ArrayType>();
        pass.IsArrayVar("a").Should().BeTrue();
    }

    [Fact]
    public void Scan_WhenDefAssignsStringLiteral_ShouldClassifyVariableAsString()
    {
        // Arrange
        var ast = Compile("(do (def msg \"hi\"))");
        var pass = new TypeInferencePass();

        // Act
        pass.Scan(ast);

        // Assert
        pass.GetVarType("msg").Should().Be(AgType.Str);
        pass.IsStringVar("msg").Should().BeTrue();
    }

    [Fact]
    public void Scan_WhenDefAssignsStrConcat_ShouldClassifyVariableAsString()
    {
        // Arrange
        var ast = Compile("(do (def msg (str.concat \"a\" \"b\")))");
        var pass = new TypeInferencePass();

        // Act
        pass.Scan(ast);

        // Assert
        pass.IsStringVar("msg").Should().BeTrue();
    }

    [Fact]
    public void Scan_WhenDefAssignsNumberLiteral_ShouldClassifyVariableAsNum()
    {
        // Arrange
        var ast = Compile("(do (def n 42))");
        var pass = new TypeInferencePass();

        // Act
        pass.Scan(ast);

        // Assert
        pass.GetVarType("n").Should().Be(AgType.Num);
        pass.IsArrayVar("n").Should().BeFalse();
        pass.IsStringVar("n").Should().BeFalse();
    }

    [Fact]
    public void Scan_WhenPlaceholderDefFollowedByArrayNewSet_ShouldSettleOnArray()
    {
        // Arrange — a common LLM mis-pattern this compiler tolerates:
        // `(def arr 0)` as a scalar placeholder, then `(set arr (arr.new N))` as the real init.
        var ast = Compile("(do (def arr 0) (set arr (arr.new 5)))");
        var pass = new TypeInferencePass();

        // Act
        pass.Scan(ast);

        // Assert
        pass.IsArrayVar("arr").Should().BeTrue();
    }

    [Fact]
    public void Scan_WhenDefunDeclared_ShouldRegisterFuncTypeWithCorrectArity()
    {
        // Arrange
        var ast = Compile("(do (defun add (a b) (return (+ a b))))");
        var pass = new TypeInferencePass();

        // Act
        pass.Scan(ast);

        // Assert
        pass.TryGetFuncType("add", out var fn).Should().BeTrue();
        fn.Params.Should().HaveCount(2);
        fn.Return.Should().Be(AgType.Num);
    }

    [Fact]
    public void InferExpression_OnComparisonOperator_ShouldReturnBool()
    {
        // Arrange
        var ast = Compile("(< 1 2)");
        var pass = new TypeInferencePass();
        pass.Scan(Compile("(do)"));

        // Act
        var t = pass.InferExpression(ast);

        // Assert
        t.Should().Be(AgType.Bool);
    }

    [Fact]
    public void InferExpression_OnUnknownIdentifier_ShouldReturnUnknown()
    {
        // Arrange
        var ast = Compile("missing");
        var pass = new TypeInferencePass();
        pass.Scan(Compile("(do)"));

        // Act
        var t = pass.InferExpression(ast);

        // Assert
        t.Should().BeOfType<UnknownType>();
    }

    [Fact]
    public void Sanitize_WhenIdentifierContainsHyphens_ShouldReplaceWithUnderscores()
    {
        // Arrange / Act
        var s = TypeInferencePass.Sanitize("my-cool-name");

        // Assert
        s.Should().Be("my_cool_name");
    }

    private static AstNode Compile(string source) =>
        new Parser(new Lexer(source).Tokenize()).Parse();
}
