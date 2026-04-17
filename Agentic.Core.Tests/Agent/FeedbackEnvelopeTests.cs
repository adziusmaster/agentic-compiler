using Agentic.Core.Agent;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Agent;

public sealed class FeedbackEnvelopeTests
{
    [Fact]
    public void AllPassed_WhenEveryTestPassedAndNoParseError_ShouldBeTrue()
    {
        // Arrange
        var envelope = new FeedbackEnvelope(new[]
        {
            new TestOutcome(new[] { "1" }, "1", "1", TestStatus.Passed),
            new TestOutcome(new[] { "2" }, "2", "2", TestStatus.Passed),
        });

        // Act
        bool allPassed = envelope.AllPassed;

        // Assert
        allPassed.Should().BeTrue();
    }

    [Fact]
    public void AllPassed_WhenAnyTestFailed_ShouldBeFalse()
    {
        // Arrange
        var envelope = new FeedbackEnvelope(new[]
        {
            new TestOutcome(new[] { "1" }, "1", "1", TestStatus.Passed),
            new TestOutcome(new[] { "2" }, "2", "BAD", TestStatus.Failed),
        });

        // Act
        bool allPassed = envelope.AllPassed;

        // Assert
        allPassed.Should().BeFalse();
    }

    [Fact]
    public void ToLlmFeedback_WhenMixedResults_ShouldCiteFailingTestsWithExpectedVsActual()
    {
        // Arrange
        var envelope = new FeedbackEnvelope(new[]
        {
            new TestOutcome(new[] { "3", "4" }, "7", "7", TestStatus.Passed),
            new TestOutcome(new[] { "10", "0" }, "10", "NaN", TestStatus.Failed),
        });

        // Act
        string feedback = envelope.ToLlmFeedback();

        // Assert
        feedback.Should().Contain("1/2 passed");
        feedback.Should().Contain("#2");
        feedback.Should().Contain("input=[10,0]");
        feedback.Should().Contain("expected='10'");
        feedback.Should().Contain("actual='NaN'");
        feedback.Should().Contain("#1");
    }

    [Fact]
    public void ToLlmFeedback_WhenTestCrashed_ShouldIncludeExceptionTypeAndMessage()
    {
        // Arrange
        var envelope = new FeedbackEnvelope(new[]
        {
            new TestOutcome(
                new[] { "9" },
                "expected",
                Actual: null,
                TestStatus.Crashed,
                CrashType: "InvalidOperationException",
                CrashMessage: "stack overflow: missing base case"),
        });

        // Act
        string feedback = envelope.ToLlmFeedback();

        // Assert
        feedback.Should().Contain("CRASHED");
        feedback.Should().Contain("InvalidOperationException");
        feedback.Should().Contain("missing base case");
    }

    [Fact]
    public void FromParseError_ShouldProduceEnvelopeWithParseErrorAndNoTestOutcomes()
    {
        // Arrange & Act
        var envelope = FeedbackEnvelope.FromParseError("Missing closing parenthesis");

        // Assert
        envelope.ParseError.Should().Be("Missing closing parenthesis");
        envelope.TestOutcomes.Should().BeEmpty();
        envelope.AllPassed.Should().BeFalse();
        envelope.ToLlmFeedback().Should().Contain("Parse error");
    }
}
