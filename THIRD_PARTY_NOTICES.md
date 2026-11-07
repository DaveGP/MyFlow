# Third-party notices

MyFlow is a C# rewrite inspired by, and partly derived from, **OpenWhispr**.

## OpenWhispr

- Source: https://github.com/OpenWhispr/openwhispr (version 1.11.1)
- Used in MyFlow: `native/macos/macos-globe-listener.swift` and `native/macos/macos-fast-paste.swift`
  (copied unmodified apart from a header comment), plus the overall design: how the hotkey, paste,
  and selection-capture flow work on macOS.

```
MIT License

Copyright (c) 2024 OpenWhispr Team

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.```

## Other components (not bundled in this repository)

MyFlow uses these at runtime. You install them separately (see README):

| Component | Use | License |
|---|---|---|
| [whisper.cpp](https://github.com/ggml-org/whisper.cpp) (`whisper-server`) | Local speech-to-text | MIT |
| Whisper models (`ggml-*.bin`, downloaded on first run) | Speech-to-text model weights | MIT (OpenAI Whisper) |
| [FFmpeg](https://ffmpeg.org) | Microphone recording | LGPL/GPL (Homebrew build) |
| [Ollama](https://ollama.com) | Local text cleanup | MIT |

NuGet packages compiled into the app:

| Package | License |
|---|---|
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) | MIT |
| [SharpHook](https://github.com/TolikPylypchuk/SharpHook) | MIT. It bundles the native [libuiohook](https://github.com/kwhat/libuiohook) library, which has its own license; check it before redistributing MyFlow. |
| [Anthropic C# SDK](https://github.com/anthropics/anthropic-sdk-csharp) | MIT |
