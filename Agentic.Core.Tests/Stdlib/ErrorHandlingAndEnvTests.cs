using Agentic.Core.Execution;
using Agentic.Core.Stdlib;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Stdlib;

public sealed class ErrorHandlingTests
{
    private static CompileResult Check(string source) =>
        new Compiler(emitBinary: false).Compile(source);

    [Fact]
    public void Throw_ShouldRaiseError()
    {
        var r = Check(@"(module T
            (defun fail () : Num (throw ""boom""))
            (test t (do
                (fail)
                (assert-true 0))))");
        r.Success.Should().BeFalse();
    }

    [Fact]
    public void TryCatch_ShouldCatchThrownError()
    {
        var r = Check(@"(module T
            (defun risky () : Str (throw ""something broke""))
            (test t (do
                (try
                    (risky)
                    (catch err
                        (assert-eq err ""something broke""))))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void TryCatch_ShouldPassThroughOnSuccess()
    {
        var r = Check(@"(module T
            (defun safe () : Num (return 42))
            (test t (do
                (def result : Num 0)
                (try
                    (do (set result (safe)))
                    (catch err (set result -1)))
                (assert-eq result 42))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void TryCatch_ShouldCatchContractViolation()
    {
        var r = Check(@"(module T
            (defun checked ((x : Num)) : Num
                (require (> x 0))
                (return x))
            (test t (do
                (def result : Num 0)
                (try
                    (do (set result (checked -5)))
                    (catch err (set result -1)))
                (assert-eq result -1))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void TryCatch_ShouldCatchDivisionByZero()
    {
        var r = Check(@"(module T
            (defun divide ((a : Num) (b : Num)) : Num
                (require (not (= b 0)))
                (return (/ a b)))
            (test t (do
                (def result : Num 0)
                (try
                    (do (set result (divide 10 0)))
                    (catch err (set result -999)))
                (assert-eq result -999))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void Throw_WithDynamicMessage()
    {
        var r = Check(@"(module T
            (defun validate ((x : Num)) : Num
                (if (< x 0)
                    (throw (str.concat ""negative: "" (str.from_num x)))
                    (return x)))
            (test t (do
                (try
                    (validate -5)
                    (catch err
                        (assert-eq (str.contains err ""negative"") 1))))))");
        r.Success.Should().BeTrue();
    }
}

public sealed class EnvModuleTests
{
    [Fact]
    public void EnvGet_WithPermission_ShouldReturnEmptyDuringVerification()
    {
        var r = new Compiler(emitBinary: false, permissions: new Permissions { AllowEnv = true })
            .Compile(@"(module T
                (test t (assert-eq (env.get ""SOME_VAR"") """")))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void EnvGetOr_ShouldReturnDefault()
    {
        var r = new Compiler(emitBinary: false, permissions: new Permissions { AllowEnv = true })
            .Compile(@"(module T
                (test t (assert-eq (env.get_or ""MISSING"" ""fallback"") ""fallback"")))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void EnvGet_WithoutPermission_ShouldFailAtTranspile()
    {
        // env.get in executable code (not just tests) should fail without permission
        var r = new Compiler(emitBinary: false, permissions: Permissions.None)
            .Compile(@"(module T
                (defun get_config () : Str (return (env.get ""DB_URL"")))
                (test t (assert-eq 1 1)))");
        r.Success.Should().BeFalse();
        r.Diagnostics.Should().Contain(d => d.Type == "permission-denied");
    }
}
