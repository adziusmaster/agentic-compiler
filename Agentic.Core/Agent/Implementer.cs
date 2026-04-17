using Agentic.Core.Execution;
using Agentic.Core.Syntax;

namespace Agentic.Core.Agent;

public sealed record ImplementerOutcome(
    bool Success,
    string? Source,
    FeedbackEnvelope? LastFeedback,
    int AttemptsUsed);

/// <summary>
/// Generates one <c>(defun …)</c> at a time and verifies it against the
/// micro-tests declared in its <see cref="FunctionSpec"/>. Retries up to
/// <c>maxAttempts</c> with structured feedback.
/// </summary>
public sealed class Implementer
{
    private readonly IAgentClient _agent;
    private readonly int _maxAttempts;
    private readonly Action<string>? _log;

    public Implementer(IAgentClient agent, int maxAttempts = 3, Action<string>? log = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _maxAttempts = maxAttempts;
        _log = log;
    }

    public async Task<ImplementerOutcome> ImplementAsync(
        FunctionSpec spec,
        ConstraintProfile profile,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = BuildSystemPrompt(profile);
        var userPrompt = BuildUserPrompt(spec);
        string? lastCode = null;
        FeedbackEnvelope? lastFeedback = null;

        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            _log?.Invoke($"[IMPLEMENTER:{spec.Name}] Attempt {attempt}/{_maxAttempts}");
            string raw = await _agent.GenerateCodeAsync(
                systemPrompt,
                userPrompt,
                lastCode,
                lastFeedback?.ToLlmFeedback(),
                cancellationToken);
            lastCode = raw;

            string? defunSource;
            try
            {
                defunSource = ExtractDefun(raw, spec.Name);
            }
            catch (Exception ex)
            {
                lastFeedback = FeedbackEnvelope.FromParseError(ex.Message);
                continue;
            }

            var feedback = RunMicroTests(defunSource, spec);
            if (feedback.AllPassed)
            {
                return new ImplementerOutcome(true, defunSource, feedback, attempt);
            }
            lastFeedback = feedback;
        }

        return new ImplementerOutcome(false, lastCode, lastFeedback, _maxAttempts);
    }

    internal static FeedbackEnvelope RunMicroTests(string defunSource, FunctionSpec spec)
    {
        var outcomes = new List<TestOutcome>(spec.Tests.Count);
        foreach (var test in spec.Tests)
        {
            string inputsLiteral = string.Join(' ', test.Inputs);
            string probeSource = $"(do {defunSource} (sys.stdout.write ({spec.Name} {inputsLiteral})))";

            AstNode probeAst;
            try
            {
                probeAst = new Parser(new Lexer(probeSource).Tokenize()).Parse();
            }
            catch (Exception ex)
            {
                outcomes.Add(new TestOutcome(
                    test.Inputs, test.Expect, Actual: null,
                    TestStatus.Crashed, "ParseException", ex.Message));
                continue;
            }

            var verifier = new Verifier(Array.Empty<string>());
            try
            {
                verifier.Evaluate(probeAst);
            }
            catch (Exception ex)
            {
                outcomes.Add(new TestOutcome(
                    test.Inputs, test.Expect, Actual: null,
                    TestStatus.Crashed, ex.GetType().Name, ex.Message));
                continue;
            }

            var status = verifier.CapturedOutput == test.Expect ? TestStatus.Passed : TestStatus.Failed;
            outcomes.Add(new TestOutcome(
                test.Inputs, test.Expect, verifier.CapturedOutput, status));
        }
        return new FeedbackEnvelope(outcomes);
    }

    /// <summary>
    /// Pulls a <c>(defun name …)</c> out of the LLM's response. Accepts either
    /// a bare defun or one wrapped in <c>(do (defun …))</c>.
    /// </summary>
    internal static string ExtractDefun(string raw, string expectedName)
    {
        var ast = new Parser(new Lexer(raw).Tokenize()).Parse();
        if (ast is not ListNode list || list.Elements.Count == 0)
            throw new InvalidOperationException("Expected an S-expression, got an atom.");

        if (IsDefunFor(list, expectedName))
            return raw.Trim();

        if (list.Elements[0] is AtomNode head && head.Token.Value == "do")
        {
            foreach (var child in list.Elements.Skip(1))
            {
                if (child is ListNode childList && IsDefunFor(childList, expectedName))
                {
                    return RenderSExpression(childList);
                }
            }
        }

        throw new InvalidOperationException(
            $"Output did not contain a (defun {expectedName} …). Return a single defun for this function.");
    }

    private static bool IsDefunFor(ListNode list, string expectedName) =>
        list.Elements.Count >= 3
        && list.Elements[0] is AtomNode head && head.Token.Value == "defun"
        && list.Elements[1] is AtomNode nameAtom && nameAtom.Token.Value == expectedName;

    private static string RenderSExpression(AstNode node) => node switch
    {
        AtomNode atom => atom.Token.Type == TokenType.String
            ? $"\"{atom.Token.Value}\""
            : atom.Token.Value,
        ListNode list => "(" + string.Join(' ', list.Elements.Select(RenderSExpression)) + ")",
        _ => throw new InvalidOperationException($"Unknown AST node: {node.GetType().Name}"),
    };

    private static string BuildSystemPrompt(ConstraintProfile profile)
    {
        return CompilerDefaults.GetSystemPrompt(profile) + @"

IMPLEMENTER MODE:
You are implementing ONE helper function in isolation. Output EXACTLY ONE (defun name (args) (do …)) form.
Do NOT wrap it in (do …). Do NOT emit any other top-level forms. No sys.stdout.write at the top level.
Every exit path of the function MUST end with (return value). Recursion is permitted; always include a terminating branch.";
    }

    private static string BuildUserPrompt(FunctionSpec spec)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Function to implement: ").Append(spec.Name).AppendLine();
        sb.Append("Signature: (").Append(spec.Name);
        foreach (var p in spec.Parameters) sb.Append(' ').Append(p);
        sb.AppendLine(")");
        if (!string.IsNullOrWhiteSpace(spec.Intent))
        {
            sb.Append("Intent: ").AppendLine(spec.Intent);
        }
        sb.AppendLine("Micro-tests this function must satisfy:");
        foreach (var t in spec.Tests)
        {
            sb.Append("  (").Append(spec.Name).Append(' ')
              .Append(string.Join(' ', t.Inputs)).Append(") => ").AppendLine(t.Expect);
        }
        sb.AppendLine();
        sb.Append("Output ONLY the (defun ").Append(spec.Name).Append(" …) form.");
        return sb.ToString();
    }
}
