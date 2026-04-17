using System.Text;
using Agentic.Core.Syntax;

namespace Agentic.Core.Agent;

public enum TestStatus
{
    Passed,
    Failed,
    Crashed,
}

public sealed record TestOutcome(
    IReadOnlyList<string> Inputs,
    string Expected,
    string? Actual,
    TestStatus Status,
    string? CrashType = null,
    string? CrashMessage = null);

/// <summary>
/// Structured diagnostic describing how a generated program fared against the
/// constraint profile's tests. Replaces the "one error string wins" feedback that
/// previously flowed into the LLM retry loop.
/// </summary>
public sealed record FeedbackEnvelope(
    IReadOnlyList<TestOutcome> TestOutcomes,
    string? ParseError = null,
    SourceSpan? ErrorSpan = null)
{
    public bool AllPassed =>
        ParseError == null && TestOutcomes.All(t => t.Status == TestStatus.Passed);

    public int PassedCount => TestOutcomes.Count(t => t.Status == TestStatus.Passed);
    public int TotalCount => TestOutcomes.Count;

    public static FeedbackEnvelope FromParseError(string message, SourceSpan? span = null) =>
        new([], message, span);

    /// <summary>
    /// Renders this envelope as a compact diagnostic the LLM can reason about —
    /// which tests passed, what the actual-vs-expected mismatch looks like, and
    /// whether the failure localized to a single test or hit a parse error.
    /// </summary>
    public string ToLlmFeedback()
    {
        var sb = new StringBuilder();

        if (ParseError is not null)
        {
            sb.Append("Parse error: ").Append(ParseError);
            if (ErrorSpan is { } span)
            {
                sb.Append(" (line ").Append(span.StartLine).Append(", col ").Append(span.StartColumn).Append(')');
            }
            return sb.ToString();
        }

        sb.Append("Tests: ").Append(PassedCount).Append('/').Append(TotalCount).Append(" passed.");

        var failures = TestOutcomes.Where(t => t.Status != TestStatus.Passed).ToList();
        if (failures.Count == 0)
        {
            return sb.ToString();
        }

        for (int i = 0; i < TestOutcomes.Count; i++)
        {
            var t = TestOutcomes[i];
            if (t.Status == TestStatus.Passed) continue;

            sb.AppendLine();
            sb.Append("- Test #").Append(i + 1)
              .Append(" input=[").Append(string.Join(",", t.Inputs)).Append("] ");

            if (t.Status == TestStatus.Crashed)
            {
                sb.Append("CRASHED (").Append(t.CrashType).Append("): ").Append(t.CrashMessage);
            }
            else
            {
                sb.Append("expected='").Append(t.Expected).Append("' actual='").Append(t.Actual).Append('\'');
            }
        }

        if (PassedCount > 0)
        {
            sb.AppendLine();
            sb.Append("Tests ")
              .Append(string.Join(",", TestOutcomes
                  .Select((t, i) => (t, i))
                  .Where(x => x.t.Status == TestStatus.Passed)
                  .Select(x => "#" + (x.i + 1))))
              .Append(" already pass — preserve behavior on those inputs.");
        }

        return sb.ToString();
    }
}
