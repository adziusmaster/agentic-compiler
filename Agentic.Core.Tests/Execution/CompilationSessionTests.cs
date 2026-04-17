using Agentic.Core.Execution;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Execution;

public sealed class CompilationSessionTests
{
    [Fact]
    public void Session_HasUniqueId()
    {
        // Arrange / Act
        var s1 = new CompilationSession();
        var s2 = new CompilationSession();

        // Assert
        s1.SessionId.Should().NotBe(s2.SessionId);
        s1.SessionId.Should().HaveLength(12);
    }

    [Fact]
    public void Record_TracksAttempts()
    {
        // Arrange
        var session = new CompilationSession();

        // Act
        session.Record("(do (def x 1))", new CompileResult { Success = true, TestsPassed = 1 });
        session.Record("(do (def x 2))", new CompileResult { Success = false, Diagnostics = new[]
        {
            new CompileDiagnostic { Severity = DiagnosticSeverity.Error, Type = "test-failure", Message = "bad" }
        }});

        // Assert
        session.TotalAttempts.Should().Be(2);
        session.Entries[0].Attempt.Should().Be(1);
        session.Entries[0].Success.Should().BeTrue();
        session.Entries[1].Attempt.Should().Be(2);
        session.Entries[1].Success.Should().BeFalse();
    }

    [Fact]
    public void HasSuccess_WhenOneSucceeds()
    {
        // Arrange
        var session = new CompilationSession();
        session.Record("bad", new CompileResult { Success = false, Diagnostics = new[]
        {
            new CompileDiagnostic { Severity = DiagnosticSeverity.Error, Type = "e", Message = "m" }
        }});
        session.Record("good", new CompileResult { Success = true });

        // Assert
        session.HasSuccess.Should().BeTrue();
    }

    [Fact]
    public void HasSuccess_WhenAllFail()
    {
        // Arrange
        var session = new CompilationSession();
        session.Record("bad", new CompileResult { Success = false, Diagnostics = new[]
        {
            new CompileDiagnostic { Severity = DiagnosticSeverity.Error, Type = "e", Message = "m" }
        }});

        // Assert
        session.HasSuccess.Should().BeFalse();
    }

    [Fact]
    public void Record_CapturesSourceHash()
    {
        // Arrange
        var session = new CompilationSession();
        const string source = "(do (def x 42))";

        // Act
        session.Record(source, new CompileResult { Success = true });

        // Assert
        session.Entries[0].SourceHash.Should().Be(CompilationCache.ComputeHash(source));
    }

    [Fact]
    public void Record_CapturesErrors()
    {
        // Arrange
        var session = new CompilationSession();

        // Act
        session.Record("bad", new CompileResult
        {
            Success = false,
            Diagnostics = new[]
            {
                new CompileDiagnostic { Severity = DiagnosticSeverity.Error, Type = "test-failure", Message = "expected 3, got 0" },
                new CompileDiagnostic { Severity = DiagnosticSeverity.Error, Type = "contract-violation", Message = "precondition failed" }
            }
        });

        // Assert
        session.Entries[0].Errors.Should().HaveCount(2);
        session.Entries[0].Errors[0].Should().Contain("test-failure");
        session.Entries[0].Errors[1].Should().Contain("contract-violation");
    }

    [Fact]
    public void ToSummary_Success_ShowsAttemptNumber()
    {
        // Arrange
        var session = new CompilationSession();
        session.Record("bad", new CompileResult { Success = false, Diagnostics = new[]
        {
            new CompileDiagnostic { Severity = DiagnosticSeverity.Error, Type = "e", Message = "m" }
        }});
        session.Record("good", new CompileResult { Success = true, TestsPassed = 3 });

        // Act
        var summary = session.ToSummary();

        // Assert
        summary.Should().Contain("succeeded on attempt 2/2");
        summary.Should().Contain("tests: 3/3");
    }

    [Fact]
    public void ToSummary_Failure_ShowsErrorCount()
    {
        // Arrange
        var session = new CompilationSession();
        session.Record("bad", new CompileResult { Success = false, Diagnostics = new[]
        {
            new CompileDiagnostic { Severity = DiagnosticSeverity.Error, Type = "test-failure", Message = "expected 3, got 0" }
        }});

        // Act
        var summary = session.ToSummary();

        // Assert
        summary.Should().Contain("failed after 1 attempt");
        summary.Should().Contain("test-failure");
    }

    [Fact]
    public void ToJson_SerializesCorrectly()
    {
        // Arrange
        var session = new CompilationSession();
        session.Record("(do (def x 1))", new CompileResult { Success = true, TestsPassed = 1 });

        // Act
        var json = session.ToJson();

        // Assert
        json.Should().Contain("\"sessionId\"");
        json.Should().Contain("\"totalAttempts\": 1");
        json.Should().Contain("\"hasSuccess\": true");
    }
}
