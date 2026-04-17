namespace Agentic.Core.Stdlib;

/// <summary>
/// String operations: concatenation, conversion, comparison, searching,
/// transformation, splitting, and joining.
/// </summary>
public sealed class StringModule : IStdlibModule
{
    public void Register(StdlibRegistry registry)
    {
        // --- existing ops ---
        registry.VerifierFuncs["str.concat"]   = a => S(a[0]) + S(a[1]);
        registry.VerifierFuncs["str.length"]   = a => (double)(S(a[0]).Length);
        registry.VerifierFuncs["str.from_num"] = a => D(a[0]).ToString();
        registry.VerifierFuncs["str.to_num"]   = a => D(a[0]);
        registry.VerifierFuncs["str.eq"]       = a => S(a[0]) == S(a[1]);

        registry.TranspilerEmitters["str.concat"]   = (a, r) => $"({r(a[0])} + {r(a[1])})";
        registry.TranspilerEmitters["str.length"]   = (a, r) => $"((double){r(a[0])}.Length)";
        registry.TranspilerEmitters["str.from_num"] = (a, r) => $"(({r(a[0])}).ToString())";
        registry.TranspilerEmitters["str.to_num"]   = (a, r) => $"Convert.ToDouble({r(a[0])})";
        registry.TranspilerEmitters["str.eq"]       = (a, r) => $"({r(a[0])} == {r(a[1])})";

        // --- new: searching ---
        registry.VerifierFuncs["str.contains"] = a => S(a[0]).Contains(S(a[1])) ? 1.0 : 0.0;
        registry.VerifierFuncs["str.index_of"] = a => (double)S(a[0]).IndexOf(S(a[1]));

        registry.TranspilerEmitters["str.contains"] = (a, r) => $"({r(a[0])}.Contains({r(a[1])}) ? 1.0 : 0.0)";
        registry.TranspilerEmitters["str.index_of"] = (a, r) => $"((double){r(a[0])}.IndexOf({r(a[1])}))";

        // --- new: extraction ---
        registry.VerifierFuncs["str.substring"] = a =>
        {
            string s = S(a[0]);
            int start = (int)D(a[1]);
            int len = (int)D(a[2]);
            return s.Substring(start, Math.Min(len, s.Length - start));
        };

        registry.TranspilerEmitters["str.substring"] = (a, r) =>
            $"{r(a[0])}.Substring((int)({r(a[1])}), Math.Min((int)({r(a[2])}), {r(a[0])}.Length - (int)({r(a[1])})))";

        // --- new: transformation ---
        registry.VerifierFuncs["str.trim"]    = a => S(a[0]).Trim();
        registry.VerifierFuncs["str.upper"]   = a => S(a[0]).ToUpperInvariant();
        registry.VerifierFuncs["str.lower"]   = a => S(a[0]).ToLowerInvariant();
        registry.VerifierFuncs["str.replace"] = a => S(a[0]).Replace(S(a[1]), S(a[2]));

        registry.TranspilerEmitters["str.trim"]    = (a, r) => $"{r(a[0])}.Trim()";
        registry.TranspilerEmitters["str.upper"]   = (a, r) => $"{r(a[0])}.ToUpperInvariant()";
        registry.TranspilerEmitters["str.lower"]   = (a, r) => $"{r(a[0])}.ToLowerInvariant()";
        registry.TranspilerEmitters["str.replace"] = (a, r) => $"{r(a[0])}.Replace({r(a[1])}, {r(a[2])})";

        // --- new: split / join ---
        registry.VerifierFuncs["str.split"] = a =>
        {
            var sep = S(a[1]);
            if (sep.Length == 0) throw new InvalidOperationException("str.split: separator cannot be empty.");
            return S(a[0]).Split(sep);
        };
        registry.VerifierFuncs["str.join"]  = a =>
        {
            var arr = (string[])a[0];
            return string.Join(S(a[1]), arr);
        };

        registry.TranspilerEmitters["str.split"] = (a, r) => $"{r(a[0])}.Split({r(a[1])})";
        registry.TranspilerEmitters["str.join"]  = (a, r) => $"string.Join({r(a[1])}, {r(a[0])})";
    }

    private static string S(object o) => Convert.ToString(o) ?? "";
    private static double D(object o) => Convert.ToDouble(o);
}
