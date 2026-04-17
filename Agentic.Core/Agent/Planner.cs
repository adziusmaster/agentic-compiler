using Agentic.Core.Execution;
using Agentic.Core.Syntax;

namespace Agentic.Core.Agent;

public sealed record PlannerOutcome(
    bool Success,
    IReadOnlyList<FunctionSpec> Functions,
    FeedbackEnvelope? LastFeedback,
    int AttemptsUsed);

/// <summary>
/// Derives helper <see cref="FunctionSpec"/>s from a bare objective by asking
/// the LLM to emit a <c>(plan …)</c> S-expression. Used only when the author
/// did not pre-declare a <c>functions:</c> block and the profile opts into pipeline mode.
/// </summary>
public sealed class Planner
{
    private readonly IAgentClient _agent;
    private readonly int _maxAttempts;
    private readonly Action<string>? _log;

    public Planner(IAgentClient agent, int maxAttempts = 3, Action<string>? log = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _maxAttempts = maxAttempts;
        _log = log;
    }

    public async Task<PlannerOutcome> PlanAsync(
        ConstraintProfile profile,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = BuildSystemPrompt(profile);
        var userPrompt = BuildUserPrompt(profile);
        string? lastCode = null;
        FeedbackEnvelope? lastFeedback = null;

        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            _log?.Invoke($"[PLANNER] Attempt {attempt}/{_maxAttempts}");
            string raw = await _agent.GenerateCodeAsync(
                systemPrompt, userPrompt, lastCode, lastFeedback?.ToLlmFeedback(), cancellationToken);
            lastCode = raw;

            try
            {
                var specs = ParsePlan(raw);
                if (specs.Count == 0)
                {
                    lastFeedback = FeedbackEnvelope.FromParseError(
                        "Plan contained zero helper functions. Emit at least one (fn …).");
                    continue;
                }
                return new PlannerOutcome(true, specs, null, attempt);
            }
            catch (Exception ex)
            {
                lastFeedback = FeedbackEnvelope.FromParseError(ex.Message);
                _log?.Invoke($"[PLANNER] Parse error: {ex.Message}");
            }
        }

        return new PlannerOutcome(false, Array.Empty<FunctionSpec>(), lastFeedback, _maxAttempts);
    }

    /// <summary>
    /// Parses a <c>(plan (fn name (p1 p2) "intent" (test (in1 in2) out) …) …)</c> form.
    /// </summary>
    internal static IReadOnlyList<FunctionSpec> ParsePlan(string raw)
    {
        var ast = new Parser(new Lexer(raw).Tokenize()).Parse();
        if (ast is not ListNode root || root.Elements.Count == 0 ||
            root.Elements[0] is not AtomNode head || head.Token.Value != "plan")
        {
            throw new InvalidOperationException("Expected a (plan …) form at the root.");
        }

        var specs = new List<FunctionSpec>();
        foreach (var child in root.Elements.Skip(1))
        {
            specs.Add(ParseFunctionSpec(child));
        }
        return specs;
    }

    private static FunctionSpec ParseFunctionSpec(AstNode node)
    {
        if (node is not ListNode fn || fn.Elements.Count < 3 ||
            fn.Elements[0] is not AtomNode head || head.Token.Value != "fn")
        {
            throw new InvalidOperationException("Each plan entry must be (fn name (params…) \"intent\" (test …)* ).");
        }

        if (fn.Elements[1] is not AtomNode nameAtom)
            throw new InvalidOperationException("(fn …) is missing a name atom.");
        string name = nameAtom.Token.Value;

        if (fn.Elements[2] is not ListNode paramsList)
            throw new InvalidOperationException($"(fn {name} …) is missing a parameter list.");
        var parameters = new List<string>(paramsList.Elements.Count);
        foreach (var p in paramsList.Elements)
        {
            if (p is not AtomNode pa) throw new InvalidOperationException($"Non-atom parameter in (fn {name} …).");
            parameters.Add(pa.Token.Value);
        }

        string intent = string.Empty;
        int testStart = 3;
        if (fn.Elements.Count > 3 && fn.Elements[3] is AtomNode intentAtom && intentAtom.Token.Type == TokenType.String)
        {
            intent = intentAtom.Token.Value;
            testStart = 4;
        }

        var tests = new List<FunctionTest>();
        for (int i = testStart; i < fn.Elements.Count; i++)
        {
            tests.Add(ParseFunctionTest(fn.Elements[i], name));
        }

        return new FunctionSpec(name, parameters, intent, tests);
    }

    private static FunctionTest ParseFunctionTest(AstNode node, string fnName)
    {
        if (node is not ListNode list || list.Elements.Count != 3 ||
            list.Elements[0] is not AtomNode head || head.Token.Value != "test")
        {
            throw new InvalidOperationException($"(fn {fnName} …) test entry must be (test (inputs…) expect).");
        }

        if (list.Elements[1] is not ListNode inputsList)
            throw new InvalidOperationException($"(fn {fnName} …) test inputs must be a list.");
        var inputs = new List<string>(inputsList.Elements.Count);
        foreach (var input in inputsList.Elements)
        {
            if (input is not AtomNode ia)
                throw new InvalidOperationException($"(fn {fnName} …) test input must be an atom (number/identifier).");
            inputs.Add(ia.Token.Value);
        }

        if (list.Elements[2] is not AtomNode expectAtom)
            throw new InvalidOperationException($"(fn {fnName} …) test expect must be an atom.");
        return new FunctionTest(inputs, expectAtom.Token.Value);
    }

    private static string BuildSystemPrompt(ConstraintProfile profile)
    {
        return CompilerDefaults.GetSystemPrompt(profile) + @"

PLANNER MODE:
You decompose the objective into small helper functions, each verifiable in isolation.
Output ONLY a single S-expression of the form:
  (plan
    (fn <name> (<param1> <param2>…) ""<intent>""
      (test (<input1> <input2>…) <expect>)
      (test (<input1> <input2>…) <expect>))
    (fn …))

Rules:
- Each helper is a single pure numeric function. Parameters and return value are numbers.
- Inputs and expected value in (test …) are numeric literals. DO NOT nest S-expressions inside a test.
- Include at least 2 tests per helper where possible. Pick inputs that will actually be used by the main program (e.g. the same values that appear in the top-level tests).
- DO NOT emit any (defun …) — that's the Implementer's job.
- DO NOT emit a main body — that's the Composer's job.
- Prefer 1–3 helpers. If the program is trivial, emit a single helper that covers its core logic.";
    }

    private static string BuildUserPrompt(ConstraintProfile profile)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Objective:");
        sb.AppendLine(profile.Objective);
        if (profile.Tests.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Top-level tests the final program must pass (use these input values when designing your helper micro-tests):");
            foreach (var t in profile.Tests)
            {
                sb.Append("  inputs=[").Append(string.Join(",", t.Inputs))
                  .Append("] expect_stdout=").AppendLine(t.ExpectStdout);
            }
        }
        sb.AppendLine();
        sb.Append("Emit ONLY the (plan …) S-expression.");
        return sb.ToString();
    }
}
