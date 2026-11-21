using NAudio.CoreAudioApi;

namespace PCSoundCapture;

internal enum CaptureSource
{
    EntirePc,
    SelectedApplication,
    MicrophoneOnly
}

internal sealed record CaptureOptions(
    CaptureSource Source,
    MMDevice? OutputDevice,
    MMDevice? MicrophoneDevice,
    int? ProcessId,
    bool IncludeMicrophone,
    bool Transcribe,
    string ModelDirectory,
    string OutputDirectory,
    bool SaveRecording,
    bool SaveTranscript);

internal sealed record CaptureCompletedEventArgs(
    string? FilePath,
    string? TranscriptFilePath,
    long CapturedBytes,
    long DroppedTranscriptChunks,
    Exception? Error) : EventArgs;

internal sealed record TranscriptEventArgs(string Text, bool IsFinal) : EventArgs;
