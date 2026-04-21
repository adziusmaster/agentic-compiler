using System.Text;

namespace Agentic.Check;

// Extract capability call sites from the emitted binary by searching for
// the transpiler's CSharpEmitExpr signatures. The signature set is fixed
// and owned by the checker — if DefaultCapabilities adds a new capability
// we must add its pattern here, by design. This is the TCB's view of the
// universe of syscalls.
//
// TCB cost: ~80 LOC.
//
// Trust assumption TA-1 (docs/safety-policy.md §3.1): the pattern set is a
// sound over-approximation of capability invocations in β. If a capability
// emits code that doesn't include the documented substring, CS silently
// fails. Mitigation: every entry in DefaultCapabilities is reviewed
// alongside Agentic.Check/CapabilityExtractor.cs.

public static class CapabilityExtractor
{
    // Each capability name maps to one or more substring signatures that
    // the emitted C# is known to contain when the capability is invoked.
    // Must match `Capability.CSharpEmitExpr` (Agentic.Core/Capabilities/
    // DefaultCapabilities.cs).
    private static readonly IReadOnlyList<(string Cap, string[] Signatures)> Patterns =
        new List<(string, string[])>
        {
            ("http.fetch",     new[] { "_httpClient.GetStringAsync" }),
            ("time.now_unix",  new[] { "DateTimeOffset.UtcNow.ToUnixTimeSeconds" }),
            ("file.read",      new[] { "System.IO.File.ReadAllText" }),
            ("file.write",     new[] { "System.IO.File.WriteAllText" }),
            ("env.get",        new[] { "System.Environment.GetEnvironmentVariable" }),
            ("db.query",       new[] { "Microsoft.Data.Sqlite", "_DbConnect", "ExecuteScalar" }),
            ("process.spawn",  new[] { "System.Diagnostics.Process" }),
        };

    /// <summary>
    /// Scan the binary bytes for each capability's emit signature. A
    /// capability is reported iff *any* of its signatures is found. Binary
    /// is read into memory — these are self-contained AOT/SC binaries of
    /// ≤ 100 MB, so this is acceptable.
    /// </summary>
    public static HashSet<string> Extract(string binaryPath)
    {
        byte[] bytes = File.ReadAllBytes(binaryPath);
        // Capabilities emit ASCII identifiers. Search the raw bytes as UTF-8.
        // A faster approach would be an Aho-Corasick; the naive Boyer-Moore
        // in IndexOf is fine for ~100 MB × 7 patterns.
        string content = Encoding.UTF8.GetString(bytes);
        var found = new HashSet<string>();
        foreach (var (cap, sigs) in Patterns)
        {
            foreach (var s in sigs)
            {
                if (content.Contains(s, StringComparison.Ordinal)) { found.Add(cap); break; }
            }
        }
        return found;
    }

    /// <summary>Signature inventory — for logging / audit output.</summary>
    public static IEnumerable<(string Capability, string[] Signatures)> KnownCapabilities() => Patterns;
}
