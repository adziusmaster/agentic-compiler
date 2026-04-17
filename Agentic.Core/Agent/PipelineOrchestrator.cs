using Agentic.Core.Execution;

namespace Agentic.Core.Agent;

/// <summary>
/// Runs the Planner → Implementer → Composer → Critic pipeline.
/// </summary>
/// <remarks>
/// Routing:
/// <list type="bullet">
///   <item>author declared <c>functions:</c> → pipeline, Planner is skipped.</item>
///   <item>no <c>functions:</c> but <c>pipeline: true</c> → Planner decomposes, then pipeline.</item>
///   <item>otherwise → single-shot <see cref="Orchestrator"/> (unchanged behavior).</item>
/// </list>
/// On Composer full-test failure the Critic is consulted exactly once and the
/// pipeline retries from the verdict's stage. The Critic is not a loop.
/// </remarks>
public sealed class PipelineOrchestrator
{
    private readonly IAgentClient _agent;
    private readonly int _maxAttempts;
    private readonly Action<string>? _log;
    private readonly bool _criticEnabled;

    public PipelineOrchestrator(IAgentClient agent, int maxAttempts = 3, Action<string>? log = null)
        : this(agent, maxAttempts, log, criticEnabled: true) { }

    private PipelineOrchestrator(IAgentClient agent, int maxAttempts, Action<string>? log, bool criticEnabled)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _maxAttempts = maxAttempts;
        _log = log;
        _criticEnabled = criticEnabled;
    }

    public async Task<PipelineResult> CompileAsync(
        ConstraintProfile profile,
        CancellationToken cancellationToken = default)
    {
        bool hasFunctions = profile.FunctionsOrEmpty.Count > 0;
        if (!hasFunctions && !profile.Pipeline)
        {
            _log?.Invoke("[PIPELINE] No functions and pipeline disabled — delegating to single-shot Orchestrator.");
            var single = await new Orchestrator(_agent, _maxAttempts, _log)
                .CompileAsync(profile, cancellationToken);
            return new PipelineResult(
                single.Success,
                single.LastGeneratedCode,
                single.Ast,
                System.Array.Empty<ImplementedFunction>(),
                single.LastFeedback,
                Stage: single.Success ? "single-shot" : "single-shot-failed");
        }

        IReadOnlyList<FunctionSpec> specs;
        if (hasFunctions)
        {
            specs = profile.FunctionsOrEmpty;
        }
        else
        {
            _log?.Invoke("[PIPELINE] Running Planner");
            var planner = new Planner(_agent, _maxAttempts, _log);
            var plan = await planner.PlanAsync(profile, cancellationToken);
            if (!plan.Success)
            {
                return new PipelineResult(
                    Success: false, FinalSource: null, Ast: null,
                    System.Array.Empty<ImplementedFunction>(),
                    plan.LastFeedback,
                    Stage: "planner");
            }
            specs = plan.Functions;
            _log?.Invoke($"[PIPELINE] Plan: {string.Join(", ", specs.Select(s => s.Name))}");
        }

        var verified = await ImplementAllAsync(specs, profile, cancellationToken);
        if (verified is not { } helpers) return _lastImplementerFailure!;

        var composed = await ComposeAsync(profile, helpers, cancellationToken);
        if (composed.Success)
        {
            return new PipelineResult(true, composed.Source, composed.Ast, helpers, composed.LastFeedback, "composed");
        }

        if (!_criticEnabled)
        {
            return new PipelineResult(
                false, composed.Source, null, helpers, composed.LastFeedback, Stage: "composer-failed");
        }

        _log?.Invoke("[PIPELINE] Composer failed — consulting Critic");
        var critic = new Critic(_agent, _log);
        var decision = await critic.DiagnoseAsync(
            profile, helpers, composed.Source, composed.LastFeedback!, cancellationToken);
        _log?.Invoke($"[CRITIC] verdict={decision.Verdict} helper={decision.HelperName} — {decision.Rationale}");

        return await ApplyVerdictAsync(profile, helpers, composed, decision, cancellationToken);
    }

    private PipelineResult? _lastImplementerFailure;

    private async Task<IReadOnlyList<ImplementedFunction>?> ImplementAllAsync(
        IReadOnlyList<FunctionSpec> specs,
        ConstraintProfile profile,
        CancellationToken cancellationToken)
    {
        var implementer = new Implementer(_agent, _maxAttempts, _log);
        var verified = new List<ImplementedFunction>(specs.Count);

        foreach (var spec in specs)
        {
            _log?.Invoke($"[PIPELINE] Implementing helper '{spec.Name}'");
            var outcome = await implementer.ImplementAsync(spec, profile, cancellationToken);
            if (!outcome.Success)
            {
                _log?.Invoke($"[PIPELINE] FAILED at helper '{spec.Name}'");
                _lastImplementerFailure = new PipelineResult(
                    Success: false,
                    FinalSource: outcome.Source,
                    Ast: null,
                    verified,
                    outcome.LastFeedback,
                    Stage: $"implementer:{spec.Name}");
                return null;
            }
            verified.Add(new ImplementedFunction(spec, outcome.Source!, outcome.AttemptsUsed));
        }
        return verified;
    }

    private async Task<ComposerOutcome> ComposeAsync(
        ConstraintProfile profile,
        IReadOnlyList<ImplementedFunction> helpers,
        CancellationToken cancellationToken)
    {
        _log?.Invoke("[PIPELINE] Composing main body");
        return await new Composer(_agent, _maxAttempts, _log)
            .ComposeAsync(profile, helpers, cancellationToken);
    }

    private async Task<PipelineResult> ApplyVerdictAsync(
        ConstraintProfile profile,
        IReadOnlyList<ImplementedFunction> helpers,
        ComposerOutcome originalComposed,
        CriticDecision decision,
        CancellationToken cancellationToken)
    {
        switch (decision.Verdict)
        {
            case CriticVerdict.Recompose:
            {
                var retry = await ComposeAsync(profile, helpers, cancellationToken);
                return new PipelineResult(
                    retry.Success,
                    retry.Source ?? originalComposed.Source,
                    retry.Ast,
                    helpers,
                    retry.LastFeedback,
                    Stage: retry.Success ? "composed-after-recompose" : "composer-failed-after-recompose");
            }

            case CriticVerdict.ReimplementHelper when decision.HelperName is { } targetName:
            {
                var targetSpec = helpers.First(h => h.Spec.Name == targetName).Spec;
                var implementer = new Implementer(_agent, _maxAttempts, _log);
                var outcome = await implementer.ImplementAsync(targetSpec, profile, cancellationToken);
                if (!outcome.Success)
                {
                    return new PipelineResult(
                        false, outcome.Source, null, helpers, outcome.LastFeedback,
                        Stage: $"implementer:{targetName}-after-critic");
                }
                var rebuilt = helpers
                    .Select(h => h.Spec.Name == targetName
                        ? new ImplementedFunction(targetSpec, outcome.Source!, outcome.AttemptsUsed)
                        : h)
                    .ToList();
                var retry = await ComposeAsync(profile, rebuilt, cancellationToken);
                return new PipelineResult(
                    retry.Success,
                    retry.Source,
                    retry.Ast,
                    rebuilt,
                    retry.LastFeedback,
                    Stage: retry.Success ? $"composed-after-reimplement:{targetName}" : $"composer-failed-after-reimplement:{targetName}");
            }

            case CriticVerdict.Replan:
            {
                var freshProfile = profile with { Functions = null, Pipeline = true };
                _log?.Invoke("[PIPELINE] Critic requested replan — re-running pipeline from scratch (critic disabled for inner run)");
                var inner = new PipelineOrchestrator(_agent, _maxAttempts, _log, criticEnabled: false);
                var result = await inner.CompileAsync(freshProfile, cancellationToken);
                return result with { Stage = result.Stage + "-after-replan" };
            }

            default:
                return new PipelineResult(
                    false, originalComposed.Source, null, helpers,
                    originalComposed.LastFeedback, Stage: "composer-failed-no-verdict");
        }
    }
}
