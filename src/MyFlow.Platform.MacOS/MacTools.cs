using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MyFlow.Platform.MacOS;

/// <summary>Finds the external programs MyFlow uses and runs short-lived commands.</summary>
public static class MacTools
{
    private static readonly string[] SearchDirectories =
    [
        "/opt/homebrew/bin", // Homebrew on Apple Silicon
        "/usr/local/bin",    // Homebrew on Intel
        "/usr/bin",
    ];

    /// <summary>
    /// Finds a bundled helper (MyFlow.app/Contents/Resources/bin/…) or, when running from
    /// source, the copy compiled into native/macos/build by scripts/build-native-macos.sh.
    /// </summary>
    public static string? FindHelper(string name)
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Combine(baseDir, "..", "Resources", "bin", name), // inside MyFlow.app
            Path.Combine(baseDir, "bin", name),
        };
        // Walk up from bin/Debug/net10.0 to the repo root.
        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 7 && dir != null; i++, dir = dir.Parent)
        {
            candidates.Add(Path.Combine(dir.FullName, "native", "macos", "build", name));
        }
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    /// <summary>Finds a command such as ffmpeg or whisper-server (bundled first, then Homebrew/PATH).</summary>
    public static string? FindTool(string name)
    {
        var bundled = FindHelper(name);
        if (bundled != null) return bundled;
        var path = Environment.GetEnvironmentVariable("PATH")?.Split(':') ?? [];
        return SearchDirectories.Concat(path)
            .Select(d => Path.Combine(d, name))
            .FirstOrDefault(File.Exists);
    }

    public sealed record CommandResult(int ExitCode, string StdOut, string StdErr);

    public static async Task<CommandResult> RunAsync(string fileName, IEnumerable<string> args,
        string? stdin = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        // pbcopy/pbpaste pick their text encoding from the locale.
        psi.Environment["LANG"] = "en_US.UTF-8";
        psi.Environment["LC_CTYPE"] = "UTF-8";
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Couldn't start {fileName}");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        if (stdin != null) await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { /* gone */ }
            throw new TimeoutException($"{Path.GetFileName(fileName)} took too long.");
        }
        return new CommandResult(process.ExitCode, await stdout, await stderr);
    }

    public static Task OpenUrlAsync(string url) => RunAsync("/usr/bin/open", [url]);
}

/// <summary>Plain-text access to the macOS clipboard via pbcopy/pbpaste.</summary>
public static class MacClipboard
{
    public static async Task<string> GetTextAsync(CancellationToken ct = default) =>
        (await MacTools.RunAsync("/usr/bin/pbpaste", ["-Prefer", "txt"], ct: ct)).StdOut;

    public static async Task SetTextAsync(string text, CancellationToken ct = default) =>
        await MacTools.RunAsync("/usr/bin/pbcopy", [], stdin: text, ct: ct);

    /// <summary>
    /// The pasteboard's change counter: it goes up every time anything is copied.
    /// Read through the Objective-C runtime so no extra helper is needed.
    /// </summary>
    public static long ChangeCount()
    {
        var pasteboardClass = ObjC.objc_getClass("NSPasteboard");
        var general = ObjC.IntPtr_objc_msgSend(pasteboardClass, ObjC.sel_registerName("generalPasteboard"));
        return ObjC.Long_objc_msgSend(general, ObjC.sel_registerName("changeCount"));
    }
}

internal static partial class ObjC
{
    private const string Lib = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr objc_getClass(string name);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr sel_registerName(string name);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial long Long_objc_msgSend(IntPtr receiver, IntPtr selector);
}
