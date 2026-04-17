using Agentic.Core.Syntax;

namespace Agentic.Core.Stdlib;

/// <summary>
/// Shared registration bag consumed by both the Verifier and the Transpiler.
/// Each <see cref="IStdlibModule"/> populates both sides in a single <c>Register()</c> call.
/// </summary>
public sealed class StdlibRegistry
{
    /// <summary>Verifier side: function name → eagerly-evaluated native handler.</summary>
    public Dictionary<string, Func<object[], object>> VerifierFuncs { get; } = new();

    /// <summary>Transpiler side: function name → C# expression emitter (args, recurse).</summary>
    public Dictionary<string, Func<IReadOnlyList<AstNode>, Func<AstNode, string>, string>> TranspilerEmitters { get; } = new();

    /// <summary>Maps function names to the permission capability they require (e.g. "file.read", "http").</summary>
    public Dictionary<string, string> PermissionRequirements { get; } = new();

    /// <summary>When true, emitted code needs a static HttpClient field.</summary>
    public bool RequiresHttpClient { get; set; }

    /// <summary>When true, emitted code needs Microsoft.Data.Sqlite helpers.</summary>
    public bool RequiresSqlite { get; set; }
}
