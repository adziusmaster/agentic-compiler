namespace Agentic.Core.Syntax;

/// <summary>
/// Parsed representation of a <c>(defun name (params) body)</c> form,
/// supporting both untyped and explicitly-typed parameter/return annotations.
/// </summary>
public sealed record DefunSignature(
    string Name,
    IReadOnlyList<(string Param, AgType Type)> Parameters,
    AgType ReturnType,
    AstNode Body);

/// <summary>
/// Utilities for extracting explicit type annotations from Agentic AST nodes.
/// </summary>
public static class TypeAnnotations
{
    /// <summary>
    /// Parses a type annotation node into an <see cref="AgType"/>.
    /// </summary>
    /// <remarks>
    /// Recognizes:
    /// <list type="bullet">
    ///   <item><c>Num</c>, <c>Str</c>, <c>Bool</c> — primitive types.</item>
    ///   <item><c>(Array T)</c> — parameterized array type.</item>
    /// </list>
    /// Returns <see cref="UnknownType"/> for unrecognized annotations.
    /// </remarks>
    public static AgType ParseAnnotation(AstNode node)
    {
        if (node is AtomNode atom)
        {
            return atom.Token.Value switch
            {
                "Num" => AgType.Num,
                "Str" => AgType.Str,
                "Bool" => AgType.Bool,
                // User-defined struct types are referred to by bare name in annotations,
                // e.g. `(defun midpoint ((a : Point) (b : Point)) : Point …)`. We emit a
                // shallow StructType here; the full field list is resolved via TypeRegistry
                // at use sites. The name is enough for code generation.
                var s when s.Length > 0 && char.IsUpper(s[0]) =>
                    new StructType(s, System.Array.Empty<(string, AgType)>()),
                _ => AgType.Unknown
            };
        }

        if (node is ListNode list
            && list.Elements.Count >= 2
            && list.Elements[0] is AtomNode head)
        {
            return head.Token.Value switch
            {
                "Array" => AgType.ArrayOf(ParseAnnotation(list.Elements[1])),
                "Map" when list.Elements.Count >= 3 => AgType.MapOf(ParseAnnotation(list.Elements[2])),
                // (Func T1 T2 … R) — last element is the return type, preceding are params.
                // (Func R) is a thunk (no params).
                "Func" => ParseFuncAnnotation(list),
                _ => AgType.Unknown
            };
        }

        return AgType.Unknown;
    }

    private static FuncType ParseFuncAnnotation(ListNode list)
    {
        // list.Elements[0] is "Func" atom; remaining are param types followed by return type.
        int n = list.Elements.Count;
        if (n < 2) return new FuncType(Array.Empty<AgType>(), AgType.Unknown);

        var retType = ParseAnnotation(list.Elements[n - 1]);
        var paramTypes = new List<AgType>(n - 2);
        for (int i = 1; i < n - 1; i++)
            paramTypes.Add(ParseAnnotation(list.Elements[i]));

        return new FuncType(paramTypes, retType);
    }

    /// <summary>
    /// Extracts the signature from a <c>(defun ...)</c> AST node.
    /// </summary>
    /// <remarks>
    /// Supports three syntaxes (LLM-friendly progressive compression):
    /// <list type="bullet">
    ///   <item><c>(defun name ((p1 : T1) (p2 : T2)) : RetT body)</c> — verbose typed.</item>
    ///   <item><c>(defun name ((p1 T1) (p2 T2)) RetT body)</c> — colon-less typed.</item>
    ///   <item><c>(defun name (p1 p2) body)</c> — untyped; defaults to Num/Num.</item>
    /// </list>
    /// </remarks>
    public static DefunSignature ParseDefun(ListNode defunList)
    {
        string name = ((AtomNode)defunList.Elements[1]).Token.Value;
        var paramList = (ListNode)defunList.Elements[2];
        var parameters = ParseParameters(paramList);

        AgType returnType;
        int bodyStart;

        if (defunList.Elements.Count >= 6
            && defunList.Elements[3] is AtomNode { Token.Value: ":" })
        {
            returnType = ParseAnnotation(defunList.Elements[4]);
            bodyStart = 5;
        }
        else if (defunList.Elements.Count >= 5
                 && IsTypeNode(defunList.Elements[3]))
        {
            // Colon-less return type: (defun name (args) Num body...)
            returnType = ParseAnnotation(defunList.Elements[3]);
            bodyStart = 4;
        }
        else
        {
            returnType = AgType.Num;
            bodyStart = 3;
        }

        var body = WrapBody(defunList.Elements, bodyStart);
        return new DefunSignature(name, parameters, returnType, body);
    }

    /// <summary>
    /// Returns true when the node names a type (Num/Str/Bool, a capitalised
    /// struct name, or a type constructor like (Array T)/(Map T)/(Func ... R)).
    /// Used to disambiguate a trailing type annotation from a body expression.
    /// </summary>
    private static bool IsTypeNode(AstNode node)
    {
        if (node is AtomNode atom)
        {
            string v = atom.Token.Value;
            if (v is "Num" or "Str" or "Bool") return true;
            return v.Length > 0 && char.IsUpper(v[0]);
        }
        if (node is ListNode list && list.Elements.Count > 0
            && list.Elements[0] is AtomNode head)
        {
            return head.Token.Value is "Array" or "Map" or "Func";
        }
        return false;
    }

