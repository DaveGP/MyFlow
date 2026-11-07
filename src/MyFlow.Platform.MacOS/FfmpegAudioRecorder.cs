using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using MyFlow.Core;

namespace MyFlow.Platform.MacOS;

/// <summary>
/// Records the microphone with ffmpeg's AVFoundation input straight into a
/// 16 kHz mono 16-bit WAV, which is exactly what Whisper wants.
/// </summary>
public sealed partial class FfmpegAudioRecorder : IAudioRecorder
{
    private readonly string _ffmpeg;
    private Process? _process;
    private string? _path;
    private readonly StringBuilder _stderr = new();

    public FfmpegAudioRecorder(string? ffmpegPath = null)
    {
        _ffmpeg = ffmpegPath ?? MacTools.FindTool("ffmpeg")
            ?? throw new InvalidOperationException("ffmpeg was not found. Install it with: brew install ffmpeg");
    }

    public bool IsRecording => _process is { HasExited: false };

    [GeneratedRegex(@"\[(\d+)\]\s+(.+)$")]
    private static partial Regex DeviceLine();

    public async Task<IReadOnlyList<AudioDevice>> ListDevicesAsync(CancellationToken ct = default)
    {
        var result = await MacTools.RunAsync(_ffmpeg,
            ["-hide_banner", "-f", "avfoundation", "-list_devices", "true", "-i", ""], ct: ct);
        var devices = new List<AudioDevice>();
        var inAudio = false;
        foreach (var line in result.StdErr.Split('\n'))
        {
            if (line.Contains("AVFoundation audio devices")) { inAudio = true; continue; }
            if (line.Contains("AVFoundation video devices")) { inAudio = false; continue; }
            if (!inAudio) continue;
            var m = DeviceLine().Match(line.Trim());
            if (m.Success) devices.Add(new AudioDevice(m.Groups[1].Value, m.Groups[2].Value.Trim()));
        }
        return devices;
    }

    public async Task StartAsync(string? deviceId, CancellationToken ct = default)
    {
        if (IsRecording) throw new InvalidOperationException("Already recording.");

        Directory.CreateDirectory(AppPaths.TempDirectory);
        _path = Path.Combine(AppPaths.TempDirectory, $"take-{DateTime.Now:yyyyMMdd-HHmmss-fff}.wav");
        _stderr.Clear();

        var psi = new ProcessStartInfo(_ffmpeg)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        var device = string.IsNullOrWhiteSpace(deviceId) ? "default" : deviceId;
        foreach (var arg in new[]
                 {
                     "-hide_banner", "-nostats", "-loglevel", "info",
                     "-f", "avfoundation", "-i", $":{device}",
                     "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le",
                     "-y", _path,
                 })
        {
            psi.ArgumentList.Add(arg);
        }

        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (_stderr) _stderr.AppendLine(e.Data);
            // ffmpeg prints the output stream mapping once the microphone is open.
            if (e.Data.Contains("Stream mapping") || e.Data.StartsWith("Output #0")) opened.TrySetResult();
        };
        process.Exited += (_, _) => opened.TrySetException(new InvalidOperationException(
            "The microphone couldn't be opened. " + LastError()));

        process.Start();
        process.BeginErrorReadLine();
        _process = process;

        var timeout = Task.Delay(TimeSpan.FromSeconds(5), ct);
        if (await Task.WhenAny(opened.Task, timeout) == timeout)
        {
            await CancelAsync();
            throw new TimeoutException("The microphone didn't start within 5 seconds.");
        }
        await opened.Task; // rethrows if ffmpeg exited
        Log.Debug($"Recording to {_path}");
    }

    public async Task<Recording> StopAsync(CancellationToken ct = default)
    {
        var process = _process ?? throw new InvalidOperationException("Not recording.");
        var path = _path!;
        _process = null;

        // 'q' asks ffmpeg to stop cleanly so the WAV header gets finalized.
        try
        {
            await process.StandardInput.WriteAsync('q');
            await process.StandardInput.FlushAsync(ct);
            process.StandardInput.Close();
        }
        catch (IOException) { /* already exiting */ }

        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            try { await process.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { process.Kill(); }
        }
        process.Dispose();

        if (!File.Exists(path))
            throw new InvalidOperationException("No audio was recorded. " + LastError());

        // 16 kHz × 16-bit mono = 32,000 bytes per second, after the 44-byte header.
        var bytes = Math.Max(0, new FileInfo(path).Length - 44);
        var duration = TimeSpan.FromSeconds(bytes / 32000.0);
        return new Recording(path, duration);
    }

    public async Task CancelAsync()
    {
        var process = _process;
        _process = null;
        if (process != null)
        {
            try { process.Kill(); await process.WaitForExitAsync(); } catch { /* gone */ }
            process.Dispose();
        }
        if (_path != null)
        {
            try { File.Delete(_path); } catch { /* ignore */ }
        }
    }

    private string LastError()
    {
        lock (_stderr)
        {
            var lines = _stderr.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return lines.Length == 0 ? "" : lines[^1].Trim();
        }
    }
}
