using System.Globalization;

namespace Agentic.Check;

// Faithful implementation of docs/semantics.md §4.1 – §4.17 for the E1
// subset used by test bodies. Every reduction path carries an
// // E1-rule-N tag so auditors can map code to semantics.
//
// TCB cost: ~550 LOC. This is the largest single file in the checker;
// every line is scrutinised.
//
// Not implemented (per §6.3):
//   - [cap-real]: unmocked capability calls are always stuck.
//   - try/catch.
//   - arr.map / arr.filter / arr.reduce with a function argument (HOF → E2).

public abstract record Value
{
    public sealed record Num(double N) : Value;
    public sealed record Str(string S) : Value;
    public sealed record Bool(bool B) : Value;
    public sealed record Unit() : Value; // for def/set/return-as-expression results
    public sealed record Arr(List<Value> Items) : Value;
    public sealed record Map(Dictionary<string, Value> Entries) : Value;
    public sealed record Rec(string TypeName, List<string> Fields, List<Value> Values) : Value;
    public sealed record Closure(List<string> Params, Node Body, Frame Env) : Value;
    public sealed record Extern(string CapabilityName) : Value;
}

public sealed class Frame
{
    public Dictionary<string, Value> Vars { get; } = new();
    public Frame? Parent { get; }
    public Frame(Frame? parent = null) { Parent = parent; }

    public bool TryGet(string name, out Value v)
    {
        for (var f = this; f != null; f = f.Parent)
            if (f.Vars.TryGetValue(name, out var found)) { v = found; return true; }
        v = null!;
        return false;
    }

    public void Set(string name, Value v) => Vars[name] = v;
}

public sealed class TestLog
{
    public List<(string Test, string Status, string? Reason)> Entries { get; } = new();
}

// [test-pass] / [test-fail] raise this to short-circuit an assertion
// failure back to the surrounding (test …) block.
internal sealed class AssertionFailure : Exception
{
    public AssertionFailure(string reason) : base(reason) { }
}

// [return] raises this to unwind to the enclosing [call] frame.
internal sealed class ReturnSignal : Exception
{
    public Value Value { get; }
    public ReturnSignal(Value v) { Value = v; }
}

// [require] / [ensure] raise this on ⟦ABORT⟧. The checker converts to
// a fail reason against the enclosing test.
internal sealed class ContractAbort : Exception
{
    public ContractAbort(string reason) : base(reason) { }
}

public sealed class ReferenceInterpreter
{
    private readonly Frame _global = new();
    private readonly Dictionary<string, List<string>> _recordTypes = new();
    private readonly HashSet<string> _declaredCapabilities = new();
    // Mock frame: (capability, key) → value. E1-rule §4.14 [mocks] adds;
    // §4.16 [test-*] snapshots and restores on exit.
    private readonly Dictionary<(string cap, string key), Value> _mocks = new();

    public TestLog Log { get; } = new();

    public ReferenceInterpreter(IEnumerable<string> declaredCapabilities)
    {
        foreach (var c in declaredCapabilities) _declaredCapabilities.Add(c);
    }

    /// <summary>Run a full program (top-level forms). Usually only called once
    /// per checker session; test runs are per-test via RunTest.</summary>
    public void Run(IEnumerable<Node> topLevel)
    {
        foreach (var form in topLevel) Eval(form, _global);
    }

