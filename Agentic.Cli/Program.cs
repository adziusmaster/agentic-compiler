using System;
using System.Threading.Tasks;
using Agentic.Core.Agent;
using Agentic.Core.Execution;

EnvLoader.Load();

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run --project Agentic.Cli <path-to.ag>");
    return;
}

var parser = new ConstraintParser();
var profile = parser.Parse(args[0]);

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
    {
        Console.WriteLine(result.LastFeedback.ToLlmFeedback());
    }
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
