using System.Text.Json;

namespace PCSoundCapture;

internal sealed class AppSettings
{
    public string OutputDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "PC Sound Captures");
    public string VoskModelDirectory { get; set; } = string.Empty;
    public string CaptureSource { get; set; } = nameof(PCSoundCapture.CaptureSource.EntirePc);
    public string? OutputDeviceId { get; set; }
    public string? MicrophoneDeviceId { get; set; }
    public bool IncludeMicrophone { get; set; }
    public bool LiveTranscription { get; set; } = true;
    public bool SaveRecording { get; set; } = false;
    public bool SaveTranscript { get; set; } = false;
    public bool AutoAnswerQuestions { get; set; } = true;
    public bool ShowAnswerOverlay { get; set; } = true;
    // Keep older local model/endpoint preferences; cloud is the new default.
    public string AiProvider { get; set; } = "Cloud";
    public string CloudModel { get; set; } = "gemma4:31b";
    public bool IncludeAiContext { get; set; } = false;
    public string AiModel { get; set; } = "llama3.2:3b";
    public string AiEndpoint { get; set; } = "http://127.0.0.1:11434";

    public bool ScreenUseCloud { get; set; }
    public string? ScreenMonitor { get; set; }
    public string? ScreenLocalModel { get; set; }
    public string? ScreenCloudModel { get; set; }
    public string? ScreenEndpoint { get; set; }
    public string? ScreenPrompt { get; set; }

    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PCSoundCapture", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(SettingsFile)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile)) ?? new()
                : new();
        }
        catch { return new(); } // Corrupted preferences must not prevent launch.
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        string temporary = SettingsFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, SettingsFile, overwrite: true);
    }
}
