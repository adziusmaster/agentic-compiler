using System.Text;
using Agentic.Core.Runtime;
using Agentic.Core.Stdlib;
using Agentic.Core.Syntax;

namespace Agentic.Core.Execution;

/// <summary>
/// Interprets an Agentic AST against a set of test inputs and captures stdout.
/// Language primitives are handled here; stdlib functions delegate to <see cref="StdlibRegistry"/>.
/// </summary>
public sealed class Verifier
{
    private readonly StringBuilder _stdoutBuffer = new();
    private readonly ExecutionEnvironment _env = new();
    private readonly Dictionary<string, Func<object[], object>> _nativeLibrary;
    private readonly string[] _inputs;

    /// <summary>Output captured by <c>(sys.stdout.write …)</c> during evaluation.</summary>
    public string CapturedOutput => _stdoutBuffer.ToString();

    /// <summary>Number of <c>(test …)</c> blocks that passed during evaluation.</summary>
    public int TestsPassed => _testsPassed;
    private int _testsPassed;

    /// <summary>Number of <c>(test …)</c> blocks that failed during evaluation.</summary>
    public int TestsFailed => _testFailures.Count;

    /// <summary>
    /// Accumulated test failures. When <see cref="CollectAllErrors"/> is true, test failures
    /// are collected here instead of throwing immediately.
    /// </summary>
    public IReadOnlyList<TestFailureException> TestFailures => _testFailures;
    private readonly List<TestFailureException> _testFailures = new();

    /// <summary>
    /// When true, the verifier collects test failures instead of throwing on the first one.
    /// This allows agents to see ALL errors in a single compilation pass.
    /// </summary>
    public bool CollectAllErrors { get; set; }

    public Verifier(string[] inputs = null!)
    {
        _inputs = inputs ?? Array.Empty<string>();
        _nativeLibrary = StdlibModules.Build().VerifierFuncs;
    }

    /// <summary>
    /// Recursively evaluates an AST node, returning the resulting value.
    /// </summary>
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
                "module" => EvaluateModule(list),
                "import" => EvaluateImport(list),
                "export" => null,
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

                "arr.new"    => new double[Convert.ToInt32(Evaluate(RequireArg(list, 1, "arr.new <size>")))],
                "arr.get"    => ExecuteArrayGet(list),
                "arr.set"    => ExecuteArraySet(list),
                "arr.length" => ExecuteArrayLength(list),
                "arr.map"    => ExecuteArrayMap(list),
                "arr.filter" => ExecuteArrayFilter(list),
                "arr.reduce" => ExecuteArrayReduce(list),

                "test"        => ExecuteTest(list),
                "assert-eq"   => ExecuteAssertEq(list),
                "assert-true" => ExecuteAssertTrue(list),
                "assert-near" => ExecuteAssertNear(list),
                "require"     => ExecuteRequire(list),
                "ensure"      => ExecuteEnsure(list),
                "try"         => ExecuteTryCatch(list),
                "throw"       => throw new AgenticRuntimeException(Convert.ToString(Evaluate(list.Elements[1])) ?? "Unknown error"),

