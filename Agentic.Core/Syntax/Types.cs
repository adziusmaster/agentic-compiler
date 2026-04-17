using System.Collections.Generic;
using System.Linq;

namespace Agentic.Core.Syntax;

// The Agentic type lattice. Used by TypeInferencePass to produce a type
// environment, and by the Transpiler to emit correctly-typed C#.
//
// Keep this file free of language-semantic logic — this is a pure data model.
public abstract record AgType
{
    public static readonly NumType Num = new();
    public static readonly StrType Str = new();
    public static readonly BoolType Bool = new();
    public static readonly UnknownType Unknown = new();

    public static ArrayType ArrayOf(AgType element) => new(element);
}

public sealed record NumType : AgType
{
    public override string ToString() => "num";
}

public sealed record StrType : AgType
{
    public override string ToString() => "str";
}

public sealed record BoolType : AgType
{
    public override string ToString() => "bool";
}

public sealed record ArrayType(AgType Element) : AgType
{
    public override string ToString() => $"array<{Element}>";
}

// Reserved for Stage 1C (records) and a later higher-order-functions pass.
// Declared now so TypeInferencePass doesn't need to change shape later.
public sealed record StructType(string Name, IReadOnlyList<(string Field, AgType Type)> Fields) : AgType
{
    public override string ToString() => $"struct {Name}";

    public bool Equals(StructType? other) =>
        other is not null && Name == other.Name && Fields.SequenceEqual(other.Fields);

    public override int GetHashCode() => Name.GetHashCode();
}

public sealed record FuncType(IReadOnlyList<AgType> Params, AgType Return) : AgType
{
    public override string ToString() =>
        $"({string.Join(", ", Params)}) -> {Return}";

    public bool Equals(FuncType? other) =>
        other is not null && Return == other.Return && Params.SequenceEqual(other.Params);

    public override int GetHashCode() => Return.GetHashCode();
}

// Used when inference cannot determine a type (e.g. a reference before definition,
// or an identifier fallback). Treated as "don't assert anything" downstream.
public sealed record UnknownType : AgType
{
    public override string ToString() => "?";
}
