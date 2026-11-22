using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GestionAlmacenVehiculos.PC;

public sealed class PcSettings
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GestionAlmacenVehiculos");

    private static readonly string LegacyAppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GestionAlmacenKngloo");

    public static string SettingsPath { get; } = Path.Combine(AppDataDir, "settings.json");

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GestionAlmacenVehiculos.PC.v1");
    private static readonly byte[] LegacyEntropy = Encoding.UTF8.GetBytes("Kngloo.PC.v1");

    public string Language { get; set; } = "es";
    public string ServerUrl { get; set; } = "http://127.0.0.1:5088";
    public string Username { get; set; } = "";

    public string? AccessTokenProtected { get; set; }
    public string? RefreshTokenProtected { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccessToken { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RefreshToken { get; set; }

    public string? UserId { get; set; }
    public string? DisplayName { get; set; }

    public static PcSettings Load()
    {
        try
        {
            MigrateLegacyAppDataOnce();
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<PcSettings>(File.ReadAllText(SettingsPath));
                if (loaded is not null)
                {
                    if (string.IsNullOrWhiteSpace(loaded.ServerUrl))
                        loaded.ServerUrl = "http://127.0.0.1:5088";
                    loaded.MigratePlaintextTokens();
                    return loaded;
                }
            }
        }
        catch { /* ignore */ }

        return new PcSettings();
    }

    private static void MigrateLegacyAppDataOnce()
    {
        try
        {
            if (Directory.Exists(AppDataDir) || !Directory.Exists(LegacyAppDataDir))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(AppDataDir)!);
            CopyDirectory(LegacyAppDataDir, AppDataDir);
        }
        catch { /* ignore */ }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    public void Save()
    {
        if (!string.IsNullOrWhiteSpace(AccessToken))
        {
            AccessTokenProtected = ProtectOrThrow(AccessToken);
            AccessToken = null;
        }
        if (!string.IsNullOrWhiteSpace(RefreshToken))
        {
            RefreshTokenProtected = ProtectOrThrow(RefreshToken);
            RefreshToken = null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        }));
    }

    public void ClearSession()
    {
        AccessToken = null;
        RefreshToken = null;
        AccessTokenProtected = null;
        RefreshTokenProtected = null;
        UserId = null;
        DisplayName = null;
    }

    public string? GetAccessToken() =>
        Unprotect(AccessTokenProtected) ?? AccessToken;

    public string? GetRefreshToken() =>
        Unprotect(RefreshTokenProtected) ?? RefreshToken;

    public void SetTokens(string? access, string? refresh)
    {
        AccessToken = null;
        RefreshToken = null;
        AccessTokenProtected = string.IsNullOrWhiteSpace(access) ? null : ProtectOrThrow(access);
        RefreshTokenProtected = string.IsNullOrWhiteSpace(refresh) ? null : ProtectOrThrow(refresh);
    }

    private void MigratePlaintextTokens()
    {
        var dirty = false;
        try
        {
            if (!string.IsNullOrWhiteSpace(AccessToken) && string.IsNullOrWhiteSpace(AccessTokenProtected))
            {
                AccessTokenProtected = ProtectOrThrow(AccessToken);
                AccessToken = null;
                dirty = true;
            }
            if (!string.IsNullOrWhiteSpace(RefreshToken) && string.IsNullOrWhiteSpace(RefreshTokenProtected))
            {
                RefreshTokenProtected = ProtectOrThrow(RefreshToken);
                RefreshToken = null;
                dirty = true;
            }
        }
        catch
        {
            return;
        }

        if (AccessToken is not null || RefreshToken is not null)
        {
            AccessToken = null;
            RefreshToken = null;
            dirty = true;
        }
        if (dirty)
        {
            try { Save(); } catch { /* ignore migrate save */ }
        }
    }

    private static string ProtectOrThrow(string plain)
    {
        try
        {
            var bytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plain),
                Entropy,
                DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }
        catch
        {
            throw new InvalidOperationException("token_protect_failed");
        }
    }

    private static string? Unprotect(string? protectedB64)
    {
        if (string.IsNullOrWhiteSpace(protectedB64)) return null;
        var raw = Convert.FromBase64String(protectedB64);
        foreach (var entropy in new[] { Entropy, LegacyEntropy })
        {
            try
            {
                var bytes = ProtectedData.Unprotect(raw, entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { /* try next */ }
        }
        return null;
    }
}
