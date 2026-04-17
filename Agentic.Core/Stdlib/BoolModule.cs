using System;
using Agentic.Core.Syntax;

namespace Agentic.Core.Stdlib;

// Boolean operators: not, and, or.
// These are short-circuit in C# but eagerly evaluated in the Verifier (acceptable for an LLM language).
public sealed class BoolModule : IStdlibModule
{
    public void Register(StdlibRegistry registry)
    {
        // --- Verifier ---
        registry.VerifierFuncs["not"] = a => !Convert.ToBoolean(a[0]);
        registry.VerifierFuncs["and"] = a => Convert.ToBoolean(a[0]) && Convert.ToBoolean(a[1]);
        registry.VerifierFuncs["or"]  = a => Convert.ToBoolean(a[0]) || Convert.ToBoolean(a[1]);

        // --- Transpiler ---
        registry.TranspilerEmitters["not"] = (a, r) => $"(!Convert.ToBoolean({r(a[0])}))";
        registry.TranspilerEmitters["and"]  = (a, r) => $"(Convert.ToBoolean({r(a[0])}) && Convert.ToBoolean({r(a[1])}))";
        registry.TranspilerEmitters["or"]   = (a, r) => $"(Convert.ToBoolean({r(a[0])}) || Convert.ToBoolean({r(a[1])}))";
    }
}
