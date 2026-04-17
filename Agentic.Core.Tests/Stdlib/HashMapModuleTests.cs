using Agentic.Core.Execution;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Stdlib;

public sealed class HashMapModuleTests
{
    private static CompileResult Check(string source) =>
        new Compiler(emitBinary: false).Compile(source);

    [Fact]
    public void MapNew_ShouldCreateEmptyMap()
    {
        var r = Check(@"(module T
            (test t (do
                (def m (map.new))
                (assert-eq (map.size m) 0))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void MapSetAndGet_ShouldStoreAndRetrieve()
    {
        var r = Check(@"(module T
            (test t (do
                (def m (map.new))
                (map.set m ""name"" ""Alice"")
                (assert-eq (map.get m ""name"") ""Alice""))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void MapSet_NumericValue()
    {
        var r = Check(@"(module T
            (test t (do
                (def m (map.new))
                (map.set m ""age"" 30)
                (assert-eq (map.get m ""age"") 30))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void MapGet_MissingKey_ShouldReturnZero()
    {
        var r = Check(@"(module T
            (test t (do
                (def m (map.new))
                (assert-eq (map.get m ""missing"") 0))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void MapHas_ShouldDetectExistence()
    {
        var r = Check(@"(module T
            (test t (do
                (def m (map.new))
                (map.set m ""key"" ""val"")
                (assert-eq (map.has m ""key"") 1)
                (assert-eq (map.has m ""nope"") 0))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void MapRemove_ShouldDeleteKey()
    {
        var r = Check(@"(module T
            (test t (do
                (def m (map.new))
                (map.set m ""x"" 42)
                (assert-eq (map.has m ""x"") 1)
                (map.remove m ""x"")
                (assert-eq (map.has m ""x"") 0))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void MapSize_ShouldTrackCount()
    {
        var r = Check(@"(module T
            (test t (do
                (def m (map.new))
                (map.set m ""a"" 1)
                (map.set m ""b"" 2)
                (map.set m ""c"" 3)
                (assert-eq (map.size m) 3)
                (map.remove m ""b"")
                (assert-eq (map.size m) 2))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void MapKeys_ShouldReturnStringArray()
    {
        var r = Check(@"(module T
            (test t (do
                (def m (map.new))
                (map.set m ""x"" 1)
                (map.set m ""y"" 2)
                (def keys : (Array Str) (map.keys m))
                (assert-eq (arr.length keys) 2))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void MapOverwrite_ShouldReplaceValue()
    {
        var r = Check(@"(module T
            (test t (do
                (def m (map.new))
                (map.set m ""key"" ""old"")
                (map.set m ""key"" ""new"")
                (assert-eq (map.get m ""key"") ""new"")
                (assert-eq (map.size m) 1))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void Map_ComplexUsage_WithFunction()
    {
        var r = Check(@"(module T
            (defun build_config () : Num
                (do
                    (def config (map.new))
                    (map.set config ""host"" ""localhost"")
                    (map.set config ""port"" 8080)
                    (return (map.size config))))
            (test t (assert-eq (build_config) 2)))");
        r.Success.Should().BeTrue();
    }
}
