using System.IO;
using GestionAlmacenVehiculos.Shared.Branding;
using GestionAlmacenVehiculos.Shared.Logi;

namespace GestionAlmacenVehiculos.PC;

public sealed class PcRuntime
{
    public static PcRuntime Instance { get; } = new();

    public PcSettings Settings { get; } = PcSettings.Load();
    public LogiClient Client { get; } = new();
    public ParkingMapper Mapper { get; }
    public BrandProfile Brand { get; private set; } = BrandStore.CreateDefault();
    public string? BrandLogoPath { get; private set; }
    public string BrandWriteDirectory { get; private set; } = BrandStore.DefaultUserBrandingDirectory();

    private PcRuntime()
    {
        var historyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GestionAlmacenVehiculos",
            "park-history.json");
        Mapper = new ParkingMapper(Client, historyPath);
        ReloadBrand();

        try
        {
            Client.Configure(Settings.ServerUrl);
            var access = Settings.GetAccessToken();
            if (!string.IsNullOrWhiteSpace(access)
                && Guid.TryParse(Settings.UserId, out var userId))
            {
                Client.RestoreSession(
                    Settings.ServerUrl,
                    access,
                    Settings.GetRefreshToken(),
                    Settings.Username,
                    userId,
                    Settings.DisplayName ?? Settings.Username);
            }
        }
        catch { /* ignore */ }
    }

    public void ReloadBrand()
    {
        var loaded = BrandStore.Load(
        [
            BrandStore.SidecarBrandingDirectory(),
            BrandStore.DefaultUserBrandingDirectory(),
        ]);
        Brand = loaded.Profile;
        BrandLogoPath = loaded.LogoFullPath;
        BrandWriteDirectory = BrandStore.DefaultUserBrandingDirectory();
    }

    public void SaveBrand(BrandProfile profile, string? sourceLogoPath = null)
    {
        BrandStore.Save(BrandWriteDirectory, profile, sourceLogoPath);
        ReloadBrand();
    }

    public void ClearBrandLogo()
    {
        BrandStore.ClearLogo(BrandWriteDirectory, Brand.Clone());
        ReloadBrand();
    }

    public void PersistSession()
    {
        Settings.SetTokens(Client.AccessToken, Client.RefreshToken);
        Settings.Username = Client.UserName;
        Settings.UserId = Client.UserId == Guid.Empty ? null : Client.UserId.ToString("D");
        Settings.DisplayName = Client.DisplayName;
        Settings.Save();
    }
}
