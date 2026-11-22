using System.Text.Json;
using System.Text.Json.Serialization;

namespace GestionAlmacenVehiculos.Shared.Branding;

public sealed class BrandLoadResult
{
    public required BrandProfile Profile { get; init; }
    public string? SourceDirectory { get; init; }
    public string? LogoFullPath { get; init; }
}

/// <summary>Carga y guarda la marca de empresa (JSON + logo).</summary>
public static class BrandStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static BrandProfile CreateDefault(bool mobile = false)
    {
        var p = new BrandProfile();
        if (mobile)
            p.Tagline = BrandDefaults.TaglineMobile;
        p.Normalize();
        return p;
    }

    public static BrandLoadResult Load(IEnumerable<string> candidateDirectories, bool mobile = false)
    {
        foreach (var dir in candidateDirectories)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var jsonPath = Path.Combine(dir, BrandDefaults.JsonFileName);
            if (!File.Exists(jsonPath)) continue;
            try
            {
                var profile = JsonSerializer.Deserialize<BrandProfile>(File.ReadAllText(jsonPath), JsonOptions)
                    ?? CreateDefault(mobile);
                profile.Normalize();
                if (mobile && string.Equals(profile.Tagline, BrandDefaults.Tagline, StringComparison.Ordinal))
                    profile.Tagline = BrandDefaults.TaglineMobile;
                var logo = ResolveLogoPath(dir, profile.LogoFileName);
                return new BrandLoadResult
                {
                    Profile = profile,
                    SourceDirectory = dir,
                    LogoFullPath = logo,
                };
            }
            catch
            {
            }
        }

        return new BrandLoadResult
        {
            Profile = CreateDefault(mobile),
            SourceDirectory = null,
            LogoFullPath = null,
        };
    }

    public static void Save(string directory, BrandProfile profile, string? sourceLogoPath = null)
    {
        Directory.CreateDirectory(directory);
        profile.Normalize();

        if (!string.IsNullOrWhiteSpace(sourceLogoPath) && File.Exists(sourceLogoPath))
        {
            var ext = Path.GetExtension(sourceLogoPath);
            if (string.IsNullOrWhiteSpace(ext) || ext.Length > 5)
                ext = ".png";
            var destName = "logo" + ext.ToLowerInvariant();
            var destPath = Path.Combine(directory, destName);
            File.Copy(sourceLogoPath, destPath, overwrite: true);
            profile.LogoFileName = destName;
        }
        else if (!string.IsNullOrWhiteSpace(profile.LogoFileName))
        {
            var existing = Path.Combine(directory, Path.GetFileName(profile.LogoFileName));
            if (!File.Exists(existing))
                profile.LogoFileName = null;
        }

        var jsonPath = Path.Combine(directory, BrandDefaults.JsonFileName);
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(profile, JsonOptions));
    }

    public static void ClearLogo(string directory, BrandProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.LogoFileName))
        {
            var path = Path.Combine(directory, Path.GetFileName(profile.LogoFileName));
            try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
        }
        profile.LogoFileName = null;
        Save(directory, profile);
    }

    public static string? ResolveLogoPath(string? directory, string? logoFileName)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(logoFileName))
            return null;
        var path = Path.Combine(directory, Path.GetFileName(logoFileName));
        return File.Exists(path) ? path : null;
    }

    public static string DefaultUserBrandingDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GestionAlmacenVehiculos",
            BrandDefaults.FolderName);
    }

    public static string SidecarBrandingDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, BrandDefaults.FolderName);
    }
}
