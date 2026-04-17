namespace Agentic.Core.Stdlib;

/// <summary>String operations: concatenation, conversion, comparison, length.</summary>
public sealed class StringModule : IStdlibModule
{
    public void Register(StdlibRegistry registry)
    {
        registry.VerifierFuncs["str.concat"]   = a => Convert.ToString(a[0]) + Convert.ToString(a[1]);
        registry.VerifierFuncs["str.length"]   = a => (double)(Convert.ToString(a[0])?.Length ?? 0);
        registry.VerifierFuncs["str.from_num"] = a => Convert.ToDouble(a[0]).ToString();
        registry.VerifierFuncs["str.to_num"]   = a => Convert.ToDouble(a[0]);
        registry.VerifierFuncs["str.eq"]       = a => Convert.ToString(a[0]) == Convert.ToString(a[1]);

        registry.TranspilerEmitters["str.concat"]   = (a, r) => $"({r(a[0])} + {r(a[1])})";
        registry.TranspilerEmitters["str.length"]   = (a, r) => $"((double){r(a[0])}.Length)";
        registry.TranspilerEmitters["str.from_num"] = (a, r) => $"(({r(a[0])}).ToString())";
        registry.TranspilerEmitters["str.to_num"]   = (a, r) => $"Convert.ToDouble({r(a[0])})";
        registry.TranspilerEmitters["str.eq"]       = (a, r) => $"({r(a[0])} == {r(a[1])})";
    }
}
