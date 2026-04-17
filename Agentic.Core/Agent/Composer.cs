using Agentic.Core.Execution;
using Agentic.Core.Syntax;

namespace Agentic.Core.Agent;

public sealed record ComposerOutcome(
    bool Success,
    string? Source,
    AstNode? Ast,
    FeedbackEnvelope? LastFeedback,
    int AttemptsUsed);

/// <summary>
/// Asks the LLM for the main program body given already-verified helper defuns,
/// then splices the helpers back in and verifies against the full
/// <see cref="ConstraintProfile.Tests"/>.
/// </summary>
public sealed class Composer
{
    private readonly IAgentClient _agent;
    private readonly int _maxAttempts;
    private readonly Action<string>? _log;

    public Composer(IAgentClient agent, int maxAttempts = 3, Action<string>? log = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _maxAttempts = maxAttempts;
        _log = log;
    }

    public async Task<ComposerOutcome> ComposeAsync(
        ConstraintProfile profile,
        IReadOnlyList<ImplementedFunction> helpers,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = BuildSystemPrompt(profile, helpers);
        var userPrompt = profile.Objective;
        string? lastCode = null;
        FeedbackEnvelope? lastFeedback = null;

        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            _log?.Invoke($"[COMPOSER] Attempt {attempt}/{_maxAttempts}");
            string raw = await _agent.GenerateCodeAsync(
                systemPrompt, userPrompt, lastCode, lastFeedback?.ToLlmFeedback(), cancellationToken);
            lastCode = raw;

            AstNode mainAst;
            try
            {
                mainAst = new Parser(new Lexer(raw).Tokenize()).Parse();
            }
            catch (Exception ex)
            {
                lastFeedback = FeedbackEnvelope.FromParseError(ex.Message);
                continue;
            }

            string composed = SpliceHelpers(raw, mainAst, helpers);
            AstNode composedAst;
            try
            {
                composedAst = new Parser(new Lexer(composed).Tokenize()).Parse();
            }
            catch (Exception ex)
            {
                lastFeedback = FeedbackEnvelope.FromParseError(ex.Message);
                continue;
            }

            var feedback = Orchestrator.RunFitnessTests(composedAst, profile);
            if (feedback.AllPassed)
            {
                return new ComposerOutcome(true, composed, composedAst, feedback, attempt);
            }
            lastFeedback = feedback;
        }

        return new ComposerOutcome(false, lastCode, null, lastFeedback, _maxAttempts);
    }

    /// <summary>
    /// Prepends the verified helper defuns into the (do …) the LLM produced,
    /// stripping any duplicate defuns the LLM emitted with the same names.
    /// </summary>
    internal static string SpliceHelpers(string mainSource, AstNode mainAst, IReadOnlyList<ImplementedFunction> helpers)
    {
        if (mainAst is not ListNode rootList || rootList.Elements.Count == 0 ||
            rootList.Elements[0] is not AtomNode head || head.Token.Value != "do")
        {
            return BuildDoBlock(helpers.Select(h => h.Source).Append(mainSource.Trim()));
        }

        var helperNames = new HashSet<string>(helpers.Select(h => h.Spec.Name));
        var keptElements = rootList.Elements.Skip(1)
            .Where(child => !IsDefunWithNameIn(child, helperNames))
            .Select(RenderSExpression);

        return BuildDoBlock(helpers.Select(h => h.Source).Concat(keptElements));
    }

    private static string BuildDoBlock(IEnumerable<string> statements)
    {
        var sb = new System.Text.StringBuilder("(do");
        foreach (var s in statements)
        {
            sb.Append(' ').Append(s);
        }
        sb.Append(')');
        return sb.ToString();
    }

    private static bool IsDefunWithNameIn(AstNode node, HashSet<string> names)
    {
        if (node is not ListNode list || list.Elements.Count < 2) return false;
        if (list.Elements[0] is not AtomNode head || head.Token.Value != "defun") return false;
        if (list.Elements[1] is not AtomNode nameAtom) return false;
        return names.Contains(nameAtom.Token.Value);
    }

    internal static string RenderSExpression(AstNode node) => node switch
    {
        AtomNode atom => atom.Token.Type == TokenType.String
            ? $"\"{atom.Token.Value}\""
            : atom.Token.Value,
        ListNode list => "(" + string.Join(' ', list.Elements.Select(RenderSExpression)) + ")",
        _ => throw new InvalidOperationException($"Unknown AST node: {node.GetType().Name}"),
    };

    private static string BuildSystemPrompt(ConstraintProfile profile, IReadOnlyList<ImplementedFunction> helpers)
    {
        var sb = new System.Text.StringBuilder(CompilerDefaults.GetSystemPrompt(profile));
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("COMPOSER MODE:");
        sb.AppendLine("The following helper functions are ALREADY VERIFIED. They will be spliced into your output automatically.");
        sb.AppendLine("Call them by name. Do NOT redefine them. If you include a (defun) with one of these names, it will be dropped.");
        foreach (var h in helpers)
        {
            sb.Append("  ").Append(h.Source).AppendLine();
            if (!string.IsNullOrWhiteSpace(h.Spec.Intent))
            {
                sb.Append("    ; intent: ").AppendLine(h.Spec.Intent);
            }
        }
        sb.AppendLine("Produce a single root (do …) program that satisfies the objective using these helpers.");
        return sb.ToString();
    }
}
