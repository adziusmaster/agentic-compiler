namespace Agentic.Core.Stdlib;

/// <summary>Math operations and LLM-hallucinated C# stdlib aliases.</summary>
public sealed class MathModule : IStdlibModule
{
    private static readonly Random _rng = new();

    public void Register(StdlibRegistry registry)
    {
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
        registry.VerifierFuncs["math.random"] = _ => _rng.NextDouble();

        registry.VerifierFuncs["double.Parse"] = a => Convert.ToDouble(a[0]);
        registry.VerifierFuncs["double.parse"] = a => Convert.ToDouble(a[0]);
        registry.VerifierFuncs["int.Parse"]    = a => (double)Convert.ToInt32(a[0]);
        registry.VerifierFuncs["int.parse"]    = a => (double)Convert.ToInt32(a[0]);

        registry.TranspilerEmitters["math.mod"] = (a, r) => $"({r(a[0])} % {r(a[1])})";
        registry.TranspilerEmitters["math.random"] = (_, _) => "new Random().NextDouble()";
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
