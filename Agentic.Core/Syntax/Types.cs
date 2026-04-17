namespace Agentic.Core.Syntax;

/// <summary>
/// Base of the Agentic type lattice. Pure data model consumed by type inference,
/// verification, and code generation.
/// </summary>
public abstract record AgType
{
    public static readonly NumType Num = new();
    public static readonly StrType Str = new();
    public static readonly BoolType Bool = new();
    public static readonly UnknownType Unknown = new();

    public static ArrayType ArrayOf(AgType element) => new(element);

    /// <summary>
    /// Maps an <see cref="AgType"/> to its C# source representation.
    /// </summary>
    public static string ToCSharp(AgType type) => type switch
    {
        NumType => "double",
        StrType => "string",
        BoolType => "bool",
        ArrayType { Element: NumType } => "double[]",
        ArrayType { Element: StrType } => "string[]",
        ArrayType => "object[]",
        StructType s => s.Name,
        _ => "var"
    };
}

/// <summary>Numeric type (C# <c>double</c>).</summary>
public sealed record NumType : AgType
{
    public override string ToString() => "Num";
}

/// <summary>String type (C# <c>string</c>).</summary>
public sealed record StrType : AgType
{
    public override string ToString() => "Str";
}

/// <summary>Boolean type (C# <c>bool</c>).</summary>
public sealed record BoolType : AgType
{
    public override string ToString() => "Bool";
}

/// <summary>Homogeneous array type parameterized by element type.</summary>
public sealed record ArrayType(AgType Element) : AgType
{
    public override string ToString() => $"(Array {Element})";
}

/// <summary>User-declared record type via <c>(defstruct Name (fields…))</c>.</summary>
public sealed record StructType(string Name, IReadOnlyList<(string Field, AgType Type)> Fields) : AgType
{
    public override string ToString() => Name;

    public bool Equals(StructType? other) =>
        other is not null && Name == other.Name && Fields.SequenceEqual(other.Fields);

    public override int GetHashCode() => Name.GetHashCode();
}

/// <summary>Function type with parameter types and return type.</summary>
public sealed record FuncType(IReadOnlyList<AgType> Params, AgType Return) : AgType
{
    public override string ToString() =>
        $"({string.Join(" ", Params)}) -> {Return}";

    public bool Equals(FuncType? other) =>
        other is not null && Return == other.Return && Params.SequenceEqual(other.Params);

    public override int GetHashCode() => Return.GetHashCode();
}

/// <summary>
/// Placeholder when inference cannot determine a type. Treated as
/// "don't assert anything" by downstream passes.
/// </summary>
public sealed record UnknownType : AgType
{
    public override string ToString() => "?";
}
