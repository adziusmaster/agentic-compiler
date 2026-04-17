using Agentic.Core.Execution;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Stdlib;

public sealed class StringModuleTests
{
    private static CompileResult Check(string source) =>
        new Compiler(emitBinary: false).Compile(source);

    [Fact]
    public void Contains_ShouldReturnOneWhenFound()
    {
        var r = Check(@"(module T
            (test t (assert-eq (str.contains ""hello world"" ""world"") 1)))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void Contains_ShouldReturnZeroWhenNotFound()
    {
        var r = Check(@"(module T
            (test t (assert-eq (str.contains ""hello"" ""xyz"") 0)))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void IndexOf_ShouldReturnPosition()
    {
        var r = Check(@"(module T
            (test t (do
                (assert-eq (str.index_of ""abcdef"" ""cd"") 2)
                (assert-eq (str.index_of ""hello"" ""xyz"") -1))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void Substring_ShouldExtractPortion()
    {
        var r = Check(@"(module T
            (test t (assert-eq (str.substring ""hello world"" 6 5) ""world"")))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void Trim_ShouldRemoveWhitespace()
    {
        var r = Check(@"(module T
            (test t (assert-eq (str.trim ""  hello  "") ""hello"")))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void Upper_ShouldUppercase()
    {
        var r = Check(@"(module T
            (test t (assert-eq (str.upper ""hello"") ""HELLO"")))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void Lower_ShouldLowercase()
    {
        var r = Check(@"(module T
            (test t (assert-eq (str.lower ""HELLO"") ""hello"")))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void Replace_ShouldSubstitute()
    {
        var r = Check(@"(module T
            (test t (assert-eq (str.replace ""hello world"" ""world"" ""there"") ""hello there"")))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void Split_ShouldProduceArray()
    {
        var r = Check(@"(module T
            (test t (do
                (def parts : (Array Str) (str.split ""a,b,c"" "",""))
                (assert-eq (arr.get parts 0) ""a"")
                (assert-eq (arr.get parts 1) ""b"")
                (assert-eq (arr.get parts 2) ""c""))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void Join_ShouldCombineArray()
    {
        var r = Check(@"(module T
            (test t (do
                (def parts : (Array Str) (str.split ""hello world"" "" ""))
                (assert-eq (str.join parts ""-"") ""hello-world""))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void Chained_StringOperations()
    {
        var r = Check(@"(module T
            (test t (do
                (def s : Str ""  Hello, World!  "")
                (def trimmed : Str (str.trim s))
                (def lower : Str (str.lower trimmed))
                (def replaced : Str (str.replace lower ""world"" ""agentic""))
                (assert-eq replaced ""hello, agentic!""))))");
        r.Success.Should().BeTrue();
    }
}
