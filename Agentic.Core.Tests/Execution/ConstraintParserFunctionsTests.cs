using Agentic.Core.Execution;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Execution;

public sealed class ConstraintParserFunctionsTests
{
    [Fact]
    public void ParseLines_WithFunctionsSection_ShouldPopulateSpecsWithParametersAndTests()
    {
        // Arrange
        string[] lines = new[]
        {
            "version: 0.5",
            "name: Demo",
            "objective: |",
            "  Test fn parsing.",
            "permissions:",
            "  - sys.stdout.write",
            "functions:",
            "  - name: square",
            "    signature: (square n)",
            "    intent: \"Return n * n\"",
            "    tests:",
            "      - inputs: [3]",
            "        expect: \"9\"",
            "      - inputs: [5]",
            "        expect: \"25\"",
            "  - name: cube",
            "    signature: (cube n)",
            "    intent: \"Return n * n * n\"",
            "    tests:",
            "      - inputs: [2]",
            "        expect: \"8\"",
            "tests:",
            "  - input: [3]",
            "    expect_stdout: \"9\"",
        };

        // Act
        var profile = new ConstraintParser().ParseLines(lines);

        // Assert
        profile.Functions.Should().NotBeNull();
        profile.Functions!.Should().HaveCount(2);

        var square = profile.Functions![0];
        square.Name.Should().Be("square");
        square.Parameters.Should().ContainSingle().Which.Should().Be("n");
        square.Intent.Should().Be("Return n * n");
        square.Tests.Should().HaveCount(2);
        square.Tests[0].Inputs.Should().ContainSingle().Which.Should().Be("3");
        square.Tests[0].Expect.Should().Be("9");
        square.Tests[1].Expect.Should().Be("25");

        var cube = profile.Functions![1];
        cube.Name.Should().Be("cube");
        cube.Tests.Should().ContainSingle();

        profile.Tests.Should().ContainSingle();
    }

    [Fact]
    public void ParseLines_WithoutFunctionsSection_ShouldLeaveFunctionsNull()
    {
        // Arrange
        string[] lines = new[]
        {
            "version: 0.5",
            "name: Demo",
            "objective: |",
            "  Simple single-shot program.",
            "permissions:",
            "  - sys.stdout.write",
            "tests:",
            "  - input: []",
            "    expect_stdout: \"42\"",
        };

        // Act
        var profile = new ConstraintParser().ParseLines(lines);

        // Assert
        profile.Functions.Should().BeNull();
        profile.FunctionsOrEmpty.Should().BeEmpty();
    }
}
