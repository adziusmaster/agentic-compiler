using System;
using System.Collections.Generic;
using System.Linq;
using Agentic.Core.Syntax;

namespace Agentic.Core.Stdlib;

// Covers all math.* operations and normalises LLM hallucinations of C# stdlib methods.
public sealed class MathModule : IStdlibModule
{
    public void Register(StdlibRegistry registry)
    {
        // --- Verifier (eagerly-evaluated) ---
        registry.VerifierFuncs["math.sin"]   = a => Math.Sin(D(a[0]));
        registry.VerifierFuncs["math.cos"]   = a => Math.Cos(D(a[0]));
        registry.VerifierFuncs["math.pow"]   = a => Math.Pow(D(a[0]), D(a[1]));
        registry.VerifierFuncs["math.abs"]   = a => Math.Abs(D(a[0]));
        registry.VerifierFuncs["math.sqrt"]  = a => Math.Sqrt(D(a[0]));
        registry.VerifierFuncs["math.floor"] = a => Math.Floor(D(a[0]));
        registry.VerifierFuncs["math.ceil"]  = a => Math.Ceiling(D(a[0]));
        registry.VerifierFuncs["math.log"]   = a => Math.Log(D(a[0]));
        registry.VerifierFuncs["math.min"]   = a => Math.Min(D(a[0]), D(a[1]));
        registry.VerifierFuncs["math.max"]   = a => Math.Max(D(a[0]), D(a[1]));
        registry.VerifierFuncs["math.mod"]   = a => D(a[0]) % D(a[1]);

        // LLM aliases — it sometimes emits raw C# method names
        registry.VerifierFuncs["double.Parse"] = a => Convert.ToDouble(a[0]);
        registry.VerifierFuncs["double.parse"] = a => Convert.ToDouble(a[0]);
        registry.VerifierFuncs["int.Parse"]    = a => (double)Convert.ToInt32(a[0]);
        registry.VerifierFuncs["int.parse"]    = a => (double)Convert.ToInt32(a[0]);

        // --- Transpiler (C# expression emitters) ---

        // math.mod is an operator, not a function call
        registry.TranspilerEmitters["math.mod"] = (a, r) => $"({r(a[0])} % {r(a[1])})";

        // Standard single-function mappings: math.X → Math.X(args)
        var directMap = new Dictionary<string, string>
        {
            ["math.sin"]   = "Math.Sin",
            ["math.cos"]   = "Math.Cos",
            ["math.pow"]   = "Math.Pow",
            ["math.abs"]   = "Math.Abs",
            ["math.sqrt"]  = "Math.Sqrt",
            ["math.floor"] = "Math.Floor",
            ["math.ceil"]  = "Math.Ceiling",
            ["math.log"]   = "Math.Log",
            ["math.min"]   = "Math.Min",
            ["math.max"]   = "Math.Max",
            // LLM aliases → safe C# equivalents
            ["double.Parse"] = "Convert.ToDouble",
            ["double.parse"] = "Convert.ToDouble",
            ["int.Parse"]    = "Convert.ToInt32",
            ["int.parse"]    = "Convert.ToInt32",
        };

        foreach (var (op, csName) in directMap)
        {
            var captured = csName;
            registry.TranspilerEmitters[op] = (args, recurse) =>
                $"{captured}({string.Join(", ", args.Select(recurse))})";
        }
    }

    private static double D(object o) => Convert.ToDouble(o);
}
