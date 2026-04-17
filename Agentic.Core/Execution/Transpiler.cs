using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Agentic.Core.Stdlib;
using Agentic.Core.Syntax;

namespace Agentic.Core.Execution;

// Gateway: translates a verified AST into C# source code.
// Language primitives are emitted here; stdlib expressions are delegated to the registry.
public sealed class Transpiler
{
    private readonly Dictionary<string, Func<IReadOnlyList<AstNode>, Func<AstNode, string>, string>> _emitters;

    // Type inference results — repopulated at the start of each Transpile() call
    private TypeInferencePass _types = new();

    // Tracks which typed variables have already been declared (reset each Transpile() call)
    private readonly HashSet<string> _declaredArrayVars = new();
    private readonly HashSet<string> _declaredStringVars = new();

    public Transpiler()
    {
        _emitters = StdlibModules.Build().TranspilerEmitters;
    }

    public string Transpile(AstNode rootNode)
    {
        _declaredArrayVars.Clear();
        _declaredStringVars.Clear();

        _types = new TypeInferencePass();
        _types.Scan(rootNode);

        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        // Hoist (defstruct …) declarations above Program so they're visible to Main.
        foreach (var s in _types.Structs.All)
        {
            var fieldList = string.Join(", ", s.Fields.Select(f => $"double {f.Field}"));
            sb.AppendLine($"public record struct {s.Name}({fieldList});");
        }
        sb.AppendLine("class Program {");
        sb.AppendLine("  static void Main(string[] args) {");
        TranspileNode(rootNode, sb);
        sb.AppendLine("  }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── Statement emitter ─────────────────────────────────────────────────────

    private void TranspileNode(AstNode node, StringBuilder sb)
    {
        if (node is not ListNode list || list.Elements.Count == 0) return;
        var op = ((AtomNode)list.Elements[0]).Token.Value;

        switch (op)
        {
            case "do":
                for (int i = 1; i < list.Elements.Count; i++) TranspileNode(list.Elements[i], sb);
                break;

            case "def":
            case "set":
            {
                string name = TypeInferencePass.Sanitize(((AtomNode)list.Elements[1]).Token.Value);
                EmitAssignment(op == "def", name, list.Elements[2], sb);
                break;
            }

            case "sys.stdout.write":
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
                var paramStrs = ((ListNode)list.Elements[2]).Elements
                    .Select(p => $"double {TypeInferencePass.Sanitize(((AtomNode)p).Token.Value)}");
                string fnName = TypeInferencePass.Sanitize(((AtomNode)list.Elements[1]).Token.Value);
                sb.AppendLine($"    double {fnName}({string.Join(", ", paramStrs)}) {{");
                TranspileNode(list.Elements[3], sb);
                sb.AppendLine("      return 0;");
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
                // Hoisted above Main — no statement-level emission.
                break;

            default:
                string expr = TranspileExpression(node);
                if (!string.IsNullOrWhiteSpace(expr)) sb.AppendLine($"    {expr};");
                break;
        }
    }

    // ── Expression emitter ────────────────────────────────────────────────────

    private string TranspileExpression(AstNode node)
    {
        if (node is AtomNode atom)
        {
            if (atom.Token.Type == TokenType.String) return $"\"{atom.Token.Value}\"";
            if (atom.Token.Type == TokenType.Identifier) return TypeInferencePass.Sanitize(atom.Token.Value);
            // All numbers in this language are doubles; suffix bare integer literals with .0
            // so C# infers `double` and avoids CS0266 on mixed arithmetic.
            string numVal = atom.Token.Value;
            return numVal.Contains('.') ? numVal : numVal + ".0";
        }

        if (node is not ListNode list || list.Elements.Count == 0) return "0";

        var op = ((AtomNode)list.Elements[0]).Token.Value;

        // ── Arithmetic / comparison operators ──
        if (op is "+" or "-" or "*" or "/")
            return $"({TranspileExpression(list.Elements[1])} {op} {TranspileExpression(list.Elements[2])})";
        if (op is "<" or ">" or "<=" or ">=")
            return $"({TranspileExpression(list.Elements[1])} {op} {TranspileExpression(list.Elements[2])})";
        if (op == "=")
            return $"({TranspileExpression(list.Elements[1])} == {TranspileExpression(list.Elements[2])})";

        // ── Array / IO primitives (stay here — not in a module) ──
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

        // ── Stdlib modules (math, string, bool, …) ──
        if (_emitters.TryGetValue(op, out var emitter))
            return emitter(list.Elements.Skip(1).ToList(), TranspileExpression);

        // ── Struct dispatch: (Foo.new …), (Foo.field obj), (Foo.set-field obj v) ──
        if (_types.Structs.TryResolveOp(op, out var typeName, out var member))
            return EmitStructOp(typeName, member, list);

        // ── User-defined function fallback ──
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

        // Plain field read
        string field = TypeInferencePass.Sanitize(member);
        return $"{args[0]}.{field}";
    }

    // ── Declaration decisions, driven by the type environment ────────────────
    //
    // Both (def) and (set) route through here. Because variables hoist their declared
    // C# type from the first assignment that actually matches the inferred type, this
    // tolerates LLM-generated placeholder forms like `(def arr 0)` followed later by
    // `(set arr (arr.new 3))` — the latter becomes the real declaration site.
    private void EmitAssignment(bool isDef, string name, AstNode rhs, StringBuilder sb)
    {
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
            // Variable's eventual type is array/string but this RHS is a placeholder
            // (e.g. `(def s 0)` before the real string assignment). Skip — the real
            // assignment will become the declaration site.
        }
        else if (isDef)
            sb.AppendLine($"    var {name} = {TranspileExpression(rhs)};");
        else
            sb.AppendLine($"    {name} = {TranspileExpression(rhs)};");
    }
}
