namespace Agentic.Core.Stdlib;

/// <summary>Boolean operators: not, and, or.</summary>
public sealed class BoolModule : IStdlibModule
{
    public void Register(StdlibRegistry registry)
    {
        registry.VerifierFuncs["not"] = a => !Convert.ToBoolean(a[0]);
        registry.VerifierFuncs["and"] = a => Convert.ToBoolean(a[0]) && Convert.ToBoolean(a[1]);
        registry.VerifierFuncs["or"]  = a => Convert.ToBoolean(a[0]) || Convert.ToBoolean(a[1]);

        registry.TranspilerEmitters["not"] = (a, r) => $"(!Convert.ToBoolean({r(a[0])}))";
        registry.TranspilerEmitters["and"]  = (a, r) => $"(Convert.ToBoolean({r(a[0])}) && Convert.ToBoolean({r(a[1])}))";
        registry.TranspilerEmitters["or"]   = (a, r) => $"(Convert.ToBoolean({r(a[0])}) || Convert.ToBoolean({r(a[1])}))";
    }
}
