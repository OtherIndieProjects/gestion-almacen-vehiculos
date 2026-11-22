using GestionAlmacenVehiculos.Shared.Branding;

namespace GestionAlmacenVehiculos.Mobile.Services;

public sealed class BrandService
{
    public BrandProfile Profile { get; private set; } = BrandStore.CreateDefault(mobile: true);
    public string? LogoPath { get; private set; }
    public string WriteDirectory { get; }

    public BrandService()
    {
        WriteDirectory = Path.Combine(FileSystem.AppDataDirectory, BrandDefaults.FolderName);
        Reload();
    }

    public void Reload()
    {
        var loaded = BrandStore.Load([WriteDirectory], mobile: true);
        Profile = loaded.Profile;
        LogoPath = loaded.LogoFullPath;
    }

    public void Save(BrandProfile profile, string? sourceLogoPath = null)
    {
        BrandStore.Save(WriteDirectory, profile, sourceLogoPath);
        Reload();
    }

    public void ClearLogo()
    {
        BrandStore.ClearLogo(WriteDirectory, Profile.Clone());
        Reload();
    }

    public void ResetToVehicle()
    {
        try
        {
            if (Directory.Exists(WriteDirectory))
            {
                foreach (var f in Directory.GetFiles(WriteDirectory))
                    File.Delete(f);
            }
        }
        catch { /* ignore */ }
        Save(BrandStore.CreateDefault(mobile: true));
    }
}
