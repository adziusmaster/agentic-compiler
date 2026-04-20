using Agentic.Core.Syntax;

namespace Agentic.Core.Capabilities;

/// <summary>
/// Declaration of a host capability exposed to Agentic programs via
/// <c>(extern defun name ((a : T)) : R @capability "name")</c>.
/// Capabilities are the only mechanism by which LLM-authored DSL code
/// can reach outside the language runtime.
/// </summary>
public sealed record Capability(
    string Name,
    IReadOnlyList<AgType> ParamTypes,
    AgType ReturnType,
    string Permission,
    Func<object[], object?> Adapter,
    string CSharpEmitExpr);

/// <summary>
/// Registry of declared capabilities. Populated at startup with trusted
/// built-ins (from <c>Stdlib</c>) and, optionally, with user-supplied
/// capability manifests. At verify time, every <c>extern defun</c> call
/// is routed through this registry; unregistered names error.
/// </summary>
public sealed class CapabilityRegistry
{
    private readonly Dictionary<string, Capability> _capabilities = new(StringComparer.Ordinal);

    public void Register(Capability capability) =>
        _capabilities[capability.Name] = capability;

    public bool TryGet(string name, out Capability capability) =>
        _capabilities.TryGetValue(name, out capability!);

    public IEnumerable<Capability> All => _capabilities.Values;

    public bool Contains(string name) => _capabilities.ContainsKey(name);
}
