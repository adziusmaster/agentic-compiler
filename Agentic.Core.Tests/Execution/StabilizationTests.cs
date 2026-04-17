using Agentic.Core.Execution;
using Agentic.Core.Syntax;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Execution;

/// <summary>
/// Tests for stabilization fixes: validation, error messages, edge cases.
/// </summary>
public sealed class StabilizationTests
{
    private static CompileResult Check(string source) =>
        new Compiler(emitBinary: false).Compile(source);

    private static AstNode Parse(string source) =>
        new Parser(new Lexer(source).Tokenize()).Parse();

    // --- S3: Division by zero ---
    [Fact]
    public void DivisionByZero_ShouldProduceClearError()
    {
        var r = Check(@"(module T
            (test div0 (assert-eq (/ 10 0) 0)))");
        r.Success.Should().BeFalse();
        r.Diagnostics.Should().Contain(d => d.Message.Contains("Division by zero"));
    }

    // --- S3: arr.new negative size ---
    [Fact]
    public void ArrayNew_NegativeSize_ShouldFail()
    {
        var r = Check(@"(module T
            (test neg (do (def a (arr.new -1)) (assert-true 1))))");
        r.Success.Should().BeFalse();
        r.Diagnostics.Should().Contain(d => d.Message.Contains("non-negative"));
    }

    // --- S3: Function arity mismatch ---
    [Fact]
    public void FunctionCall_WrongArity_ShouldProduceArityError()
    {
        var r = Check(@"(module T
            (defun add ((a : Num) (b : Num)) : Num (return (+ a b)))
            (test t (assert-eq (add 1) 1)))");
        r.Success.Should().BeFalse();
        r.Diagnostics.Should().Contain(d => d.Message.Contains("Arity"));
    }

    // --- S3: Undefined function ---
    [Fact]
    public void UndefinedFunction_ShouldProduceClearError()
    {
        var r = Check(@"(module T
            (test t (assert-eq (nonexistent 1) 1)))");
        r.Success.Should().BeFalse();
        r.Diagnostics.Should().Contain(d => d.Message.Contains("not defined"));
    }

    // --- S5: try/catch catches all exception types ---
    [Fact]
    public void TryCatch_CatchesAllExceptions()
    {
        var r = Check(@"(module T
            (test t (do
                (def result : Num 0)
                (try
                    (/ 1 0)
                    (catch err (set result 99)))
                (assert-eq result 99))))");
        r.Success.Should().BeTrue();
        r.TestsPassed.Should().Be(1);
    }

    // --- S6: require shows condition ---
    [Fact]
    public void Require_ErrorMessageIncludesCondition()
    {
        var r = Check(@"(module T
            (defun check ((x : Num)) : Num
                (do (require (> x 0)) (return x)))
            (test t (assert-eq (check -1) -1)))");
        r.Success.Should().BeFalse();
        r.Diagnostics.Should().Contain(d => d.Message.Contains("precondition") && d.Message.Contains(">"));
    }

    // --- S6: str.split empty separator ---
    [Fact]
    public void StrSplit_EmptySeparator_ShouldFail()
    {
        var r = Check(@"(module T
            (test t (do
                (def parts (str.split ""hello"" """"))
                (assert-eq 1 1))))");
        r.Success.Should().BeFalse();
        r.Diagnostics.Should().Contain(d => d.Message.Contains("separator"));
    }

    // --- S1: map.set as statement works ---
    [Fact]
    public void MapSet_AsStatement_ShouldTranspileCleanly()
    {
        var r = Check(@"(module T
            (defun f () : Num
                (do
                    (def m (map.new))
                    (map.set m ""key"" ""val"")
                    (return (map.size m))))
            (test t (assert-eq (f) 1)))");
        r.Success.Should().BeTrue();
        r.GeneratedSource.Should().Contain("m[\"key\"] = \"val\"");
    }

    // --- S1: if as expression (ternary) ---
    [Fact]
    public void IfExpression_ShouldEmitTernary()
    {
        var r = Check(@"(module T
            (defun pick ((x : Num)) : Num
                (return (if (> x 0) 1 0)))
            (test t (assert-eq (pick 5) 1)))");
        r.Success.Should().BeTrue();
        r.GeneratedSource.Should().Contain("?");
    }

    // --- S7: EmitAutoEntryPoint args bounds check ---
    [Fact]
    public void EmitAutoEntryPoint_ShouldIncludeArgsCheck()
    {
        var r = Check(@"(module T
            (defun greet ((name : Str)) : Str
                (return (str.concat ""Hi "" name))))");
        r.Success.Should().BeTrue();
        r.GeneratedSource.Should().Contain("args.Length");
    }
}
