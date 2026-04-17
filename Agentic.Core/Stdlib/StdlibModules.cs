namespace Agentic.Core.Stdlib;

/// <summary>
/// Central registry of all stdlib modules. Add new modules here;
/// the Verifier and Transpiler pick them up automatically.
/// </summary>
public static class StdlibModules
{
    public static IReadOnlyList<IStdlibModule> All { get; } =
    [
        new MathModule(),
        new StringModule(),
        new BoolModule(),
        new FileModule(),
        new HttpModule(),
        new JsonModule(),
        new ServerModule(),
    ];

    public static StdlibRegistry Build()
    {
        var registry = new StdlibRegistry();
        foreach (var module in All)
            module.Register(registry);
        return registry;
    }
}
