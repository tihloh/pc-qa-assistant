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

## Screen assistant (OCR and vision)

Open **Screen assistant** from the main window or tray menu. Select a monitor, processing mode and an installed Ollama model. Optionally edit the analysis prompt. Local inference is the default; enable Cloud explicitly to use the saved API key.

Choose **Analyze once** or **Start monitoring**. Confirm the destination and capture consent. Monitoring analyzes the first frame immediately, then samples every 1.5 seconds and checks for changed frames; only one inference runs at a time. Results stream into an always-on-top window. Stop or close the screen assistant to cancel monitoring. Screenshots are held in memory only; the app masks its own main and answer windows and the screen assistant window before sending images. Keep these windows away from content you want analyzed.

Processing modes:

- **OCR only (default):** Set AI model to `llama3.2:1b` or another installed text model. Tesseract recognizes screen text at the original screenshot resolution, and the app sends only text (up to 4,000 characters) to Ollama. Unchanged OCR text is skipped during monitoring.
- **Hybrid:** Set AI model to a text model and Hybrid vision model to an installed vision model (for example `gemma3:4b` on local Ollama). Screens with fewer than 20 recognized letters/digits fall back to the vision model. This heuristic cannot reliably identify diagrams that also contain text; use Vision only for those.
- **Vision only:** Select a vision-capable model in AI model. Local/remote Ollama's model capabilities are checked via `/api/show` before an image is submitted, when available.

On first OCR use, the app downloads the public English `eng.traineddata` from Tesseract's tessdata_fast repository to `%LOCALAPPDATA%/PCSoundCapture/tessdata/`. Internet access is required for that first-time download, not subsequent local OCR. NuGet restores the Tesseract 5.2.0 wrapper and its native binaries; Windows may require the Microsoft Visual C++ 2015–2022 x64 redistributable. No separate OCR server is required. OCR currently recognizes English. Screenshots and recognized text are not saved by the screen assistant. Settings are persisted in the existing configuration file.

The app supports individual monitors, not individual windows or selected regions. Audio and screen input can run independently; their results and context are not yet merged. Small changes may be missed by the approximate change detector. The live activity panel reports OCR timing, request type, and Ollama timing statistics when returned.

Screenshots can contain confidential information. Cloud and remote endpoints receive recognized text or captured images, depending on the selected mode, so use only authorized content and trusted endpoints. Local inference is recommended for sensitive documents. Do not use monitoring where assistance or capture is prohibited.

Windows build and runtime verification of screen capture and streaming are pending.

## Privacy and security

The audio feature does not upload raw audio to Ollama Cloud. The separate opt-in screen feature sends recognized screen text or screenshot images to the selected Ollama endpoint, according to the selected mode. The audio feature sends recognized question text, and only includes recent recognized context if you enable that option. Request appropriate consent before listening to or processing meeting conversations. Do not commit secrets, recorded audio, transcripts, or local model files.

## Development and verification

The imported code is the original **v4 Ollama Cloud development source**. Its source package passed basic static checks; a full Windows build and end-to-end runtime test have **not** been performed in this environment. Verify audio device support, model loading, cloud connectivity, and tray/overlay behavior on Windows before distributing it.

No GitHub Actions workflow or automatic build/release trigger is included.
