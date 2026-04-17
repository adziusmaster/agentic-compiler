using Agentic.Core.Agent;
using Agentic.Core.Execution;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Agentic.Core.Tests.Agent;

public sealed class PipelineOrchestratorTests
{
    [Fact]
    public async Task CompileAsync_WhenNoFunctionsDeclared_ShouldDelegateToSingleShotOrchestrator()
    {
        // Arrange
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("(sys.stdout.write 42)"));
        var profile = new ConstraintProfile(
            "0.5", "Demo", "write 42",
            new[] { "sys.stdout.write" },
            new[] { new ConstraintTest(Array.Empty<string>(), "42") });

        // Act
        var result = await new PipelineOrchestrator(agent).CompileAsync(profile);

        // Assert
        result.Success.Should().BeTrue();
        result.Stage.Should().Be("single-shot");
        result.VerifiedFunctions.Should().BeEmpty();
    }

    [Fact]
    public async Task CompileAsync_WhenFunctionsDeclared_ShouldImplementEachThenCompose()
    {
        // Arrange
        var agent = Substitute.For<IAgentClient>();
        var call = 0;
        agent.GenerateCodeAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(call++ switch
            {
                0 => "(defun square (n) (return (* n n)))",
                1 => "(do (sys.stdout.write (square 5)))",
                _ => throw new InvalidOperationException("unexpected call")
            }));

        var spec = new FunctionSpec(
            "square", new[] { "n" }, "n*n",
            new[] { new FunctionTest(new[] { "3" }, "9") });
        var profile = new ConstraintProfile(
            "0.5", "Demo", "print square of 5",
            new[] { "sys.stdout.write" },
            new[] { new ConstraintTest(Array.Empty<string>(), "25") },
            new[] { spec });

        // Act
        var result = await new PipelineOrchestrator(agent).CompileAsync(profile);

        // Assert
        result.Success.Should().BeTrue();
        result.Stage.Should().Be("composed");
        result.VerifiedFunctions.Should().ContainSingle()
            .Which.Spec.Name.Should().Be("square");
        result.FinalSource.Should().Contain("(defun square");
        result.FinalSource.Should().Contain("(square 5)");
    }

    [Fact]
    public async Task CompileAsync_WhenImplementerFailsHelper_ShouldReturnFailureAtImplementerStage()
    {
        // Arrange
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("(defun square (n) (return 0))"));

        var spec = new FunctionSpec(
            "square", new[] { "n" }, "n*n",
            new[] { new FunctionTest(new[] { "3" }, "9") });
        var profile = new ConstraintProfile(
            "0.5", "Demo", "print square of 5",
            new[] { "sys.stdout.write" },
            new[] { new ConstraintTest(Array.Empty<string>(), "25") },
            new[] { spec });

        // Act
        var result = await new PipelineOrchestrator(agent, maxAttempts: 2).CompileAsync(profile);

        // Assert
        result.Success.Should().BeFalse();
        result.Stage.Should().Be("implementer:square");
        result.VerifiedFunctions.Should().BeEmpty();
    }

    [Fact]
    public async Task CompileAsync_WhenPipelineFlagSetWithoutFunctions_ShouldRunPlannerThenImplementerThenComposer()
    {
        // Arrange — Planner emits one helper, Implementer verifies, Composer succeeds.
        var agent = Substitute.For<IAgentClient>();
        var call = 0;
        agent.GenerateCodeAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(call++ switch
            {
                0 => "(plan (fn square (n) \"n*n\" (test (5) 25)))",
                1 => "(defun square (n) (return (* n n)))",
                2 => "(do (sys.stdout.write (square 5)))",
                _ => throw new InvalidOperationException("unexpected call")
            }));
        var profile = new ConstraintProfile(
            "0.5", "Demo", "print 25",
            new[] { "sys.stdout.write" },
            new[] { new ConstraintTest(Array.Empty<string>(), "25") },
            Functions: null,
            Pipeline: true);

        // Act
        var result = await new PipelineOrchestrator(agent).CompileAsync(profile);

        // Assert
        result.Success.Should().BeTrue();
        result.Stage.Should().Be("composed");
        result.VerifiedFunctions.Should().ContainSingle()
            .Which.Spec.Name.Should().Be("square");
    }

    [Fact]
    public async Task CompileAsync_WhenComposerFailsAndCriticSaysRecompose_ShouldRetryComposerAndSucceed()
    {
        // Arrange
        // 0: Implementer → valid helper
        // 1: Composer (1st) → wrong main body (prints 0)
        // 2: Composer retry within Composer's own maxAttempts → still wrong
        // 3: Critic → recompose verdict
        // 4: Recompose attempt 1 → wrong again (Composer internal retries will try 3 times)
        // This gets complicated — simplify by using maxAttempts=1 so each stage has exactly 1 shot.
        var agent = Substitute.For<IAgentClient>();
        var call = 0;
        agent.GenerateCodeAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(call++ switch
            {
                0 => "(defun square (n) (return (* n n)))",                   // Implementer
                1 => "(do (sys.stdout.write (square 2)))",                    // Composer #1 → prints "4" ≠ "25"
                2 => "(verdict recompose \"wrong input to square\")",        // Critic
                3 => "(do (sys.stdout.write (square 5)))",                    // Recompose retry → prints "25"
                _ => throw new InvalidOperationException($"unexpected call {call - 1}")
            }));
        var spec = new FunctionSpec(
            "square", new[] { "n" }, "", new[] { new FunctionTest(new[] { "3" }, "9") });
        var profile = new ConstraintProfile(
            "0.5", "Demo", "print 25",
            new[] { "sys.stdout.write" },
            new[] { new ConstraintTest(Array.Empty<string>(), "25") },
            new[] { spec });

        // Act
        var result = await new PipelineOrchestrator(agent, maxAttempts: 1).CompileAsync(profile);

        // Assert
        result.Success.Should().BeTrue();
        result.Stage.Should().Be("composed-after-recompose");
    }

    [Fact]
    public async Task CompileAsync_WhenCriticSaysReimplement_ShouldRegenerateHelperAndRecompose()
    {
        // Arrange — Critic targets 'buggy'; the re-implemented version passes, then compose succeeds.
        var agent = Substitute.For<IAgentClient>();
        var call = 0;
        agent.GenerateCodeAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(call++ switch
            {
                // Implementer first pass — triple(n)=2n+5 gives triple(3)=11 (passes micro-test) and triple(5)=15 (fails full test).
                0 => "(defun triple (n) (return (+ (* n 2) 5)))",
                1 => "(do (sys.stdout.write (triple 5)))",  // Composer — prints "15" ≠ expected "17"
                2 => "(verdict reimplement triple \"wrong coefficient\")",
                3 => "(defun triple (n) (return (+ (* n 3) 2)))",  // New helper — triple(3)=11 ✓ micro-test, triple(5)=17 ✓ full test
                4 => "(do (sys.stdout.write (triple 5)))",  // Recompose — prints "17" ✓
                _ => throw new InvalidOperationException($"unexpected call {call - 1}")
            }));
        var spec = new FunctionSpec(
            "triple", new[] { "n" }, "", new[] { new FunctionTest(new[] { "3" }, "11") });
        var profile = new ConstraintProfile(
            "0.5", "Demo", "triple something",
            new[] { "sys.stdout.write" },
            new[] { new ConstraintTest(Array.Empty<string>(), "17") },
            new[] { spec });

        // Act
        var result = await new PipelineOrchestrator(agent, maxAttempts: 1).CompileAsync(profile);

        // Assert
        result.Success.Should().BeTrue();
        result.Stage.Should().Be("composed-after-reimplement:triple");
        result.VerifiedFunctions.Should().ContainSingle()
            .Which.Source.Should().Contain("(+ (* n 3) 2)");
    }

    [Fact]
    public async Task CompileAsync_WhenPlannerFlagAbsentAndNoFunctions_ShouldFallBackToSingleShot()
    {
        // Arrange — no functions, no pipeline: true → legacy single-shot path preserved.
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("(sys.stdout.write 42)"));
        var profile = new ConstraintProfile(
            "0.5", "Demo", "write 42",
            new[] { "sys.stdout.write" },
            new[] { new ConstraintTest(Array.Empty<string>(), "42") });

        // Act
        var result = await new PipelineOrchestrator(agent).CompileAsync(profile);

        // Assert
        result.Success.Should().BeTrue();
        result.Stage.Should().Be("single-shot");
    }

    [Fact]
    public async Task CompileAsync_WhenComposerOutputRedeclaresHelper_ShouldDropDuplicateAndKeepVerifiedHelper()
    {
        // Arrange — LLM re-emits (defun square ...) with buggy body; splicing drops it.
        var agent = Substitute.For<IAgentClient>();
        var call = 0;
        agent.GenerateCodeAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(call++ switch
            {
                0 => "(defun square (n) (return (* n n)))",
                1 => "(do (defun square (n) (return 0)) (sys.stdout.write (square 5)))",
                _ => throw new InvalidOperationException("unexpected call")
            }));

        var spec = new FunctionSpec(
            "square", new[] { "n" }, "",
            new[] { new FunctionTest(new[] { "3" }, "9") });
        var profile = new ConstraintProfile(
            "0.5", "Demo", "print square of 5",
            new[] { "sys.stdout.write" },
            new[] { new ConstraintTest(Array.Empty<string>(), "25") },
            new[] { spec });

        // Act
        var result = await new PipelineOrchestrator(agent).CompileAsync(profile);

        // Assert
        result.Success.Should().BeTrue();
        result.FinalSource.Should().Contain("(defun square (n) (return (* n n)))");
        // The buggy (return 0) body must NOT survive splicing.
        result.FinalSource.Should().NotContain("(return 0)");
    }
}
