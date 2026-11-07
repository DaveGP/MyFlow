using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace MyFlow.Core.LanguageModels;

/// <summary>Talks to a local Ollama server (https://ollama.com) over its HTTP API.</summary>
public sealed class OllamaLanguageModel(HttpClient http, string baseUrl, string model) : ILanguageModel
{
    private readonly Uri _base = new(baseUrl.TrimEnd('/') + "/");

    public string DisplayName => $"Ollama ({model})";

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            var tags = await http.GetFromJsonAsync<JsonObject>(new Uri(_base, "api/tags"), cts.Token);
            var models = tags?["models"]?.AsArray();
            return models != null && models.Any(m =>
                string.Equals((string?)m?["name"], model, StringComparison.OrdinalIgnoreCase) ||
                string.Equals((string?)m?["model"], model, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Loads the model into memory ahead of the first dictation so cleanup is fast.</summary>
    public async Task WarmUpAsync(CancellationToken ct = default)
    {
        var body = new JsonObject { ["model"] = model, ["keep_alive"] = "30m", ["messages"] = new JsonArray() };
        using var response = await http.PostAsJsonAsync(new Uri(_base, "api/chat"), body, ct);
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["stream"] = false,
            ["keep_alive"] = "30m",
            ["think"] = false,
            ["options"] = new JsonObject { ["temperature"] = 0.1 },
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userMessage },
            },
        };
        using var response = await http.PostAsJsonAsync(new Uri(_base, "api/chat"), body, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            // Older Ollama builds / non-thinking models reject "think"; retry without it.
            if (error.Contains("think", StringComparison.OrdinalIgnoreCase))
            {
                body.Remove("think");
                using var retry = await http.PostAsJsonAsync(new Uri(_base, "api/chat"), body, ct);
                retry.EnsureSuccessStatusCode();
                return ReadContent(await retry.Content.ReadFromJsonAsync<JsonObject>(ct));
            }
            throw new HttpRequestException($"Ollama returned {(int)response.StatusCode}: {error}");
        }
        return ReadContent(await response.Content.ReadFromJsonAsync<JsonObject>(ct));
    }

    private static string ReadContent(JsonObject? json) =>
        (string?)json?["message"]?["content"] ?? throw new InvalidOperationException("Ollama returned no message.");
}
