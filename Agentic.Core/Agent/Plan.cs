using Agentic.Core.Execution;

namespace Agentic.Core.Agent;

/// <summary>
/// Decomposed view of a compilation target.
/// </summary>
/// <remarks>
/// Today the Plan comes straight from the author's <c>functions:</c> block.
/// Future work: an LLM-driven Planner that derives subfunctions from a bare objective.
/// </remarks>
public sealed record Plan(
    string Objective,
    IReadOnlyList<FunctionSpec> Functions);

public sealed record ImplementedFunction(
    FunctionSpec Spec,
    string Source,
    int AttemptsUsed);

public sealed record PipelineResult(
    bool Success,
    string? FinalSource,
    Syntax.AstNode? Ast,
    IReadOnlyList<ImplementedFunction> VerifiedFunctions,
    FeedbackEnvelope? LastFeedback,
    string Stage);
