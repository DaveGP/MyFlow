using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace MyFlow.Core.LanguageModels;

/// <summary>Optional cloud cleanup via OpenAI's Chat Completions API. The key comes from the Keychain.</summary>
public sealed class OpenAILanguageModel(HttpClient http, Func<string?> apiKey, string model) : ILanguageModel
{
    public string DisplayName => $"OpenAI ({model})";

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(apiKey()));

    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var key = apiKey() ?? throw new InvalidOperationException("No OpenAI API key saved.");
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userMessage },
            },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // Never log the request (it has the key); the status code is enough.
            throw new HttpRequestException($"OpenAI returned {(int)response.StatusCode}.");
        }
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
        return (string?)json?["choices"]?[0]?["message"]?["content"]
               ?? throw new InvalidOperationException("OpenAI returned no text.");
    }
}
