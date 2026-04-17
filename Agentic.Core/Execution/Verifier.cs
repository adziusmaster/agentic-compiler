using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Agentic.Core.Runtime;
using Agentic.Core.Stdlib;
using Agentic.Core.Syntax;

namespace Agentic.Core.Execution;

// Gateway: interprets an AST against a set of test inputs and captures stdout.
// Language primitives are handled here; stdlib functions are delegated to the registry.
public sealed class Verifier
{
    private readonly StringBuilder _stdoutBuffer = new();
    private readonly ExecutionEnvironment _env = new();
    private readonly Dictionary<string, Func<object[], object>> _nativeLibrary;
    private readonly string[] _inputs;

    public string CapturedOutput => _stdoutBuffer.ToString();

    public Verifier(string[] inputs = null!)
    {
        _inputs = inputs ?? Array.Empty<string>();
        _nativeLibrary = StdlibModules.Build().VerifierFuncs;
    }

    public object? Evaluate(AstNode node)
    {
        if (node is AtomNode atom)
        {
            if (atom.Token.Type == TokenType.Number) return double.Parse(atom.Token.Value);
            if (atom.Token.Type == TokenType.String) return atom.Token.Value;
            if (atom.Token.Type == TokenType.Identifier) return _env.Get(atom.Token.Value.Replace("-", "_"));
            return null;
        }

        if (node is not ListNode list || list.Elements.Count == 0) return null;

        var op = (list.Elements[0] as AtomNode)?.Token.Value ?? string.Empty;

