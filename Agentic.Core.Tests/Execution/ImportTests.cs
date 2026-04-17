using Agentic.Core.Execution;
using Agentic.Core.Syntax;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Execution;

/// <summary>
/// Tests for multi-file import/export functionality.
/// Uses temp files to test actual file-based module resolution.
/// </summary>
public sealed class ImportTests : IDisposable
{
    private readonly string _testDir;

    public ImportTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "AgenticImportTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    private string WriteModule(string filename, string source)
    {
        var path = Path.Combine(_testDir, filename);
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, source);
        return path;
    }

    private CompileResult CompileFile(string filename)
    {
        var path = Path.Combine(_testDir, filename);
        var source = File.ReadAllText(path);
        var compiler = new Compiler(emitBinary: false);
        return compiler.Compile(source, Path.GetFileNameWithoutExtension(filename), _testDir);
    }

    [Fact]
    public void Import_BasicExportedFunction_ShouldBeCallable()
    {
        // Arrange
        WriteModule("math_utils.ag", @"
            (module MathUtils
              (export add)
              (defun add ((a : Num) (b : Num)) : Num
                (return (+ a b))))");

        WriteModule("main.ag", @"
            (module Main
              (import ""./math_utils.ag"")
              (defun compute () : Num
                (return (add 2.0 3.0)))
              (test compute
                (assert-eq (compute) 5.0)))");

        // Act
        var result = CompileFile("main.ag");

        // Assert
        result.Success.Should().BeTrue();
        result.TestsPassed.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void Import_OnlyExportedSymbolsAvailable()
    {
        // Arrange — helper is NOT exported
        WriteModule("utils.ag", @"
            (module Utils
              (export public_fn)
              (defun helper ((x : Num)) : Num (return (* x 2.0)))
              (defun public_fn ((x : Num)) : Num (return (helper x))))");

        WriteModule("main.ag", @"
            (module Main
              (import ""./utils.ag"")
              (test call_private
                (assert-eq (helper 5.0) 10.0)))");

        // Act
        var result = CompileFile("main.ag");

        // Assert — should fail because helper is not exported
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Message.Contains("not exported"));
    }

    [Fact]
    public void Import_ExportedFunctionCanCallPrivateHelper()
    {
        // Arrange — public_fn calls private helper internally
        WriteModule("utils.ag", @"
            (module Utils
              (export double_it)
              (defun helper ((x : Num)) : Num (return (* x 2.0)))
              (defun double_it ((x : Num)) : Num (return (helper x))))");

        WriteModule("main.ag", @"
            (module Main
              (import ""./utils.ag"")
              (test double_it
                (assert-eq (double_it 5.0) 10.0)))");

        // Act
        var result = CompileFile("main.ag");

        // Assert
        result.Success.Should().BeTrue();
        result.TestsPassed.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void Import_CircularDependency_ShouldFail()
    {
        // Arrange
        WriteModule("a.ag", @"
            (module A
              (import ""./b.ag"")
              (export fn_a)
              (defun fn_a () : Num (return 1.0)))");

        WriteModule("b.ag", @"
            (module B
              (import ""./a.ag"")
              (export fn_b)
              (defun fn_b () : Num (return 2.0)))");

        // Act
        var result = CompileFile("a.ag");

        // Assert
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Message.Contains("Circular"));
    }

    [Fact]
    public void Import_DiamondPattern_ShouldWork()
    {
        // Arrange: main → a, b; a → shared; b → shared
        WriteModule("shared.ag", @"
            (module Shared
              (export constant)
              (defun constant () : Num (return 42.0)))");

        WriteModule("a.ag", @"
            (module A
              (import ""./shared.ag"")
              (export use_a)
              (defun use_a () : Num (return (constant))))");

        WriteModule("b.ag", @"
            (module B
              (import ""./shared.ag"")
              (export use_b)
              (defun use_b () : Num (return (constant))))");

        WriteModule("main.ag", @"
            (module Main
              (import ""./a.ag"")
              (import ""./b.ag"")
              (test diamond
                (do
                  (assert-eq (use_a) 42.0)
                  (assert-eq (use_b) 42.0))))");

        // Act
        var result = CompileFile("main.ag");

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Import_MissingFile_ShouldFail()
    {
        // Arrange
        WriteModule("main.ag", @"
            (module Main
              (import ""./nonexistent.ag""))");

        // Act
        var result = CompileFile("main.ag");

        // Assert
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Message.Contains("not found"));
    }

    [Fact]
    public void Import_WithoutExtension_ShouldAutoResolve()
    {
        // Arrange — import without .ag extension should still work
        WriteModule("helper.ag", @"
            (module Helper
              (export greet)
              (defun greet () : Str (return ""hello"")))");

        WriteModule("main.ag", @"
            (module Main
              (import ""./helper"")
              (test greet
                (assert-eq (greet) ""hello"")))");

        // Act
        var result = CompileFile("main.ag");

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Import_TestsInImportedModule_ShouldRun()
    {
        // Arrange — imported module has its own tests
        WriteModule("utils.ag", @"
            (module Utils
              (export add)
              (defun add ((a : Num) (b : Num)) : Num (return (+ a b)))
              (test add_test (assert-eq (add 1.0 2.0) 3.0)))");

        WriteModule("main.ag", @"
            (module Main
              (import ""./utils.ag"")
              (test use_add (assert-eq (add 10.0 20.0) 30.0)))");

        // Act
        var result = CompileFile("main.ag");

        // Assert
        result.Success.Should().BeTrue();
        // Should count tests from both modules
        result.TestsPassed.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public void Import_TranspiledCode_ShouldContainImportedFunctions()
    {
        // Arrange
        WriteModule("math.ag", @"
            (module Math
              (export multiply)
              (defun multiply ((a : Num) (b : Num)) : Num (return (* a b))))");

        WriteModule("main.ag", @"
            (module Main
              (import ""./math.ag"")
              (sys.stdout.write (multiply 3.0 4.0)))");

        // Act
        var result = CompileFile("main.ag");

        // Assert
        result.Success.Should().BeTrue();
        result.GeneratedSource.Should().Contain("multiply");
        result.GeneratedSource.Should().Contain("Console.Write");
    }

    [Fact]
    public void Import_MultipleExports_ShouldAllBeAvailable()
    {
        // Arrange
        WriteModule("ops.ag", @"
            (module Ops
              (export add subtract)
              (defun add ((a : Num) (b : Num)) : Num (return (+ a b)))
              (defun subtract ((a : Num) (b : Num)) : Num (return (- a b))))");

        WriteModule("main.ag", @"
            (module Main
              (import ""./ops.ag"")
              (test ops
                (do
                  (assert-eq (add 5.0 3.0) 8.0)
                  (assert-eq (subtract 5.0 3.0) 2.0))))");

        // Act
        var result = CompileFile("main.ag");

        // Assert
        result.Success.Should().BeTrue();
        result.TestsPassed.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void Import_StdlibImport_ShouldStillWork()
    {
        // Arrange — stdlib imports should remain no-ops and not break
        WriteModule("main.ag", @"
            (module Main
              (import std.math)
              (import std.string)
              (defun run () : Num (return 1.0))
              (test run (assert-eq (run) 1.0)))");

        // Act
        var result = CompileFile("main.ag");

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void ModuleLoader_CircularDetection_ShouldThrow()
    {
        // Arrange — circular imports detected at verifier level, not loader level
        WriteModule("loop_a.ag", @"
            (module A
              (import ""./loop_b.ag"")
              (export fn_a)
              (defun fn_a () : Num (return 1.0)))");

        WriteModule("loop_b.ag", @"
            (module B
              (import ""./loop_a.ag"")
              (export fn_b)
              (defun fn_b () : Num (return 2.0)))");

        // Act
        var result = CompileFile("loop_a.ag");

        // Assert
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Message.Contains("Circular"));
    }

    [Fact]
    public void ModuleLoader_CachesLoadedModules()
    {
        // Arrange
        WriteModule("cached.ag", @"
            (module Cached
              (export val)
              (defun val () : Num (return 99.0)))");

        var loader = new ModuleLoader(_testDir);

        // Act
        var first = loader.Load("./cached.ag");
        var second = loader.Load("./cached.ag");

        // Assert — same instance returned
        first.Should().BeSameAs(second);
    }

    [Fact]
    public void ModuleLoader_MissingFile_ShouldThrow()
    {
        // Arrange
        var loader = new ModuleLoader(_testDir);

        // Act
        Action act = () => loader.Load("./does_not_exist.ag");

        // Assert
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*not found*");
    }

    [Fact]
    public void ModuleLoader_InvalidPath_ShouldThrow()
    {
        // Arrange
        var loader = new ModuleLoader(_testDir);

        // Act
        Action act = () => loader.Load("no_prefix_module");

        // Assert
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*must start with*");
    }
}
