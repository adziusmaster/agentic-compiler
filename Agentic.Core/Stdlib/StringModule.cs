using System;
using Agentic.Core.Syntax;

namespace Agentic.Core.Stdlib;

// Covers all str.* string operations.
public sealed class StringModule : IStdlibModule
{
    public void Register(StdlibRegistry registry)
    {
        // --- Verifier ---
        registry.VerifierFuncs["str.concat"]   = a => Convert.ToString(a[0]) + Convert.ToString(a[1]);
        registry.VerifierFuncs["str.length"]   = a => (double)(Convert.ToString(a[0])?.Length ?? 0);
        registry.VerifierFuncs["str.from_num"] = a => Convert.ToDouble(a[0]).ToString();
        // str.to_num accepts both string and double inputs (LLM wraps numeric args with it)
        registry.VerifierFuncs["str.to_num"]   = a => Convert.ToDouble(a[0]);
        registry.VerifierFuncs["str.eq"]       = a => Convert.ToString(a[0]) == Convert.ToString(a[1]);

        // --- Transpiler ---
        registry.TranspilerEmitters["str.concat"]   = (a, r) => $"({r(a[0])} + {r(a[1])})";
        registry.TranspilerEmitters["str.length"]   = (a, r) => $"((double){r(a[0])}.Length)";
        registry.TranspilerEmitters["str.from_num"] = (a, r) => $"(({r(a[0])}).ToString())";
        // Convert.ToDouble handles both string and double — prevents CS1503 when LLM wraps numerics
        registry.TranspilerEmitters["str.to_num"]   = (a, r) => $"Convert.ToDouble({r(a[0])})";
        registry.TranspilerEmitters["str.eq"]       = (a, r) => $"({r(a[0])} == {r(a[1])})";
    }
}
