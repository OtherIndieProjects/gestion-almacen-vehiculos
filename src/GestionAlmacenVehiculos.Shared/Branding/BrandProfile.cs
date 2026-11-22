namespace GestionAlmacenVehiculos.Shared.Branding;

/// <summary>Nombre, logo y colores de la empresa en la interfaz.</summary>
public sealed class BrandProfile
{
    public string ProductName { get; set; } = BrandDefaults.ProductName;
    public string BrandDisplay { get; set; } = BrandDefaults.BrandDisplay;
    public string AppTitle { get; set; } = BrandDefaults.AppTitle;
    public string Tagline { get; set; } = BrandDefaults.Tagline;
    public string Accent { get; set; } = BrandDefaults.Accent;
    public string AccentDeep { get; set; } = BrandDefaults.AccentDeep;
    public string? LogoFileName { get; set; }

    public BrandProfile Clone() => new()
    {
        ProductName = ProductName,
        BrandDisplay = BrandDisplay,
        AppTitle = AppTitle,
        Tagline = Tagline,
        Accent = Accent,
        AccentDeep = AccentDeep,
        LogoFileName = LogoFileName,
    };

    public void Normalize()
    {
        ProductName = string.IsNullOrWhiteSpace(ProductName) ? BrandDefaults.ProductName : ProductName.Trim();
        BrandDisplay = string.IsNullOrWhiteSpace(BrandDisplay) ? ProductName.ToUpperInvariant() : BrandDisplay.Trim();
        AppTitle = string.IsNullOrWhiteSpace(AppTitle) ? $"{ProductName} · Almacén" : AppTitle.Trim();
        Tagline = string.IsNullOrWhiteSpace(Tagline) ? BrandDefaults.Tagline : Tagline.Trim();
        Accent = NormalizeHex(Accent, BrandDefaults.Accent);
        AccentDeep = NormalizeHex(AccentDeep, BrandDefaults.AccentDeep);
        if (string.IsNullOrWhiteSpace(LogoFileName))
            LogoFileName = null;
        else
            LogoFileName = Path.GetFileName(LogoFileName.Trim());
    }

    private static string NormalizeHex(string? hex, string fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        var h = hex.Trim();
        if (!h.StartsWith('#')) h = "#" + h;
        if (h.Length is not (4 or 7 or 9)) return fallback;
        return h;
    }
}

public static class BrandDefaults
{
    public const string ProductName = "Vehicle";
    public const string BrandDisplay = "VEHICLE";
    public const string AppTitle = "Vehicle · Almacén";
    public const string Tagline = "PC · Logi Compass";
    public const string TaglineMobile = "Android · campo · Logi Compass";
    public const string Accent = "#0F766E";
    public const string AccentDeep = "#115E59";
    public const string FolderName = "branding";
    public const string JsonFileName = "branding.json";
}
