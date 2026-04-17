using System.Text;
using System.Text.Json;
using Agentic.Core.Agent;

namespace Agentic.Core.Execution;

public sealed class AgentClient : IAgentClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    public AgentClient(string apiKey, string model = "gemini-2.5-flash")
    {
        _httpClient = new HttpClient();
        _apiKey = apiKey;
        _model = model;
    }

    public async Task<string> GenerateCodeAsync(
        string systemPrompt,
        string userConstraint,
        string? previousCode = null,
        string? previousError = null,
        CancellationToken cancellationToken = default)
    {
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

        string combinedPrompt = previousCode == null
            ? userConstraint
            : $"{userConstraint}\n\n[CRITICAL SYSTEM FAULT]\nYour previous attempt produced this S-expression:\n\n{previousCode}\n\nIt failed with this diagnostic:\n{previousError}\n\nStudy the fault in your own code above. Return ONLY a corrected, structurally valid S-expression that satisfies all constraints and passes every test.";

        var payload = new
        {
            systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = combinedPrompt } } } },
            generationConfig = new { temperature = 0.0 }
        };

        string jsonPayload = JsonSerializer.Serialize(payload);

        int maxRetries = 3;
        for (int i = 0; i < maxRetries; i++)
        {
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(responseString);

                string rawOutput = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString() ?? string.Empty;

                return CleanMarkdown(rawOutput);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || response.StatusCode == (System.Net.HttpStatusCode)429)
            {
                Console.WriteLine($"  [API JITTER] {response.StatusCode}. Retrying in 2 seconds...");
                await Task.Delay(2000, cancellationToken);
                continue;
            }

            string error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"API Failure: {response.StatusCode} - {error}");
        }

        throw new Exception("Agent API failed after maximum retries.");
    }

    private string CleanMarkdown(string input)
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