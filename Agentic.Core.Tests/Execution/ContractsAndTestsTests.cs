using Agentic.Core.Execution;
using Agentic.Core.Syntax;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Execution;

/// <summary>
/// Tests for in-language contracts (<c>require</c>, <c>ensure</c>) and
/// test blocks (<c>test</c>, <c>assert-eq</c>, <c>assert-true</c>, <c>assert-near</c>).
/// </summary>
public sealed class ContractsAndTestsTests
{
    [Fact]
    public void AssertEq_WhenEqual_ShouldPass()
    {
        // Arrange
        var ast = Compile("(do (assert-eq 5 5))");
        var verifier = new Verifier();

        // Act
        var act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void AssertEq_WhenNotEqual_ShouldThrowContractViolation()
    {
        // Arrange
        var ast = Compile("(do (assert-eq 5 10))");
        var verifier = new Verifier();

        // Act
        var act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().Throw<ContractViolationException>()
            .WithMessage("*assert-eq*expected 10*got 5*");
    }

    [Fact]
    public void AssertEq_WithStringValues_ShouldCompareCorrectly()
    {
        // Arrange
        var ast = Compile(@"(do (assert-eq ""hello"" ""hello""))");
        var verifier = new Verifier();

        // Act
        var act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void AssertEq_WithStringMismatch_ShouldFail()
    {
        // Arrange
        var ast = Compile(@"(do (assert-eq ""hello"" ""world""))");
        var verifier = new Verifier();

        // Act
        var act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().Throw<ContractViolationException>()
            .WithMessage("*assert-eq*");
    }

    [Fact]
    public void AssertTrue_WhenTrue_ShouldPass()
    {
        // Arrange
        var ast = Compile("(do (assert-true (> 10 5)))");
        var verifier = new Verifier();

        // Act
        var act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void AssertTrue_WhenFalse_ShouldThrowContractViolation()
    {
        // Arrange
        var ast = Compile("(do (assert-true (< 10 5)))");
        var verifier = new Verifier();

        // Act
        var act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().Throw<ContractViolationException>()
            .WithMessage("*assert-true*expected truthy*");
    }

    [Fact]
    public void AssertNear_WithinEpsilon_ShouldPass()
    {
        // Arrange
        var ast = Compile("(do (assert-near 3.14 3.14159 0.01))");
        var verifier = new Verifier();

        // Act
        var act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void AssertNear_OutsideEpsilon_ShouldThrowContractViolation()
    {
        // Arrange
        var ast = Compile("(do (assert-near 3.0 4.0 0.01))");
        var verifier = new Verifier();

        // Act
        var act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().Throw<ContractViolationException>()
            .WithMessage("*assert-near*expected 4*got 3*");
    }

    [Fact]
    public void Require_WhenTrue_ShouldPass()
    {
        // Arrange
        var ast = Compile("(do (def x 10) (require (> x 0)))");
        var verifier = new Verifier();

        // Act
        var act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Require_WhenFalse_ShouldThrowContractViolation()
    {
        // Arrange
        var ast = Compile("(do (def x 0) (require (> x 0)))");
        var verifier = new Verifier();

        // Act
        var act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().Throw<ContractViolationException>()
            .WithMessage("*require*precondition*");
    }

    [Fact]
    public void Ensure_WhenTrue_ShouldPass()
    {
        // Arrange
        var ast = Compile("(do (def result 42) (ensure (> result 0)))");
        var verifier = new Verifier();

        // Act
        var act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Ensure_WhenFalse_ShouldThrowContractViolation()
    {
        // Arrange
        var ast = Compile("(do (def result 0) (ensure (> result 0)))");
        var verifier = new Verifier();

        // Act
        var act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().Throw<ContractViolationException>()
            .WithMessage("*ensure*postcondition*");
    }

    [Fact]
    public void Test_AllAssertionsPass_ShouldSucceed()
    {
        // Arrange
        const string program = @"
            (do
              (defun double-it ((n : Num)) : Num
                (return (* n 2)))
              (test double-it
                (assert-eq (double-it 3) 6)
                (assert-eq (double-it 0) 0)
                (assert-eq (double-it 5) 10)))";
        var ast = Compile(program);
        var verifier = new Verifier();

        // Act
        var act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().NotThrow();
        verifier.TestsPassed.Should().Be(1);
    }

    [Fact]
    public void Test_AssertionFails_ShouldThrowTestFailure()
    {
        // Arrange
        const string program = @"
            (do
              (defun double-it ((n : Num)) : Num
                (return (* n 3)))
              (test double-it
                (assert-eq (double-it 3) 6)))";
        var ast = Compile(program);
        var verifier = new Verifier();

        // Act
        var act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().Throw<TestFailureException>()
            .WithMessage("*double-it*failed*expected 6*got 9*");
    }

    [Fact]
    public void Test_MultipleTestBlocks_ShouldCountAll()
    {
        // Arrange
        const string program = @"
            (do
              (defun add ((a : Num) (b : Num)) : Num (return (+ a b)))
              (defun sub ((a : Num) (b : Num)) : Num (return (- a b)))
              (test add
                (assert-eq (add 1 2) 3))
              (test sub
                (assert-eq (sub 5 3) 2)))";
        var ast = Compile(program);
        var verifier = new Verifier();

        // Act
        verifier.Evaluate(ast);

        // Assert
        verifier.TestsPassed.Should().Be(2);
    }

    [Fact]
    public void Test_WithAssertNear_ShouldWorkForFloats()
    {
        // Arrange — test floating point math with tolerance
        const string program = @"
            (do
              (defun avg ((a : Num) (b : Num) (c : Num)) : Num
                (return (/ (+ a (+ b c)) 3.0)))
              (test avg
                (assert-near (avg 1 2 3) 2.0 0.0001)
                (assert-near (avg 10 20 30) 20.0 0.0001)))";
        var ast = Compile(program);
        var verifier = new Verifier();

        // Act
        verifier.Evaluate(ast);

        // Assert
        verifier.TestsPassed.Should().Be(1);
    }

    [Fact]
    public void Require_InsideDefun_ShouldGuardCalls()
    {
        // Arrange — require rejects negative input
        const string program = @"
            (do
              (defun safe-sqrt ((n : Num)) : Num
                (do
                  (require (>= n 0))
                  (return (math.sqrt n))))
              (safe-sqrt -1))";
        var ast = Compile(program);
        var verifier = new Verifier();

        // Act
        var act = () => verifier.Evaluate(ast);

        // Assert
        act.Should().Throw<ContractViolationException>()
            .WithMessage("*require*precondition*");
    }

    [Fact]
    public void Require_InsideDefun_WithValidInput_ShouldPass()
    {
        // Arrange
        const string program = @"
            (do
              (defun safe-sqrt ((n : Num)) : Num
                (do
                  (require (>= n 0))
                  (return (math.sqrt n))))
              (sys.stdout.write (str.from_num (safe-sqrt 9))))";
        var ast = Compile(program);
        var verifier = new Verifier();

        // Act
        verifier.Evaluate(ast);

        // Assert
        verifier.CapturedOutput.Should().Be("3");
    }

    [Fact]
    public void Transpiler_TestBlock_ShouldNotEmitToOutput()
    {
        // Arrange
        const string program = @"
            (do
              (defun add ((a : Num) (b : Num)) : Num (return (+ a b)))
              (test add
                (assert-eq (add 1 2) 3)))";
        var ast = Compile(program);

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert — test block should be stripped from C# output
        csharp.Should().NotContain("assert");
        csharp.Should().NotContain("test");
        csharp.Should().Contain("double add(double a, double b)");
    }

    [Fact]
    public void Transpiler_Require_ShouldEmitRuntimeGuard()
    {
        // Arrange
        const string program = @"
            (do
              (defun safe-div ((a : Num) (b : Num)) : Num
                (do
                  (require (> b 0))
                  (return (/ a b)))))";
        var ast = Compile(program);

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert
        csharp.Should().Contain("Contract violation");
        csharp.Should().Contain("throw new Exception");
    }

    [Fact]
    public void Transpiler_Ensure_ShouldEmitRuntimeGuard()
    {
        // Arrange
        const string program = @"
            (do
              (def result 10)
              (ensure (> result 0)))";
        var ast = Compile(program);

        // Act
        string csharp = new Transpiler().Transpile(ast);

        // Assert
        csharp.Should().Contain("Contract violation");
    }

    private static AstNode Compile(string source) =>
        new Parser(new Lexer(source).Tokenize()).Parse();
}
