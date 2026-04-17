using System.Threading;
using System.Threading.Tasks;
using Agentic.Core.Agent;
using Agentic.Core.Execution;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Agentic.Core.Tests.Agent;

public sealed class CriticTests
{
    [Fact]
    public async Task DiagnoseAsync_WhenLlmEmitsRecomposeVerdict_ShouldReturnRecomposeDecision()
    {
        // Arrange
        var agent = ArrangeAgent("(verdict recompose \"main body uses helpers wrong\")");

        // Act
        var decision = await new Critic(agent).DiagnoseAsync(
            BuildProfile(), Array.Empty<ImplementedFunction>(),
            "(do ...)", FailingFeedback(), CancellationToken.None);

        // Assert
        decision.Verdict.Should().Be(CriticVerdict.Recompose);
        decision.HelperName.Should().BeNull();
        decision.Rationale.Should().Contain("main body");
    }

    [Fact]
    public async Task DiagnoseAsync_WhenLlmChoosesReimplement_ShouldTargetNamedHelper()
    {
        // Arrange
        var agent = ArrangeAgent("(verdict reimplement square \"always returns 0 for n=3\")");
        var helpers = new[]
        {
            BuildHelper("square"),
            BuildHelper("cube"),
        };

        // Act
        var decision = await new Critic(agent).DiagnoseAsync(
            BuildProfile(), helpers, "(do ...)", FailingFeedback(), CancellationToken.None);

        // Assert
        decision.Verdict.Should().Be(CriticVerdict.ReimplementHelper);
        decision.HelperName.Should().Be("square");
    }

    [Fact]
    public async Task DiagnoseAsync_WhenCriticPicksUnknownHelper_ShouldFallBackToRecompose()
    {
        // Arrange — unknown helper name is a parse failure; graceful default.
        var agent = ArrangeAgent("(verdict reimplement nonexistent \"reasons\")");

        // Act
        var decision = await new Critic(agent).DiagnoseAsync(
            BuildProfile(), new[] { BuildHelper("square") },
            "(do ...)", FailingFeedback(), CancellationToken.None);

        // Assert
        decision.Verdict.Should().Be(CriticVerdict.Recompose);
        decision.Rationale.Should().Contain("unparseable");
    }

    [Fact]
    public async Task DiagnoseAsync_WhenLlmOutputIsGarbage_ShouldDefaultToRecompose()
    {
        // Arrange
        var agent = ArrangeAgent("totally not an s-expression");

        // Act
        var decision = await new Critic(agent).DiagnoseAsync(
            BuildProfile(), Array.Empty<ImplementedFunction>(),
            null, FailingFeedback(), CancellationToken.None);

        // Assert
        decision.Verdict.Should().Be(CriticVerdict.Recompose);
    }

    [Fact]
    public async Task DiagnoseAsync_WhenAgentThrows_ShouldDefaultToRecompose()
    {
        // Arrange
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new System.Net.Http.HttpRequestException("boom"));

        // Act
        var decision = await new Critic(agent).DiagnoseAsync(
            BuildProfile(), Array.Empty<ImplementedFunction>(),
            null, FailingFeedback(), CancellationToken.None);

        // Assert — failure to reach the critic must not crash the pipeline.
        decision.Verdict.Should().Be(CriticVerdict.Recompose);
        decision.Rationale.Should().Contain("failed");
    }

    private static IAgentClient ArrangeAgent(string response)
    {
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));
        return agent;
    }

    private static ImplementedFunction BuildHelper(string name) =>
        new(
            new FunctionSpec(name, new[] { "n" }, "", Array.Empty<FunctionTest>()),
            $"(defun {name} (n) (return n))",
            AttemptsUsed: 1);

    private static FeedbackEnvelope FailingFeedback() =>
        new(new[]
        {
            new TestOutcome(new[] { "3" }, "9", "0", TestStatus.Failed),
        });

    private static ConstraintProfile BuildProfile() =>
        new("0.5", "Test", "obj", new[] { "sys.stdout.write" }, Array.Empty<ConstraintTest>());
}
