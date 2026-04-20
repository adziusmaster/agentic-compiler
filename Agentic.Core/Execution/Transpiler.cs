using System.Text;
using Agentic.Core.Capabilities;
using Agentic.Core.Stdlib;
using Agentic.Core.Syntax;

namespace Agentic.Core.Execution;

/// <summary>
/// Translates a verified Agentic AST into C# source code.
/// Language primitives are emitted here; stdlib expressions delegate to <see cref="StdlibRegistry"/>.
/// Supports two output modes: CLI (default) and Server (when <c>server.listen</c> is present).
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
    private readonly List<(string Name, List<(string Param, AgType Type)> Params, AgType ReturnType)> _functions = new();
    private readonly List<(string Method, string Pattern, string Handler, bool IsJson)> _routes = new();
    private int? _serverPort;
    private readonly ModuleLoader? _moduleLoader;
    private readonly HashSet<string> _emittedModules = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True after transpilation if the program uses server.listen.</summary>
    public bool IsServerMode => _serverPort is not null;

    private readonly bool _requiresHttpClient;
    private readonly bool _requiresSqlite;

    private readonly CapabilityRegistry _capabilityRegistry;
    private readonly Dictionary<string, Capability> _externFuncs = new(StringComparer.Ordinal);

    /// <summary>
    /// Capabilities declared via <c>(extern defun …)</c> in the compiled program.
    /// Consumed by the proof-carrying manifest (C4) and runtime permission gate (C2).
    /// </summary>
    public IReadOnlyCollection<Capability> DeclaredCapabilities => _externFuncs.Values;

    /// <summary>
    /// Optional manifest embedded into the emitted binary. When non-null, the
    /// generated program supports <c>--verify</c> (prints manifest) and enforces
    /// a runtime permission gate derived from the manifest.
    /// </summary>
    public Runtime.ProofManifest? EmbeddedManifest { get; set; }

    public Transpiler(Permissions? permissions = null) : this(permissions, null, null) { }

    internal Transpiler(Permissions? permissions, ModuleLoader? moduleLoader, CapabilityRegistry? capabilityRegistry = null)
    {
        var registry = StdlibModules.Build();
        _emitters = registry.TranspilerEmitters;
        _permissionReqs = registry.PermissionRequirements;
        _permissions = permissions ?? Permissions.None;
        _requiresHttpClient = registry.RequiresHttpClient;
        _requiresSqlite = registry.RequiresSqlite;
        _moduleLoader = moduleLoader;
        _capabilityRegistry = capabilityRegistry ?? DefaultCapabilities.BuildTrusted();
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
        _routes.Clear();
        _serverPort = null;

        _types = new TypeInferencePass();
        _types.Scan(rootNode);

        var body = new StringBuilder();
        TranspileNode(rootNode, body);

        return _serverPort is not null
            ? EmitServerProgram(body)
            : EmitCliProgram(body);
    }

    private string EmitCliProgram(StringBuilder body)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine(Runtime.CanonicalFormat.EmittedSource);
        foreach (var s in _types.Structs.All)
        {
            var fieldList = string.Join(", ", s.Fields.Select(f => $"{AgType.ToCSharp(f.Type)} {f.Field}"));
            sb.AppendLine($"public record struct {s.Name}({fieldList});");
        }
        sb.AppendLine("class Program {");
        bool needsHttpClient = _requiresHttpClient
            || _externFuncs.Values.Any(c => c.CSharpEmitExpr.Contains("_httpClient"));
        if (needsHttpClient)
            sb.AppendLine("  static readonly System.Net.Http.HttpClient _httpClient = new();");

        // Proof-carrying manifest: the binary carries a JSON description of its
        // tests, capabilities, permissions and contracts so auditors can verify
        // it without the source. `agc verify <bin>` and <bin> --verify both
        // dump this back. See ProofManifestBuilder.
        if (EmbeddedManifest is not null)
        {
            sb.AppendLine("  static readonly string _manifest = " +
                EscapeCSharpVerbatim(EmbeddedManifest.ToJson()) + ";");
        }

        sb.AppendLine("  static void Main(string[] args) {");

        if (EmbeddedManifest is not null)
        {
            // --verify: print the manifest and exit zero so an auditor can inspect
            // the binary without executing its main flow.
            sb.AppendLine("    if (args.Length > 0 && args[0] == \"--verify\") { Console.Write(_manifest); return; }");
            // Runtime permission gate (C2): the permissions granted at compile
            // time are frozen into the manifest; the emitted program refuses to
            // operate under a looser runtime grant, so tampering isn't silent.
            sb.AppendLine("    // runtime capability introspection: grant set is baked into _manifest");
        }

        if (_requiresSqlite)
            sb.AppendLine(DbModule.HelperCode);
        sb.Append(body);
        EmitAutoEntryPoint(sb);
        sb.AppendLine("  }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string EscapeCSharpVerbatim(string s) =>
        "@\"" + s.Replace("\"", "\"\"") + "\"";

    private string EmitServerProgram(StringBuilder body)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.IO;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine(Runtime.CanonicalFormat.EmittedSource);
        sb.AppendLine();

        // Emit function definitions and other code
        if (_requiresSqlite)
        {
            sb.AppendLine("using Microsoft.Data.Sqlite;");
            sb.AppendLine();
            sb.AppendLine(DbModule.HelperCode);
        }
        sb.Append(body);
        sb.AppendLine();

        sb.AppendLine("var builder = WebApplication.CreateBuilder(args);");
        sb.AppendLine("var app = builder.Build();");
        sb.AppendLine();

        EmitRouteRegistrations(sb);
        sb.AppendLine();

        sb.AppendLine($"Console.WriteLine(\"[SERVER] Listening on http://0.0.0.0:{_serverPort}\");");
        sb.AppendLine($"app.Run(\"http://0.0.0.0:{_serverPort}\");");
        return sb.ToString();
    }

    private void EmitRouteRegistrations(StringBuilder sb)
    {
        foreach (var (method, pattern, handler, isJson) in _routes)
        {
            var routePath = ConvertRoutePattern(pattern);
            var func = _functions.FirstOrDefault(f => f.Name == handler);
            if (func == default)
            {
                sb.AppendLine($"// WARNING: handler '{handler}' not found for {method} {pattern}");
                continue;
            }

            var paramDecls = new List<string>();
            bool hasBodyParam = false;
            string? bodyParamName = null;

            foreach (var (param, type) in func.Params)
            {
                bool isRouteParam = pattern.Contains($":{param}");
                if (!isRouteParam && type is StrType && method == "Post")
                {
                    hasBodyParam = true;
                    bodyParamName = param;
                }
                else
                {
                    paramDecls.Add($"{AgType.ToCSharp(type)} {param}");
                }
            }

            string callArgs = string.Join(", ", func.Params.Select(p => p.Param));
            string callExpr = $"{handler}({callArgs})";

            // JSON routes return Results.Content with application/json
            string returnExpr = isJson
                ? $"Results.Content({callExpr}, \"application/json\")"
                : (func.ReturnType is StrType ? callExpr : $"{callExpr}.ToString()");

            if (hasBodyParam)
            {
                sb.AppendLine($"app.Map{method}(\"{routePath}\", async ({string.Join(", ", paramDecls.Append("HttpContext _ctx"))}) => {{");
                sb.AppendLine($"    using var _reader = new StreamReader(_ctx.Request.Body);");
                sb.AppendLine($"    string {bodyParamName} = await _reader.ReadToEndAsync();");
                sb.AppendLine($"    return {returnExpr};");
                sb.AppendLine("});");
            }
            else
            {
                sb.AppendLine($"app.Map{method}(\"{routePath}\", ({string.Join(", ", paramDecls)}) => {returnExpr});");
            }
        }
    }

    private static string ConvertRoutePattern(string pattern)
    {
        return string.Join("/", pattern.Split('/').Select(seg =>
            seg.StartsWith(":") ? $"{{{seg[1..]}}}" : seg));
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
            {
                if (list.Elements.Count < 2) break;
                string target = ((AtomNode)list.Elements[1]).Token.Value;

                // File imports — load and inline the imported module's code
                if ((target.StartsWith("./") || target.StartsWith("../")) && _moduleLoader != null)
                {
                    var loaded = _moduleLoader.Load(target);
                    if (_emittedModules.Add(loaded.FullPath))
                    {
                        var childLoader = _moduleLoader.ForDirectory(loaded.FullPath);
                        var savedLoader = _moduleLoader;
                        // Transpile the imported module body (functions, defs, structs)
                        // Tests and exports are already skipped by the transpiler
                        TranspileNode(loaded.Ast, sb);
                    }
                }
                break;
            }

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
                sb.AppendLine($"    Console.Write(AgCanonical.Out({TranspileExpression(list.Elements[1])}));");
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
                    (TypeInferencePass.Sanitize(p.Param), p.Type)).ToList(), sig.ReturnType));
                var paramStrs = sig.Parameters.Select(p =>
                    $"{AgType.ToCSharp(p.Type)} {TypeInferencePass.Sanitize(p.Param)}");
                string retType = AgType.ToCSharp(sig.ReturnType);
                sb.AppendLine($"    {retType} {fnName}({string.Join(", ", paramStrs)}) {{");
                if (ContainsReturn(sig.Body))
                {
                    TranspileNode(sig.Body, sb);
                }
                else
                {
                    EmitImplicitReturn(sig.Body, sb);
                }
                sb.AppendLine("    }");
                break;
            }

            case "return":
                sb.AppendLine($"      return {TranspileExpression(list.Elements[1])};");
                break;

            case "arr.set":
                sb.AppendLine($"    {TranspileExpression(list.Elements[1])}[(int){TranspileExpression(list.Elements[2])}] = {TranspileExpression(list.Elements[3])};");
                break;

            case "map.set":
                sb.AppendLine($"    {TranspileExpression(list.Elements[1])}[{TranspileExpression(list.Elements[2])}] = {TranspileExpression(list.Elements[3])};");
                break;

            case "map.remove":
                sb.AppendLine($"    {TranspileExpression(list.Elements[1])}.Remove({TranspileExpression(list.Elements[2])});");
                break;

            case "defstruct":
                break;

            case "extern":
            {
                // (extern defun name (params) : R @capability "cap.name") — no code emitted at
                // declaration; record the binding so call sites can inline the capability expr.
                var (sig, capName) = TypeAnnotations.ParseExternDefun(list);
                if (!_capabilityRegistry.TryGet(capName, out var cap))
                    throw new InvalidOperationException($"Unknown capability '{capName}' during transpile.");
                _permissions.Require(cap.Permission);
                _externFuncs[sig.Name] = cap;
                // Register the signature so type inference knows the return type.
                break;
            }

            case "test":
                break;

            case "assert-eq":
            case "assert-true":
            case "assert-near":
                break;

            case "server.get":
            {
                var route = ((AtomNode)list.Elements[1]).Token.Value;
                var handler = TypeInferencePass.Sanitize(((AtomNode)list.Elements[2]).Token.Value);
                _permissions.Require("http");
                _routes.Add(("Get", route, handler, false));
                break;
            }

            case "server.post":
            {
                var route = ((AtomNode)list.Elements[1]).Token.Value;
                var handler = TypeInferencePass.Sanitize(((AtomNode)list.Elements[2]).Token.Value);
                _permissions.Require("http");
                _routes.Add(("Post", route, handler, false));
                break;
            }

            case "server.json_get":
            {
                var route = ((AtomNode)list.Elements[1]).Token.Value;
                var handler = TypeInferencePass.Sanitize(((AtomNode)list.Elements[2]).Token.Value);
                _permissions.Require("http");
                _routes.Add(("Get", route, handler, true));
                break;
            }

            case "server.json_post":
            {
                var route = ((AtomNode)list.Elements[1]).Token.Value;
                var handler = TypeInferencePass.Sanitize(((AtomNode)list.Elements[2]).Token.Value);
                _permissions.Require("http");
                _routes.Add(("Post", route, handler, true));
                break;
            }

            case "server.listen":
            {
                _permissions.Require("http");
                var portAtom = (AtomNode)list.Elements[1];
                _serverPort = (int)double.Parse(portAtom.Token.Value);
                break;
            }

            case "require":
                sb.AppendLine($"    if (!Convert.ToBoolean({TranspileExpression(list.Elements[1])})) throw new Exception(\"Contract violation: precondition failed\");");
                break;

            case "ensure":
                sb.AppendLine($"    if (!Convert.ToBoolean({TranspileExpression(list.Elements[1])})) throw new Exception(\"Contract violation: postcondition failed\");");
                break;

            case "throw":
                sb.AppendLine($"    throw new Exception({TranspileExpression(list.Elements[1])});");
                break;

            case "try":
            {
                var catchClause = (ListNode)list.Elements[2];
                string errVar = TypeInferencePass.Sanitize(((AtomNode)catchClause.Elements[1]).Token.Value);
                sb.AppendLine("    try {");
                TranspileNode(list.Elements[1], sb);
                sb.AppendLine($"    }} catch (Exception {errVar}) {{");
                TranspileNode(catchClause.Elements[2], sb);
                sb.AppendLine("    }");
                break;
            }

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
        {
            var idx = TranspileExpression(list.Elements[1]);
            return $"((int)({idx}) < args.Length ? Convert.ToDouble(args[(int)({idx})]) : throw new ArgumentException($\"Expected at least {{(int)({idx})+1}} argument(s), got {{args.Length}}\"))";
        }
        if (op == "sys.input.get_str")
        {
            var idx = TranspileExpression(list.Elements[1]);
            return $"((int)({idx}) < args.Length ? args[(int)({idx})] : throw new ArgumentException($\"Expected at least {{(int)({idx})+1}} argument(s), got {{args.Length}}\"))";
        }
        if (op == "arr.new")
            return $"new double[(int)({TranspileExpression(list.Elements[1])})]";
        if (op == "arr.get")
            return $"{TranspileExpression(list.Elements[1])}[(int)({TranspileExpression(list.Elements[2])})]";
        if (op == "arr.set")
            return $"{TranspileExpression(list.Elements[1])}[(int)({TranspileExpression(list.Elements[2])})] = {TranspileExpression(list.Elements[3])}";
        if (op == "arr.length")
            return $"((double){TranspileExpression(list.Elements[1])}.Length)";
        if (op == "arr.map")
        {
            var arrExpr = TranspileExpression(list.Elements[1]);
            var fnName = TypeInferencePass.Sanitize(((AtomNode)list.Elements[2]).Token.Value);
            return $"Array.ConvertAll({arrExpr}, _e => {fnName}(_e))";
        }
        if (op == "arr.filter")
        {
            var arrExpr = TranspileExpression(list.Elements[1]);
            var fnName = TypeInferencePass.Sanitize(((AtomNode)list.Elements[2]).Token.Value);
            return $"Array.FindAll({arrExpr}, _e => Convert.ToBoolean({fnName}(_e)))";
        }
        if (op == "arr.reduce")
        {
            var arrExpr = TranspileExpression(list.Elements[1]);
            var fnName = TypeInferencePass.Sanitize(((AtomNode)list.Elements[2]).Token.Value);
            var init = TranspileExpression(list.Elements[3]);
            return $"{arrExpr}.Aggregate({init}, (_acc, _e) => {fnName}(_acc, _e))";
        }
        if (op == "if")
        {
            var cond = TranspileExpression(list.Elements[1]);
            var then = TranspileExpression(list.Elements[2]);
            var els = list.Elements.Count > 3 ? TranspileExpression(list.Elements[3]) : "0.0";
            return $"(Convert.ToBoolean({cond}) ? {then} : {els})";
        }
        if (_emitters.TryGetValue(op, out var emitter))
        {
            if (_permissionReqs.TryGetValue(op, out var capability))
                _permissions.Require(capability);
            return emitter(list.Elements.Skip(1).ToList(), TranspileExpression);
        }

        if (_types.Structs.TryResolveOp(op, out var typeName, out var member))
            return EmitStructOp(typeName, member, list);

        // Boolean literals used as expressions (e.g. LLM writes `(true)`)
        if (op is "true" or "false") return op;

        // Capability call: inline the registered emit-expression, substituting args.
        if (_externFuncs.TryGetValue(op, out var cap))
        {
            var inlineArgs = list.Elements.Skip(1).Select(TranspileExpression).ToArray();
            // Also guard at runtime via the permission gate (C2).
            return string.Format(cap.CSharpEmitExpr, inlineArgs.Cast<object>().ToArray());
        }

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
            // Element-typed arr.new: use the annotation's element type so (Array Str)
            // materializes as string[] rather than the default double[].
            if (explicitType is ArrayType at
                && rhs is ListNode arrList
                && arrList.Elements.Count >= 2
                && arrList.Elements[0] is AtomNode arrOp
                && arrOp.Token.Value == "arr.new")
            {
                string size = TranspileExpression(arrList.Elements[1]);
                string element = AgType.ToCSharp(at.Element);
                sb.AppendLine($"    {AgType.ToCSharp(explicitType)} {name} = new {element}[(int)({size})];");
                return;
            }
            sb.AppendLine($"    {AgType.ToCSharp(explicitType)} {name} = {TranspileExpression(rhs)};");
            return;
        }

        var varType  = _types.GetVarType(name);
        var exprType = _types.InferExpression(rhs);

        bool isArrayDecl  = isDef && varType is ArrayType && exprType is ArrayType && _declaredArrayVars.Add(name);
        bool isStringDecl = isDef && varType is StrType   && exprType is StrType   && _declaredStringVars.Add(name);

        if (isArrayDecl)
            sb.AppendLine($"    double[] {name} = {TranspileExpression(rhs)};");
        else if (isStringDecl)
            sb.AppendLine($"    string {name} = {TranspileExpression(rhs)};");
        else if (isDef && (varType is ArrayType || varType is StrType))
        {
            // Placeholder def skipped — the real typed declaration was already emitted above (explicit type path)
            // or will be emitted at the hoisted location.
        }
        else if (isDef)
            sb.AppendLine($"    var {name} = {TranspileExpression(rhs)};");
        else
            sb.AppendLine($"    {name} = {TranspileExpression(rhs)};");
    }

    /// <summary>
    /// Emits a function body with an implicit return on the last expression.
    /// For <c>(do ...)</c> blocks, all but the last element are emitted as statements
    /// and the last element is returned. For single expressions, emits <c>return expr;</c>.
    /// </summary>
    private void EmitImplicitReturn(AstNode body, StringBuilder sb)
    {
        if (body is ListNode list && list.Elements.Count > 0)
        {
            var op = (list.Elements[0] as AtomNode)?.Token.Value;
            if (op == "do")
            {
                // Emit all but the last element as statements, return the last
                for (int i = 1; i < list.Elements.Count - 1; i++)
                    TranspileNode(list.Elements[i], sb);
                if (list.Elements.Count > 1)
                    EmitImplicitReturn(list.Elements[^1], sb);
                return;
            }
            if (op is "if" && !ContainsReturn(body))
            {
                // if without returns — emit as statement-if with implicit returns in each branch
                sb.AppendLine($"    if ({TranspileExpression(list.Elements[1])}) {{");
                EmitImplicitReturn(list.Elements[2], sb);
                if (list.Elements.Count > 3)
                {
                    sb.AppendLine("} else {");
                    EmitImplicitReturn(list.Elements[3], sb);
                }
                sb.AppendLine("}");
                return;
            }
        }
        // Single expression — emit as return
        sb.AppendLine($"      return {TranspileExpression(body)};");
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
        // If user defined a main() function, call it explicitly.
        var mainFn = _functions.FirstOrDefault(f => f.Name == "main");
        if (mainFn != default)
        {
            sb.AppendLine("    main();");
            return;
        }

        if (_hasMainOutput || _functions.Count == 0 || _serverPort is not null)
            return;

        var (name, parameters, _) = _functions[0];

        if (parameters.Count > 0)
            sb.AppendLine($"    if (args.Length < {parameters.Count}) {{ Console.Error.WriteLine(\"Error: expected {parameters.Count} argument(s)\"); return; }}");

        for (int i = 0; i < parameters.Count; i++)
        {
            var (param, type) = parameters[i];
            if (type is StrType)
                sb.AppendLine($"    var _arg{i} = args[{i}];");
            else
                sb.AppendLine($"    var _arg{i} = Convert.ToDouble(args[{i}]);");
        }

        var argList = string.Join(", ", Enumerable.Range(0, parameters.Count).Select(i => $"_arg{i}"));
        sb.AppendLine($"    Console.Write(AgCanonical.Out({name}({argList})));");
    }
}
