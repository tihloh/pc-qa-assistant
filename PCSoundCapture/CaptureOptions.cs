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

internal sealed class CaptureCompletedEventArgs(
    string? FilePath,
    string? TranscriptFilePath,
    long CapturedBytes,
    long DroppedTranscriptChunks,
    Exception? Error) : EventArgs
{
    public string? FilePath { get; } = FilePath;
    public string? TranscriptFilePath { get; } = TranscriptFilePath;
    public long CapturedBytes { get; } = CapturedBytes;
    public long DroppedTranscriptChunks { get; } = DroppedTranscriptChunks;
    public Exception? Error { get; } = Error;
}

internal sealed class TranscriptEventArgs(string Text, bool IsFinal) : EventArgs
{
    public string Text { get; } = Text;
    public bool IsFinal { get; } = IsFinal;
}
