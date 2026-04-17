using Agentic.Core.Execution;
using Agentic.Core.Syntax;

namespace Agentic.Core.Agent;

public sealed record OrchestrationResult(
    bool Success,
    AstNode? Ast,
    string? LastGeneratedCode,
    FeedbackEnvelope? LastFeedback,
    int AttemptsUsed);

/// <summary>
/// Drives the LLM retry loop: generate → parse → verify → feed back.
/// </summary>
/// <remarks>
/// Extracted from <c>Agentic.Cli/Program.cs</c> so the pipeline can be tested
/// offline and composed into later stages (Planner/Implementer/Critic).
/// </remarks>
public sealed class Orchestrator
{
    private readonly IAgentClient _agent;
    private readonly int _maxAttempts;
    private readonly Action<string>? _log;

    public Orchestrator(IAgentClient agent, int maxAttempts = 3, Action<string>? log = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _maxAttempts = maxAttempts;
        _log = log;
    }

    public async Task<OrchestrationResult> CompileAsync(
        ConstraintProfile profile,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = CompilerDefaults.GetSystemPrompt(profile);
        string? lastCode = null;
        FeedbackEnvelope? lastFeedback = null;

        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            _log?.Invoke($"[AGENT] Generating AST (Attempt {attempt}/{_maxAttempts})...");

            string output = await _agent.GenerateCodeAsync(
                systemPrompt,
                profile.Objective,
                lastCode,
                lastFeedback?.ToLlmFeedback(),
                cancellationToken);
            lastCode = output;

            AstNode ast;
            try
            {
                ast = new Parser(new Lexer(output).Tokenize()).Parse();
            }
            catch (Exception ex)
            {
                lastFeedback = FeedbackEnvelope.FromParseError(ex.Message);
                _log?.Invoke($"[COMPILER TRAP] {ex.Message}");
                continue;
            }

            var feedback = RunFitnessTests(ast, profile);
            if (feedback.AllPassed)
            {
                _log?.Invoke("[SUCCESS] Constraints passed.");
                return new OrchestrationResult(true, ast, output, feedback, attempt);
            }

            lastFeedback = feedback;
            _log?.Invoke($"[VERIFIER FAULT] {feedback.PassedCount}/{feedback.TotalCount} passed.");
        }

        return new OrchestrationResult(false, null, lastCode, lastFeedback, _maxAttempts);
    }

    internal static FeedbackEnvelope RunFitnessTests(AstNode ast, ConstraintProfile profile)
    {
        var outcomes = new List<TestOutcome>(profile.Tests.Count);
        foreach (var test in profile.Tests)
        {
            var verifier = new Verifier(test.Inputs.ToArray());
            try
            {
                verifier.Evaluate(ast);
            }
            catch (Exception ex)
            {
                outcomes.Add(new TestOutcome(
                    test.Inputs,
                    test.ExpectStdout,
                    Actual: null,
                    TestStatus.Crashed,
                    ex.GetType().Name,
                    ex.Message));
                continue;
            }

            var status = verifier.CapturedOutput == test.ExpectStdout
                ? TestStatus.Passed
                : TestStatus.Failed;
            outcomes.Add(new TestOutcome(
                test.Inputs,
                test.ExpectStdout,
                verifier.CapturedOutput,
                status));
        }

        return new FeedbackEnvelope(outcomes);
    }
}
