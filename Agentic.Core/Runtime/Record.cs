using System.Collections.Generic;
using System.Linq;

namespace Agentic.Core.Runtime;

// Runtime value for an Agentic (defstruct ...) instance.
//
// Records are immutable from the LLM-authored program's point of view: (Foo.set-x p v)
// returns a new Record — the original is untouched. The underlying dictionary is treated
// as read-only after construction; With(...) copies before mutating.
internal sealed class Record
{
    public string TypeName { get; }
    private readonly Dictionary<string, object> _fields;

    public Record(string typeName, IEnumerable<KeyValuePair<string, object>> fields)
    {
        TypeName = typeName;
        _fields = new Dictionary<string, object>(fields);
    }

    public object Get(string field) =>
        _fields.TryGetValue(field, out var v)
            ? v
            : throw new System.InvalidOperationException(
                $"Field '{field}' not found on record of type '{TypeName}'.");

    public Record With(string field, object value)
    {
        if (!_fields.ContainsKey(field))
            throw new System.InvalidOperationException(
                $"Cannot set unknown field '{field}' on record of type '{TypeName}'.");
        var copy = new Dictionary<string, object>(_fields) { [field] = value };
        return new Record(TypeName, copy);
    }

    public override string ToString() =>
        $"{TypeName}({string.Join(", ", _fields.Select(kv => $"{kv.Key}={kv.Value}"))})";
}
