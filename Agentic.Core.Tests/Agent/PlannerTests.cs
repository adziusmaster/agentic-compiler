using System.Threading;
using System.Threading.Tasks;
using Agentic.Core.Agent;
using Agentic.Core.Execution;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Agentic.Core.Tests.Agent;

public sealed class PlannerTests
{
    [Fact]
    public async Task PlanAsync_WhenLlmEmitsValidPlan_ShouldReturnSpecsWithParametersIntentAndTests()
    {
        // Arrange
        string plan =
            "(plan " +
            "  (fn square (n) \"Return n*n\" (test (3) 9) (test (5) 25)) " +
            "  (fn cube (n) \"Return n*n*n\" (test (2) 8)))";
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(plan));

        // Act
        var outcome = await new Planner(agent).PlanAsync(BuildProfile(), CancellationToken.None);

        // Assert
        outcome.Success.Should().BeTrue();
        outcome.Functions.Should().HaveCount(2);

        var square = outcome.Functions[0];
        square.Name.Should().Be("square");
        square.Parameters.Should().ContainSingle().Which.Should().Be("n");
        square.Intent.Should().Be("Return n*n");
        square.Tests.Should().HaveCount(2);
        square.Tests[0].Inputs.Should().ContainSingle().Which.Should().Be("3");
        square.Tests[0].Expect.Should().Be("9");
        square.Tests[1].Expect.Should().Be("25");

        outcome.Functions[1].Name.Should().Be("cube");
    }

    [Fact]
    public async Task PlanAsync_WhenLlmOutputMalformed_ShouldRetryWithParseErrorFeedback()
    {
        // Arrange
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult("(not-a-plan)"),
                Task.FromResult("(plan (fn square (n) \"n*n\" (test (3) 9)))"));

        // Act
        var outcome = await new Planner(agent).PlanAsync(BuildProfile(), CancellationToken.None);

        // Assert
        outcome.Success.Should().BeTrue();
        outcome.AttemptsUsed.Should().Be(2);
        await agent.Received(1).GenerateCodeAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<string?>(code => code != null && code.Contains("not-a-plan")),
            Arg.Is<string?>(err => err != null && err.Contains("Parse error")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlanAsync_WhenPlanHasNoFunctions_ShouldRetryWithCountFeedback()
    {
        // Arrange
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("(plan)"));

        // Act
        var outcome = await new Planner(agent, maxAttempts: 2).PlanAsync(BuildProfile(), CancellationToken.None);

        // Assert
        outcome.Success.Should().BeFalse();
        outcome.LastFeedback!.ParseError.Should().Contain("zero helper functions");
    }

    [Fact]
    public void ParsePlan_WhenIntentOmitted_ShouldDefaultToEmptyString()
    {
        // Arrange — intent is optional; parser treats element #3 as a test if it's not a string.
        string raw = "(plan (fn double (n) (test (2) 4)))";

        // Act
        var specs = Planner.ParsePlan(raw);

        // Assert
        specs.Should().ContainSingle();
        specs[0].Intent.Should().BeEmpty();
        specs[0].Tests.Should().ContainSingle().Which.Expect.Should().Be("4");
    }

    [Fact]
    public void ParsePlan_WhenTestShapeWrong_ShouldThrowWithHelperNameInMessage()
    {
        // Arrange — test inputs must be wrapped in a list; this one passes a bare atom.
        string raw = "(plan (fn square (n) \"intent\" (test 3 9)))";

        // Act
        Action act = () => Planner.ParsePlan(raw);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*square*test inputs must be a list*");
    }

    private static ConstraintProfile BuildProfile() =>
        new("0.5", "Test", "compute a^2 + b^2",
            new[] { "sys.stdout.write" },
            new[] { new ConstraintTest(new[] { "3", "4" }, "25") });
}
