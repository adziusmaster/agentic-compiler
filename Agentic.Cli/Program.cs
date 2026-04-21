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

    case "verify":
        if (args.Length < 2) { Console.WriteLine("Usage: agc verify <binary>"); return; }
        RunVerify(args[1]);
        return;

    case "inspect":
        if (args.Length < 2) { Console.WriteLine("Usage: agc inspect <binary>"); return; }
        RunInspect(args[1]);
        return;

    case "agent":
        if (args.Length < 2) { Console.WriteLine("Usage: agc agent <file.ag | \"intent\"> [--out Name]"); return; }
        if (File.Exists(args[1]) && args[1].EndsWith(".ag"))
            await RunLegacyAgent(args[1]);
        else
            await RunIntentAgent(string.Join(' ', args.Skip(1).TakeWhile(a => !a.StartsWith("--"))),
                emitBinary: !args.Contains("--check"),
                outputFormat: args.Contains("--json") ? "json" : "sexpr",
                outputName: ParseOption(args, "--out"),
                explicitPermissions: ParsePermissions(args));
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
    Console.WriteLine("  agc verify <binary>               Extract embedded proof manifest from a compiled binary");
    Console.WriteLine("  agc inspect <binary>              Dump the raw manifest JSON from a binary");
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
    Console.WriteLine("  --allow-env                       Allow reading environment variables");
    Console.WriteLine("  --allow-db                        Allow SQLite database operations");
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
    AllowEnv = args.Contains("--allow-env"),
    AllowDb = args.Contains("--allow-db"),
    AllowTime = args.Contains("--allow-time"),
    AllowProcess = args.Contains("--allow-process"),
};

