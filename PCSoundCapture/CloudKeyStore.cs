using System.Security.Cryptography;
using System.Text;

namespace PCSoundCapture;

/// <summary>
/// The API key is never serialized into settings.json or compiled into the app.
/// A saved key is encrypted with Windows DPAPI for the signed-in Windows account.
/// OLLAMA_API_KEY (if present) takes precedence, so managed deployments can
/// provision a key without saving one through the UI.
/// </summary>
internal static class CloudKeyStore
{
    private static string KeyFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PCSoundCapture", "ollama-cloud-key.dpapi");

    public static bool IsEnvironmentConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OLLAMA_API_KEY"));

    public static bool HasStoredKey => File.Exists(KeyFile);

    public static string? ReadKey()
    {
        string? env = Environment.GetEnvironmentVariable("OLLAMA_API_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        if (!File.Exists(KeyFile)) return null;
        try
        {
            byte[] encrypted = File.ReadAllBytes(KeyFile);
            byte[] decrypted = ProtectedData.Unprotect(encrypted, null,
                DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(decrypted); }
            finally { CryptographicOperations.ZeroMemory(decrypted); }
        }
        catch (Exception ex) when (ex is CryptographicException or IOException)
        {
            throw new InvalidOperationException(
                "The saved Ollama Cloud key cannot be unlocked for this Windows user. " +
                "Remove it and save a new key.", ex);
        }
    }

    public static void SaveKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Paste an Ollama Cloud API key before saving.");
        string folder = Path.GetDirectoryName(KeyFile)!;
        Directory.CreateDirectory(folder);
        byte[] clear = Encoding.UTF8.GetBytes(key.Trim());
        byte[] protectedBytes;
        try { protectedBytes = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(clear); }
        string temp = KeyFile + ".tmp";
        try
        {
            File.WriteAllBytes(temp, protectedBytes);
            File.Move(temp, KeyFile, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static void RemoveKey()
    {
        if (File.Exists(KeyFile)) File.Delete(KeyFile);
    }
}
