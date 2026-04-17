using System.Collections.Generic;
using Agentic.Core.Syntax;

namespace Agentic.Core.Runtime;

// Registry of user-declared (defstruct ...) types. Shared between the Verifier
// (runtime dispatch for constructors/accessors) and the Transpiler (ahead-of-time
// hoisting of C# record struct declarations).
internal sealed class TypeRegistry
{
    private readonly Dictionary<string, StructType> _structs = new();

    public IReadOnlyCollection<StructType> All => _structs.Values;

    public void Register(StructType type) => _structs[type.Name] = type;

    public bool TryGet(string name, out StructType type) =>
        _structs.TryGetValue(name, out type!);

    // Splits an op like "Point.x" or "Point.set-x" into (type, member).
    // Returns false if the type isn't registered.
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