        try
        {
            return op switch
            {
                "do"     => EvaluateSequence(list),
                "def"    => ExecuteDef(list),
                "set"    => ExecuteSet(list),
                "+" or "-" or "*" or "/" => ExecuteMath(op, list),
                "sys.stdout.write"  => ExecuteStdoutWrite(list),
                "sys.input.get"     => ExecuteInputGet(list),
                "sys.input.get_str" => ExecuteInputGetStr(list),
                "<" or ">" or "=" or "<=" or ">=" => ExecuteComparison(op, list),
                "if"     => ExecuteIf(list),
                "while"  => ExecuteWhile(list),
                "defun"     => ExecuteDefun(list),
                "defstruct" => ExecuteDefStruct(list),
                "return"    => throw new ReturnException(Evaluate(list.Elements[1])),

                // Array primitives — need lazy Evaluate(), so they stay here rather than in a module
                "arr.new" => new double[Convert.ToInt32(Evaluate(RequireArg(list, 1, "arr.new <size>")))],
                "arr.get" => ((double[])Evaluate(RequireArg(list, 1, "arr.get <array> <index>"))!)
                                [Convert.ToInt32(Evaluate(RequireArg(list, 2, "arr.get <array> <index>")))],
                "arr.set" => ExecuteArraySet(list),

                _ => ExecuteFunctionCall(op, list)
            };
        }
        catch (ReturnException) { throw; } // control-flow signal — must propagate
        catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "index")
        {
            // list.Elements[N] out-of-bounds: LLM generated a node with too few arguments
            throw new InvalidOperationException(
                $"Arity error in '({op} ...)': received {list.Elements.Count - 1} argument(s) but " +
                $"the operation tried to read more. Ensure every call to '{op}' has the correct number of arguments.", ex);
        }
    }

    // Guards that a list has at least (argIndex+1) elements and returns that element.
    // Throws a clear diagnostic if the argument is missing.
    private static AstNode RequireArg(ListNode list, int argIndex, string signature)
    {
        if (list.Elements.Count <= argIndex)
        {
            string op = ((AtomNode)list.Elements[0]).Token.Value;
            throw new InvalidOperationException(
                $"Arity error: '({op} ...)' requires argument at position {argIndex} — expected signature: ({signature}). " +
                $"Got only {list.Elements.Count - 1} argument(s).");
        }
        return list.Elements[argIndex];
    }

    // ── Array primitive ───────────────────────────────────────────────────────

    private object ExecuteArraySet(ListNode list)
    {
        var arr = (double[])Evaluate(RequireArg(list, 1, "arr.set <array> <index> <value>"))!;
        int idx  = Convert.ToInt32(Evaluate(RequireArg(list, 2, "arr.set <array> <index> <value>")));
        double val = Convert.ToDouble(Evaluate(RequireArg(list, 3, "arr.set <array> <index> <value>")));
        arr[idx] = val;
        return val;
    }

    // ── Stdlib / user-defined function dispatch ───────────────────────────────

    private object? ExecuteFunctionCall(string name, ListNode list)
    {
        if (_nativeLibrary.TryGetValue(name, out var native))
            return native(list.Elements.Skip(1).Select(Evaluate).ToArray()!);

        // (Foo.new …), (Foo.field obj), (Foo.set-field obj v) — resolved via the
        // struct registry before falling through to user-defined function dispatch.
        if (_env.Types.TryResolveOp(name, out var typeName, out var member))
            return ExecuteStructOp(typeName, member, list);

        if (!_env.TryGetFunction(name, out var funcDef))
            throw new UnauthorizedAccessException($"CRITICAL: Function '{name}' not found in registry.");

        var paramList = (ListNode)funcDef.Elements[2];
        var frame = new Dictionary<string, object>();
        for (int i = 0; i < paramList.Elements.Count; i++)
            frame[((AtomNode)paramList.Elements[i]).Token.Value] = Evaluate(list.Elements[i + 1])!;

        _env.PushFrame(frame);
        try   { return Evaluate(funcDef.Elements[3]); }
        catch (ReturnException rex) { return rex.Value; }
        finally { _env.PopFrame(); }
    }

    // ── Language primitives ───────────────────────────────────────────────────

    private object ExecuteDef(ListNode list)
    {
        string name = ((AtomNode)list.Elements[1]).Token.Value.Replace("-", "_");
        return _env.Define(name, Evaluate(list.Elements[2])!);
    }

    private object ExecuteSet(ListNode list)
    {
        string name = ((AtomNode)list.Elements[1]).Token.Value.Replace("-", "_");
        return _env.Set(name, Evaluate(list.Elements[2])!);
    }

    private object? EvaluateSequence(ListNode list)
    {
        object? res = null;
        for (int i = 1; i < list.Elements.Count; i++) res = Evaluate(list.Elements[i]);
        return res;
    }

    private double ExecuteMath(string op, ListNode list)
    {
        double l = Convert.ToDouble(Evaluate(list.Elements[1]));
        double r = Convert.ToDouble(Evaluate(list.Elements[2]));
        return op switch { "+" => l + r, "-" => l - r, "*" => l * r, "/" => l / r, _ => 0 };
    }

    private object? ExecuteStdoutWrite(ListNode list) { _stdoutBuffer.Append(Evaluate(list.Elements[1])); return null; }

    private object ExecuteInputGet(ListNode list)
    {
        int idx = Convert.ToInt32(Evaluate(list.Elements[1]));
        if (idx < 0 || idx >= _inputs.Length)
            throw new IndexOutOfRangeException($"OS Fault: Requested input index {idx}, but only {_inputs.Length} inputs provided.");
        return double.Parse(_inputs[idx]);
    }

    private object ExecuteInputGetStr(ListNode list)
    {
        int idx = Convert.ToInt32(Evaluate(list.Elements[1]));
        if (idx < 0 || idx >= _inputs.Length)
            throw new IndexOutOfRangeException($"OS Fault: Requested string input index {idx}, but only {_inputs.Length} inputs provided.");
        return _inputs[idx];
    }

    private bool ExecuteComparison(string op, ListNode list)
    {
        double l = Convert.ToDouble(Evaluate(list.Elements[1]));
        double r = Convert.ToDouble(Evaluate(list.Elements[2]));
        return op switch { "<" => l < r, ">" => l > r, "=" => l == r, "<=" => l <= r, ">=" => l >= r, _ => false };
    }

    private object? ExecuteIf(ListNode list) =>
        Convert.ToBoolean(Evaluate(list.Elements[1]))
            ? Evaluate(list.Elements[2])
            : (list.Elements.Count > 3 ? Evaluate(list.Elements[3]) : null);

    private object? ExecuteWhile(ListNode list)
    {
        object? res = null;
        while (Convert.ToBoolean(Evaluate(list.Elements[1]))) res = Evaluate(list.Elements[2]);
        return res;
    }

    private object? ExecuteDefun(ListNode list)
    {
        _env.RegisterFunction(((AtomNode)list.Elements[1]).Token.Value, list);
        return null;
    }

    // (defstruct Name (field1 field2 …)) — register the type. All fields are numeric
    // in Stage 1C; future stages can introduce per-field type annotations.
    private object? ExecuteDefStruct(ListNode list)
    {
        string name = ((AtomNode)RequireArg(list, 1, "defstruct <Name> (<fields…>)")).Token.Value;
        var fieldList = (ListNode)RequireArg(list, 2, "defstruct <Name> (<fields…>)");
        var fields = new List<(string, AgType)>(fieldList.Elements.Count);
        foreach (var f in fieldList.Elements)
            fields.Add((((AtomNode)f).Token.Value, AgType.Num));
        _env.Types.Register(new StructType(name, fields));
        return null;
    }

    // Dispatches (Foo.new …), (Foo.field obj), (Foo.set-field obj v) against a
    // registered struct. Arity and field-name errors surface as readable diagnostics.
    private object ExecuteStructOp(string typeName, string member, ListNode list)
    {
        _env.Types.TryGet(typeName, out var type);
        var callArgs = list.Elements.Skip(1).Select(Evaluate).ToList();

        if (member == "new")
        {
            if (callArgs.Count != type.Fields.Count)
                throw new InvalidOperationException(
                    $"Arity error in ({typeName}.new …): expected {type.Fields.Count} field value(s) " +
                    $"({string.Join(", ", type.Fields.Select(f => f.Field))}), got {callArgs.Count}.");
            var init = new Dictionary<string, object>();
            for (int i = 0; i < type.Fields.Count; i++)
                init[type.Fields[i].Field] = callArgs[i]!;
            return new Record(typeName, init);
        }

        if (member.StartsWith("set-"))
        {
            string fieldName = member[4..];
            if (callArgs.Count != 2)
                throw new InvalidOperationException(
                    $"Arity error in ({typeName}.set-{fieldName} …): expected 2 arguments (record, value), got {callArgs.Count}.");
            if (callArgs[0] is not Record rec)
                throw new InvalidOperationException(
                    $"Type error in ({typeName}.set-{fieldName} …): first argument must be a {typeName} record.");
            return rec.With(fieldName, callArgs[1]!);
        }

        // Plain field read: (Foo.field obj)
        if (callArgs.Count != 1)
            throw new InvalidOperationException(
                $"Arity error in ({typeName}.{member} …): expected 1 argument (the record), got {callArgs.Count}.");
        if (callArgs[0] is not Record r)
            throw new InvalidOperationException(
                $"Type error in ({typeName}.{member} …): argument must be a {typeName} record.");
        return r.Get(member);
    }
}