static void RunVerify(string binaryPath)
{
    if (!File.Exists(binaryPath))
    {
        Console.WriteLine($"Error: binary '{binaryPath}' not found.");
        Environment.ExitCode = 2;
        return;
    }

    // C6: prefer the sidecar `<binaryPath>.manifest.json` over the embedded
    // copy. The sidecar carries BinaryHash; the embedded copy does not
    // (chicken-and-egg). If only the embedded copy is available, we run in
    // "legacy" mode and skip the binary-hash check with a warning.
    string sidecarPath = Agentic.Core.Runtime.ProofManifestBuilder.SidecarPathFor(binaryPath);
    Agentic.Core.Runtime.ProofManifest manifest;
    string source;
    if (File.Exists(sidecarPath))
    {
        manifest = Agentic.Core.Runtime.ProofManifest.FromJson(File.ReadAllText(sidecarPath));
        source = "sidecar";
    }
    else
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = binaryPath,
            Arguments = "--verify",
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        string manifestJson = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (string.IsNullOrWhiteSpace(manifestJson))
        {
            Console.WriteLine("Error: binary returned no manifest. Was it compiled with proof-carrying support?");
            Environment.ExitCode = 2;
            return;
        }
        manifest = Agentic.Core.Runtime.ProofManifest.FromJson(manifestJson);
        source = "embedded";
    }

    Console.WriteLine($"Agentic binary manifest (schema {manifest.SchemaVersion}, source: {source})");
    Console.WriteLine($"  Source hash  : {manifest.SourceHash[..16]}…");
    if (!string.IsNullOrEmpty(manifest.BinaryHash))
        Console.WriteLine($"  Binary hash  : {manifest.BinaryHash[..16]}…  (declared)");
    Console.WriteLine($"  Built at     : {manifest.BuiltAt:u}");
    Console.WriteLine($"  Capabilities : {(manifest.Capabilities.Count == 0 ? "(none)" : string.Join(", ", manifest.Capabilities))}");
    Console.WriteLine($"  Permissions  : {(manifest.Permissions.Count == 0 ? "(none — pure)" : string.Join(", ", manifest.Permissions))}");
    Console.WriteLine($"  Tests        : {manifest.Tests.Count} (compile-time passes: {(manifest.Tests.Count > 0 ? manifest.Tests[0].ExpectedPasses.ToString() : "0")})");
    foreach (var t in manifest.Tests)
        Console.WriteLine($"    - {t.Name}");
    Console.WriteLine($"  Contracts    : {manifest.Contracts.Count}");
    foreach (var c in manifest.Contracts)
        Console.WriteLine($"    - {c.Function} ({c.Kind}): {c.SourceSnippet}");

    if (string.IsNullOrEmpty(manifest.BinaryHash))
    {
        Console.WriteLine();
        Console.WriteLine("Warning: manifest carries no BinaryHash (legacy or sidecar missing). Cannot detect post-emission tampering.");
        Console.WriteLine("Verified: manifest extracted and structurally valid (unhashed).");
        return;
    }

    string actualHash = Agentic.Core.Runtime.ProofManifestBuilder.HashBinary(binaryPath);
    Console.WriteLine($"  Binary hash  : {actualHash[..16]}…  (actual)");
    if (!string.Equals(actualHash, manifest.BinaryHash, StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine();
        Console.WriteLine("Error: binary-tampered — SHA256(binary) does not match manifest.BinaryHash.");
        Console.WriteLine($"  declared : {manifest.BinaryHash}");
        Console.WriteLine($"  actual   : {actualHash}");
        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine();
    Console.WriteLine("Verified: manifest extracted, structurally valid, and binary hash matches.");
}

static void RunInspect(string binaryPath)
{
    if (!File.Exists(binaryPath)) { Console.WriteLine($"Error: binary '{binaryPath}' not found."); return; }
    var psi = new System.Diagnostics.ProcessStartInfo
    {
        FileName = binaryPath, Arguments = "--verify",
        RedirectStandardOutput = true, UseShellExecute = false
    };
    using var process = System.Diagnostics.Process.Start(psi)!;
    Console.Write(process.StandardOutput.ReadToEnd());
    process.WaitForExit();
}

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
        var notFoundResult = new CompileResult
        {
            Success = false,
            Diagnostics = new List<CompileDiagnostic>
            {
                new()
                {
                    Severity = DiagnosticSeverity.Error,
                    Type = "file-not-found",
                    Message = $"File not found: {filePath}",
                    FixHint = "Check that the file path is correct and the file exists."
                }
            }
        };
        Console.WriteLine(outputFormat == "json" ? notFoundResult.ToJson() : notFoundResult.ToSExpr());
        return;
    }

    string source = File.ReadAllText(filePath);
    string cacheDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".", ".agc-cache");
    var cache = new CompilationCache(cacheDir);

    string basePath = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";
    var compiler = new Compiler(emitBinary, cache, permissions);
    var fileResult = compiler.Compile(source, outputName, basePath);
    cache.Flush();

    Console.WriteLine(outputFormat == "json" ? fileResult.ToJson() : fileResult.ToSExpr());

    // Show generated C# source
    if (fileResult.GeneratedSource is not null)
    {
        Console.WriteLine("\n[GENERATED C#]");
        Console.WriteLine("----------------------------------");
        Console.WriteLine(fileResult.GeneratedSource);
        Console.WriteLine("----------------------------------");
    }

    // Copy .ag source next to the binary
    if (fileResult.Success && fileResult.BinaryPath is not null)
    {
        var binaryDir = Path.GetDirectoryName(fileResult.BinaryPath)!;
        var agDestination = Path.Combine(binaryDir, Path.GetFileName(filePath));
        File.Copy(filePath, agDestination, overwrite: true);
        Console.WriteLine($"[SOURCE] {agDestination}");
    }
}

