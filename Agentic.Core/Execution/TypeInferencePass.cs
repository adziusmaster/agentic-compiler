using Agentic.Core.Runtime;
using Agentic.Core.Syntax;

namespace Agentic.Core.Execution;

/// <summary>
/// First compilation pass: walks the AST once before code generation to determine
/// the type of each defined variable and user function.
/// </summary>
/// <remarks>
/// Supports both inferred types (from RHS expressions) and explicit type annotations
/// via <c>(def x : Num 5)</c> and <c>(defun f ((x : Num)) : Num body)</c> syntax.
/// </remarks>
internal sealed class TypeInferencePass
{
    private readonly Dictionary<string, AgType> _varTypes = new();
    private readonly Dictionary<string, FuncType> _funcTypes = new();

    /// <summary>
    /// Registry of <c>(defstruct …)</c> declarations, shared with the Transpiler
    /// for C# record-struct hoisting.
    /// </summary>
    public TypeRegistry Structs { get; } = new();

    /// <summary>
    /// Walks the full AST, populating the variable and function type environments.
    /// </summary>
    public void Scan(AstNode root)
    {
        _varTypes.Clear();
        _funcTypes.Clear();
        Walk(root);
    }

    public AgType GetVarType(string sanitizedName) =>
        _varTypes.TryGetValue(sanitizedName, out var t) ? t : AgType.Unknown;

    public bool IsArrayVar(string sanitizedName) =>
        _varTypes.TryGetValue(sanitizedName, out var t) && t is ArrayType;

    public bool IsStringVar(string sanitizedName) =>
        _varTypes.TryGetValue(sanitizedName, out var t) && t is StrType;

    public bool TryGetFuncType(string sanitizedName, out FuncType type) =>
        _funcTypes.TryGetValue(sanitizedName, out type!);

    private void Walk(AstNode node)
    {
        if (node is not ListNode list || list.Elements.Count == 0) return;
        var op = (list.Elements[0] as AtomNode)?.Token.Value;

        switch (op)
        {
            case "def":
                if (list.Elements.Count >= 3 && list.Elements[1] is AtomNode defAtom)
                {
                    var (rawName, explicitType, valueNode) = TypeAnnotations.ParseDef(list);
                    var name = Sanitize(rawName);
                    var resolved = explicitType ?? InferExpression(valueNode);
                    if (resolved is not UnknownType || !_varTypes.ContainsKey(name))
                        _varTypes[name] = resolved;
                }
                break;

            case "set":
                if (list.Elements.Count >= 3 && list.Elements[1] is AtomNode setAtom)
                {
                    var name = Sanitize(setAtom.Token.Value);
                    var inferred = InferExpression(list.Elements[2]);
                    if (inferred is not UnknownType || !_varTypes.ContainsKey(name))
                        _varTypes[name] = inferred;
                }
                break;

            case "defun":
                if (list.Elements.Count >= 4 && list.Elements[1] is AtomNode)
                {
                    var sig = TypeAnnotations.ParseDefun(list);
                    _funcTypes[Sanitize(sig.Name)] =
                        new FuncType(sig.Parameters.Select(p => p.Type).ToList(), sig.ReturnType);
                }
                break;

            case "defstruct":
                if (list.Elements.Count >= 3
                    && list.Elements[1] is AtomNode structName
                    && list.Elements[2] is ListNode structFields)
                {
                    var fields = new List<(string, AgType)>(structFields.Elements.Count);
                    foreach (var f in structFields.Elements)
                        if (f is AtomNode fa) fields.Add((fa.Token.Value, AgType.Num));
                    Structs.Register(new StructType(structName.Token.Value, fields));
                }
                break;
        }

        foreach (var child in list.Elements) Walk(child);
    }

    /// <summary>
    /// Infers the static type of an expression node without executing it.
    /// </summary>
    public AgType InferExpression(AstNode node)
    {
        if (node is AtomNode atom)
        {
            return atom.Token.Type switch
            {
                TokenType.String => AgType.Str,
                TokenType.Number => AgType.Num,
                TokenType.Identifier =>
                    _varTypes.TryGetValue(Sanitize(atom.Token.Value), out var t) ? t : AgType.Unknown,
                _ => AgType.Unknown
            };
        }

        if (node is not ListNode l || l.Elements.Count == 0) return AgType.Unknown;
        var op = (l.Elements[0] as AtomNode)?.Token.Value;

        return op switch
        {
            "arr.new"           => AgType.ArrayOf(AgType.Num),
            "arr.get"           => AgType.Num,
            "arr.length"        => AgType.Num,
            "arr.map"           => AgType.ArrayOf(AgType.Num),
            "arr.filter"        => AgType.ArrayOf(AgType.Num),
            "arr.reduce"        => AgType.Num,
            "map.new"           => AgType.MapOf(AgType.Unknown),
            "map.get"           => AgType.Unknown,
            "map.has"           => AgType.Num,
            "map.remove"        => AgType.Num,
            "map.keys"          => AgType.ArrayOf(AgType.Str),
            "map.size"          => AgType.Num,
            "env.get"           => AgType.Str,
            "env.get_or"        => AgType.Str,
            "str.concat"        => AgType.Str,
            "str.from_num"      => AgType.Str,
            "str.substring"     => AgType.Str,
            "str.trim"          => AgType.Str,
            "str.upper"         => AgType.Str,
            "str.lower"         => AgType.Str,
            "str.replace"       => AgType.Str,
            "str.join"          => AgType.Str,
            "str.split"         => AgType.ArrayOf(AgType.Str),
            "sys.input.get_str" => AgType.Str,
            "sys.input.get"     => AgType.Num,
            "str.length"        => AgType.Num,
            "str.to_num"        => AgType.Num,
            "str.index_of"      => AgType.Num,
            "str.contains"      => AgType.Num,
            "str.eq"            => AgType.Bool,
            "+" or "-" or "*" or "/"                => AgType.Num,
            "<" or ">" or "=" or "<=" or ">="       => AgType.Bool,
            "not" or "and" or "or"                  => AgType.Bool,
            "assert-eq" or "assert-true" or "assert-near" => AgType.Bool,
            "require" or "ensure"                   => AgType.Bool,
            _ when op is not null && Structs.TryResolveOp(op, out var tn, out var member) =>
                InferStructOp(tn, member),
            _ when op is not null && _funcTypes.TryGetValue(Sanitize(op), out var fn) => fn.Return,
            _ => AgType.Unknown
        };
    }

    private AgType InferStructOp(string typeName, string member)
    {
        if (!Structs.TryGet(typeName, out var t)) return AgType.Unknown;
        if (member == "new") return t;
        if (member.StartsWith("set-")) return t;
        foreach (var f in t.Fields)
            if (f.Field == member) return f.Type;
        return AgType.Unknown;
    }

    internal static string Sanitize(string name)
    {
        var sanitized = name.Replace("-", "_");
        return sanitized switch
        {
            "out" or "in" or "ref" or "base" or "class" or "event" or "object"
            or "string" or "double" or "int" or "bool" or "var" or "void"
            or "new" or "return" or "if" or "else" or "while" or "for"
            or "do" or "lock" or "checked" or "unchecked" or "fixed"
            => "@" + sanitized,
            _ => sanitized
        };
    }
}
