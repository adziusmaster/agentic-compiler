using FluentAssertions;
using Xunit;
using Record = Agentic.Core.Runtime.Record;
using CanonicalFormat = Agentic.Core.Runtime.CanonicalFormat;

namespace Agentic.Core.Tests.Runtime;

public sealed class CanonicalFormatTests
{
    [Fact]
    public void Serialize_IntegerDouble_ShouldOmitTrailingZero()
    {
        // Arrange / Act / Assert
        CanonicalFormat.Serialize(42.0).Should().Be("42");
        CanonicalFormat.Serialize(-0.0).Should().Be("0");
        CanonicalFormat.Serialize(0.0).Should().Be("0");
    }

    [Fact]
    public void Serialize_FractionalDouble_ShouldUseRoundTripForm()
    {
        // Arrange / Act / Assert
        CanonicalFormat.Serialize(3.14).Should().Be("3.14");
        CanonicalFormat.Serialize(1.0 / 3.0).Should().StartWith("0.333");
    }

    [Fact]
    public void Serialize_SpecialDoubles_ShouldSpellOut()
    {
        // Arrange / Act / Assert
        CanonicalFormat.Serialize(double.NaN).Should().Be("NaN");
        CanonicalFormat.Serialize(double.PositiveInfinity).Should().Be("Inf");
        CanonicalFormat.Serialize(double.NegativeInfinity).Should().Be("-Inf");
    }

    [Fact]
    public void Serialize_String_ShouldDoubleQuoteAndEscape()
    {
        // Arrange / Act / Assert
        CanonicalFormat.Serialize("hello").Should().Be("\"hello\"");
        CanonicalFormat.Serialize("a\nb").Should().Be("\"a\\nb\"");
        CanonicalFormat.Serialize("quote\"inside").Should().Be("\"quote\\\"inside\"");
    }

    [Fact]
    public void Serialize_DoubleArray_ShouldRenderBracketedList()
    {
        // Arrange
        var arr = new double[] { 1, 2, 3 };

        // Act
        var result = CanonicalFormat.Serialize(arr);

        // Assert
        result.Should().Be("[1, 2, 3]");
    }

    [Fact]
    public void Serialize_StringArray_ShouldQuoteElements()
    {
        // Arrange
        var arr = new[] { "a", "b" };

        // Act
        var result = CanonicalFormat.Serialize(arr);

        // Assert
        result.Should().Be("[\"a\", \"b\"]");
    }

    [Fact]
    public void Serialize_Record_ShouldRenderInDeclarationOrder()
    {
        // Arrange
        var rec = new Record("Point", new Dictionary<string, object>
        {
            ["x"] = 3.0,
            ["y"] = 4.0
        });

        // Act
        var result = CanonicalFormat.Serialize(rec);

        // Assert
        result.Should().Be("Point{x: 3, y: 4}");
    }

    [Fact]
    public void Serialize_Map_ShouldSortKeysLexicographically()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            ["zebra"] = 1.0,
            ["apple"] = 2.0,
            ["mango"] = 3.0
        };

        // Act
        var result = CanonicalFormat.Serialize(dict);

        // Assert — canonical form orders keys so output is deterministic across runs.
        result.Should().Be("{\"apple\": 2, \"mango\": 3, \"zebra\": 1}");
    }

    [Fact]
    public void Serialize_NestedArrayOfRecords_ShouldComposeCleanly()
    {
        // Arrange
        var items = new object[]
        {
            new Record("Item", new Dictionary<string, object> { ["name"] = "apple", ["qty"] = 3.0 }),
            new Record("Item", new Dictionary<string, object> { ["name"] = "pear", ["qty"] = 5.0 }),
        };

        // Act
        var result = CanonicalFormat.Serialize(items);

        // Assert
        result.Should().Be("[Item{name: \"apple\", qty: 3}, Item{name: \"pear\", qty: 5}]");
    }

    [Fact]
    public void ForWrite_Primitives_ShouldMatchLegacyOutput()
    {
        // Arrange / Act / Assert — existing samples depend on these forms.
        CanonicalFormat.ForWrite(42.0).Should().Be("42");
        CanonicalFormat.ForWrite("hello").Should().Be("hello");
        CanonicalFormat.ForWrite(true).Should().Be("true");
        CanonicalFormat.ForWrite(null).Should().Be("nil");
    }

    [Fact]
    public void ForWrite_Composites_ShouldUseCanonicalForm()
    {
        // Arrange / Act / Assert — previously these printed as "System.Double[]".
        CanonicalFormat.ForWrite(new double[] { 1, 2, 3 }).Should().Be("[1, 2, 3]");
        var map = new Dictionary<string, object> { ["x"] = 1.0 };
        CanonicalFormat.ForWrite(map).Should().Be("{\"x\": 1}");
    }
}
