# PC Q&A Assistant

A Windows desktop AI question-answering assistant that listens to PC and microphone audio, detects spoken questions, and displays AI-generated answers on screen using **Ollama Cloud**.

## Features

- Capture system audio, microphone audio, or audio from a selected application.
- Transcribe locally with a [Vosk](https://alphacephei.com/vosk/models) model.
- Detect spoken questions and send recognized question text to Ollama Cloud.
- Display answers in an always-on-top floating overlay.
- Continue listening in the Windows system tray.
- Type questions directly without setting up speech recognition.
- Optionally record WAV audio or save transcripts; both are off by default.
- Keep the Ollama API key encrypted using Windows DPAPI.

## Source code

The initial **v4 Ollama Cloud source package** is included in this repository as `pc-qa-assistant-v4-ollama-cloud-source.zip`. Extract the archive to work with the C# project. The archive retains the original internal `pc-audio-capture/PCSoundCapture` folder structure.

> **Build status:** Source has received static checks but has not yet been compiled or tested on Windows. Treat this as a development build, not a production release.

## Requirements

- Windows 11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Ollama account, cloud API key and available usage allowance
- Local Vosk model for spoken questions (not required for typed Q&A)

## Run from source

After extracting `pc-qa-assistant-v4-ollama-cloud-source.zip`:

```powershell
cd pc-audio-capture/PCSoundCapture
dotnet restore
dotnet run
```

Open the app, choose **Ollama Cloud**, enter your API key, save it, and test with **Ask AI**. For voice input, select a local Vosk model folder and an audio source.

To create a Windows executable, run `BUILD-WINDOWS.ps1` in the extracted `pc-audio-capture` folder.

## Privacy

- Spoken audio is processed locally for speech-to-text using Vosk.
- Recognized questions are sent to Ollama Cloud. Sending recent conversation context is optional.
- Do not send confidential meeting content without appropriate permission.
- Do not commit API keys, local settings, recording files, or transcript files.

## Documentation

- [Ollama Cloud](https://docs.ollama.com/cloud)
- [Ollama API authentication](https://docs.ollama.com/api/authentication)
- [Vosk models](https://alphacephei.com/vosk/models)

No GitHub Actions workflows are included.