    /// <summary>Run a single (test …) form. Snapshots mocks, runs body,
    /// restores mocks on exit. Appends (name, pass|fail) to the log.</summary>
    public void RunTest(Node testForm)
    {
        if (testForm is not SList list || list.Elements.Count < 2 ||
            list.Elements[0] is not Atom a || a.Value != "test" ||
            list.Elements[1] is not Atom nameAtom)
            throw new InvalidOperationException("RunTest expects (test <name> …).");

        string name = nameAtom.Value;
        var mockSnapshot = new Dictionary<(string, string), Value>(_mocks);

        try
        {
            for (int i = 2; i < list.Elements.Count; i++)
                Eval(list.Elements[i], _global); // E1-rule §4.16 [test-pass]
            Log.Entries.Add((name, "pass", null));
        }
        catch (AssertionFailure af) // E1-rule §4.16 [test-fail] (via §4.15)
        {
            Log.Entries.Add((name, "fail", af.Message));
        }
        catch (ContractAbort ca)
        {
            Log.Entries.Add((name, "fail", $"contract aborted: {ca.Message}"));
        }
        catch (Exception ex)
        {
            Log.Entries.Add((name, "fail", $"stuck: {ex.Message}"));
        }
        finally
        {
            _mocks.Clear();
            foreach (var kvp in mockSnapshot) _mocks[kvp.Key] = kvp.Value;
        }
    }

    private Value Eval(Node node, Frame env)
    {
        // E1-rule §4.2 [val] / [var] — literals and variable reference.
        if (node is Atom atom) return EvalAtom(atom, env);

        if (node is not SList list || list.Elements.Count == 0)
            throw new InvalidOperationException("Cannot evaluate empty list.");

        var headAtom = list.Elements[0] as Atom;
        if (headAtom is not null && headAtom.Kind == AtomKind.Symbol)
        {
            switch (headAtom.Value)
            {
                case "do":            return EvalDo(list, env);              // §4.4 [do-step]/[do-done]
                case "if":            return EvalIf(list, env);              // §4.4 [if-*]
                case "while":         return EvalWhile(list, env);           // §4.5 [while]
                case "def":           return EvalDef(list, env);             // §4.6 [def]
                case "set":           return EvalSet(list, env);             // §4.6 [set]
                case "defun":         return EvalDefun(list, env);           // §4.7 [defun]
                case "return":        return EvalReturn(list, env);          // §4.8 [return]
                case "defstruct":     return EvalDefstruct(list);            // §4.9 [rec-new]
                case "extern":        return EvalExtern(list, env);          // §4.13 [extern-decl]
                case "mocks":         return EvalMocks(list, env);           // §4.14 [mocks]
                case "assert-eq":     return EvalAssertEq(list, env);        // §4.15 [assert-eq-*]
                case "assert-true":   return EvalAssertTrue(list, env);      // §4.15 [assert-true]
                case "assert-near":   return EvalAssertNear(list, env);      // §4.15 [assert-near]
                case "require":       return EvalRequire(list, env);         // §4.17 [require]
                case "ensure":        return EvalEnsure(list, env);          // §4.17 [ensure]
                case "test":
                    // Nested (test …) forms inside another program — run them
                    // independently; caller usually uses RunTest instead.
                    RunTest(list);
                    return new Value.Unit();
            }

            // Arithmetic / comparison — §4.3 [bin-op].
            if (IsBinOp(headAtom.Value)) return EvalBinOp(headAtom.Value, list, env);

            // Record constructors and accessors. §4.9 [rec-new] / [rec-get] / [rec-set].
            if (headAtom.Value.EndsWith(".new")) return EvalRecNew(headAtom.Value, list, env);
            if (headAtom.Value.StartsWith("set-")) return EvalRecSet(headAtom.Value, list, env);
            if (IsRecordGetter(headAtom.Value, out var typeName, out var fieldName))
                return EvalRecGet(typeName, fieldName, list, env);

            // Array / Map / Str / Math stdlib — §4.10, §4.11, §5.1.
            var stdlib = EvalStdlib(headAtom.Value, list, env);
            if (stdlib is not null) return stdlib;
        }

        // Otherwise: function or capability call — §4.7 [call] / §4.12 [cap-mocked].
        return EvalCall(list, env);
    }

    // ---- Atoms & literals ---------------------------------------------------

