using System.Text;
using System.Text.Json;
using Agentic.Core.Agent;

namespace Agentic.Core.Execution;

/// <summary>
/// IAgentClient implementation backed by the OpenAI Chat Completions API.
/// </summary>
public sealed class OpenAiClient : IAgentClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    public OpenAiClient(string apiKey, string model = "gpt-4o")
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
            temperature = 0.0,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = combinedPrompt }
            }
        };

        string jsonPayload = JsonSerializer.Serialize(payload);

        int maxRetries = 3;
        for (int i = 0; i < maxRetries; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(responseString);

                string rawOutput = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? string.Empty;

                if (doc.RootElement.TryGetProperty("usage", out var usage))
                {
                    int tin = usage.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : 0;
                    int tout = usage.TryGetProperty("completion_tokens", out var c) ? c.GetInt32() : 0;
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
            throw new Exception($"OpenAI API Failure: {response.StatusCode} - {error}");
        }

        throw new Exception("OpenAI API failed after maximum retries.");
    }
}
