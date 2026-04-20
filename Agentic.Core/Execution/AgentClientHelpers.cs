namespace Agentic.Core.Execution;

/// <summary>
/// Provider-independent helpers for IAgentClient implementations.
/// </summary>
internal static class AgentClientHelpers
{
    /// <summary>
    /// Strips markdown code fences that LLMs frequently wrap around code output.
    /// </summary>
    public static string CleanMarkdown(string input)
    {
        input = input.Trim();
        if (input.StartsWith("```"))
        {
            var lines = input.Split('\n').ToList();
            if (lines.Count > 2)
            {
                lines.RemoveAt(0);
                lines.RemoveAt(lines.Count - 1);
                return string.Join('\n', lines).Trim();
            }
        }
        return input;
    }
}
