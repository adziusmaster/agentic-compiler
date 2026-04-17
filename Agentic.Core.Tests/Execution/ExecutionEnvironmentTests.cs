using Agentic.Core.Execution;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Execution;

public sealed class ExecutionEnvironmentTests
{
    [Fact]
    public void PushFrame_WhenDepthExceedsMax_ShouldThrowReadableDiagnostic()
    {
        // Arrange
        var env = new ExecutionEnvironment();

        // Act
        Action act = () =>
        {
            for (int i = 0; i < ExecutionEnvironment.MaxCallDepth + 1; i++)
                env.PushFrame(new Dictionary<string, object>());
        };

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*call depth exceeded*base case*");
    }

    [Fact]
    public void PushFrame_WhenDepthJustBelowMax_ShouldSucceed()
    {
        // Arrange
        var env = new ExecutionEnvironment();

        // Act
        Action act = () =>
        {
            for (int i = 0; i < ExecutionEnvironment.MaxCallDepth - 1; i++)
                env.PushFrame(new Dictionary<string, object>());
        };

        // Assert
        act.Should().NotThrow();
        env.CallDepth.Should().Be(ExecutionEnvironment.MaxCallDepth - 1);
    }

    [Fact]
    public void Set_WhenVariableNotDeclared_ShouldThrowSegfault()
    {
        // Arrange
        var env = new ExecutionEnvironment();

        // Act
        Action act = () => env.Set("missing", 42.0);

        // Assert
        act.Should().Throw<Exception>().WithMessage("*not declared*");
    }
}