                _ => ExecuteFunctionCall(op, list)
            };
        }
        catch (ReturnException) { throw; }
        catch (ContractViolationException) { throw; }
        catch (TestFailureException) { throw; }
        catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "index")
        {
            throw new InvalidOperationException(
                $"Arity error in '({op} ...)': received {list.Elements.Count - 1} argument(s) but " +
                $"the operation tried to read more. Ensure every call to '{op}' has the correct number of arguments.", ex);
        }
    }

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

    private object ExecuteArrayGet(ListNode list)
    {
        var arr = Evaluate(RequireArg(list, 1, "arr.get <array> <index>"))!;
        int idx = Convert.ToInt32(Evaluate(RequireArg(list, 2, "arr.get <array> <index>")));
        return arr switch
        {
            double[] da => da[idx],
            string[] sa => sa[idx],
            object[] oa => oa[idx],
            _ => throw new InvalidOperationException($"arr.get: expected array, got {arr.GetType().Name}")
        };
    }

    private object ExecuteArraySet(ListNode list)
    {
        var arr = Evaluate(RequireArg(list, 1, "arr.set <array> <index> <value>"))!;
        int idx  = Convert.ToInt32(Evaluate(RequireArg(list, 2, "arr.set <array> <index> <value>")));
        var val = Evaluate(RequireArg(list, 3, "arr.set <array> <index> <value>"))!;
        switch (arr)
        {
            case double[] da: da[idx] = Convert.ToDouble(val); break;
            case string[] sa: sa[idx] = Convert.ToString(val)!; break;
            case object[] oa: oa[idx] = val; break;
            default: throw new InvalidOperationException($"arr.set: expected array, got {arr.GetType().Name}");
        }
        return val;
    }

    private object ExecuteArrayLength(ListNode list)
    {
        var arr = Evaluate(RequireArg(list, 1, "arr.length <array>"))!;
        return arr switch
        {
            double[] da => (double)da.Length,
            string[] sa => (double)sa.Length,
            object[] oa => (double)oa.Length,
            _ => throw new InvalidOperationException($"arr.length: expected array, got {arr.GetType().Name}")
        };
    }

    /// <summary>
    /// (arr.map array func_name) — applies a named function to each element, returns new array.
    /// </summary>
    private object ExecuteArrayMap(ListNode list)
    {
        var arr = Evaluate(RequireArg(list, 1, "arr.map <array> <func>"))!;
        string fnName = ((AtomNode)list.Elements[2]).Token.Value;

        if (arr is double[] darr)
        {
            var result = new double[darr.Length];
            for (int i = 0; i < darr.Length; i++)
                result[i] = Convert.ToDouble(InvokeByName(fnName, darr[i]));
            return result;
        }
        if (arr is string[] sarr)
        {
            var result = new string[sarr.Length];
            for (int i = 0; i < sarr.Length; i++)
                result[i] = Convert.ToString(InvokeByName(fnName, sarr[i]))!;
            return result;
        }
        throw new InvalidOperationException($"arr.map: expected array, got {arr.GetType().Name}");
    }

    /// <summary>
    /// (arr.filter array func_name) — keeps elements where func returns truthy, returns new array.
    /// </summary>
    private object ExecuteArrayFilter(ListNode list)
    {
        var arr = Evaluate(RequireArg(list, 1, "arr.filter <array> <func>"))!;
        string fnName = ((AtomNode)list.Elements[2]).Token.Value;

        if (arr is double[] darr)
        {
            var result = new List<double>();
            for (int i = 0; i < darr.Length; i++)
                if (IsTruthy(InvokeByName(fnName, darr[i])))
                    result.Add(darr[i]);
            return result.ToArray();
        }
        if (arr is string[] sarr)
        {
            var result = new List<string>();
            for (int i = 0; i < sarr.Length; i++)
                if (IsTruthy(InvokeByName(fnName, sarr[i])))
                    result.Add(sarr[i]);
            return result.ToArray();
        }
        throw new InvalidOperationException($"arr.filter: expected array, got {arr.GetType().Name}");
    }

    /// <summary>
    /// (arr.reduce array func_name initial) — folds left with a binary function.
    /// </summary>
    private object ExecuteArrayReduce(ListNode list)
    {
        var arr = Evaluate(RequireArg(list, 1, "arr.reduce <array> <func> <initial>"))!;
        string fnName = ((AtomNode)list.Elements[2]).Token.Value;
        var acc = Evaluate(RequireArg(list, 3, "arr.reduce <array> <func> <initial>"))!;

        if (arr is double[] darr)
        {
            for (int i = 0; i < darr.Length; i++)
                acc = InvokeByName(fnName, acc, darr[i])!;
            return acc;
        }
        if (arr is string[] sarr)
        {
            for (int i = 0; i < sarr.Length; i++)
                acc = InvokeByName(fnName, acc, sarr[i])!;
            return acc;
        }
        throw new InvalidOperationException($"arr.reduce: expected array, got {arr.GetType().Name}");
    }

    /// <summary>Invokes a named user function with the given arguments.</summary>
    private object? InvokeByName(string name, params object[] args)
    {
        if (_nativeLibrary.TryGetValue(name, out var native))
            return native(args);

        if (!_env.TryGetFunction(name, out var funcDef))
            throw new InvalidOperationException($"Function '{name}' not found (referenced by arr.map/filter/reduce).");

        var sig = TypeAnnotations.ParseDefun(funcDef);
        var frame = new Dictionary<string, object>();
        for (int i = 0; i < sig.Parameters.Count && i < args.Length; i++)
            frame[sig.Parameters[i].Param] = args[i];

        _env.PushFrame(frame);
        try   { return Evaluate(sig.Body); }
        catch (ReturnException rex) { return rex.Value; }
        finally { _env.PopFrame(); }
    }

    private static bool IsTruthy(object? val) => val switch
    {
        null => false,
        bool b => b,
        double d => d != 0.0,
        string s => s.Length > 0,
        _ => true
    };

    private object? ExecuteFunctionCall(string name, ListNode list)
    {
        if (_nativeLibrary.TryGetValue(name, out var native))
            return native(list.Elements.Skip(1).Select(Evaluate).ToArray()!);

        if (_env.Types.TryResolveOp(name, out var typeName, out var member))
            return ExecuteStructOp(typeName, member, list);

        if (!_env.TryGetFunction(name, out var funcDef))
            throw new UnauthorizedAccessException($"CRITICAL: Function '{name}' not found in registry.");

        var sig = TypeAnnotations.ParseDefun(funcDef);
        var frame = new Dictionary<string, object>();
        for (int i = 0; i < sig.Parameters.Count; i++)
            frame[sig.Parameters[i].Param] = Evaluate(list.Elements[i + 1])!;

        _env.PushFrame(frame);
        try   { return Evaluate(sig.Body); }
        catch (ReturnException rex) { return rex.Value; }
        finally { _env.PopFrame(); }
    }

    private object ExecuteDef(ListNode list)
    {
        var (rawName, _, valueNode) = TypeAnnotations.ParseDef(list);
        string name = rawName.Replace("-", "_");
        return _env.Define(name, Evaluate(valueNode)!);
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

    /// <summary>
    /// <c>(module Name body…)</c> — evaluates body expressions sequentially.
    /// The module name is stored for diagnostics.
    /// </summary>
    private object? EvaluateModule(ListNode list)
    {
        object? res = null;
        for (int i = 2; i < list.Elements.Count; i++) res = Evaluate(list.Elements[i]);
        return res;
    }

    /// <summary>
    /// <c>(import std.math)</c> — validates the import is a known stdlib module.
    /// Currently a no-op since all stdlib is always loaded.
    /// </summary>
    private object? EvaluateImport(ListNode list)
    {
        if (list.Elements.Count < 2) return null;
        string target = ((AtomNode)list.Elements[1]).Token.Value;
        if (!target.StartsWith("std.") && !target.StartsWith("./"))
            throw new InvalidOperationException(
                $"Unknown import '{target}'. Use 'std.math', 'std.string', 'std.bool', or './local-module'.");
        return null;
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
            return 0.0; // Default during verification when no CLI args provided
        return double.Parse(_inputs[idx]);
    }

    private object ExecuteInputGetStr(ListNode list)
    {
        int idx = Convert.ToInt32(Evaluate(list.Elements[1]));
        if (idx < 0 || idx >= _inputs.Length)
            return ""; // Default during verification when no CLI args provided
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

        if (callArgs.Count != 1)
            throw new InvalidOperationException(
                $"Arity error in ({typeName}.{member} …): expected 1 argument (the record), got {callArgs.Count}.");
        if (callArgs[0] is not Record r)
            throw new InvalidOperationException(
                $"Type error in ({typeName}.{member} …): argument must be a {typeName} record.");
        return r.Get(member);
    }

    /// <summary>
    /// Executes a <c>(test "name" assertion1 assertion2 …)</c> block.
    /// All assertions must pass or compilation fails.
    /// </summary>
    private object? ExecuteTest(ListNode list)
    {
        string testName = list.Elements[1] is AtomNode a ? a.Token.Value
            : list.Elements[1] is ListNode ? "anonymous" : "unnamed";

        for (int i = 2; i < list.Elements.Count; i++)
        {
            try
            {
                Evaluate(list.Elements[i]);
            }
            catch (ContractViolationException ex)
            {
                var failure = new TestFailureException(testName, ex.Message, ex);
                if (CollectAllErrors)
                {
                    _testFailures.Add(failure);
                    return null;
                }
                throw failure;
            }
            catch (TestFailureException ex)
            {
                if (CollectAllErrors)
                {
                    _testFailures.Add(ex);
                    return null;
                }
                throw;
            }
            catch (Exception ex) when (ex is not ReturnException)
            {
                var failure = new TestFailureException(testName, ex.Message, ex);
                if (CollectAllErrors)
                {
                    _testFailures.Add(failure);
                    return null;
                }
                throw failure;
            }
        }
        _testsPassed++;
        return null;
    }

    /// <summary>
    /// <c>(assert-eq actual expected)</c> — asserts two values are equal.
    /// </summary>
    private object? ExecuteAssertEq(ListNode list)
    {
        var actual = Evaluate(RequireArg(list, 1, "assert-eq <actual> <expected>"));
        var expected = Evaluate(RequireArg(list, 2, "assert-eq <actual> <expected>"));

        if (!ValuesEqual(actual, expected))
            throw new ContractViolationException("assert-eq",
                $"expected {FormatValue(expected)}, got {FormatValue(actual)}");
        return null;
    }

    /// <summary>
    /// <c>(assert-true expr)</c> — asserts the expression is truthy.
    /// </summary>
    private object? ExecuteAssertTrue(ListNode list)
    {
        var value = Evaluate(RequireArg(list, 1, "assert-true <expr>"));
        if (!Convert.ToBoolean(value))
            throw new ContractViolationException("assert-true",
                $"expression evaluated to {FormatValue(value)}, expected truthy");
        return null;
    }

    /// <summary>
    /// <c>(assert-near actual expected epsilon)</c> — asserts two numbers are within epsilon.
    /// </summary>
    private object? ExecuteAssertNear(ListNode list)
    {
        double actual = Convert.ToDouble(Evaluate(RequireArg(list, 1, "assert-near <actual> <expected> <epsilon>")));
        double expected = Convert.ToDouble(Evaluate(RequireArg(list, 2, "assert-near <actual> <expected> <epsilon>")));
        double epsilon = Convert.ToDouble(Evaluate(RequireArg(list, 3, "assert-near <actual> <expected> <epsilon>")));

        if (Math.Abs(actual - expected) > epsilon)
            throw new ContractViolationException("assert-near",
                $"expected {expected} ± {epsilon}, got {actual} (diff = {Math.Abs(actual - expected)})");
        return null;
    }

    /// <summary>
    /// <c>(require expr)</c> — precondition guard. Throws if false.
    /// </summary>
    private object? ExecuteRequire(ListNode list)
    {
        var value = Evaluate(RequireArg(list, 1, "require <expr>"));
        if (!Convert.ToBoolean(value))
            throw new ContractViolationException("require",
                $"precondition failed");
        return null;
    }

    /// <summary>
    /// <c>(ensure expr)</c> — postcondition guard. Throws if false.
    /// </summary>
    private object? ExecuteEnsure(ListNode list)
    {
        var value = Evaluate(RequireArg(list, 1, "ensure <expr>"));
        if (!Convert.ToBoolean(value))
            throw new ContractViolationException("ensure",
                $"postcondition failed");
        return null;
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is double da && b is double db)
            return da == db;
        return Equals(a?.ToString(), b?.ToString());
    }

    private static string FormatValue(object? v) => v switch
    {
        null => "nil",
        double d => d.ToString(),
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        _ => v.ToString() ?? "nil"
    };

    /// <summary>
    /// <c>(try expr (catch err-var handler))</c> — evaluates expr; on exception,
    /// binds the error message to err-var and evaluates handler.
    /// </summary>
    private object? ExecuteTryCatch(ListNode list)
    {
        var tryBody = list.Elements[1];

        // Parse (catch err-var handler) clause
        var catchClause = (ListNode)list.Elements[2];
        var catchOp = ((AtomNode)catchClause.Elements[0]).Token.Value;
        if (catchOp != "catch")
            throw new InvalidOperationException("try: second argument must be (catch var handler)");

        string errVar = ((AtomNode)catchClause.Elements[1]).Token.Value;
        var handler = catchClause.Elements[2];

        try
        {
            return Evaluate(tryBody);
        }
        catch (AgenticRuntimeException ex)
        {
            _env.PushFrame(new Dictionary<string, object> { [errVar] = ex.Message });
            try   { return Evaluate(handler); }
            finally { _env.PopFrame(); }
        }
        catch (ContractViolationException ex)
        {
            _env.PushFrame(new Dictionary<string, object> { [errVar] = ex.Message });
            try   { return Evaluate(handler); }
            finally { _env.PopFrame(); }
        }
        catch (InvalidOperationException ex)
        {
            _env.PushFrame(new Dictionary<string, object> { [errVar] = ex.Message });
            try   { return Evaluate(handler); }
            finally { _env.PopFrame(); }
        }
    }
}

/// <summary>
/// Explicit user-thrown error via <c>(throw msg)</c>.
/// </summary>
public sealed class AgenticRuntimeException : Exception
{
    public AgenticRuntimeException(string message) : base(message) { }
}