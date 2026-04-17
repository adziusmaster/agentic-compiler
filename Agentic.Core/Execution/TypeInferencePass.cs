using System;
using System.Collections.Generic;
using Agentic.Core.Runtime;
using Agentic.Core.Syntax;

namespace Agentic.Core.Execution;

// First compilation pass: walks the AST once before code generation to determine
// the type of each defined variable and user function.
//
// The inferred types are stored in a centralised environment so the Transpiler
// and future passes (type-checking, records, higher-order functions) can consult
// a single source of truth instead of sprinkling ad-hoc predicates.
internal sealed class TypeInferencePass
{
    private readonly Dictionary<string, AgType> _varTypes = new();
    private readonly Dictionary<string, FuncType> _funcTypes = new();

    // Shared with the Verifier's runtime registry in spirit but populated independently —
    // the Transpiler needs the set of defstructs ahead of time to hoist their C# declarations.
    public TypeRegistry Structs { get; } = new();

    // ── Public query API ──────────────────────────────────────────────────────

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

    // ── Inference walk ────────────────────────────────────────────────────────

    private void Walk(AstNode node)
    {
        if (node is not ListNode list || list.Elements.Count == 0) return;
        var op = (list.Elements[0] as AtomNode)?.Token.Value;

        switch (op)
        {
            case "def":
            case "set":
                if (list.Elements.Count >= 3 && list.Elements[1] is AtomNode nameAtom)
                {
                    var name = Sanitize(nameAtom.Token.Value);
                    var inferred = InferExpression(list.Elements[2]);
                    // (set …) should not downgrade an already-known array/string back to Unknown
                    if (inferred is not UnknownType || !_varTypes.ContainsKey(name))
                        _varTypes[name] = inferred;
                }
                break;

            case "defun":
                if (list.Elements.Count >= 4 &&
                    list.Elements[1] is AtomNode fnName &&
                    list.Elements[2] is ListNode paramList)
                {
                    var paramTypes = new List<AgType>(paramList.Elements.Count);
                    foreach (var _ in paramList.Elements) paramTypes.Add(AgType.Num);
                    // Return type is currently assumed numeric (Transpiler emits `double`).
                    // A future pass can tighten this by walking the body for (return ...).
                    _funcTypes[Sanitize(fnName.Token.Value)] = new FuncType(paramTypes, AgType.Num);
                }
                break;

            case "defstruct":
                if (list.Elements.Count >= 3 &&
                    list.Elements[1] is AtomNode structName &&
                    list.Elements[2] is ListNode structFields)
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

    // Infers the static type of an expression node without executing it.
    // Safe to call after Scan(); uses the populated variable/function environments.
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
            "str.concat"        => AgType.Str,
            "str.from_num"      => AgType.Str,
            "sys.input.get_str" => AgType.Str,
            "sys.input.get"     => AgType.Num,
            "str.length"        => AgType.Num,
            "str.to_num"        => AgType.Num,
            "str.eq"            => AgType.Bool,
            "+" or "-" or "*" or "/"                => AgType.Num,
            "<" or ">" or "=" or "<=" or ">="       => AgType.Bool,
            "not" or "and" or "or"                  => AgType.Bool,
            _ when op is not null && Structs.TryResolveOp(op, out var tn, out var member) =>
                InferStructOp(tn, member),
            _ when op is not null && _funcTypes.TryGetValue(Sanitize(op), out var fn) => fn.Return,
            _ => AgType.Unknown
        };
    }

    private AgType InferStructOp(string typeName, string member)
    {
        if (!Structs.TryGet(typeName, out var t)) return AgType.Unknown;
        if (member == "new") return t;                    // constructor yields the struct
        if (member.StartsWith("set-")) return t;          // wither yields the same struct
        foreach (var f in t.Fields)
            if (f.Field == member) return f.Type;         // field read yields the field's type
        return AgType.Unknown;
    }

    internal static string Sanitize(string name) => name.Replace("-", "_");
}
