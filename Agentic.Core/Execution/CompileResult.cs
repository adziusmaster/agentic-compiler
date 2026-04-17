using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Agentic.Core.Syntax;

namespace Agentic.Core.Execution;

/// <summary>Severity level for a compiler diagnostic.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiagnosticSeverity { Error, Warning, Info }

/// <summary>
/// A single diagnostic emitted by the compiler. Designed to be machine-parseable
/// by AI agents for automated fix cycles.
/// </summary>
public sealed record CompileDiagnostic
{
    /// <summary>Severity: Error stops compilation, Warning/Info are advisory.</summary>
    public required DiagnosticSeverity Severity { get; init; }

    /// <summary>Machine-readable error category (e.g. "type-mismatch", "contract-violation", "test-failure").</summary>
    public required string Type { get; init; }

    /// <summary>Human-readable description of the problem.</summary>
    public required string Message { get; init; }

    /// <summary>Source location, if known.</summary>
    public SourceSpan? Span { get; init; }

    /// <summary>What was expected (for type errors, assertion failures).</summary>
    public string? Expected { get; init; }

    /// <summary>What was actually encountered.</summary>
    public string? Actual { get; init; }

    /// <summary>Context: which function or test block the error occurred in.</summary>
    public string? InContext { get; init; }

    /// <summary>Machine-actionable suggestion for the agent.</summary>
    public string? FixHint { get; init; }

    /// <summary>Emits this diagnostic as an S-expression string.</summary>
    public string ToSExpr()
    {
        var parts = new List<string>
        {
            $"(severity {Severity.ToString().ToLower()})",
            $"(type \"{Type}\")",
            $"(message \"{Escape(Message)}\")"
        };
        if (Span is not null)
            parts.Add($"(span (line {Span.Value.StartLine}) (col {Span.Value.StartColumn}) (end-line {Span.Value.EndLine}) (end-col {Span.Value.EndColumn}))");
        if (Expected is not null) parts.Add($"(expected \"{Escape(Expected)}\")");
        if (Actual is not null) parts.Add($"(actual \"{Escape(Actual)}\")");
        if (InContext is not null) parts.Add($"(in-context \"{Escape(InContext)}\")");
        if (FixHint is not null) parts.Add($"(fix-hint \"{Escape(FixHint)}\")");
        return $"(diagnostic {string.Join(" ", parts)})";
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

/// <summary>
/// Structured result of a compilation attempt. Contains success/failure status,
/// test results, diagnostics, and the binary path on success.
/// </summary>
public sealed record CompileResult
{
    public required bool Success { get; init; }
    public string? BinaryPath { get; init; }
    public int TestsPassed { get; init; }
    public int TestsFailed { get; init; }
    public string? GeneratedSource { get; init; }
    public IReadOnlyList<CompileDiagnostic> Diagnostics { get; init; } = Array.Empty<CompileDiagnostic>();

    /// <summary>
    /// Emits the result as an S-expression for agent consumption.
    /// </summary>
    public string ToSExpr()
    {
        if (Success)
        {
            var sb = new StringBuilder();
            sb.Append("(ok");
            if (BinaryPath is not null) sb.Append($" (binary \"{BinaryPath}\")");
            sb.Append($" (tests-passed {TestsPassed}/{TestsPassed + TestsFailed})");
            var warnings = Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();
            if (warnings.Count > 0)
            {
                sb.Append(" (warnings");
                foreach (var w in warnings) sb.Append($" {w.ToSExpr()}");
                sb.Append(')');
            }
            sb.Append(')');
            return sb.ToString();
        }
        else
        {
            var sb = new StringBuilder();
            sb.Append("(error");
            sb.Append($" (tests-passed {TestsPassed}/{TestsPassed + TestsFailed})");
            foreach (var d in Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                sb.Append($" {d.ToSExpr()}");
            sb.Append(')');
            return sb.ToString();
        }
    }

    /// <summary>
    /// Emits the result as JSON for tool integration.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
