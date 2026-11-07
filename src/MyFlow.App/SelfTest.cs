using System.Diagnostics;
using MyFlow.Core;
using MyFlow.Core.Text;
using MyFlow.Core.Transcription;
using MyFlow.Platform.MacOS;

namespace MyFlow.App;

/// <summary>
/// Runs the dictation pipeline on an audio file from the command line, so every stage
/// except the microphone and the shortcut can be tested without speaking:
///
///   MyFlow --selftest speech.wav                      transcribe + clean, print the result
///   MyFlow --selftest speech.wav --paste              …then paste it into the focused app (after 3 s)
///   MyFlow --selftest speech.wav --edit "some text"   treat speech as an edit instruction
/// </summary>
internal static class SelfTest
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || !File.Exists(args[0]))
        {
            Console.Error.WriteLine("usage: MyFlow --selftest <file.wav> [--edit \"selected text\"] [--paste]");
            return 2;
        }

        var wav = args[0];
        var paste = args.Contains("--paste");
        var editIndex = Array.IndexOf(args, "--edit");
        var selection = editIndex >= 0 && editIndex + 1 < args.Length ? args[editIndex + 1] : null;

        using var host = new AppHost();
        var settings = host.Settings;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using var transcriber = new WhisperServerTranscriber(http, () => MacTools.FindTool("whisper-server"),
            settings.WhisperModel, settings.Language, settings.CustomVocabulary);
        var model = host.CreateLanguageModel();
        var cleaner = new LlmTextCleaner(model);

        var sw = Stopwatch.StartNew();
        await transcriber.EnsureReadyAsync(new Progress<string>(Console.WriteLine));
        Console.WriteLine($"[ready in {sw.ElapsedMilliseconds} ms] model={settings.WhisperModel} cleanup={model?.DisplayName ?? "off"}");

        sw.Restart();
        var raw = TranscriptFilter.Clean(await transcriber.TranscribeAsync(wav));
        Console.WriteLine($"[transcribed in {sw.ElapsedMilliseconds} ms] RAW: {raw}");

        sw.Restart();
        string final;
        if (selection != null)
        {
            final = await cleaner.EditAsync(selection, raw);
            Console.WriteLine($"[edited in {sw.ElapsedMilliseconds} ms] EDITED: {final}");
        }
        else
        {
            if (model != null && !await model.IsAvailableAsync())
                Console.WriteLine($"(note: {model.DisplayName} is not available, cleanup will be skipped)");
            final = await cleaner.CleanAsync(raw);
            Console.WriteLine($"[cleaned in {sw.ElapsedMilliseconds} ms] CLEAN: {final}");
        }

        if (paste)
        {
            Console.WriteLine("Pasting into the focused app in 3 seconds…");
            await Task.Delay(3000);
            var result = await new MacTextInserter(() => settings).InsertAsync(final);
            Console.WriteLine($"Insert result: {result}");
        }
        return 0;
    }
}
