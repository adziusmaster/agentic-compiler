using Agentic.Core.Execution;

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
    private readonly Action<string>? _log;

    public AgentWorkflow(
        IAgentClient agent,
        int maxAttempts = 3,
        bool emitBinary = true,
        Action<string>? log = null)
    {
        _agent = agent;
        _maxAttempts = maxAttempts;
        _emitBinary = emitBinary;
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

            var compiler = new Compiler(_emitBinary);
            lastResult = compiler.Compile(source, moduleName);
            session.Record(source, lastResult);

            if (lastResult.Success)
            {
                _log?.Invoke($"[SUCCESS] Compiled on attempt {attempt}. Tests: {lastResult.TestsPassed}/{lastResult.TestsPassed + lastResult.TestsFailed}");
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

            _log?.Invoke($"[RETRY] Compilation failed: {lastResult.Diagnostics[0].Message}");
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
9. The while loop body MUST be wrapped in (do …).";
    }

    /// <summary>
    /// Builds the initial user prompt for the first generation attempt.
    /// </summary>
    internal static string BuildInitialPrompt(string intent, string moduleName)
    {
        return $@"Write an Agentic module named ""{moduleName}"" that does the following:

{intent}

Requirements:
- Define the core logic as (defun …) with explicit type annotations.
- Include at least one (test …) block with (assert-eq …) assertions to verify correctness.
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
