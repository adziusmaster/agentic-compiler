using Agentic.Core.Agent;
using Agentic.Core.Execution;
using Agentic.Core.Stdlib;

EnvLoader.Load();

if (args.Length == 0)
{
    PrintUsage();
    return;
}

string command = args[0].ToLowerInvariant();

switch (command)
{
    case "compile":
        if (args.Length < 2) { Console.WriteLine("Usage: agc compile <file.ag>"); return; }
        RunCompile(args[1], emitBinary: true, outputFormat: args.Contains("--json") ? "json" : "sexpr", permissions: ParsePermissions(args));
        break;

    case "check":
        if (args.Length < 2) { Console.WriteLine("Usage: agc check <file.ag>"); return; }
        RunCompile(args[1], emitBinary: false, outputFormat: args.Contains("--json") ? "json" : "sexpr", permissions: ParsePermissions(args));
        break;

    case "agent":
        if (args.Length < 2) { Console.WriteLine("Usage: agc agent <file.ag | \"intent\"> [--out Name]"); return; }
        if (File.Exists(args[1]) && args[1].EndsWith(".ag"))
            await RunLegacyAgent(args[1]);
        else
            await RunIntentAgent(string.Join(' ', args.Skip(1).TakeWhile(a => !a.StartsWith("--"))),
                emitBinary: !args.Contains("--check"),
                outputFormat: args.Contains("--json") ? "json" : "sexpr",
                outputName: ParseOption(args, "--out"));
        break;

    default:
        if (File.Exists(args[0]) && args[0].EndsWith(".ag"))
        {
            // Legacy: bare file path → auto-detect mode
            string content = File.ReadAllText(args[0]);
            if (content.TrimStart().StartsWith("name:") || content.TrimStart().StartsWith("Name:"))
                await RunLegacyAgent(args[0]);
            else
                RunCompile(args[0], emitBinary: true, outputFormat: "sexpr");
        }
        else
        {
            Console.WriteLine($"Unknown command: {args[0]}");
            PrintUsage();
        }
        break;
}

static void PrintUsage()
{
    Console.WriteLine("Agentic Compiler");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  agc compile <file.ag|dir/>        Compile .ag source to native binary (no LLM)");
    Console.WriteLine("  agc check <file.ag|dir/>          Type-check and run tests only (fast feedback)");
    Console.WriteLine("  agc agent <file.ag>               LLM-assisted compilation from constraint spec");
    Console.WriteLine("  agc agent \"build me a ...\"        Intent-driven: LLM writes .ag, compiler verifies");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --out <Name>                      Output filename for agent-generated source (default: from module name)");
    Console.WriteLine("  --json                            Output structured results as JSON instead of S-expr");
    Console.WriteLine("  --check (agent only)              Verify without emitting binary");
    Console.WriteLine("  --allow-file                      Allow file read/write operations");
    Console.WriteLine("  --allow-file-read                 Allow file read only");
    Console.WriteLine("  --allow-file-write                Allow file write only");
    Console.WriteLine("  --allow-http                      Allow outbound HTTP requests");
}

static string? ParseOption(string[] args, string flag)
{
    int idx = Array.IndexOf(args, flag);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

static Permissions ParsePermissions(string[] args) => new()
{
    AllowFileRead = args.Contains("--allow-file-read") || args.Contains("--allow-file"),
    AllowFileWrite = args.Contains("--allow-file-write") || args.Contains("--allow-file"),
    AllowHttp = args.Contains("--allow-http"),
};

static void RunCompile(string filePath, bool emitBinary, string outputFormat, Permissions? permissions = null)
{
    string outputName = Path.GetFileNameWithoutExtension(filePath);

    // Directory → multi-file project compilation
    if (Directory.Exists(filePath))
    {
        outputName = new DirectoryInfo(filePath).Name;
        var projectCompiler = new ProjectCompiler(emitBinary);
        var result = projectCompiler.CompileDirectory(filePath, outputName);
        Console.WriteLine(outputFormat == "json" ? result.ToJson() : result.ToSExpr());
        return;
    }

    if (!File.Exists(filePath))
    {
        Console.WriteLine($"(error (type \"file-not-found\") (message \"File not found: {filePath}\"))");
        return;
    }

    string source = File.ReadAllText(filePath);
    string cacheDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".", ".agc-cache");
    var cache = new CompilationCache(cacheDir);

    var compiler = new Compiler(emitBinary, cache, permissions);
    var fileResult = compiler.Compile(source, outputName);
    cache.Flush();

    Console.WriteLine(outputFormat == "json" ? fileResult.ToJson() : fileResult.ToSExpr());
}

