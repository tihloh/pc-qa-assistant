using Tesseract;
using System.Net.Http;
using System.Drawing.Imaging;

namespace PCSoundCapture;

/// <summary>Local English OCR. Only the public language data is downloaded; images stay in memory.</summary>
internal static class ScreenOcr
{
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCSoundCapture", "tessdata");

    private const string DataUrl =
        "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main/eng.traineddata";

    public static bool IsReady => File.Exists(Path.Combine(DataDirectory, "eng.traineddata"));

    public static async Task EnsureDataAsync(CancellationToken token)
    {
        if (IsReady) return;
        Directory.CreateDirectory(DataDirectory);
        string path = Path.Combine(DataDirectory, "eng.traineddata");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            using var response = await http.GetAsync(DataUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            using (var file = File.Create(temporary))
                await response.Content.CopyToAsync(file, token);
            token.ThrowIfCancellationRequested();
            if (new FileInfo(temporary).Length < 100_000)
                throw new InvalidDataException("Invalid OCR language data download.");
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static Task<string> ReadAsync(byte[] png, CancellationToken token) =>
        Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var engine = new TesseractEngine(DataDirectory, "eng", EngineMode.LstmOnly);
            using var pix = Pix.LoadFromMemory(png);
            using var page = engine.Process(pix, PageSegMode.Auto);
            string text = page.GetText().Trim();
            token.ThrowIfCancellationRequested();
            return text;
        }, token);
}
