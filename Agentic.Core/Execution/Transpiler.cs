using System.Text;
using Agentic.Core.Stdlib;
using Agentic.Core.Syntax;

namespace Agentic.Core.Execution;

/// <summary>
/// Translates a verified Agentic AST into C# source code.
/// Language primitives are emitted here; stdlib expressions delegate to <see cref="StdlibRegistry"/>.
/// </summary>
public sealed class Transpiler
{
    private readonly Dictionary<string, Func<IReadOnlyList<AstNode>, Func<AstNode, string>, string>> _emitters;
    private readonly Dictionary<string, string> _permissionReqs;
    private readonly Permissions _permissions;
    private TypeInferencePass _types = new();
    private readonly HashSet<string> _declaredArrayVars = new();
    private readonly HashSet<string> _declaredStringVars = new();
    private bool _hasMainOutput;
    private readonly List<(string Name, List<(string Param, AgType Type)> Params)> _functions = new();

    public Transpiler(Permissions? permissions = null)
    {
        var registry = StdlibModules.Build();
        _emitters = registry.TranspilerEmitters;
        _permissionReqs = registry.PermissionRequirements;
        _permissions = permissions ?? Permissions.None;
    }

    /// <summary>
    /// Transpiles the full AST into a standalone C# program source.
    /// </summary>
    public string Transpile(AstNode rootNode)
    {
        _declaredArrayVars.Clear();
        _declaredStringVars.Clear();
        _hasMainOutput = false;
        _functions.Clear();

        _types = new TypeInferencePass();
        _types.Scan(rootNode);

        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        foreach (var s in _types.Structs.All)
        {
            var fieldList = string.Join(", ", s.Fields.Select(f => $"double {f.Field}"));
            sb.AppendLine($"public record struct {s.Name}({fieldList});");
        }
        sb.AppendLine("class Program {");
        sb.AppendLine("  static void Main(string[] args) {");
        TranspileNode(rootNode, sb);
        EmitAutoEntryPoint(sb);
        sb.AppendLine("  }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private void TranspileNode(AstNode node, StringBuilder sb)
    {
        if (node is not ListNode list || list.Elements.Count == 0) return;
        var op = ((AtomNode)list.Elements[0]).Token.Value;

        switch (op)
        {
            case "do":
                for (int i = 1; i < list.Elements.Count; i++) TranspileNode(list.Elements[i], sb);
                break;

            case "module":
                for (int i = 2; i < list.Elements.Count; i++) TranspileNode(list.Elements[i], sb);
                break;

            case "import":
            case "export":
                break;

            case "def":
            {
                var (rawName, explicitType, valueNode) = TypeAnnotations.ParseDef(list);
                string name = TypeInferencePass.Sanitize(rawName);
                EmitAssignment(true, name, valueNode, explicitType, sb);
                break;
            }

            case "set":
            {
                string name = TypeInferencePass.Sanitize(((AtomNode)list.Elements[1]).Token.Value);
                EmitAssignment(false, name, list.Elements[2], null, sb);
                break;
            }

            case "sys.stdout.write":
                _hasMainOutput = true;
                sb.AppendLine($"    Console.Write({TranspileExpression(list.Elements[1])});");
                break;

            case "if":
                sb.AppendLine($"    if ({TranspileExpression(list.Elements[1])}) {{");
                TranspileNode(list.Elements[2], sb);
                if (list.Elements.Count > 3) { sb.AppendLine("} else {"); TranspileNode(list.Elements[3], sb); }
                sb.AppendLine("}");
                break;

            case "while":
                sb.AppendLine($"    while ({TranspileExpression(list.Elements[1])}) {{");
                TranspileNode(list.Elements[2], sb);
                sb.AppendLine("    }");
                break;

            case "defun":
            {
                var sig = TypeAnnotations.ParseDefun(list);
                string fnName = TypeInferencePass.Sanitize(sig.Name);
                _functions.Add((fnName, sig.Parameters.Select(p =>
                    (TypeInferencePass.Sanitize(p.Param), p.Type)).ToList()));
                var paramStrs = sig.Parameters.Select(p =>
                    $"{AgType.ToCSharp(p.Type)} {TypeInferencePass.Sanitize(p.Param)}");
                string retType = AgType.ToCSharp(sig.ReturnType);
                sb.AppendLine($"    {retType} {fnName}({string.Join(", ", paramStrs)}) {{");
                TranspileNode(sig.Body, sb);
                if (!ContainsReturn(sig.Body))
                    sb.AppendLine($"      return default({retType})!;");
                sb.AppendLine("    }");
                break;
            }

            case "return":
                sb.AppendLine($"      return {TranspileExpression(list.Elements[1])};");
                break;

            case "arr.set":
                sb.AppendLine($"    {TranspileExpression(list.Elements[1])}[(int){TranspileExpression(list.Elements[2])}] = {TranspileExpression(list.Elements[3])};");
                break;

            case "defstruct":
                break;

            case "test":
                break;

            case "assert-eq":
            case "assert-true":
            case "assert-near":
                break;

            case "require":
                sb.AppendLine($"    if (!Convert.ToBoolean({TranspileExpression(list.Elements[1])})) throw new Exception(\"Contract violation: precondition failed\");");
                break;

            case "ensure":
                sb.AppendLine($"    if (!Convert.ToBoolean({TranspileExpression(list.Elements[1])})) throw new Exception(\"Contract violation: postcondition failed\");");
                break;

            default:
                string expr = TranspileExpression(node);
                if (!string.IsNullOrWhiteSpace(expr)) sb.AppendLine($"    {expr};");
                break;
        }
    }

    private string TranspileExpression(AstNode node)
    {
        if (node is AtomNode atom)
        {
            if (atom.Token.Type == TokenType.String)
            {
                var escaped = atom.Token.Value
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
                return $"\"{escaped}\"";
            }
            if (atom.Token.Type == TokenType.Identifier) return TypeInferencePass.Sanitize(atom.Token.Value);
            string numVal = atom.Token.Value;
            return numVal.Contains('.') ? numVal : numVal + ".0";
        }

        if (node is not ListNode list || list.Elements.Count == 0) return "0";

        var op = ((AtomNode)list.Elements[0]).Token.Value;

        if (op is "+" or "-" or "*" or "/")
            return $"({TranspileExpression(list.Elements[1])} {op} {TranspileExpression(list.Elements[2])})";
        if (op is "<" or ">" or "<=" or ">=")
            return $"({TranspileExpression(list.Elements[1])} {op} {TranspileExpression(list.Elements[2])})";
        if (op == "=")
            return $"({TranspileExpression(list.Elements[1])} == {TranspileExpression(list.Elements[2])})";

        if (op == "sys.input.get")
            return $"Convert.ToDouble(args[(int)({TranspileExpression(list.Elements[1])})])";
        if (op == "sys.input.get_str")
            return $"args[(int)({TranspileExpression(list.Elements[1])})]";
        if (op == "arr.new")
            return $"new double[(int)({TranspileExpression(list.Elements[1])})]";
        if (op == "arr.get")
            return $"{TranspileExpression(list.Elements[1])}[(int)({TranspileExpression(list.Elements[2])})]";
        if (op == "arr.set")
            return $"{TranspileExpression(list.Elements[1])}[(int)({TranspileExpression(list.Elements[2])})] = {TranspileExpression(list.Elements[3])}";

        if (_emitters.TryGetValue(op, out var emitter))
        {
            if (_permissionReqs.TryGetValue(op, out var capability))
                _permissions.Require(capability);
            return emitter(list.Elements.Skip(1).ToList(), TranspileExpression);
        }

        if (_types.Structs.TryResolveOp(op, out var typeName, out var member))
            return EmitStructOp(typeName, member, list);

        var callArgs = list.Elements.Skip(1).Select(TranspileExpression);
        return $"{TypeInferencePass.Sanitize(op)}({string.Join(", ", callArgs)})";
    }

    private string EmitStructOp(string typeName, string member, ListNode list)
    {
        var args = list.Elements.Skip(1).Select(TranspileExpression).ToList();

        if (member == "new")
            return $"new {typeName}({string.Join(", ", args)})";

        if (member.StartsWith("set-"))
        {
            string fieldName = TypeInferencePass.Sanitize(member[4..]);
            return $"({args[0]} with {{ {fieldName} = {args[1]} }})";
        }

        string field = TypeInferencePass.Sanitize(member);
        return $"{args[0]}.{field}";
    }

    /// <summary>
    /// Emits a variable declaration or assignment. When an explicit type annotation
    /// is present, it is used directly. Otherwise, the inferred type drives the
    /// C# declaration to handle array/string hoisting correctly.
    /// </summary>
    private void EmitAssignment(bool isDef, string name, AstNode rhs, AgType? explicitType, StringBuilder sb)
    {
        if (explicitType is not null && explicitType is not UnknownType)
        {
            sb.AppendLine($"    {AgType.ToCSharp(explicitType)} {name} = {TranspileExpression(rhs)};");
            return;
        }

        var varType  = _types.GetVarType(name);
        var exprType = _types.InferExpression(rhs);

        bool isArrayDecl  = varType is ArrayType && exprType is ArrayType && _declaredArrayVars.Add(name);
        bool isStringDecl = varType is StrType   && exprType is StrType   && _declaredStringVars.Add(name);

        if (isArrayDecl)
            sb.AppendLine($"    double[] {name} = {TranspileExpression(rhs)};");
        else if (isStringDecl)
            sb.AppendLine($"    string {name} = {TranspileExpression(rhs)};");
        else if (varType is ArrayType || varType is StrType)
        {
            // Placeholder assignment skipped — the real typed declaration comes later.
        }
        else if (isDef)
            sb.AppendLine($"    var {name} = {TranspileExpression(rhs)};");
        else
            sb.AppendLine($"    {name} = {TranspileExpression(rhs)};");
    }

    /// <summary>
    /// Checks whether an AST node (or any descendant) contains a return statement.
    /// </summary>
    private static bool ContainsReturn(AstNode node)
    {
        if (node is ListNode list && list.Elements.Count > 0)
        {
            var op = (list.Elements[0] as AtomNode)?.Token.Value;
            if (op == "return") return true;
            return list.Elements.Any(ContainsReturn);
        }
        return false;
    }

    /// <summary>
    /// When a module defines functions but has no top-level I/O (no sys.stdout.write),
    /// auto-generates a main entry point that reads CLI args, calls the first function,
    /// and prints the result.
    /// </summary>
    private void EmitAutoEntryPoint(StringBuilder sb)
    {
        if (_hasMainOutput || _functions.Count == 0)
            return;

        var (name, parameters) = _functions[0];

        for (int i = 0; i < parameters.Count; i++)
        {
            var (param, type) = parameters[i];
            if (type is StrType)
                sb.AppendLine($"    var _arg{i} = args[{i}];");
            else
                sb.AppendLine($"    var _arg{i} = Convert.ToDouble(args[{i}]);");
        }

        var argList = string.Join(", ", Enumerable.Range(0, parameters.Count).Select(i => $"_arg{i}"));
        sb.AppendLine($"    Console.Write({name}({argList}));");
    }
}
