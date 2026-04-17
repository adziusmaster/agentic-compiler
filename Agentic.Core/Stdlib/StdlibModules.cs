using System.Collections.Generic;

namespace Agentic.Core.Stdlib;

// Single source of truth for all stdlib modules.
// Add new modules here; Verifier and Transpiler pick them up automatically.
public static class StdlibModules
{
    public static IReadOnlyList<IStdlibModule> All { get; } =
    [
        new MathModule(),
        new StringModule(),
        new BoolModule(),
    ];

    public static StdlibRegistry Build()
    {
        var registry = new StdlibRegistry();
        foreach (var module in All)
            module.Register(registry);
        return registry;
    }
}
