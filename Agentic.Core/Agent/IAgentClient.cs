namespace Agentic.Core.Agent;

/// <summary>
/// Outbound port for the LLM that generates Agentic S-expressions.
/// </summary>
/// <remarks>
/// Production code uses <c>Agentic.Core.Execution.AgentClient</c>; tests inject
/// a substitute so the Orchestrator loop can be exercised offline.
/// </remarks>
public interface IAgentClient
{
    Task<string> GenerateCodeAsync(
        string systemPrompt,
        string userConstraint,
        string? previousCode = null,
        string? previousError = null,
        CancellationToken cancellationToken = default);
}
