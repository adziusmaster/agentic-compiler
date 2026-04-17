using Agentic.Core.Execution;
using Agentic.Core.Syntax;

namespace Agentic.Core.Agent;

public enum CriticVerdict
{
    Recompose,
    ReimplementHelper,
    Replan,
}

public sealed record CriticDecision(
    CriticVerdict Verdict,
    string? HelperName,
    string Rationale);

/// <summary>
/// Given a failing composed program, asks the LLM which stage of the pipeline is most
/// likely at fault. Emits a single <c>(verdict …)</c> S-expression that the orchestrator
/// turns into a concrete recovery action.
/// </summary>
/// <remarks>
/// The Critic never writes code itself — it only decides WHICH stage gets re-run.
/// That keeps the decision loop bounded (there are only three verdicts) and
/// avoids letting the LLM silently rewrite a verified helper.
/// </remarks>
public sealed class Critic
{
    private readonly IAgentClient _agent;
    private readonly Action<string>? _log;

    public Critic(IAgentClient agent, Action<string>? log = null)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _log = log;
    }

    public async Task<CriticDecision> DiagnoseAsync(
        ConstraintProfile profile,
        IReadOnlyList<ImplementedFunction> helpers,
        string? composedSource,
        FeedbackEnvelope failingFeedback,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = BuildSystemPrompt(helpers);
        var userPrompt = BuildUserPrompt(profile, helpers, composedSource, failingFeedback);

        string raw;
        try
        {
            raw = await _agent.GenerateCodeAsync(systemPrompt, userPrompt, null, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[CRITIC] Agent call failed: {ex.Message}. Defaulting to Recompose.");
            return new CriticDecision(CriticVerdict.Recompose, null, "Critic agent failed; retrying composer.");
        }

        try
        {
            return ParseVerdict(raw, helpers);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[CRITIC] Malformed verdict: {ex.Message}. Defaulting to Recompose.");
            return new CriticDecision(CriticVerdict.Recompose, null, "Critic output unparseable; retrying composer.");
        }
    }

    internal static CriticDecision ParseVerdict(string raw, IReadOnlyList<ImplementedFunction> helpers)
    {
        var ast = new Parser(new Lexer(raw).Tokenize()).Parse();
        if (ast is not ListNode list || list.Elements.Count < 2 ||
            list.Elements[0] is not AtomNode head || head.Token.Value != "verdict")
        {
            throw new InvalidOperationException("Expected (verdict <recompose|reimplement|replan> …).");
        }

        if (list.Elements[1] is not AtomNode kindAtom)
            throw new InvalidOperationException("Verdict kind must be an atom.");

        string kind = kindAtom.Token.Value;
        string rationale = list.Elements.Count >= 3
            ? RationaleFrom(list.Elements[^1])
            : string.Empty;

        return kind switch
        {
            "recompose" => new CriticDecision(CriticVerdict.Recompose, null, rationale),
            "replan"    => new CriticDecision(CriticVerdict.Replan, null, rationale),
            "reimplement" => ParseReimplement(list, helpers, rationale),
            _ => throw new InvalidOperationException($"Unknown verdict kind '{kind}'. Use recompose/reimplement/replan."),
        };
    }

    private static CriticDecision ParseReimplement(
        ListNode list,
        IReadOnlyList<ImplementedFunction> helpers,
        string rationale)
    {
        if (list.Elements.Count < 3 || list.Elements[2] is not AtomNode nameAtom)
            throw new InvalidOperationException("(verdict reimplement …) requires a helper name as the 3rd atom.");

        string name = nameAtom.Token.Value;
        if (!helpers.Any(h => h.Spec.Name == name))
        {
            throw new InvalidOperationException(
                $"Unknown helper '{name}'. Choose one of: {string.Join(", ", helpers.Select(h => h.Spec.Name))}.");
        }
        return new CriticDecision(CriticVerdict.ReimplementHelper, name, rationale);
    }

    private static string RationaleFrom(AstNode node) =>
        node is AtomNode a && a.Token.Type == TokenType.String ? a.Token.Value : string.Empty;

    private static string BuildSystemPrompt(IReadOnlyList<ImplementedFunction> helpers)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are a failure diagnostician for a pipelined compiler.");
        sb.AppendLine("The pipeline has three stages: Planner (decomposition), Implementer (one helper function at a time, verified against micro-tests), Composer (main body that calls the helpers).");
        sb.AppendLine("A composed program failed its full-profile tests. Decide which stage to re-run.");
        sb.AppendLine();
        sb.AppendLine("Output EXACTLY one S-expression of the form:");
        sb.AppendLine("  (verdict recompose \"<why>\")                      ; main body is wrong; helpers are fine");
        sb.AppendLine("  (verdict reimplement <helperName> \"<why>\")       ; a specific helper is buggy");
        sb.AppendLine("  (verdict replan \"<why>\")                          ; the decomposition itself is wrong");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- Prefer `recompose` if the helpers' micro-tests all passed and the failure looks like wrong glue logic.");
        sb.AppendLine("- Choose `reimplement` only if a top-level failure strongly suggests a specific helper returns wrong values, AND that helper's micro-tests are thin.");
        sb.AppendLine("- Use `replan` as a last resort — it discards all verified helpers.");
        if (helpers.Count > 0)
        {
            sb.AppendLine();
            sb.Append("Helper names you may target: ")
              .AppendLine(string.Join(", ", helpers.Select(h => h.Spec.Name)));
        }
    return sb.ToString();
    }

    private static string BuildUserPrompt(
        ConstraintProfile profile,
        IReadOnlyList<ImplementedFunction> helpers,
        string? composedSource,
        FeedbackEnvelope failingFeedback)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("OBJECTIVE:");
        sb.AppendLine(profile.Objective);
        sb.AppendLine();
        sb.AppendLine("VERIFIED HELPERS (passed their micro-tests):");
        foreach (var h in helpers)
        {
            sb.Append("  ").AppendLine(h.Source);
        }
        if (!string.IsNullOrWhiteSpace(composedSource))
        {
            sb.AppendLine();
            sb.AppendLine("COMPOSED PROGRAM:");
            sb.AppendLine(composedSource);
        }
        sb.AppendLine();
        sb.AppendLine("FAILING FEEDBACK:");
        sb.AppendLine(failingFeedback.ToLlmFeedback());
        sb.AppendLine();
        sb.Append("Emit ONLY the (verdict …) S-expression.");
        return sb.ToString();
    }
}
