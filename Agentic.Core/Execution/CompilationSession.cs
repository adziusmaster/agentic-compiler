using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agentic.Core.Execution;

/// <summary>
/// Records a sequence of compilation attempts within a single session.
/// Useful for auditing agent iteration loops and analyzing convergence.
/// </summary>
public sealed class CompilationSession
{
    private readonly List<SessionEntry> _entries = new();

    /// <summary>Unique identifier for this session.</summary>
    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>When the session was created.</summary>
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>Ordered list of compilation attempts in this session.</summary>
    public IReadOnlyList<SessionEntry> Entries => _entries;

    /// <summary>Whether any attempt in the session succeeded.</summary>
    public bool HasSuccess => _entries.Any(e => e.Success);

    /// <summary>Total number of compilation attempts.</summary>
    public int TotalAttempts => _entries.Count;

    /// <summary>
    /// Records a compilation attempt with its source and result.
    /// </summary>
    public void Record(string source, CompileResult result)
    {
        _entries.Add(new SessionEntry
        {
            Attempt = _entries.Count + 1,
            SourceHash = CompilationCache.ComputeHash(source),
            SourceLength = source.Length,
            Success = result.Success,
            TestsPassed = result.TestsPassed,
            TestsFailed = result.TestsFailed,
            Errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"[{d.Type}] {d.Message}")
                .ToList(),
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Returns a compact summary of the session for logging.
    /// </summary>
    public string ToSummary()
    {
        var successAttempt = _entries.FirstOrDefault(e => e.Success);
        if (successAttempt is not null)
            return $"Session {SessionId}: succeeded on attempt {successAttempt.Attempt}/{TotalAttempts} " +
                   $"(tests: {successAttempt.TestsPassed}/{successAttempt.TestsPassed + successAttempt.TestsFailed})";

        return $"Session {SessionId}: failed after {TotalAttempts} attempt(s), " +
               $"last errors: {string.Join("; ", _entries.Last().Errors.Take(3))}";
    }

    /// <summary>
    /// Serializes the session to JSON for persistence or analysis.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(new
    {
        sessionId = SessionId,
        startedAt = StartedAt,
        totalAttempts = TotalAttempts,
        hasSuccess = HasSuccess,
        entries = _entries
    }, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>
/// A single compilation attempt recorded in a session.
/// </summary>
public sealed record SessionEntry
{
    public required int Attempt { get; init; }
    public required string SourceHash { get; init; }
    public required int SourceLength { get; init; }
    public required bool Success { get; init; }
    public int TestsPassed { get; init; }
    public int TestsFailed { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}
