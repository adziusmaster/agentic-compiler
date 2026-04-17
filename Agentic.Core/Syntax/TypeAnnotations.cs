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
                _ => AgType.Unknown
            };
        }

        return AgType.Unknown;
    }

    /// <summary>
    /// Extracts the signature from a <c>(defun ...)</c> AST node.
    /// </summary>
    /// <remarks>
    /// Supports two syntaxes:
    /// <list type="bullet">
    ///   <item><c>(defun name (p1 p2) body)</c> — untyped; params default to Num, return defaults to Num.</item>
    ///   <item><c>(defun name ((p1 : T1) (p2 : T2)) : RetT body)</c> — explicitly typed.</item>
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
        else
        {
            returnType = AgType.Num;
            bodyStart = 3;
        }

        var body = WrapBody(defunList.Elements, bodyStart);
        return new DefunSignature(name, parameters, returnType, body);
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

        return (name, null, defList.Elements[2]);
    }

    /// <summary>
    /// Parses a parameter list, handling both untyped atoms and typed <c>(name : Type)</c> pairs.
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