    private Value EvalAtom(Atom atom, Frame env) => atom.Kind switch
    {
        AtomKind.Number => new Value.Num(double.Parse(atom.Value, CultureInfo.InvariantCulture)),
        AtomKind.String => new Value.Str(atom.Value),
        AtomKind.True   => new Value.Bool(true),
        AtomKind.False  => new Value.Bool(false),
        AtomKind.Symbol => env.TryGet(atom.Value, out var v) ? v
            : throw new InvalidOperationException($"Unbound variable: {atom.Value}"),
        _ => throw new InvalidOperationException($"Unknown atom kind: {atom.Kind}")
    };

    // ---- Control flow -------------------------------------------------------

    private Value EvalDo(SList list, Frame env)
    {
        Value last = new Value.Unit();
        for (int i = 1; i < list.Elements.Count; i++) last = Eval(list.Elements[i], env);
        return last; // E1-rule §4.4 [do-done]
    }

    private Value EvalIf(SList list, Frame env)
    {
        if (list.Elements.Count < 3 || list.Elements.Count > 4)
            throw new InvalidOperationException("(if cond then [else])");
        var cond = Eval(list.Elements[1], env);
        return IsTruthy(cond)
            ? Eval(list.Elements[2], env) // [if-true]
            : (list.Elements.Count == 4 ? Eval(list.Elements[3], env) : new Value.Num(0)); // [if-false]
    }

    private Value EvalWhile(SList list, Frame env)
    {
        // E1-rule §4.5 [while]: desugar to if-recursion; iterate in the host
        // to avoid blowing the .NET stack on long loops.
        while (IsTruthy(Eval(list.Elements[1], env)))
        {
            for (int i = 2; i < list.Elements.Count; i++) Eval(list.Elements[i], env);
        }
        return new Value.Num(0);
    }

    // ---- Binding ------------------------------------------------------------

    private Value EvalDef(SList list, Frame env)
    {
        // Forms: (def x e) or (def x : T e)
        string name = ((Atom)list.Elements[1]).Value;
        Node rhs = list.Elements[^1];
        var v = Eval(rhs, env);
        env.Set(name, v);
        return v; // E1-rule §4.6 [def]
    }

    private Value EvalSet(SList list, Frame env)
    {
        string name = ((Atom)list.Elements[1]).Value;
        if (!env.TryGet(name, out _)) throw new InvalidOperationException($"set on unbound {name}");
        var v = Eval(list.Elements[2], env);
        // Walk up to the frame where name was defined and set there.
        for (var f = env; f != null; f = f.Parent)
            if (f.Vars.ContainsKey(name)) { f.Vars[name] = v; return v; } // E1-rule §4.6 [set]
        throw new InvalidOperationException($"set on unbound {name}");
    }

    // ---- Functions ----------------------------------------------------------

    private Value EvalDefun(SList list, Frame env)
    {
        // (defun f ((x : T) (y : T)) : R body...)
        string name = ((Atom)list.Elements[1]).Value;
        var paramList = (SList)list.Elements[2];
        var paramNames = new List<string>();
        foreach (var p in paramList.Elements)
        {
            // param may be "x" (untyped) or (x : T)
            if (p is Atom pa) paramNames.Add(pa.Value);
            else if (p is SList ps && ps.Elements.Count > 0 && ps.Elements[0] is Atom pn)
                paramNames.Add(pn.Value);
        }
        // Skip optional `: <ReturnType>` annotation; remaining forms are the body.
        int bodyStart = 3;
        if (bodyStart < list.Elements.Count
            && list.Elements[bodyStart] is Atom colon && colon.Value == ":")
            bodyStart += 2;

        Node body;
        int bodyCount = list.Elements.Count - bodyStart;
        if (bodyCount <= 0)
            body = new Atom("0", AtomKind.Number); // empty body → returns 0
        else if (bodyCount == 1)
            body = list.Elements[bodyStart];
        else
        {
            // Multi-form body (e.g. require/ensure preceding return) → synthetic (do …).
            var doForms = new List<Node> { new Atom("do", AtomKind.Symbol) };
            for (int i = bodyStart; i < list.Elements.Count; i++)
                doForms.Add(list.Elements[i]);
            body = new SList(doForms);
        }
        // E1-rule §4.7 [defun]: register closure in the frame.
        env.Set(name, new Value.Closure(paramNames, body, env));
        return new Value.Unit();
    }

