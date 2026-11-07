using MyFlow.Core;
using MyFlow.Core.Text;

namespace MyFlow.Tests;

public class DictationControllerTests
{
    private readonly FakeRecorder _recorder = new();
    private readonly FakeTranscriber _transcriber = new();
    private readonly FakeInserter _inserter = new();
    private readonly FakeSelection _selection = new();
    private MyFlowSettings _settings = new();

    private DictationController Create(ILanguageModel? model) =>
        new(_recorder, _transcriber, new LlmTextCleaner(model), _inserter, _selection, () => _settings);

    [Fact]
    public async Task Dictation_is_transcribed_cleaned_and_inserted()
    {
        _transcriber.Text = "um send the report today";
        var controller = Create(new FakeModel("Send the report today."));

        await controller.BeginAsync();
        Assert.Equal(DictationState.Recording, controller.State);
        var result = await controller.FinishAsync();

        Assert.NotNull(result);
        Assert.False(result.WasEdit);
        Assert.Equal(["Send the report today."], _inserter.Inserted);
        Assert.Equal(DictationState.Idle, controller.State);
        Assert.False(File.Exists(_recorder.LastPath)); // temp audio is deleted
    }

    [Fact]
    public async Task Selected_text_is_rewritten_by_the_spoken_instruction()
    {
        _selection.Text = "hey can u send it";
        _transcriber.Text = "make this more formal";
        var model = new FakeModel("Could you please send it?");
        var controller = Create(model);

        await controller.BeginAsync();
        var result = await controller.FinishAsync();

        Assert.True(result!.WasEdit);
        Assert.Equal(["Could you please send it?"], _inserter.Inserted);
        Assert.Equal(Prompts.EditSystem, model.LastSystem);
    }

    [Fact]
    public async Task Very_short_taps_are_ignored()
    {
        _recorder.Duration = TimeSpan.FromMilliseconds(100);
        var controller = Create(new FakeModel("x"));

        await controller.BeginAsync();
        Assert.Null(await controller.FinishAsync());
        Assert.Empty(_inserter.Inserted);
        Assert.Equal(0, _transcriber.Calls);
    }

    [Fact]
    public async Task Silence_inserts_nothing()
    {
        _transcriber.Text = "[BLANK_AUDIO]";
        var controller = Create(new FakeModel("x"));

        await controller.BeginAsync();
        Assert.Null(await controller.FinishAsync());
        Assert.Empty(_inserter.Inserted);
    }

    [Fact]
    public async Task Raw_text_is_inserted_when_cleanup_is_unavailable()
    {
        _transcriber.Text = "hello world";
        var controller = Create(new FakeModel(new HttpRequestException("Ollama not running")));

        await controller.BeginAsync();
        await controller.FinishAsync();
        Assert.Equal(["hello world"], _inserter.Inserted);
    }

    [Fact]
    public async Task Push_to_talk_starts_on_press_and_finishes_on_release()
    {
        _transcriber.Text = "hello";
        var controller = Create(null);
        var done = new TaskCompletionSource();
        controller.Completed += (_, _) => done.TrySetResult();

        controller.OnHotkeyPressed();
        await WaitFor(() => controller.State == DictationState.Recording);
        controller.OnHotkeyReleased();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["hello"], _inserter.Inserted);
    }

    [Fact]
    public async Task Toggle_mode_ignores_release_and_finishes_on_second_press()
    {
        _settings = _settings with { HotkeyMode = HotkeyMode.Toggle };
        _transcriber.Text = "hello";
        var controller = Create(null);
        var done = new TaskCompletionSource();
        controller.Completed += (_, _) => done.TrySetResult();

        controller.OnHotkeyPressed();
        await WaitFor(() => controller.State == DictationState.Recording);
        controller.OnHotkeyReleased();
        Assert.Equal(DictationState.Recording, controller.State);
        controller.OnHotkeyPressed();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["hello"], _inserter.Inserted);
    }

    [Fact]
    public async Task Cancel_discards_the_recording()
    {
        var controller = Create(null);
        await controller.BeginAsync();
        await controller.CancelAsync();
        Assert.Equal(DictationState.Idle, controller.State);
        Assert.True(_recorder.Cancelled);
        Assert.Empty(_inserter.Inserted);
    }

    [Fact]
    public async Task Microphone_failure_reports_an_error_and_returns_to_idle()
    {
        _recorder.FailOnStart = true;
        var controller = Create(null);
        string? error = null;
        controller.Failed += (_, message) => error = message;

        await controller.BeginAsync();
        Assert.Equal(DictationState.Idle, controller.State);
        Assert.Contains("microphone", error, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }
}
