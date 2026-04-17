using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agentic.Core.Agent;
using Agentic.Core.Execution;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Agentic.Core.Tests.Agent;

public sealed class OrchestratorTests
{
    [Fact]
    public async Task CompileAsync_WhenFirstAttemptPasses_ShouldReturnSuccessWithoutRetry()
    {
        // Arrange
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("(sys.stdout.write 42)"));
        var profile = BuildProfile(
            new ConstraintTest(Array.Empty<string>(), "42"));

        // Act
        var result = await new Orchestrator(agent).CompileAsync(profile);

        // Assert
        result.Success.Should().BeTrue();
        result.AttemptsUsed.Should().Be(1);
        result.Ast.Should().NotBeNull();
        await agent.Received(1).GenerateCodeAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompileAsync_WhenFirstAttemptFails_ShouldRetryWithStructuredFeedback()
    {
        // Arrange
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult("(sys.stdout.write 0)"),
                Task.FromResult("(sys.stdout.write 42)"));
        var profile = BuildProfile(
            new ConstraintTest(Array.Empty<string>(), "42"));

        // Act
        var result = await new Orchestrator(agent).CompileAsync(profile);

        // Assert — retry fed structured feedback into previousError
        result.Success.Should().BeTrue();
        result.AttemptsUsed.Should().Be(2);
        await agent.Received(1).GenerateCodeAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<string?>(code => code == null),
            Arg.Is<string?>(err => err == null),
            Arg.Any<CancellationToken>());
        await agent.Received(1).GenerateCodeAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<string?>(code => code == "(sys.stdout.write 0)"),
            Arg.Is<string?>(err => err != null && err.Contains("expected='42'") && err.Contains("actual='0'")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompileAsync_WhenAllAttemptsFail_ShouldReturnFailureWithLastFeedback()
    {
        // Arrange
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("(sys.stdout.write 0)"));
        var profile = BuildProfile(
            new ConstraintTest(Array.Empty<string>(), "42"));

        // Act
        var result = await new Orchestrator(agent, maxAttempts: 2).CompileAsync(profile);

        // Assert
        result.Success.Should().BeFalse();
        result.AttemptsUsed.Should().Be(2);
        result.LastFeedback.Should().NotBeNull();
        result.LastFeedback!.AllPassed.Should().BeFalse();
        result.LastGeneratedCode.Should().Be("(sys.stdout.write 0)");
    }

    [Fact]
    public async Task CompileAsync_WhenParseFails_ShouldFeedParseErrorIntoNextAttempt()
    {
        // Arrange
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult("(sys.stdout.write 42"),
                Task.FromResult("(sys.stdout.write 42)"));
        var profile = BuildProfile(
            new ConstraintTest(Array.Empty<string>(), "42"));

        // Act
        var result = await new Orchestrator(agent).CompileAsync(profile);

        // Assert
        result.Success.Should().BeTrue();
        await agent.Received(1).GenerateCodeAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<string?>(code => code == "(sys.stdout.write 42"),
            Arg.Is<string?>(err => err != null && err.Contains("Parse error")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompileAsync_WhenVerifierCrashes_ShouldSurfaceCrashTypeInFeedback()
    {
        // Arrange — an undefined identifier crashes the Verifier at runtime.
        var agent = Substitute.For<IAgentClient>();
        agent.GenerateCodeAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult("(sys.stdout.write (nope.what 1))"),
                Task.FromResult("(sys.stdout.write 42)"));
        var profile = BuildProfile(
            new ConstraintTest(Array.Empty<string>(), "42"));

        // Act
        var result = await new Orchestrator(agent).CompileAsync(profile);

        // Assert — second call should see "CRASHED" somewhere in the feedback.
        result.Success.Should().BeTrue();
        await agent.Received(1).GenerateCodeAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Is<string?>(err => err != null && err.Contains("CRASHED")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Constructor_WhenMaxAttemptsBelowOne_ShouldThrow()
    {
        // Arrange
        var agent = Substitute.For<IAgentClient>();

        // Act
        Action act = () => new Orchestrator(agent, maxAttempts: 0);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static ConstraintProfile BuildProfile(params ConstraintTest[] tests) =>
        new(
            Version: "0.5",
            Name: "Test",
            Objective: "write the number",
            Permissions: new List<string> { "sys.stdout.write" },
            Tests: tests);
}