    private Value EvalReturn(SList list, Frame env)
    {
        var v = list.Elements.Count >= 2 ? Eval(list.Elements[1], env) : new Value.Num(0);
        throw new ReturnSignal(v); // E1-rule §4.8 [return]
    }

    private Value EvalCall(SList list, Frame env)
    {
        // Evaluate head. If it's a capability (Extern), route to [cap-mocked].
        var head = Eval(list.Elements[0], env);
        var args = new List<Value>();
        for (int i = 1; i < list.Elements.Count; i++) args.Add(Eval(list.Elements[i], env));

        if (head is Value.Extern ext) return InvokeCapability(ext, args); // §4.12 [cap-mocked]
        if (head is Value.Closure cl) return InvokeClosure(cl, args);     // §4.7 [call]

        throw new InvalidOperationException($"Cannot call non-function value: {head.GetType().Name}");
    }

    private Value InvokeClosure(Value.Closure cl, List<Value> args)
    {
        if (args.Count != cl.Params.Count)
            throw new InvalidOperationException($"arity mismatch: expected {cl.Params.Count}, got {args.Count}");
        var frame = new Frame(cl.Env);
        for (int i = 0; i < args.Count; i++) frame.Set(cl.Params[i], args[i]);
        try { return Eval(cl.Body, frame); }
        catch (ReturnSignal rs) { return rs.Value; } // §4.8 [return] pops the frame
    }

    // ---- Capabilities -------------------------------------------------------

    private Value EvalExtern(SList list, Frame env)
    {
        // (extern defun f ((p : T)) : R @capability "cap.name")
        string fname = ((Atom)list.Elements[2]).Value;
        string capName = "";
        for (int i = 3; i < list.Elements.Count - 1; i++)
        {
            if (list.Elements[i] is Atom a && a.Value == "@capability" &&
                list.Elements[i + 1] is Atom s && s.Kind == AtomKind.String)
            { capName = s.Value; break; }
        }
        if (capName == "") throw new InvalidOperationException($"extern missing @capability: {fname}");
        _declaredCapabilities.Add(capName); // §4.13 [extern-decl]
        env.Set(fname, new Value.Extern(capName));
        return new Value.Unit();
    }

    private Value InvokeCapability(Value.Extern ext, List<Value> args)
    {
        // E1-rule §4.12 [cap-mocked]. Key is the first string/number arg if any,
        // else "". Look up exact key, then wildcard "*". No real I/O.
        string key = args.Count > 0 ? ValueToKey(args[0]) : "";
        if (_mocks.TryGetValue((ext.CapabilityName, key), out var v)) return v;
        if (_mocks.TryGetValue((ext.CapabilityName, "*"), out var w)) return w;
        throw new InvalidOperationException(
            $"unmocked capability call: ({ext.CapabilityName} {key}) — [cap-real] is not implemented by the checker.");
    }

    private static string ValueToKey(Value v) => v switch
    {
        Value.Str s => s.S,
        Value.Num n => n.N.ToString(CultureInfo.InvariantCulture),
        Value.Bool b => b.B ? "true" : "false",
        _ => "",
    };

    private Value EvalMocks(SList list, Frame env)
    {
        // (mocks (c1 k1 v1) (c2 k2 v2) …). §4.14 [mocks].
        for (int i = 1; i < list.Elements.Count; i++)
        {
            var entry = (SList)list.Elements[i];
            string cap = ((Atom)entry.Elements[0]).Value;
            string key = ValueToKey(Eval(entry.Elements[1], env));
            Value v = Eval(entry.Elements[2], env);
            _mocks[(cap, key)] = v;
        }
        return new Value.Unit();
    }

    // ---- Assertions ---------------------------------------------------------

