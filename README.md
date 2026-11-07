# MyFlow

Press a shortcut anywhere, speak naturally, and polished text appears where you're typing.
Select some text first and you can edit it by voice ("make this friendlier", "turn this into bullet points").

MyFlow is a C# (.NET 10 + Avalonia) menu-bar app for macOS. It's a rewrite of the ideas in
[OpenWhispr](https://github.com/OpenWhispr/openwhispr) (MIT) and is built so the code is easy to read and extend.
Everything runs on your Mac by default: speech-to-text uses whisper.cpp on the GPU, and the cleanup step uses a
local Ollama model. Cloud cleanup (OpenAI or Claude) is optional, and its API keys are stored in the macOS Keychain.

## How to use it

| Action | What to do |
|---|---|
| Dictate | Hold **🌐 Globe (fn)**, speak, release. The text is pasted where your cursor is. |
| Edit by voice | Select text, hold 🌐, say an instruction ("make this more formal"), release. The selection is replaced. |
| Cancel | Press **Esc** while recording or processing. |
| Settings | Click the sound-wave icon in the menu bar → **Settings…** |

While it's working, a small capsule at the bottom of the screen shows **Listening…** (red), **Listening for an edit
instruction…** (orange), then **Transcribing… / Polishing… / Editing selection…** (blue). It never takes keyboard
focus away from the app you're typing in.

You can change the shortcut to Right Option, Ctrl+Option+Space or any custom combo, and switch between
hold-to-talk and press-to-toggle.

## Install and run

### 1. Prerequisites (one time)

```bash
brew install --cask dotnet-sdk
```

```bash
brew install ffmpeg whisper-cpp
```

```bash
brew install ollama && ollama pull mistral-nemo:12b
```

You also need the Xcode command-line tools (`xcode-select --install`) to compile the two small Swift helpers.
Ollama is optional: without it MyFlow pastes Whisper's raw transcript.

### 2. Build the app

```bash
scripts/package-macos.sh --install
```

This builds `dist/MyFlow.app`, signs it with your Apple Development certificate if you have one, and copies it to
`/Applications`. The first time, macOS may ask to let `codesign` use your keychain; click **Always Allow**.

### 3. Launch and grant permissions

```bash
open /Applications/MyFlow.app
```

1. **Accessibility.** The Settings window opens on first launch. Click **Grant…** and switch on **MyFlow** in
   System Settings → Privacy & Security → Accessibility. MyFlow needs this to see the Globe key and to paste.
2. **Microphone.** The first time you dictate, macOS asks for microphone access. Click **Allow**.
3. The first launch downloads the Whisper model (~550 MB) to `~/Library/Application Support/MyFlow/models`.
   The menu-bar menu shows progress.

To start MyFlow at login, add it in System Settings → General → Login Items.

### Run from source (for development)

```bash
scripts/build-native-macos.sh
```

```bash
dotnet run --project src/MyFlow.App
```

When run this way, macOS attributes the permissions to your terminal app, so grant Accessibility to the terminal.

### Test without speaking

```bash
say -o /tmp/t.aiff "um, can you send me the the report by Friday" && ffmpeg -y -i /tmp/t.aiff -ar 16000 -ac 1 /tmp/t.wav
```

```bash
dotnet run --project src/MyFlow.App -- --selftest /tmp/t.wav
```

Add `--paste` to paste the result into the focused app after 3 seconds, or `--edit "some text"` to test voice editing.

### Unit tests

```bash
dotnet test
```

## How it works

```
 Globe key ──► macos-globe-listener (Swift) ──FN_DOWN/FN_UP──► MacHotkeyService
                                                                    │
                                                         DictationController (Core)
   ┌───────────────────────┬─────────────────────┬──────────────────┼───────────────────────┐
 record                select text          transcribe            polish / edit            paste
 FfmpegAudioRecorder   MacSelectionReader   WhisperServerTranscriber  LlmTextCleaner        MacTextInserter
 (ffmpeg, 16 kHz WAV)  (⌘C via helper)      (whisper-server, local)   (Ollama / cloud)      (⌘V via macos-fast-paste)
```

| Project | What's in it |
|---|---|
| `src/MyFlow.Core` | Interfaces (`IHotkeyService`, `IAudioRecorder`, `ITranscriber`, `ITextCleaner`, `ITextInserter`, `ISelectionReader`, `ISettingsStore`, `ISecretStore`), the `DictationController` pipeline, prompts, the Whisper, Ollama, OpenAI and Claude clients. No OS-specific code. |
| `src/MyFlow.Platform.MacOS` | macOS implementations: Globe and keyboard shortcuts, ffmpeg recording, paste and selection, Keychain, Accessibility checks. |
| `src/MyFlow.App` | The Avalonia menu-bar app: tray menu, recording pill, settings window. `AppHost.cs` wires everything together. |
| `tests/MyFlow.Tests` | xUnit tests for the pipeline, shortcut parsing, cleanup safety checks and settings. |
| `native/macos` | Two Swift helpers from OpenWhispr (MIT), compiled by `scripts/build-native-macos.sh`. |

To support Windows later, add a `MyFlow.Platform.Windows` project that implements the same interfaces and select it in
`AppHost`.

### Privacy

- Audio is recorded to a temp file, sent to `whisper-server` on `127.0.0.1`, and deleted straight after.
- With Ollama cleanup, nothing leaves your Mac. With OpenAI or Claude, only the transcript text (and any selected text
  for edits) is sent to that provider.
- Transcripts aren't written to the log unless you set `MYFLOW_DEBUG=1`.
- Settings: `~/Library/Application Support/MyFlow/settings.json`. Logs: `…/MyFlow/logs/myflow.log`.

## License

MIT, see [LICENSE](LICENSE). Contains code from OpenWhispr (MIT, © 2024 OpenWhispr Team), see
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
