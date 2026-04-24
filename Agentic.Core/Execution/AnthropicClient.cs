using System.Text;
using System.Text.Json;
using Agentic.Core.Agent;

namespace Agentic.Core.Execution;

/// <summary>
/// IAgentClient implementation backed by the Anthropic Messages API.
/// </summary>
public sealed class AnthropicClient : IAgentClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    public AnthropicClient(string apiKey, string model = "claude-sonnet-4-6")
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
        string combinedPrompt = previousCode == null
            ? userConstraint
            : $"{userConstraint}\n\n[CRITICAL SYSTEM FAULT]\nYour previous attempt produced this S-expression:\n\n{previousCode}\n\nIt failed with this diagnostic:\n{previousError}\n\nStudy the fault in your own code above. Return ONLY a corrected, structurally valid S-expression that satisfies all constraints and passes every test.";

        var payload = new
        {
            model = _model,
            max_tokens = 4096,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = combinedPrompt } },
            temperature = 0.0
        };

        string jsonPayload = JsonSerializer.Serialize(payload);

        int maxRetries = 3;
        for (int i = 0; i < maxRetries; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(responseString);

                string rawOutput = doc.RootElement
                    .GetProperty("content")[0]
                    .GetProperty("text")
                    .GetString() ?? string.Empty;

                if (doc.RootElement.TryGetProperty("usage", out var usage))
                {
                    int tin = usage.TryGetProperty("input_tokens", out var p) ? p.GetInt32() : 0;
                    int tout = usage.TryGetProperty("output_tokens", out var c) ? c.GetInt32() : 0;
                    Console.WriteLine($"[TOKENS in={tin} out={tout}]");
                }

                return AgentClientHelpers.CleanMarkdown(rawOutput);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable
                || response.StatusCode == (System.Net.HttpStatusCode)429)
            {
                Console.WriteLine($"  [API JITTER] {response.StatusCode}. Retrying in 2 seconds...");
                await Task.Delay(2000, cancellationToken);
                continue;
            }

            string error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"Anthropic API Failure: {response.StatusCode} - {error}");
        }

        throw new Exception("Anthropic API failed after maximum retries.");
    }
}