    private Value EvalAssertEq(SList list, Frame env)
    {
        var a = Eval(list.Elements[1], env);
        var b = Eval(list.Elements[2], env);
        if (!ValueEquals(a, b))
            throw new AssertionFailure($"assert-eq: {Show(a)} ≠ {Show(b)}"); // §4.15 [assert-eq-fail]
        return new Value.Unit(); // [assert-eq-pass]
    }

    private Value EvalAssertTrue(SList list, Frame env)
    {
        var v = Eval(list.Elements[1], env);
        if (!IsTruthy(v)) throw new AssertionFailure($"assert-true: {Show(v)} not truthy");
        return new Value.Unit();
    }

    private Value EvalAssertNear(SList list, Frame env)
    {
        double a = AsNum(Eval(list.Elements[1], env));
        double b = AsNum(Eval(list.Elements[2], env));
        double eps = AsNum(Eval(list.Elements[3], env));
        if (Math.Abs(a - b) > eps)
            throw new AssertionFailure($"assert-near: |{a} - {b}| > {eps}");
        return new Value.Unit();
    }

    // ---- Contracts ----------------------------------------------------------

    private Value EvalRequire(SList list, Frame env)
    {
        if (!IsTruthy(Eval(list.Elements[1], env)))
            throw new ContractAbort("require failed"); // §4.17 [require]
        return new Value.Unit();
    }

    private Value EvalEnsure(SList list, Frame env)
    {
        if (!IsTruthy(Eval(list.Elements[1], env)))
            throw new ContractAbort("ensure failed"); // §4.17 [ensure]
        return new Value.Unit();
    }

    // ---- Records ------------------------------------------------------------

    private Value EvalDefstruct(SList list)
    {
        string typeName = ((Atom)list.Elements[1]).Value;
        var fields = new List<string>();
        foreach (var fe in ((SList)list.Elements[2]).Elements)
        {
            if (fe is Atom fa) fields.Add(fa.Value);
            else if (fe is SList fs && fs.Elements[0] is Atom fn) fields.Add(fn.Value);
        }
        _recordTypes[typeName] = fields;
        return new Value.Unit();
    }

    private Value EvalRecNew(string head, SList list, Frame env)
    {
        string typeName = head[..^".new".Length];
        if (!_recordTypes.TryGetValue(typeName, out var fields))
            throw new InvalidOperationException($"unknown record type: {typeName}");
        var vals = new List<Value>();
        for (int i = 1; i < list.Elements.Count; i++) vals.Add(Eval(list.Elements[i], env));
        if (vals.Count != fields.Count)
            throw new InvalidOperationException($"{typeName}.new arity {fields.Count} expected, got {vals.Count}");
        return new Value.Rec(typeName, fields, vals);
    }

    private bool IsRecordGetter(string head, out string typeName, out string fieldName)
    {
        typeName = ""; fieldName = "";
        int dot = head.IndexOf('.');
        if (dot < 0) return false;
        typeName = head[..dot];
        fieldName = head[(dot + 1)..];
        return _recordTypes.ContainsKey(typeName);
    }

    private Value EvalRecGet(string typeName, string field, SList list, Frame env)
    {
        var r = (Value.Rec)Eval(list.Elements[1], env);
        int idx = r.Fields.IndexOf(field);
        if (idx < 0) throw new InvalidOperationException($"{typeName}.{field}: no such field");
        return r.Values[idx];
    }

    private Value EvalRecSet(string head, SList list, Frame env)
    {
        // set-<field>: returns a new Rec with the given field replaced.
        string field = head[4..];
        var r = (Value.Rec)Eval(list.Elements[1], env);
        var v = Eval(list.Elements[2], env);
        int idx = r.Fields.IndexOf(field);
        if (idx < 0) throw new InvalidOperationException($"set-{field}: no such field");
        var vals = new List<Value>(r.Values);
        vals[idx] = v;
        return new Value.Rec(r.TypeName, r.Fields, vals);
    }

