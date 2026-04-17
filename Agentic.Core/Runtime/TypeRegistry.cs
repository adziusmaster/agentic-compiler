using Agentic.Core.Syntax;

namespace Agentic.Core.Runtime;

/// <summary>
/// Registry of user-declared <c>(defstruct …)</c> types, shared between
/// the Verifier (runtime dispatch) and the Transpiler (C# record-struct hoisting).
/// </summary>
internal sealed class TypeRegistry
{
    private readonly Dictionary<string, StructType> _structs = new();

    public IReadOnlyCollection<StructType> All => _structs.Values;

    public void Register(StructType type) => _structs[type.Name] = type;

    public bool TryGet(string name, out StructType type) =>
        _structs.TryGetValue(name, out type!);

    /// <summary>
    /// Splits an op like <c>"Point.x"</c> or <c>"Point.set-x"</c> into (type, member).
    /// Returns <c>false</c> if the type isn't registered.
    /// </summary>
    public bool TryResolveOp(string op, out string typeName, out string member)
    {
        int dot = op.IndexOf('.');
        if (dot > 0)
        {
            typeName = op[..dot];
            member = op[(dot + 1)..];
            if (_structs.ContainsKey(typeName)) return true;
        }
        typeName = string.Empty;
        member = string.Empty;
        return false;
    }
}
