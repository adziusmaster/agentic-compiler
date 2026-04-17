namespace Agentic.Core.Stdlib;

/// <summary>
/// HashMap (dictionary) operations. Keys are always strings.
/// Values can be Num or Str depending on usage.
/// </summary>
public sealed class HashMapModule : IStdlibModule
{
    private int _tempVarCounter;

    public void Register(StdlibRegistry registry)
    {
        // --- verifier ---
        registry.VerifierFuncs["map.new"] = _ => new Dictionary<string, object>();

        registry.VerifierFuncs["map.get"] = a =>
        {
            var map = (Dictionary<string, object>)a[0];
            var key = Convert.ToString(a[1])!;
            return map.TryGetValue(key, out var val) ? val : 0.0;
        };

        registry.VerifierFuncs["map.set"] = a =>
        {
            var map = (Dictionary<string, object>)a[0];
            var key = Convert.ToString(a[1])!;
            map[key] = a[2];
            return a[2];
        };

        registry.VerifierFuncs["map.has"] = a =>
        {
            var map = (Dictionary<string, object>)a[0];
            var key = Convert.ToString(a[1])!;
            return map.ContainsKey(key) ? 1.0 : 0.0;
        };

        registry.VerifierFuncs["map.remove"] = a =>
        {
            var map = (Dictionary<string, object>)a[0];
            var key = Convert.ToString(a[1])!;
            return map.Remove(key) ? 1.0 : 0.0;
        };

        registry.VerifierFuncs["map.keys"] = a =>
        {
            var map = (Dictionary<string, object>)a[0];
            return map.Keys.ToArray();
        };

        registry.VerifierFuncs["map.size"] = a =>
        {
            var map = (Dictionary<string, object>)a[0];
            return (double)map.Count;
        };

        // --- transpiler ---
        registry.TranspilerEmitters["map.new"] = (_, _) =>
            "new Dictionary<string, object>()";

        registry.TranspilerEmitters["map.get"] = (a, r) =>
        {
            var varName = $"_mv{_tempVarCounter++}";
            return $"({r(a[0])}.TryGetValue({r(a[1])}, out var {varName}) ? {varName} : (object)0.0)";
        };

        registry.TranspilerEmitters["map.set"] = (a, r) =>
        {
            var map = r(a[0]);
            var key = r(a[1]);
            var val = r(a[2]);
            return $"{map}[{key}] = {val}";
        };

        registry.TranspilerEmitters["map.has"] = (a, r) =>
            $"({r(a[0])}.ContainsKey({r(a[1])}) ? 1.0 : 0.0)";

        registry.TranspilerEmitters["map.remove"] = (a, r) =>
            $"({r(a[0])}.Remove({r(a[1])}) ? 1.0 : 0.0)";

        registry.TranspilerEmitters["map.keys"] = (a, r) =>
            $"{r(a[0])}.Keys.ToArray()";

        registry.TranspilerEmitters["map.size"] = (a, r) =>
            $"((double){r(a[0])}.Count)";
    }
}