    // ---- Helpers ------------------------------------------------------------

    private static bool IsBinOp(string s) => s is "+" or "-" or "*" or "/" or
        "<" or ">" or "=" or "<=" or ">=" or "and" or "or" or "not";

    private Value EvalBinOp(string op, SList list, Frame env)
    {
        if (op == "not")
            return new Value.Bool(!IsTruthy(Eval(list.Elements[1], env)));
        var a = Eval(list.Elements[1], env);
        var b = Eval(list.Elements[2], env);
        return op switch
        {
            "+" => new Value.Num(AsNum(a) + AsNum(b)),
            "-" => new Value.Num(AsNum(a) - AsNum(b)),
            "*" => new Value.Num(AsNum(a) * AsNum(b)),
            "/" => new Value.Num(AsNum(a) / AsNum(b)),
            "<" => new Value.Bool(AsNum(a) < AsNum(b)),
            ">" => new Value.Bool(AsNum(a) > AsNum(b)),
            "=" => new Value.Bool(ValueEquals(a, b)),
            "<=" => new Value.Bool(AsNum(a) <= AsNum(b)),
            ">=" => new Value.Bool(AsNum(a) >= AsNum(b)),
            "and" => new Value.Bool(IsTruthy(a) && IsTruthy(b)),
            "or" => new Value.Bool(IsTruthy(a) || IsTruthy(b)),
            _ => throw new InvalidOperationException($"unknown op {op}")
        };
    }

    private Value? EvalStdlib(string op, SList list, Frame env)
    {
        // §5.1 / §4.10 / §4.11 — pure host-BCL-backed metafunctions.
        Value[] Args()
        {
            var xs = new Value[list.Elements.Count - 1];
            for (int i = 1; i < list.Elements.Count; i++) xs[i - 1] = Eval(list.Elements[i], env);
            return xs;
        }

        switch (op)
        {
            case "str.concat":     { var a = Args(); return new Value.Str(AsStr(a[0]) + AsStr(a[1])); }
            case "str.length":     { var a = Args(); return new Value.Num(AsStr(a[0]).Length); }
            case "str.trim":       { var a = Args(); return new Value.Str(AsStr(a[0]).Trim()); }
            case "str.to_num":     { var a = Args(); return new Value.Num(double.Parse(AsStr(a[0]), CultureInfo.InvariantCulture)); }
            case "str.from_num":   { var a = Args(); return new Value.Str(AsNum(a[0]).ToString(CultureInfo.InvariantCulture)); }
            case "str.substring":  { var a = Args(); return new Value.Str(AsStr(a[0]).Substring((int)AsNum(a[1]), (int)AsNum(a[2]))); }
            case "str.index_of":   { var a = Args(); return new Value.Num(AsStr(a[0]).IndexOf(AsStr(a[1]), StringComparison.Ordinal)); }
            case "str.eq":         { var a = Args(); return new Value.Num(AsStr(a[0]) == AsStr(a[1]) ? 1 : 0); }
            case "str.upper":      { var a = Args(); return new Value.Str(AsStr(a[0]).ToUpperInvariant()); }
            case "str.lower":      { var a = Args(); return new Value.Str(AsStr(a[0]).ToLowerInvariant()); }
            case "str.starts_with": { var a = Args(); return new Value.Num(AsStr(a[0]).StartsWith(AsStr(a[1]), StringComparison.Ordinal) ? 1 : 0); }
            case "str.ends_with":  { var a = Args(); return new Value.Num(AsStr(a[0]).EndsWith(AsStr(a[1]), StringComparison.Ordinal) ? 1 : 0); }

            case "math.abs":       { var a = Args(); return new Value.Num(Math.Abs(AsNum(a[0]))); }
            case "math.floor":     { var a = Args(); return new Value.Num(Math.Floor(AsNum(a[0]))); }
            case "math.ceil":      { var a = Args(); return new Value.Num(Math.Ceiling(AsNum(a[0]))); }
            case "math.round":     { var a = Args(); return new Value.Num(Math.Round(AsNum(a[0]), MidpointRounding.ToEven)); }
            case "math.min":       { var a = Args(); return new Value.Num(Math.Min(AsNum(a[0]), AsNum(a[1]))); }
            case "math.max":       { var a = Args(); return new Value.Num(Math.Max(AsNum(a[0]), AsNum(a[1]))); }
            case "math.sqrt":      { var a = Args(); return new Value.Num(Math.Sqrt(AsNum(a[0]))); }
            case "math.pow":       { var a = Args(); return new Value.Num(Math.Pow(AsNum(a[0]), AsNum(a[1]))); }
            case "math.mod":       { var a = Args(); return new Value.Num(AsNum(a[0]) % AsNum(a[1])); }

            // §4.10 Arrays
            case "arr.new":        { var a = Args(); var n = (int)AsNum(a[0]); var items = new List<Value>(n); for (int i = 0; i < n; i++) items.Add(new Value.Num(0)); return new Value.Arr(items); }
            case "arr.get":        { var a = Args(); return ((Value.Arr)a[0]).Items[(int)AsNum(a[1])]; }
            case "arr.set":        { var a = Args(); var src = (Value.Arr)a[0]; var copy = new List<Value>(src.Items); copy[(int)AsNum(a[1])] = a[2]; return new Value.Arr(copy); }
            case "arr.length":     { var a = Args(); return new Value.Num(((Value.Arr)a[0]).Items.Count); }

            // §4.11 Maps
            case "map.new":        return new Value.Map(new Dictionary<string, Value>());
            case "map.set":        { var a = Args(); var src = (Value.Map)a[0]; var copy = new Dictionary<string, Value>(src.Entries); copy[AsStr(a[1])] = a[2]; return new Value.Map(copy); }
            case "map.get":        { var a = Args(); var src = (Value.Map)a[0]; return src.Entries.TryGetValue(AsStr(a[1]), out var v) ? v : new Value.Num(0); }
            case "map.has":        { var a = Args(); return new Value.Num(((Value.Map)a[0]).Entries.ContainsKey(AsStr(a[1])) ? 1 : 0); }
        }

        return null;
    }