    /// <summary>
    /// Parses an <c>(extern defun name ((p : T)) : R @capability "cap.name")</c> form.
    /// Returns the capability name plus the signature. Caller validates that the
    /// capability is registered.
    /// </summary>
    public static (DefunSignature Signature, string Capability) ParseExternDefun(ListNode externList)
    {
        // (extern defun name (params) : RetType @capability "cap.name")
        // Re-slice as (defun name (params) : RetType) for ParseDefun, then pull the capability.
        var defunElems = new List<AstNode> {
            new AtomNode(new Token(TokenType.Identifier, "defun", 0, 0))
        };
        string? capability = null;

        for (int i = 2; i < externList.Elements.Count; i++)
        {
            var el = externList.Elements[i];
            if (el is AtomNode { Token.Value: "@capability" } && i + 1 < externList.Elements.Count
                && externList.Elements[i + 1] is AtomNode capAtom)
            {
                capability = capAtom.Token.Value;
                break;
            }
            defunElems.Add(el);
        }

        if (capability is null)
            throw new InvalidOperationException(
                "extern defun requires '@capability \"name\"' trailing declaration.");

        // extern has no body; supply a placeholder for ParseDefun's WrapBody.
        defunElems.Add(new AtomNode(new Token(TokenType.Number, "0", 0, 0)));

        var sig = ParseDefun(new ListNode(defunElems));
        return (sig, capability);
    }

    /// <summary>
    /// Extracts the name, optional explicit type, and value expression from a <c>(def ...)</c> form.
    /// </summary>
    /// <remarks>
    /// Supports:
    /// <list type="bullet">
    ///   <item><c>(def x 5)</c> — untyped.</item>
    ///   <item><c>(def x : Num 5)</c> — explicitly typed.</item>
    /// </list>
    /// </remarks>
    public static (string Name, AgType? ExplicitType, AstNode Value) ParseDef(ListNode defList)
    {
        string name = ((AtomNode)defList.Elements[1]).Token.Value;

        if (defList.Elements.Count >= 5
            && defList.Elements[2] is AtomNode { Token.Value: ":" })
        {
            var type = ParseAnnotation(defList.Elements[3]);
            return (name, type, defList.Elements[4]);
        }

        // Colon-less typed: (def x Num 5) — exactly 4 elements, 3rd is a type.
        if (defList.Elements.Count == 4 && IsTypeNode(defList.Elements[2]))
        {
            var type = ParseAnnotation(defList.Elements[2]);
            return (name, type, defList.Elements[3]);
        }

        return (name, null, defList.Elements[2]);
    }

    /// <summary>
    /// Parses a struct field list, accepting either <c>field_name</c> (defaults to
    /// <see cref="NumType"/>) or a typed <c>(field_name : Type)</c> pair.
    /// </summary>
    public static IReadOnlyList<(string Field, AgType Type)> ParseStructFields(ListNode fieldList)
    {
        var result = new List<(string, AgType)>(fieldList.Elements.Count);

        foreach (var element in fieldList.Elements)
        {
            if (element is AtomNode atom)
            {
                result.Add((atom.Token.Value, AgType.Num));
            }
            else if (element is ListNode typed
                     && typed.Elements.Count >= 3
                     && typed.Elements[0] is AtomNode nameAtom
                     && typed.Elements[1] is AtomNode { Token.Value: ":" })
            {
                result.Add((nameAtom.Token.Value, ParseAnnotation(typed.Elements[2])));
            }
        }

        return result;
    }

    /// <summary>
    /// Parses a parameter list. Accepts (in decreasing verbosity):
    /// <list type="bullet">
    ///   <item><c>(p : Num)</c> — explicit typed with colon.</item>
    ///   <item><c>(p Num)</c> — colon-less typed shorthand.</item>
    ///   <item><c>p</c> — untyped; defaults to Num.</item>
    /// </list>
    /// </summary>
    private static IReadOnlyList<(string Param, AgType Type)> ParseParameters(ListNode paramList)
    {
        var result = new List<(string, AgType)>(paramList.Elements.Count);

        foreach (var element in paramList.Elements)
        {
            if (element is AtomNode atom)
            {
                result.Add((atom.Token.Value, AgType.Num));
            }
            else if (element is ListNode typed
                     && typed.Elements.Count >= 3
                     && typed.Elements[0] is AtomNode nameAtom
                     && typed.Elements[1] is AtomNode { Token.Value: ":" })
            {
                result.Add((nameAtom.Token.Value, ParseAnnotation(typed.Elements[2])));
            }
            else if (element is ListNode shortTyped
                     && shortTyped.Elements.Count == 2
                     && shortTyped.Elements[0] is AtomNode shortName
                     && IsTypeNode(shortTyped.Elements[1]))
            {
                result.Add((shortName.Token.Value, ParseAnnotation(shortTyped.Elements[1])));
            }
        }

        return result;
    }

    /// <summary>
    /// Collects body expressions starting at <paramref name="startIndex"/>.
    /// If there is exactly one, returns it directly. If multiple, wraps them
    /// in a synthetic <c>(do ...)</c> block so multi-statement function bodies
    /// work without an explicit <c>do</c>.
    /// </summary>
    private static AstNode WrapBody(IReadOnlyList<AstNode> elements, int startIndex)
    {
        if (startIndex >= elements.Count)
            return new ListNode(Array.Empty<AstNode>());

        if (elements.Count - startIndex == 1)
            return elements[startIndex];

        var bodyExprs = new List<AstNode> { new AtomNode(new Token(TokenType.Identifier, "do", 0, 0)) };
        for (int i = startIndex; i < elements.Count; i++)
            bodyExprs.Add(elements[i]);

        return new ListNode(bodyExprs);
    }
}
