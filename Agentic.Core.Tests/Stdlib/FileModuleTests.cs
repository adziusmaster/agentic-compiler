using Agentic.Core.Execution;
using Agentic.Core.Stdlib;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Stdlib;

public sealed class FileModuleTests
{
    #region Verifier (In-Memory Virtual FS)

    [Fact]
    public void FileWrite_ThenRead_ShouldRoundTrip()
    {
        // Arrange
        const string source = @"
            (do
              (file.write ""test.txt"" ""hello world"")
              (def content (file.read ""test.txt""))
              (test file-io (assert-eq content ""hello world"")))";

        var permissions = new Permissions { AllowFileRead = true, AllowFileWrite = true };
        var compiler = new Compiler(emitBinary: false, permissions: permissions);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
        result.TestsPassed.Should().Be(1);
    }

    [Fact]
    public void FileExists_ShouldReturnCorrectValue()
    {
        // Arrange
        const string source = @"
            (do
              (file.write ""exists.txt"" ""data"")
              (test file-exists
                (assert-eq (file.exists ""exists.txt"") 1.0)
                (assert-eq (file.exists ""nope.txt"") 0.0)))";

        var permissions = new Permissions { AllowFileRead = true, AllowFileWrite = true };
        var compiler = new Compiler(emitBinary: false, permissions: permissions);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void FileAppend_ShouldConcatenate()
    {
        // Arrange
        const string source = @"
            (do
              (file.write ""log.txt"" ""line1"")
              (file.append ""log.txt"" ""-line2"")
              (test file-append (assert-eq (file.read ""log.txt"") ""line1-line2"")))";

        var permissions = new Permissions { AllowFileRead = true, AllowFileWrite = true };
        var compiler = new Compiler(emitBinary: false, permissions: permissions);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void FileDelete_ShouldRemoveFile()
    {
        // Arrange
        const string source = @"
            (do
              (file.write ""temp.txt"" ""data"")
              (file.delete ""temp.txt"")
              (test file-delete (assert-eq (file.exists ""temp.txt"") 0.0)))";

        var permissions = new Permissions { AllowFileRead = true, AllowFileWrite = true };
        var compiler = new Compiler(emitBinary: false, permissions: permissions);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void FileRead_NonexistentFile_ShouldFail()
    {
        // Arrange
        const string source = @"(do (file.read ""missing.txt""))";
        var compiler = new Compiler(emitBinary: false);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Message.Contains("not found"));
    }

    #endregion

    #region Permission Enforcement

    [Fact]
    public void FileOps_WithoutPermission_ShouldFailAtTranspile()
    {
        // Arrange — file.write used without --allow-file permission
        const string source = @"(do (file.write ""out.txt"" ""data""))";
        var compiler = new Compiler(emitBinary: false, permissions: Permissions.None);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Type == "permission-denied");
    }

    [Fact]
    public void FileOps_WithPermission_ShouldSucceed()
    {
        // Arrange
        const string source = @"(do (file.write ""out.txt"" ""data""))";
        var permissions = new Permissions { AllowFileRead = true, AllowFileWrite = true };
        var compiler = new Compiler(emitBinary: false, permissions: permissions);

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
        result.GeneratedSource.Should().Contain("System.IO.File.WriteAllText");
    }

    #endregion

    #region Transpiler Output

    [Fact]
    public void FileRead_ShouldTranspileToSystemIO()
    {
        // Arrange
        var permissions = new Permissions { AllowFileRead = true, AllowFileWrite = true };
        var compiler = new Compiler(emitBinary: false, permissions: permissions);
        var source = @"(do (file.write ""data.txt"" ""x"") (def content (file.read ""data.txt"")))";

        // Act
        var result = compiler.Compile(source);

        // Assert
        result.Success.Should().BeTrue();
        result.GeneratedSource.Should().Contain("System.IO.File.ReadAllText");
    }

    #endregion
}
