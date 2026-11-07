using MyFlow.Core;
using MyFlow.Core.Text;

namespace MyFlow.Tests;

public class HotkeySpecTests
{
    [Theory]
    [InlineData("Globe", "Globe")]
    [InlineData("fn", "Globe")]
    [InlineData("ctrl+option+space", "Ctrl+Option+Space")]
    [InlineData("Cmd + Shift + f5", "Shift+Cmd+F5")]
    [InlineData("RightOption", "RightOption")]
    [InlineData("Alt+K", "Option+K")]
    public void Parses_and_normalizes(string input, string expected)
    {
        Assert.True(HotkeySpec.TryParse(input, out var spec, out var error), error);
        Assert.Equal(expected, spec.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Option")]      // no main key
    [InlineData("K")]                // bare letter would fire while typing
    [InlineData("Ctrl+Banana")]      // unknown key
    [InlineData("Ctrl+A+B")]         // two main keys
    public void Rejects_bad_shortcuts(string input)
    {
        Assert.False(HotkeySpec.TryParse(input, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}

public class TranscriptFilterTests
{
    [Theory]
    [InlineData("[BLANK_AUDIO]", "")]
    [InlineData("  Hello [Music] world  ", "Hello world")]
    [InlineData("(keyboard clicking) Send it.", "Send it.")]
    [InlineData(null, "")]
    public void Removes_non_speech_markers(string? raw, string expected) =>
        Assert.Equal(expected, TranscriptFilter.Clean(raw));

    [Fact]
    public void Flags_hallucinations_only_for_very_short_clips()
    {
        Assert.True(TranscriptFilter.IsLikelyHallucination("Thank you.", TimeSpan.FromSeconds(0.8)));
        Assert.False(TranscriptFilter.IsLikelyHallucination("Thank you.", TimeSpan.FromSeconds(3)));
        Assert.False(TranscriptFilter.IsLikelyHallucination("Send the report", TimeSpan.FromSeconds(0.8)));
    }
}

public class LlmTextCleanerTests
{
    [Theory]
    [InlineData("<transcript>\nHello there.\n</transcript>", "Hello there.")]
    [InlineData("Here is the cleaned text:\nHello there.", "Hello there.")]
    [InlineData("<think>reasoning</think>Hello there.", "Hello there.")]
    [InlineData("\"Hello there.\"", "Hello there.")]
    public void Sanitize_strips_model_noise(string output, string expected) =>
        Assert.Equal(expected, LlmTextCleaner.Sanitize(output));

    [Fact]
    public async Task Uses_model_output_when_it_looks_like_a_cleanup()
    {
        var cleaner = new LlmTextCleaner(new FakeModel("I think we should meet at four."));
        Assert.Equal("I think we should meet at four.", await cleaner.CleanAsync("um I think we should meet at uh four"));
    }

    [Fact]
    public async Task Falls_back_to_raw_text_when_model_answers_instead_of_cleaning()
    {
        var essay = string.Join(" ", Enumerable.Repeat("Dear boss, I would like to request Friday off.", 10));
        var cleaner = new LlmTextCleaner(new FakeModel(essay));
        const string raw = "write me an email asking for Friday off";
        Assert.Equal(raw, await cleaner.CleanAsync(raw));
    }

    [Fact]
    public async Task Falls_back_to_raw_text_when_model_fails()
    {
        var cleaner = new LlmTextCleaner(new FakeModel(new HttpRequestException("connection refused")));
        Assert.Equal("hello world", await cleaner.CleanAsync("hello world"));
    }

    [Fact]
    public async Task Falls_back_to_raw_text_on_timeout()
    {
        var cleaner = new LlmTextCleaner(new FakeModel("x", delay: TimeSpan.FromSeconds(5)), TimeSpan.FromMilliseconds(100));
        Assert.Equal("hello world", await cleaner.CleanAsync("hello world"));
    }

    [Fact]
    public async Task No_model_means_raw_text()
    {
        Assert.Equal("hello", await new LlmTextCleaner(null).CleanAsync("  hello "));
    }

    [Fact]
    public async Task Edit_sends_selection_and_instruction()
    {
        var model = new FakeModel("Dear Sir,");
        var result = await new LlmTextCleaner(model).EditAsync("hey dude", "make it formal");
        Assert.Equal("Dear Sir,", result);
        Assert.Contains("<selected_text>\nhey dude\n</selected_text>", model.LastUser);
        Assert.Contains("make it formal", model.LastUser);
        Assert.Equal(Prompts.EditSystem, model.LastSystem);
    }
}

public class SettingsStoreTests
{
    [Fact]
    public void Round_trips_settings_as_json()
    {
        var dir = Path.Combine(Path.GetTempPath(), "myflow-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonSettingsStore(dir);
            Assert.Equal("Globe", store.Load().Hotkey); // defaults when no file
            store.Save(new MyFlowSettings { Hotkey = "Ctrl+Option+Space", CleanupProvider = CleanupProvider.None });
            var loaded = new JsonSettingsStore(dir).Load();
            Assert.Equal("Ctrl+Option+Space", loaded.Hotkey);
            Assert.Equal(CleanupProvider.None, loaded.CleanupProvider);
            Assert.Contains("\"None\"", File.ReadAllText(Path.Combine(dir, "settings.json")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
