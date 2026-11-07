using MyFlow.Core;

namespace MyFlow.Tests;

internal sealed class FakeModel : ILanguageModel
{
    private readonly string? _reply;
    private readonly Exception? _error;
    private readonly TimeSpan _delay;

    public FakeModel(string reply, TimeSpan? delay = null) { _reply = reply; _delay = delay ?? TimeSpan.Zero; }
    public FakeModel(Exception error) { _error = error; }

    public string? LastSystem { get; private set; }
    public string? LastUser { get; private set; }
    public string DisplayName => "Fake";
    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        LastSystem = systemPrompt;
        LastUser = userMessage;
        if (_delay > TimeSpan.Zero) await Task.Delay(_delay, ct);
        if (_error != null) throw _error;
        return _reply!;
    }
}

internal sealed class FakeRecorder : IAudioRecorder
{
    public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(2);
    public bool FailOnStart { get; set; }
    public bool Cancelled { get; private set; }
    public string? LastPath { get; private set; }
    public bool IsRecording { get; private set; }

    public Task<IReadOnlyList<AudioDevice>> ListDevicesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AudioDevice>>([new AudioDevice("0", "Fake Mic")]);

    public Task StartAsync(string? deviceId, CancellationToken ct = default)
    {
        if (FailOnStart) throw new InvalidOperationException("device busy");
        IsRecording = true;
        return Task.CompletedTask;
    }

    public Task<Recording> StopAsync(CancellationToken ct = default)
    {
        IsRecording = false;
        LastPath = Path.GetTempFileName();
        return Task.FromResult(new Recording(LastPath, Duration));
    }

    public Task CancelAsync()
    {
        IsRecording = false;
        Cancelled = true;
        return Task.CompletedTask;
    }
}

internal sealed class FakeTranscriber : ITranscriber
{
    public string Text { get; set; } = "";
    public int Calls { get; private set; }
    public Task EnsureReadyAsync(IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;

    public Task<string> TranscribeAsync(string wavPath, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(Text);
    }
}

internal sealed class FakeInserter : ITextInserter
{
    public List<string> Inserted { get; } = [];

    public Task<InsertResult> InsertAsync(string text, CancellationToken ct = default)
    {
        Inserted.Add(text);
        return Task.FromResult(InsertResult.Pasted);
    }
}

internal sealed class FakeSelection : ISelectionReader
{
    public string? Text { get; set; }
    public Task<string?> GetSelectedTextAsync(CancellationToken ct = default) => Task.FromResult(Text);
}