    private static bool IsTruthy(Value v) => v switch
    {
        Value.Bool b => b.B,
        Value.Num n => n.N != 0.0,
        Value.Str s => s.S.Length > 0,
        Value.Unit => false,
        _ => true,
    };

    private static double AsNum(Value v) => v switch
    {
        Value.Num n => n.N,
        Value.Bool b => b.B ? 1 : 0,
        Value.Str s => double.Parse(s.S, CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException($"expected Num, got {v.GetType().Name}")
    };

    private static string AsStr(Value v) => v switch
    {
        Value.Str s => s.S,
        Value.Num n => n.N.ToString(CultureInfo.InvariantCulture),
        Value.Bool b => b.B ? "true" : "false",
        _ => throw new InvalidOperationException($"expected Str, got {v.GetType().Name}")
    };

    private static bool ValueEquals(Value a, Value b) => (a, b) switch
    {
        (Value.Num na, Value.Num nb) => na.N == nb.N,
        (Value.Str sa, Value.Str sb) => sa.S == sb.S,
        (Value.Bool ba, Value.Bool bb) => ba.B == bb.B,
        // cross-type coercion matches the compiler's verifier on Num <-> Str
        (Value.Num na, Value.Str sb) => na.N.ToString(CultureInfo.InvariantCulture) == sb.S,
        (Value.Str sa, Value.Num nb) => sa.S == nb.N.ToString(CultureInfo.InvariantCulture),
        _ => false,
    };

    private static string Show(Value v) => v switch
    {
        Value.Num n => n.N.ToString(CultureInfo.InvariantCulture),
        Value.Str s => $"\"{s.S}\"",
        Value.Bool b => b.B ? "true" : "false",
        _ => v.GetType().Name
    };
}
