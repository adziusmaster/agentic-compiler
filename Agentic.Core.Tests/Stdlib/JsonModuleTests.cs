using Agentic.Core.Execution;
using Agentic.Core.Stdlib;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Stdlib;

public sealed class JsonModuleTests
{
    [Fact]
    public void JsonGet_ShouldExtractStringValue()
    {
        // Arrange
        const string source = "(do\n" +
            "  (def data \"{\\\"name\\\":\\\"Alice\\\"}\")\n" +
            "  (def name (json.get data \"name\"))\n" +
            "  (test json-get (assert-eq name \"Alice\")))";

        var compiler = new Compiler(emitBinary: false);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void JsonGetNum_ShouldExtractNumericValue()
    {
        // Arrange
        const string source = "(do\n" +
            "  (def data \"{\\\"age\\\":30}\")\n" +
            "  (def age (json.get_num data \"age\"))\n" +
            "  (test json-num (assert-eq age 30)))";

        var compiler = new Compiler(emitBinary: false);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void JsonObject_ShouldBuildJsonString()
    {
        // Arrange
        const string source = "(do\n" +
            "  (def obj (json.object \"name\" \"Bob\"))\n" +
            "  (def extracted (json.get obj \"name\"))\n" +
            "  (test json-roundtrip (assert-eq extracted \"Bob\")))";

        var compiler = new Compiler(emitBinary: false);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void JsonArrayLength_ShouldCountElements()
    {
        // Arrange
        const string source = "(do\n" +
            "  (def arr \"[1,2,3]\")\n" +
            "  (def len (json.array_length arr))\n" +
            "  (test json-array (assert-eq len 3)))";

        var compiler = new Compiler(emitBinary: false);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void JsonGet_MissingKey_ShouldReturnEmptyString()
    {
        // Missing keys return "" by design — safe default, consistent with the
        // transpiler's TryGetProperty emission path. See LanguageSpec "JSON" section.
        const string source = "(do\n" +
            "  (def data \"{\\\"a\\\":1}\")\n" +
            "  (def missing (json.get data \"missing\"))\n" +
            "  (test json-missing (assert-eq missing \"\")))";

        var compiler = new Compiler(emitBinary: false);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void JsonModule_NoPermissionsNeeded()
    {
        // JSON is pure in-memory — no permissions required
        const string source = "(do\n" +
            "  (def data \"{\\\"x\\\":42}\")\n" +
            "  (test json-no-perm (assert-eq (json.get_num data \"x\") 42)))";

        var compiler = new Compiler(emitBinary: false, permissions: Permissions.None);
        var result = compiler.Compile(source);

        result.Success.Should().BeTrue();
    }
}
