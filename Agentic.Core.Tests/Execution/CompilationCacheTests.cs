using Agentic.Core.Execution;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Execution;

public sealed class CompilationCacheTests : IDisposable
{
    private readonly string _tempDir;

    public CompilationCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AgenticCacheTests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    #region Hash

    [Fact]
    public void ComputeHash_SameInput_ReturnsSameHash()
    {
        // Arrange / Act
        var h1 = CompilationCache.ComputeHash("(do (def x 1))");
        var h2 = CompilationCache.ComputeHash("(do (def x 1))");

        // Assert
        h1.Should().Be(h2);
    }

    [Fact]
    public void ComputeHash_DifferentInput_ReturnsDifferentHash()
    {
        // Arrange / Act
        var h1 = CompilationCache.ComputeHash("(do (def x 1))");
        var h2 = CompilationCache.ComputeHash("(do (def x 2))");

        // Assert
        h1.Should().NotBe(h2);
    }

    [Fact]
    public void ComputeHash_ReturnsHexString()
    {
        // Act
        var hash = CompilationCache.ComputeHash("test");

        // Assert
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    #endregion

    #region In-Memory Cache

    [Fact]
    public void Store_SuccessfulResult_CanBeRetrieved()
    {
        // Arrange
        var cache = new CompilationCache();
        var result = new CompileResult { Success = true, TestsPassed = 2, TestsFailed = 0 };
        var hash = CompilationCache.ComputeHash("(do (def x 1))");

        // Act
        cache.Store(hash, result);
        var found = cache.TryGet(hash, out var cached);

        // Assert
        found.Should().BeTrue();
        cached!.Success.Should().BeTrue();
        cached.TestsPassed.Should().Be(2);
    }

    [Fact]
    public void Store_FailedResult_ShouldNotBeCached()
    {
        // Arrange
        var cache = new CompilationCache();
        var result = new CompileResult
        {
            Success = false,
            Diagnostics = new[]
            {
                new CompileDiagnostic { Severity = DiagnosticSeverity.Error, Type = "test-failure", Message = "bad" }
            }
        };

        // Act
        cache.Store("hash123", result);

        // Assert
        cache.TryGet("hash123", out _).Should().BeFalse();
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void TryGet_MissingKey_ReturnsFalse()
    {
        // Arrange
        var cache = new CompilationCache();

        // Act / Assert
        cache.TryGet("nonexistent", out _).Should().BeFalse();
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        // Arrange
        var cache = new CompilationCache();
        cache.Store("h1", new CompileResult { Success = true });
        cache.Store("h2", new CompileResult { Success = true });

        // Act
        cache.Clear();

        // Assert
        cache.Count.Should().Be(0);
    }

    #endregion

    #region File Persistence

    [Fact]
    public void Flush_PersistsToDisk()
    {
        // Arrange
        var cache = new CompilationCache(_tempDir);
        var hash = CompilationCache.ComputeHash("(do (def x 1))");
        cache.Store(hash, new CompileResult { Success = true, TestsPassed = 1 });

        // Act
        cache.Flush();

        // Assert
        Directory.GetFiles(_tempDir, "*.json").Should().HaveCount(1);
    }

    [Fact]
    public void LoadFromDisk_RestoresEntries()
    {
        // Arrange — write cache, then create a new instance from same dir
        var hash = CompilationCache.ComputeHash("(do (def x 42))");
        var cache1 = new CompilationCache(_tempDir);
        cache1.Store(hash, new CompileResult { Success = true, TestsPassed = 3 });
        cache1.Flush();

        // Act
        var cache2 = new CompilationCache(_tempDir);

        // Assert
        cache2.TryGet(hash, out var restored).Should().BeTrue();
        restored!.TestsPassed.Should().Be(3);
    }

    [Fact]
    public void Clear_RemovesDiskFiles()
    {
        // Arrange
        var cache = new CompilationCache(_tempDir);
        cache.Store("h1", new CompileResult { Success = true });
        cache.Flush();

        // Act
        cache.Clear();

        // Assert
        Directory.GetFiles(_tempDir, "*.json").Should().BeEmpty();
    }

    #endregion

    #region Compiler Integration

    [Fact]
    public void Compiler_WithCache_ReturnsCachedResult()
    {
        // Arrange
        var cache = new CompilationCache();
        var compiler = new Compiler(emitBinary: false, cache: cache);
        const string source = "(do (defun f ((x : Num)) : Num (return (+ x 1))) (test f (assert-eq (f 1) 2)))";

        // Act — compile twice
        var result1 = compiler.Compile(source);
        var result2 = compiler.Compile(source);

        // Assert
        result1.Success.Should().BeTrue();
        result2.Success.Should().BeTrue();
        cache.Count.Should().Be(1);
    }

    [Fact]
    public void Compiler_WithCache_DifferentSource_CompilesSeparately()
    {
        // Arrange
        var cache = new CompilationCache();
        var compiler = new Compiler(emitBinary: false, cache: cache);

        // Act
        compiler.Compile("(do (def x 1))");
        compiler.Compile("(do (def x 2))");

        // Assert
        cache.Count.Should().Be(2);
    }

    [Fact]
    public void Compiler_WithCache_FailedResult_NotCached()
    {
        // Arrange
        var cache = new CompilationCache();
        var compiler = new Compiler(emitBinary: false, cache: cache);

        // Act
        compiler.Compile("(do (defun f () (return 0)) (test f (assert-eq (f) 999)))");

        // Assert
        cache.Count.Should().Be(0);
    }

    #endregion
}
