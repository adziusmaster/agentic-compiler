namespace Agentic.Core.Stdlib;

/// <summary>
/// Central registry of all stdlib modules. Add new modules here;
/// the Verifier and Transpiler pick them up automatically.
/// </summary>
public static class StdlibModules
{
    private static IReadOnlyList<IStdlibModule> CreateModules() =>
    [
        new MathModule(),
        new StringModule(),
        new BoolModule(),
        new FileModule(),
        new HttpModule(),
        new JsonModule(),
        new ServerModule(),
        new HashMapModule(),
        new EnvModule(),
        new DbModule(),
    ];

    /// <summary>Shared module list for read-only use (verifier). Do NOT use for transpiler.</summary>
    public static IReadOnlyList<IStdlibModule> All { get; } = CreateModules();

    /// <summary>Creates a fresh registry with new module instances (resets transpiler state like counters).</summary>
    public static StdlibRegistry Build()
    {
        var registry = new StdlibRegistry();
        foreach (var module in CreateModules())
            module.Register(registry);
        return registry;
    }
}
