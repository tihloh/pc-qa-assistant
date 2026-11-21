# PC Q&A Assistant

A Windows desktop assistant that listens to PC or microphone audio, detects spoken questions, and displays answers from **Ollama Cloud** in an always-on-top window. Audio recording is optional and disabled by default.

## Features

- Capture full PC audio, a selected application's audio, or a microphone.
- Combine microphone and PC audio, where supported.
- Transcribe speech with a local [Vosk](https://alphacephei.com/vosk/models) model.
- Detect spoken questions and send recognized text to Ollama Cloud.
- Show responses in a floating answer window while working in another app.
- Continue listening from the Windows system tray.
- Ask typed questions without configuring speech recognition.
- Optionally save recordings as WAV and transcripts as text.
- Save the Ollama Cloud API key encrypted for the Windows user via DPAPI; the `OLLAMA_API_KEY` environment variable is also supported.
- Optionally use a local Ollama instance.

## Repository structure

```text
pc-qa-assistant/
├── PCQA.slnx                     # Open in Visual Studio or .NET CLI
├── PCSoundCapture/
│   ├── PCSoundCapture.csproj     # Windows Forms executable project
│   ├── MainForm.cs               # Main Q&A settings and controls
│   ├── AudioCaptureService.cs    # Live audio capture and speech-to-text
│   ├── CaptureOptions.cs
│   ├── QuestionDetector.cs       # Detects spoken questions
│   ├── OllamaClient.cs           # Ollama Cloud/local client
│   ├── CloudKeyStore.cs          # DPAPI-protected credential storage
│   ├── AnswerOverlayForm.cs      # Floating answers
│   ├── AppSettings.cs
│   └── Program.cs
├── BUILD-WINDOWS.ps1             # Manual Windows executable publish
├── .editorconfig
└── .gitignore
```

All source files are committed separately and are directly editable in GitHub. No source archive or setup extraction step is needed.

## Requirements

- Windows 11 x64, including Windows WASAPI support.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (or a Visual Studio version that supports .NET 10).
- Ollama Cloud account, an [API key](https://ollama.com/settings/keys), and available cloud usage.
- A local [Vosk speech model](https://alphacephei.com/vosk/models) for listening/transcription, but **not** for typed questions.

## Start developing

Clone the repository or use GitHub's download option. In the root directory:

```powershell
dotnet restore PCQA.slnx
dotnet run --project PCSoundCapture/PCSoundCapture.csproj
```

Or open `PCQA.slnx` in a compatible Visual Studio environment.

To manually publish a self-contained Windows executable:

```powershell
./BUILD-WINDOWS.ps1
```

The script publishes under `PCSoundCapture/bin/Release/net10.0-windows/win-x64/publish/`.

## Configure Q&A

1. Launch the app, select **Ollama Cloud**, and save your API key (or set `OLLAMA_API_KEY`).
2. Choose an Ollama cloud-enabled model available to your account.
3. Test with a typed question using **Ask AI**.
4. For speech input, download and extract a Vosk model, select its folder, and select your audio source.
5. Turn on listening and automatic answers. Use the floating panel or tray controls as needed.

No local Ollama language model is required when using Ollama Cloud. The Vosk model is downloaded separately because speech-to-text runs locally.

## Privacy and security

The app does not upload raw audio to Ollama Cloud. It sends recognized question text, and only includes recent recognized context if you enable that option. Request appropriate consent before listening to or processing meeting conversations. Do not commit secrets, recorded audio, transcripts, or local model files.

## Development and verification

The imported code is the original **v4 Ollama Cloud development source**. Its source package passed basic static checks; a full Windows build and end-to-end runtime test have **not** been performed in this environment. Verify audio device support, model loading, cloud connectivity, and tray/overlay behavior on Windows before distributing it.

No GitHub Actions workflow or automatic build/release trigger is included.
