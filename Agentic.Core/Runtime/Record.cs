namespace Agentic.Core.Runtime;

/// <summary>
/// Immutable runtime value for an Agentic <c>(defstruct …)</c> instance.
/// <c>With()</c> returns a copy — the original is never mutated.
/// </summary>
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

    /// <summary>Fields in insertion (i.e. declaration) order, for canonical serialization.</summary>
    public IEnumerable<KeyValuePair<string, object>> EnumerateFields() => _fields;

    /// <summary>Returns a copy with a single field replaced (wither pattern).</summary>
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
