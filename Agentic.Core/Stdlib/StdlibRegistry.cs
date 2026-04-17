using System;
using System.Collections.Generic;
using Agentic.Core.Syntax;

namespace Agentic.Core.Stdlib;

// Shared registration bag that both Verifier and Transpiler read from.
// Each IStdlibModule populates both sides in a single Register() call.
public sealed class StdlibRegistry
{
    // Verifier side: function name → eagerly-evaluated native handler
    public Dictionary<string, Func<object[], object>> VerifierFuncs { get; } = new();

    // Transpiler side: function name → emitter delegate
    // Receives (astArgs, recurse) where recurse translates a child AstNode to a C# expression string.
    public Dictionary<string, Func<IReadOnlyList<AstNode>, Func<AstNode, string>, string>> TranspilerEmitters { get; } = new();
}
