using Agentic.Core.Execution;
using Agentic.Core.Runtime;
using Agentic.Core.Syntax;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Syntax;

public sealed class FuncTypeAnnotationTests
{
    [Fact]
    public void ParseAnnotation_FuncUnary_ShouldProduceFuncType()
    {
        // (Func Num Num) — unary Num → Num
        const string src = "(Func Num Num)";
        var node = new Parser(new Lexer(src).Tokenize()).Parse();

        var result = TypeAnnotations.ParseAnnotation(node);

        result.Should().BeOfType<FuncType>();
        var ft = (FuncType)result;
        ft.Params.Should().HaveCount(1);
        ft.Params[0].Should().BeOfType<NumType>();
        ft.Return.Should().BeOfType<NumType>();
    }

    [Fact]
    public void ParseAnnotation_FuncBinary_ShouldParseBothParamTypes()
    {
        // (Func Num Num Num) — binary Num × Num → Num
        const string src = "(Func Num Num Num)";
        var node = new Parser(new Lexer(src).Tokenize()).Parse();

        var ft = (FuncType)TypeAnnotations.ParseAnnotation(node);

        ft.Params.Should().HaveCount(2);
        ft.Return.Should().BeOfType<NumType>();
    }

    [Fact]
    public void ToCSharp_FuncType_ShouldEmitGenericFunc()
    {
        var ft = new FuncType(new AgType[] { AgType.Num }, AgType.Num);
        AgType.ToCSharp(ft).Should().Be("Func<double, double>");
    }

    [Fact]
    public void Verify_ApplyTwice_WithFunctionValueArg_ShouldReturnTwiceApplied()
    {
        // `apply-twice` takes a function and applies it twice. LLM-friendly idiom.
        const string src = @"
            (do
              (defun inc ((n : Num)) : Num (return (+ n 1)))
              (defun apply-twice ((f : (Func Num Num)) (x : Num)) : Num
                (return (f (f x))))
              (test twice
                (assert-eq (apply-twice inc 5) 7)))";
        var ast = new Parser(new Lexer(src).Tokenize()).Parse();

        var verifier = new Verifier();
        verifier.Evaluate(ast);

        verifier.TestsPassed.Should().Be(1);
        verifier.TestsFailed.Should().Be(0);
    }

    [Fact]
    public void Transpile_FuncTypedParam_ShouldEmitFuncGenericAndInvoke()
    {
        const string src = @"
            (do
              (defun inc ((n : Num)) : Num (return (+ n 1)))
              (defun apply-twice ((f : (Func Num Num)) (x : Num)) : Num
                (return (f (f x))))
              (sys.stdout.write (apply-twice inc 5)))";
        var ast = new Parser(new Lexer(src).Tokenize()).Parse();

        string csharp = new Transpiler().Transpile(ast);

        csharp.Should().Contain("double apply_twice(Func<double, double> f, double x)");
        csharp.Should().Contain("apply_twice(inc, 5");
    }
}
