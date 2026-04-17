using Agentic.Core.Agent;
using Agentic.Core.Execution;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Agentic.Core.Tests.Agent;

public sealed class ImplementerTests
{
    [Fact]
    public async Task ImplementAsync_WhenFirstAttemptSatisfiesMicroTests_ShouldReturnSuccess()
    {
        // Arrange
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("(defun square (n) (return (* n n)))"));

        var spec = new FunctionSpec(
            "square", new[] { "n" }, "Return n * n",
            new[]
            {
                new FunctionTest(new[] { "3" }, "9"),
                new FunctionTest(new[] { "5" }, "25"),
            });

        // Act
        var outcome = await new Implementer(agent).ImplementAsync(spec, BuildProfile(), CancellationToken.None);

        // Assert
        outcome.Success.Should().BeTrue();
        outcome.AttemptsUsed.Should().Be(1);
        outcome.Source.Should().Contain("defun square");
    }

    [Fact]
    public async Task ImplementAsync_WhenFirstAttemptFails_ShouldRetryWithStructuredFeedback()
    {
        // Arrange
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult("(defun square (n) (return n))"),
                Task.FromResult("(defun square (n) (return (* n n)))"));

        var spec = new FunctionSpec(
            "square", new[] { "n" }, "",
            new[] { new FunctionTest(new[] { "3" }, "9") });

        // Act
        var outcome = await new Implementer(agent).ImplementAsync(spec, BuildProfile(), CancellationToken.None);

        // Assert
        outcome.Success.Should().BeTrue();
        outcome.AttemptsUsed.Should().Be(2);
        await agent.Received(1).GenerateCodeAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<string?>(code => code != null && code.Contains("return n")),
            Arg.Is<string?>(err => err != null && err.Contains("expected='9'") && err.Contains("actual='3'")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImplementAsync_WhenOutputIsWrappedInDo_ShouldExtractDefunAndVerify()
    {
        // Arrange
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("(do (defun double (n) (return (* n 2))))"));

        var spec = new FunctionSpec(
            "double", new[] { "n" }, "",
            new[] { new FunctionTest(new[] { "4" }, "8") });

        // Act
        var outcome = await new Implementer(agent).ImplementAsync(spec, BuildProfile(), CancellationToken.None);

        // Assert
        outcome.Success.Should().BeTrue();
        outcome.Source.Should().StartWith("(defun double");
    }

    [Fact]
    public async Task ImplementAsync_WhenAllAttemptsExhausted_ShouldReturnFailureWithLastFeedback()
    {
        // Arrange
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("(defun square (n) (return 0))"));

        var spec = new FunctionSpec(
            "square", new[] { "n" }, "",
            new[] { new FunctionTest(new[] { "3" }, "9") });

        // Act
        var outcome = await new Implementer(agent, maxAttempts: 2).ImplementAsync(spec, BuildProfile(), CancellationToken.None);

        // Assert
        outcome.Success.Should().BeFalse();
        outcome.AttemptsUsed.Should().Be(2);
        outcome.LastFeedback!.AllPassed.Should().BeFalse();
    }

    [Fact]
    public void ExtractDefun_WhenOutputLacksExpectedName_ShouldThrowReadableError()
    {
        // Arrange
        string raw = "(defun other (n) (return n))";

        // Act
        Action act = () => Implementer.ExtractDefun(raw, "square");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*(defun square*");
    }

    private static ConstraintProfile BuildProfile() =>
        new("0.5", "Test", "objective", new[] { "sys.stdout.write" }, Array.Empty<ConstraintTest>());
}
