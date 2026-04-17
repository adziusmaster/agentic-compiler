using Agentic.Core.Execution;
using Agentic.Core.Stdlib;
using System.Linq;

namespace Agentic.Core.Agent;

/// <summary>
/// Intent-driven agent workflow. Takes an English description, uses an LLM to
/// write a complete .ag program, then compiles it with the deterministic compiler.
/// On failure, feeds structured diagnostics back to the LLM for self-correction.
/// </summary>
public sealed class AgentWorkflow
{
    private readonly IAgentClient _agent;
    private readonly int _maxAttempts;
    private readonly bool _emitBinary;
    private readonly Permissions _permissions;
    private readonly Action<string>? _log;

    public AgentWorkflow(
        IAgentClient agent,
        int maxAttempts = 5,
        bool emitBinary = true,
        Permissions? permissions = null,
        Action<string>? log = null)
    {
        _agent = agent;
        _maxAttempts = maxAttempts;
        _emitBinary = emitBinary;
        _permissions = permissions ?? Permissions.None;
        _log = log;
    }

    /// <summary>
    /// The session recorder for this workflow run. Populated after <see cref="RunAsync"/> completes.
    /// </summary>
    public CompilationSession? Session { get; private set; }

    /// <summary>
    /// Runs the intent→compile→feedback loop until the program compiles
    /// (all tests pass, contracts satisfied) or attempts are exhausted.
    /// </summary>
    public async Task<AgentWorkflowResult> RunAsync(
        string intent,
        string moduleName = "Program",
        CancellationToken cancellationToken = default)
    {
        string systemPrompt = BuildSystemPrompt();
        string? lastSource = null;
        CompileResult? lastResult = null;
        var session = new CompilationSession();
        Session = session;

        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            _log?.Invoke($"[AGENT] Generating .ag source (attempt {attempt}/{_maxAttempts})...");

            string userPrompt = lastResult is null
                ? BuildInitialPrompt(intent, moduleName)
                : BuildRetryPrompt(intent, lastSource!, lastResult);

            string source = await _agent.GenerateCodeAsync(
                systemPrompt,
                userPrompt,
                previousCode: lastSource,
                previousError: lastResult?.ToSExpr(),
                cancellationToken: cancellationToken);

            source = CleanAgentOutput(source);
            lastSource = source;

            _log?.Invoke($"[COMPILER] Compiling generated source ({source.Length} chars)...");

            var compiler = new Compiler(_emitBinary, permissions: _permissions);
            lastResult = compiler.Compile(source, moduleName);
            session.Record(source, lastResult);

            if (lastResult.Success)
            {
                int totalTests = lastResult.TestsPassed + lastResult.TestsFailed;
                string testStatus = totalTests == 0
                    ? "No tests defined"
                    : $"Tests: {lastResult.TestsPassed}/{totalTests}";
                _log?.Invoke($"[SUCCESS] Compiled on attempt {attempt}. {testStatus}");
                _log?.Invoke($"[SESSION] {session.ToSummary()}");
                return new AgentWorkflowResult
                {
                    Success = true,
                    Source = source,
                    CompileResult = lastResult,
                    AttemptsUsed = attempt,
                    Session = session
                };
            }

            var errors = lastResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)
                .ToList();
            var errorSummary = errors.Count <= 1
                ? errors.FirstOrDefault() ?? "Unknown error"
                : string.Join("; ", errors);
            _log?.Invoke($"[RETRY] Compilation failed: {errorSummary}");
        }

        _log?.Invoke($"[FAILED] All {_maxAttempts} attempts exhausted.");
        _log?.Invoke($"[SESSION] {session.ToSummary()}");
        return new AgentWorkflowResult
        {
            Success = false,
            Source = lastSource,
            CompileResult = lastResult,
            AttemptsUsed = _maxAttempts,
            Session = session
        };
    }

    /// <summary>
    /// Builds the system prompt: language spec + generation rules.
    /// </summary>
    internal static string BuildSystemPrompt()
    {
        return $@"{LanguageSpec.GetSpec()}

## Code Generation Rules

You are an expert Agentic programmer. When given a task, output ONLY valid Agentic source code.

1. The root MUST be (module Name …) containing defuns, tests, and optionally a main block.
2. Include (test …) blocks with (assert-eq …) to verify every function.
3. Use explicit type annotations on ALL function signatures: (defun f ((x : Num)) : Num …).
4. Use (require …) preconditions where appropriate (e.g. division by zero guards).
5. Do NOT output explanations, markdown, or comments — ONLY the S-expression source code.
6. Do NOT use any constructs not listed in the spec above.
7. Math is strictly binary: (+ a (+ b c)), never (+ a b c).
8. Initialize arrays with (arr.new N), strings with string literals.
9. The while loop body MUST be wrapped in (do …).
10. Tests MUST be self-contained. Do NOT use (sys.input.get …) inside tests — use literal values.
11. Place (sys.input.get …) calls ONLY in the main execution block AFTER all tests.
12. If the task involves an HTTP server/API, use server.get/server.post/server.json_get/server.json_post and server.listen. Do NOT read CLI args for server apps — the server handles request routing.";
    }

    /// <summary>
    /// Builds the initial user prompt for the first generation attempt.
    /// </summary>
    internal static string BuildInitialPrompt(string intent, string moduleName)
    {
        var lower = intent.ToLowerInvariant();
        bool isServerApp = ContainsAny(lower,
            "api", "endpoint", "server", "route", "listen", "port", "webhook");

        if (isServerApp)
        {
            return $@"Write an Agentic module named ""{moduleName}"" that does the following:

{intent}

Requirements:
- Define the core logic as (defun …) with explicit type annotations.
- Include at least one (test …) block with (assert-eq …) assertions to verify the logic functions.
- Tests MUST use literal values, NOT (sys.input.get …).
- Register HTTP routes with server.get/server.post/server.json_get/server.json_post.
- End with (server.listen 8080) to start the server.
- Do NOT use (sys.input.get …) anywhere — the server handles request routing.
- Output ONLY the Agentic source code. No explanations.";
        }

        return $@"Write an Agentic module named ""{moduleName}"" that does the following:

{intent}

Requirements:
- Define the core logic as (defun …) with explicit type annotations.
- Include at least one (test …) block with (assert-eq …) assertions to verify correctness.
- Tests MUST use literal values, NOT (sys.input.get …).
- AFTER the tests, include a main execution block that:
  1. Reads inputs from CLI args using (sys.input.get 0), (sys.input.get 1), etc.
  2. Calls the function with those inputs.
  3. Outputs the result with (sys.stdout.write …).
- Output ONLY the Agentic source code. No explanations.";
    }

    /// <summary>
    /// Builds the retry prompt with structured error feedback from the compiler.
    /// </summary>
    internal static string BuildRetryPrompt(string intent, string previousSource, CompileResult failedResult)
    {
        return $@"Your previous Agentic code failed to compile. Here are the structured errors:

{failedResult.ToSExpr()}

Previous source:
{previousSource}

Original task: {intent}

Fix ALL errors listed above. Output ONLY the corrected Agentic source code. No explanations.";
    }

    /// <summary>
    /// Strips markdown fences and whitespace that LLMs commonly add around code.
    /// </summary>
    internal static string CleanAgentOutput(string raw)
    {
        var trimmed = raw.Trim();

        if (trimmed.StartsWith("```"))
        {
            int firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
                trimmed = trimmed[(firstNewline + 1)..];
        }

        if (trimmed.EndsWith("```"))
            trimmed = trimmed[..^3];

        return trimmed.Trim();
    }

    /// <summary>
    /// Infers permissions from intent keywords so users don't have to
    /// manually pass --allow-http for obvious API/server tasks.
    /// </summary>
    public static Permissions InferPermissions(string intent)
    {
        var lower = intent.ToLowerInvariant();

        bool http = ContainsAny(lower,
            "api", "endpoint", "server", "http", "rest", "route", "get request",
            "post request", "listen", "port", "webhook", "url", "fetch");

        bool fileRead = ContainsAny(lower,
            "read file", "read from file", "load file", "open file", "parse file",
            "file input", "csv", "read csv", "log file");

        bool fileWrite = ContainsAny(lower,
            "write file", "write to file", "save file", "create file",
            "file output", "write csv", "log to file", "append file",
            "save to file", "output to file")
            || (lower.Contains("write") && lower.Contains("file"))
            || (lower.Contains("save") && lower.Contains("file"));

        bool env = ContainsAny(lower,
            "environment variable", "env var", "getenv", "config from env",
            "read env", "api key from env");

        bool db = ContainsAny(lower,
            "database", "sqlite", "crud", "persist", "store data", "save to db",
            "read from db", "insert into", "select from", "table", "sql");

        return new Permissions
        {
            AllowHttp = http,
            AllowFileRead = fileRead || fileWrite,
            AllowFileWrite = fileWrite,
            AllowEnv = env,
            AllowDb = db,
        };
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        foreach (var kw in keywords)
            if (text.Contains(kw))
                return true;
        return false;
    }

    /// <summary>
    /// Merges two permission sets — grants a capability if either set grants it.
    /// </summary>
    public static Permissions MergePermissions(Permissions a, Permissions b) => new()
    {
        AllowFileRead = a.AllowFileRead || b.AllowFileRead,
        AllowFileWrite = a.AllowFileWrite || b.AllowFileWrite,
        AllowHttp = a.AllowHttp || b.AllowHttp,
        AllowEnv = a.AllowEnv || b.AllowEnv,
        AllowDb = a.AllowDb || b.AllowDb,
    };
}

/// <summary>
/// Result of an intent-driven agent workflow run.
/// </summary>
public sealed record AgentWorkflowResult
{
    /// <summary>Whether the program compiled successfully (all tests passed).</summary>
    public required bool Success { get; init; }

    /// <summary>The final .ag source code generated by the agent.</summary>
    public string? Source { get; init; }

    /// <summary>The structured compile result from the last compilation attempt.</summary>
    public CompileResult? CompileResult { get; init; }

    /// <summary>How many LLM generation attempts were used.</summary>
    public int AttemptsUsed { get; init; }

    /// <summary>Session recording of all compilation attempts for audit/analysis.</summary>
    public CompilationSession? Session { get; init; }
}
