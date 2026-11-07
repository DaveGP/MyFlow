using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace MyFlow.Core.Transcription;

/// <summary>
/// Local speech-to-text using whisper.cpp's <c>whisper-server</c>. The server is started once,
/// keeps the model loaded (on the GPU via Metal on Apple Silicon), and audio is sent to it over
/// HTTP on 127.0.0.1 only. Nothing leaves the computer.
/// </summary>
public sealed class WhisperServerTranscriber : ITranscriber, IDisposable
{
    private readonly HttpClient _http;
    private readonly Func<string?> _findServerBinary;
    private readonly WhisperModelInfo _model;
    private readonly string _language;
    private readonly string _vocabulary;
    private readonly string _modelsDirectory;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private Process? _process;
    private int _port;

    public WhisperServerTranscriber(HttpClient http, Func<string?> findServerBinary, string modelName,
        string language, string vocabulary, string? modelsDirectory = null)
    {
        _http = http;
        _findServerBinary = findServerBinary;
        _model = WhisperModels.Find(modelName);
        _language = string.IsNullOrWhiteSpace(language) ? "auto" : language;
        _vocabulary = vocabulary.Trim();
        _modelsDirectory = modelsDirectory ?? AppPaths.ModelsDirectory;
    }

    public string ModelName => _model.Name;

    public async Task EnsureReadyAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        await _startLock.WaitAsync(ct);
        try
        {
            if (_process is { HasExited: false }) return;

            var binary = _findServerBinary() ?? throw new InvalidOperationException(
                "whisper-server was not found. Install it with: brew install whisper-cpp");
            var modelPath = await new ModelDownloader(_http).EnsureAsync(_model, _modelsDirectory, progress, ct);

            _port = FreePort();
            progress?.Report("Loading speech model…");
            var psi = new ProcessStartInfo(binary)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in new[]
                     {
                         "--model", modelPath, "--host", "127.0.0.1", "--port", _port.ToString(),
                         "--language", _language, "--no-timestamps",
                         "--threads", Math.Max(4, Environment.ProcessorCount / 2).ToString(),
                     })
            {
                psi.ArgumentList.Add(arg);
            }

            Log.Info($"Starting whisper-server ({_model.Name}) on port {_port}");
            _process = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start whisper-server.");
            _process.OutputDataReceived += (_, e) => { if (e.Data != null) Log.Debug($"[whisper] {e.Data}"); };
            _process.ErrorDataReceived += (_, e) => { if (e.Data != null) Log.Debug($"[whisper] {e.Data}"); };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            // Wait until it answers HTTP (the model takes a second or two to load).
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                if (_process.HasExited)
                    throw new InvalidOperationException($"whisper-server exited with code {_process.ExitCode}. Check the log for details.");
                try
                {
                    using var probe = await _http.GetAsync($"http://127.0.0.1:{_port}/", ct);
                    progress?.Report("Speech model ready.");
                    Log.Info("whisper-server ready");
                    return;
                }
                catch (HttpRequestException)
                {
                    await Task.Delay(250, ct);
                }
            }
            throw new TimeoutException("whisper-server didn't start within 90 seconds.");
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task<string> TranscribeAsync(string wavPath, CancellationToken ct = default)
    {
        await EnsureReadyAsync(null, ct);

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(wavPath, ct));
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", Path.GetFileName(wavPath));
        form.Add(new StringContent("json"), "response_format");
        form.Add(new StringContent("0.0"), "temperature");
        if (_vocabulary.Length > 0)
        {
            // An initial prompt biases Whisper towards these spellings (names, jargon).
            form.Add(new StringContent(_vocabulary), "prompt");
        }

        using var response = await _http.PostAsync($"http://127.0.0.1:{_port}/inference", form, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"whisper-server returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
        }
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
        return (string?)json?["text"] ?? "";
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch { /* already gone */ }
        _process?.Dispose();
        _process = null;
    }
}
