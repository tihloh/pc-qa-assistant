# PC Q&A Assistant

A Windows desktop AI question-answering assistant that listens to PC and microphone audio, detects spoken questions, and displays AI-generated answers on screen using **Ollama Cloud**.

## Features

- Capture system audio, microphone audio, or audio from a selected application.
- Transcribe speech locally with a [Vosk](https://alphacephei.com/vosk/models) model.
- Detect spoken questions and send recognized question text to Ollama Cloud.
- Display generated answers in an always-on-top floating overlay.
- Continue listening in the Windows system tray.
- Ask typed questions without configuring speech recognition.
- Optionally save WAV audio or transcripts (disabled by default).
- Encrypt the Ollama Cloud API key with Windows DPAPI.

## Source

The initial **v4 Ollama Cloud source** is provided as an XZ-compressed TAR archive: [`pc-qa-assistant-v4-source.tar.xz`](pc-qa-assistant-v4-source.tar.xz). It preserves the complete original C# source code and project directory structure.

This is an initial source-package import. The individual files are packaged in the archive rather than separately committed to this repository.

## Requirements

- Windows 11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Ollama Cloud account and API key with available usage allowance
- Local Vosk model for audio transcription (not necessary for typed questions)

## Build and run

Clone or download this repository, then in **PowerShell**:

```powershell
tar -xJf pc-qa-assistant-v4-source.tar.xz
cd pc-audio-capture/PCSoundCapture
dotnet restore
dotnet run
```

In the application, choose **Ollama Cloud**, save your API key, and test with a typed question using **Ask AI**. For spoken questions, select a downloaded Vosk model folder and audio source.

To publish a Windows executable, run `pc-audio-capture/BUILD-WINDOWS.ps1` from the extracted package.

> **Build verification:** The package passed integrity and structural checks but has not been compiled or run on Windows. It is development source, not a production-ready release.

## Privacy

- Audio is transcribed locally with Vosk.
- Recognized questions are sent to Ollama Cloud; sending recent context is optional.
- Check meeting consent and organizational policies before using cloud Q&A.
- Do not commit API keys, recordings, settings files, or transcripts.

## References

- [Ollama Cloud](https://docs.ollama.com/cloud)
- [Ollama API authentication](https://docs.ollama.com/api/authentication)
- [Vosk models](https://alphacephei.com/vosk/models)

No GitHub Actions workflows are included.