static async Task RunLegacyAgent(string filePath)
{
    var parser = new ConstraintParser();
    var profile = parser.Parse(filePath);

    string? apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Console.WriteLine("\n[FATAL] GEMINI_API_KEY missing.");
        return;
    }

    IAgentClient agent = new AgentClient(apiKey);
    var pipeline = new PipelineOrchestrator(agent, maxAttempts: 3, log: Console.WriteLine);

    Console.WriteLine($"[ORCHESTRATOR] Loaded: {profile.Name}\n[CONSTRAINT] {profile.Objective}\n");
    if (profile.FunctionsOrEmpty.Count > 0)
    {
        Console.WriteLine($"[PIPELINE] {profile.FunctionsOrEmpty.Count} helper(s) declared: "
            + string.Join(", ", profile.FunctionsOrEmpty.Select(f => f.Name)));
    }

    var result = await pipeline.CompileAsync(profile);

    if (!result.Success)
    {
        Console.WriteLine($"\n[FATAL] Pipeline failed at stage '{result.Stage}'.");
        if (result.LastFeedback is not null)
            Console.WriteLine(result.LastFeedback.ToLlmFeedback());
        if (result.FinalSource is not null)
        {
            Console.WriteLine("\n[DEBUG] Last generated S-expression:");
            Console.WriteLine(result.FinalSource);
        }
        return;
    }

    Console.WriteLine($"\n[SUCCESS] Verified at stage '{result.Stage}'.");

    var transpiler = new Transpiler();
    string csharpCode = transpiler.Transpile(result.Ast!);

    try
    {
        var path = new NativeEmitter().Emit(csharpCode, profile.Name);
        Console.WriteLine($"\n[COMPLETED] Native binary assembled: {path}");
    }
    catch (Exception ex)
    {
        Console.WriteLine("\n[DEBUG] THE GENERATED C# SOURCE:");
        Console.WriteLine("----------------------------------");
        Console.WriteLine(csharpCode);
        Console.WriteLine("----------------------------------");
        Console.WriteLine($"\n[FATAL] Native Compilation Failed: {ex.Message}");
    }
}

static async Task RunIntentAgent(string intent, bool emitBinary, string outputFormat, string? outputName = null)
{
    if (string.IsNullOrWhiteSpace(intent))
    {
        Console.WriteLine("Usage: agc agent \"describe what you want to build\" [--out Name]");
        return;
    }

    string? apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Console.WriteLine("\n[FATAL] GEMINI_API_KEY missing.");
        return;
    }

    string moduleName = outputName ?? "Program";

    IAgentClient agent = new AgentClient(apiKey);
    var workflow = new AgentWorkflow(agent, maxAttempts: 3, emitBinary: emitBinary, log: Console.WriteLine);

    Console.WriteLine($"[INTENT] {intent}\n");

    var result = await workflow.RunAsync(intent, moduleName);

    if (result.CompileResult is not null)
        Console.WriteLine(outputFormat == "json" ? result.CompileResult.ToJson() : result.CompileResult.ToSExpr());

    if (result.Success && result.Source is not null)
    {
        var savePath = FindSafePath(moduleName);
        File.WriteAllText(savePath, result.Source);
        Console.WriteLine($"[SAVED] Source: {savePath}");
    }

    if (!result.Success && result.Source is not null)
    {
        Console.WriteLine($"\n[DEBUG] Last generated source:\n{result.Source}");
    }
}

/// <summary>
/// Finds a non-conflicting filename. Never overwrites existing files.
/// Returns "Name.ag", "Name_1.ag", "Name_2.ag", etc.
/// </summary>
static string FindSafePath(string baseName)
{
    var dir = Directory.GetCurrentDirectory();
    var candidate = Path.Combine(dir, $"{baseName}.ag");
    if (!File.Exists(candidate)) return candidate;

    for (int i = 1; ; i++)
    {
        candidate = Path.Combine(dir, $"{baseName}_{i}.ag");
        if (!File.Exists(candidate)) return candidate;
    }
}
