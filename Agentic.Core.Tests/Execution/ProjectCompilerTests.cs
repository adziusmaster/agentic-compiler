using Agentic.Core.Execution;
using Agentic.Core.Syntax;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Execution;

public sealed class ProjectCompilerTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectCompilerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AgenticTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void WriteFile(string name, string content)
    {
        File.WriteAllText(Path.Combine(_tempDir, name), content);
    }

    #region Single File

    [Fact]
    public void CompileDirectory_SingleFile_ShouldSucceed()
    {
        // Arrange
        WriteFile("main.ag", @"
            (module Main
              (defun add ((a : Num) (b : Num)) : Num (return (+ a b)))
              (test add (assert-eq (add 1 2) 3)))");

        var compiler = new ProjectCompiler(emitBinary: false);

        // Act
        var result = compiler.CompileDirectory(_tempDir, "TestProgram");

        // Assert
        result.Success.Should().BeTrue();
        result.TestsPassed.Should().Be(1);
    }

    #endregion

    #region Multi-File

    [Fact]
    public void CompileDirectory_TwoModulesWithImport_ShouldResolveOrder()
    {
        // Arrange — math.ag defines add, main.ag imports and uses it
        WriteFile("math.ag", @"
            (module Math
              (defun add ((a : Num) (b : Num)) : Num (return (+ a b))))");

        WriteFile("main.ag", @"
            (module Main
              (import ./Math)
              (test add_integration (assert-eq (add 10 20) 30)))");

        var compiler = new ProjectCompiler(emitBinary: false);

        // Act
        var result = compiler.CompileDirectory(_tempDir, "TestProject");

        // Assert
        result.Success.Should().BeTrue();
        result.TestsPassed.Should().Be(1);
    }

    [Fact]
    public void CompileDirectory_ThreeModules_ChainedImports()
    {
        // Arrange — C imports B imports A
        WriteFile("base.ag", @"
            (module Base
              (defun double ((x : Num)) : Num (return (* x 2))))");

        WriteFile("middle.ag", @"
            (module Middle
              (import ./Base)
              (defun quadruple ((x : Num)) : Num (return (double (double x)))))");

        WriteFile("top.ag", @"
            (module Top
              (import ./Middle)
              (test quadruple_test (assert-eq (quadruple 3) 12)))");

        var compiler = new ProjectCompiler(emitBinary: false);

        // Act
        var result = compiler.CompileDirectory(_tempDir, "ChainProject");

        // Assert
        result.Success.Should().BeTrue();
        result.TestsPassed.Should().Be(1);
    }

    [Fact]
    public void CompileDirectory_MultipleTestsAcrossModules()
    {
        // Arrange
        WriteFile("math.ag", @"
            (module Math
              (defun add ((a : Num) (b : Num)) : Num (return (+ a b)))
              (test add (assert-eq (add 1 2) 3)))");

        WriteFile("app.ag", @"
            (module App
              (import ./Math)
              (defun sum3 ((a : Num) (b : Num) (c : Num)) : Num (return (add a (add b c))))
              (test sum3 (assert-eq (sum3 1 2 3) 6)))");

        var compiler = new ProjectCompiler(emitBinary: false);

        // Act
        var result = compiler.CompileDirectory(_tempDir, "MultiTest");

        // Assert
        result.Success.Should().BeTrue();
        result.TestsPassed.Should().Be(2);
    }

    #endregion

    #region Error Handling

    [Fact]
    public void CompileDirectory_NonexistentDir_ShouldReturnError()
    {
        // Arrange
        var compiler = new ProjectCompiler(emitBinary: false);

        // Act
        var result = compiler.CompileDirectory("/nonexistent/path");

        // Assert
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Type == "project-error");
    }

    [Fact]
    public void CompileDirectory_EmptyDir_ShouldReturnError()
    {
        // Arrange
        var emptyDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyDir);
        var compiler = new ProjectCompiler(emitBinary: false);

        // Act
        var result = compiler.CompileDirectory(emptyDir);

        // Assert
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Message.Contains("No .ag files"));
    }

    [Fact]
    public void CompileDirectory_CircularDependency_ShouldReturnError()
    {
        // Arrange
        WriteFile("a.ag", @"(module A (import ./B) (def x 1))");
        WriteFile("b.ag", @"(module B (import ./A) (def y 2))");

        var compiler = new ProjectCompiler(emitBinary: false);

        // Act
        var result = compiler.CompileDirectory(_tempDir);

        // Assert
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Type == "import-error");
        result.Diagnostics[0].Message.Should().Contain("Circular");
    }

    [Fact]
    public void CompileDirectory_MissingImport_ShouldReturnError()
    {
        // Arrange
        WriteFile("main.ag", @"(module Main (import ./Nonexistent) (def x 1))");

        var compiler = new ProjectCompiler(emitBinary: false);

        // Act
        var result = compiler.CompileDirectory(_tempDir);

        // Assert
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Type == "import-error");
        result.Diagnostics[0].Message.Should().Contain("Nonexistent");
    }

    [Fact]
    public void CompileDirectory_ParseError_ShouldReturnError()
    {
        // Arrange
        WriteFile("bad.ag", "(module Bad ((( unclosed");

        var compiler = new ProjectCompiler(emitBinary: false);

        // Act
        var result = compiler.CompileDirectory(_tempDir);

        // Assert
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Type == "parse-error");
    }

    #endregion

    #region Module Info Extraction

    [Fact]
    public void ExtractModuleInfo_ShouldFindModuleName()
    {
        // Arrange
        var tokens = new Lexer("(module Calculator (def x 1))").Tokenize();
        var ast = new Parser(tokens).Parse();

        // Act
        var info = ProjectCompiler.ExtractModuleInfo("Calculator.ag", ast);

        // Assert
        info.Name.Should().Be("Calculator");
        info.Imports.Should().BeEmpty();
    }

    [Fact]
    public void ExtractModuleInfo_ShouldFindLocalImports()
    {
        // Arrange
        var tokens = new Lexer("(module App (import ./Math) (import std.io) (def x 1))").Tokenize();
        var ast = new Parser(tokens).Parse();

        // Act
        var info = ProjectCompiler.ExtractModuleInfo("App.ag", ast);

        // Assert
        info.Name.Should().Be("App");
        info.Imports.Should().ContainSingle().Which.Should().Be("Math");
    }

    [Fact]
    public void ExtractModuleInfo_DoBlock_ShouldUseFallbackName()
    {
        // Arrange
        var tokens = new Lexer("(do (import ./Util) (def x 1))").Tokenize();
        var ast = new Parser(tokens).Parse();

        // Act
        var info = ProjectCompiler.ExtractModuleInfo("script.ag", ast);

        // Assert
        info.Name.Should().Be("script");
        info.Imports.Should().ContainSingle().Which.Should().Be("Util");
    }

    #endregion

    #region Topological Sort

    [Fact]
    public void TopologicalSort_NoDeps_ShouldReturnAll()
    {
        // Arrange
        var modules = new[]
        {
            new ModuleInfo("A", "a.ag", CreateDummyAst(), Array.Empty<string>()),
            new ModuleInfo("B", "b.ag", CreateDummyAst(), Array.Empty<string>())
        };

        // Act
        var sorted = ProjectCompiler.TopologicalSort(modules);

        // Assert
        sorted.Should().HaveCount(2);
    }

    [Fact]
    public void TopologicalSort_LinearChain_ShouldOrderCorrectly()
    {
        // Arrange — C depends on B, B depends on A
        var a = new ModuleInfo("A", "a.ag", CreateDummyAst(), Array.Empty<string>());
        var b = new ModuleInfo("B", "b.ag", CreateDummyAst(), new[] { "A" });
        var c = new ModuleInfo("C", "c.ag", CreateDummyAst(), new[] { "B" });

        // Act
        var sorted = ProjectCompiler.TopologicalSort(new[] { c, a, b });

        // Assert
        sorted[0].Name.Should().Be("A");
        sorted[1].Name.Should().Be("B");
        sorted[2].Name.Should().Be("C");
    }

    private static AstNode CreateDummyAst()
    {
        var doToken = new Token(TokenType.Identifier, "do", 0, 0);
        var one = new Token(TokenType.Number, "1", 0, 0);
        return new ListNode(
            new AstNode[]
            {
                new AtomNode(doToken),
                new AtomNode(one)
            });
    }

    #endregion
}
