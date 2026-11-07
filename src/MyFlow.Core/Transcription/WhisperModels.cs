namespace MyFlow.Core.Transcription;

public sealed record WhisperModelInfo(string Name, string Description, long ApproxSizeMb)
{
    public string FileName => $"ggml-{Name}.bin";
    public Uri DownloadUrl => new($"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{FileName}");
}

/// <summary>The whisper.cpp models MyFlow offers. All run fully on this computer.</summary>
public static class WhisperModels
{
    public static readonly IReadOnlyList<WhisperModelInfo> All =
    [
        new("large-v3-turbo-q5_0", "Best accuracy, fast on Apple Silicon (recommended)", 574),
        new("large-v3-turbo", "Best accuracy, full precision", 1624),
        new("small.en", "Good, English only, light", 488),
        new("base.en", "Basic, English only, very light", 148),
    ];

    public static WhisperModelInfo Find(string name) =>
        All.FirstOrDefault(m => m.Name == name) ?? All[0];
}

/// <summary>Downloads a model file once, with a .part file so an interrupted download is never mistaken for a finished one.</summary>
public sealed class ModelDownloader(HttpClient http)
{
    public async Task<string> EnsureAsync(WhisperModelInfo model, string directory, IProgress<string>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, model.FileName);
        if (File.Exists(path)) return path;

        var part = path + ".part";
        progress?.Report($"Downloading speech model {model.Name} (~{model.ApproxSizeMb} MB)…");
        Log.Info($"Downloading {model.DownloadUrl} -> {path}");

        using var response = await http.GetAsync(model.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var target = File.Create(part))
        {
            var buffer = new byte[1 << 20];
            long done = 0;
            var lastPercent = -1;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0)
                {
                    var percent = (int)(done * 100 / total.Value);
                    if (percent != lastPercent && percent % 5 == 0)
                    {
                        lastPercent = percent;
                        progress?.Report($"Downloading speech model {model.Name}: {percent}%");
                    }
                }
            }
        }
        File.Move(part, path, overwrite: true);
        progress?.Report("Speech model downloaded.");
        return path;
    }
}
