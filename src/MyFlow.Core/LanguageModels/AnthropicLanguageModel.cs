using Anthropic;
using Anthropic.Models.Messages;

namespace MyFlow.Core.LanguageModels;

/// <summary>Optional cloud cleanup via Claude, using the official Anthropic C# SDK. The key comes from the Keychain.</summary>
public sealed class AnthropicLanguageModel(Func<string?> apiKey, string model) : ILanguageModel
{
    public string DisplayName => $"Claude ({model})";

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(apiKey()));

    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var key = apiKey() ?? throw new InvalidOperationException("No Anthropic API key saved.");
        AnthropicClient client = new() { ApiKey = key };

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = model,
            MaxTokens = 16000,
            System = systemPrompt,
            // Cleanup is a simple, latency-sensitive task: keep thinking effort low.
            OutputConfig = new OutputConfig { Effort = Effort.Low },
            Messages = [new UserMessageParam { Content = userMessage }],
        }, ct);

        if (response.StopReason == StopReason.Refusal)
            throw new InvalidOperationException("Claude declined this request.");

        return string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
    }
}