static IAgentClient? BuildAgentClient()
{
    string provider = (Environment.GetEnvironmentVariable("AGENTIC_PROVIDER") ?? "").ToLowerInvariant();
    string? anthropic = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    string? openai   = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    string? gemini   = Environment.GetEnvironmentVariable("GEMINI_API_KEY");

    if (provider == "anthropic" && !string.IsNullOrWhiteSpace(anthropic))
        return new AnthropicClient(anthropic, Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-sonnet-4-6");
    if (provider == "openai" && !string.IsNullOrWhiteSpace(openai))
        return new OpenAiClient(openai, Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o");
    if (provider == "gemini" && !string.IsNullOrWhiteSpace(gemini))
        return new AgentClient(gemini);

    // Auto-select by whichever key is present (priority: Anthropic → OpenAI → Gemini).
    if (!string.IsNullOrWhiteSpace(anthropic))
        return new AnthropicClient(anthropic, Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-sonnet-4-6");
    if (!string.IsNullOrWhiteSpace(openai))
        return new OpenAiClient(openai, Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o");
    if (!string.IsNullOrWhiteSpace(gemini))
        return new AgentClient(gemini);

    return null;
}

static async Task RunLegacyAgent(string filePath)
{
    var parser = new ConstraintParser();
    var profile = parser.Parse(filePath);

    IAgentClient? agent = BuildAgentClient();
    if (agent is null)
    {
        Console.WriteLine("\n[FATAL] No LLM provider key set. Provide ANTHROPIC_API_KEY, OPENAI_API_KEY, or GEMINI_API_KEY.");
        return;
    }
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

static async Task RunIntentAgent(string intent, bool emitBinary, string outputFormat, string? outputName = null, Permissions? explicitPermissions = null)
{
    if (string.IsNullOrWhiteSpace(intent))
    {
        Console.WriteLine("Usage: agc agent \"describe what you want to build\" [--out Name]");
        return;
    }

    IAgentClient? agent = BuildAgentClient();
    if (agent is null)
    {
        Console.WriteLine("\n[FATAL] No LLM provider key set. Provide ANTHROPIC_API_KEY, OPENAI_API_KEY, or GEMINI_API_KEY.");
        return;
    }

    string moduleName = outputName ?? "Program";

    var inferred = AgentWorkflow.InferPermissions(intent);
    var permissions = AgentWorkflow.MergePermissions(explicitPermissions ?? Permissions.None, inferred);

    var autoGranted = new List<string>();
    if (inferred.AllowHttp && !(explicitPermissions?.AllowHttp ?? false)) autoGranted.Add("http");
    if (inferred.AllowFileRead && !(explicitPermissions?.AllowFileRead ?? false)) autoGranted.Add("file-read");
    if (inferred.AllowFileWrite && !(explicitPermissions?.AllowFileWrite ?? false)) autoGranted.Add("file-write");
    if (inferred.AllowEnv && !(explicitPermissions?.AllowEnv ?? false)) autoGranted.Add("env");
    if (inferred.AllowDb && !(explicitPermissions?.AllowDb ?? false)) autoGranted.Add("db");
    if (autoGranted.Count > 0)
        Console.WriteLine($"[PERMISSIONS] Auto-granted from intent: {string.Join(", ", autoGranted)}");

    var workflow = new AgentWorkflow(agent, maxAttempts: 5, emitBinary: emitBinary, permissions: permissions, log: Console.WriteLine);

    Console.WriteLine($"[INTENT] {intent}\n");

    var result = await workflow.RunAsync(intent, moduleName);

    if (result.CompileResult is not null)
        Console.WriteLine(outputFormat == "json" ? result.CompileResult.ToJson() : result.CompileResult.ToSExpr());

    if (result.Success && result.Source is not null)
    {
        var savePath = FindSafePath(moduleName);
        File.WriteAllText(savePath, result.Source);
        Console.WriteLine($"[SAVED] Source: {savePath}");

        if (result.CompileResult?.GeneratedSource is not null)
        {
            Console.WriteLine("\n[GENERATED C#]");
            Console.WriteLine("----------------------------------");
            Console.WriteLine(result.CompileResult.GeneratedSource);
            Console.WriteLine("----------------------------------");
        }
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
