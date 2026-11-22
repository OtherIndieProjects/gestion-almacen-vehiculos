namespace GestionAlmacenVehiculos.Mobile.Services;

public sealed class AppSettings
{
    private const string KeyHostIp = "host_ip";
    private const string KeyHostPort = "host_port";
    private const string KeyToken = "token";
    private const string KeyRefresh = "refresh_token";
    private const string KeyUserId = "user_id";
    private const string KeyDisplayName = "display_name";
    private const string KeyUsername = "username";
    private const string KeyLang = "lang";
    private const string KeyWarehouseId = "warehouse_id";
    private const string SecurePasswordKey = "host_password";
    private const string SecureTokenKey = "access_token";
    private const string SecureRefreshKey = "refresh_token_secure";

    public string HostIp
    {
        get => Preferences.Default.Get(KeyHostIp, "");
        set => Preferences.Default.Set(KeyHostIp, (value ?? "").Trim());
    }

    public int HostPort
    {
        get => Preferences.Default.Get(KeyHostPort, 5088);
        set => Preferences.Default.Set(KeyHostPort, value is > 0 and < 65536 ? value : 5088);
    }

    public string BaseUrl
    {
        get
        {
            var ip = HostIp;
            if (string.IsNullOrWhiteSpace(ip))
                return "";
            if (ip.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || ip.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var trimmed = ip.Trim().TrimEnd('/');
                if (trimmed.Contains(':', StringComparison.Ordinal) && !trimmed.EndsWith($":{HostPort}", StringComparison.Ordinal))
                {
                    try
                    {
                        var uri = new Uri(trimmed);
                        if (uri.IsDefaultPort)
                            return $"{uri.Scheme}://{uri.Host}:{HostPort}";
                        return trimmed;
                    }
                    catch
                    {
                        return trimmed;
                    }
                }
                return trimmed;
            }
            return $"http://{ip.Trim().TrimEnd('/')}:{HostPort}";
        }
    }

    public string? Token
    {
        get => ReadSecureOrMigrate(SecureTokenKey, KeyToken);
        set => WriteSecure(SecureTokenKey, KeyToken, value);
    }

    public string? RefreshToken
    {
        get => ReadSecureOrMigrate(SecureRefreshKey, KeyRefresh);
        set => WriteSecure(SecureRefreshKey, KeyRefresh, value);
    }

    public string UserId
    {
        get => Preferences.Default.Get(KeyUserId, "");
        set => Preferences.Default.Set(KeyUserId, value ?? "");
    }

    public string DisplayName
    {
        get => Preferences.Default.Get(KeyDisplayName, "");
        set => Preferences.Default.Set(KeyDisplayName, value);
    }

    public string Username
    {
        get => Preferences.Default.Get(KeyUsername, "");
        set => Preferences.Default.Set(KeyUsername, value ?? "");
    }

    public string Language
    {
        get => Preferences.Default.Get(KeyLang, "es");
        set => Preferences.Default.Set(KeyLang, value);
    }

    public string LastWarehouseId
    {
        get => Preferences.Default.Get(KeyWarehouseId, "");
        set => Preferences.Default.Set(KeyWarehouseId, value ?? "");
    }

    public bool HasHostConfigured =>
        !string.IsNullOrWhiteSpace(HostIp) && HostPort is > 0 and < 65536;

    public async Task SetPasswordAsync(string? password)
    {
        if (string.IsNullOrEmpty(password))
            SecureStorage.Default.Remove(SecurePasswordKey);
        else
            await SecureStorage.Default.SetAsync(SecurePasswordKey, password);
    }

    public async Task<string?> GetPasswordAsync()
    {
        try { return await SecureStorage.Default.GetAsync(SecurePasswordKey); }
        catch { return null; }
    }

    public void ClearSession()
    {
        Token = null;
        RefreshToken = null;
        UserId = "";
        DisplayName = "";
    }

    public void ApplyHost(string ip, int port)
    {
        HostIp = ip;
        HostPort = port;
    }

    private static string? ReadSecureOrMigrate(string secureKey, string legacyPrefKey)
    {
        try
        {
            var secure = SecureStorage.Default.GetAsync(secureKey).GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(secure))
                return secure;
        }
        catch { /* fall through */ }

        var legacy = Preferences.Default.Get<string?>(legacyPrefKey, null);
        if (!string.IsNullOrEmpty(legacy))
        {
            try
            {
                SecureStorage.Default.SetAsync(secureKey, legacy).GetAwaiter().GetResult();
                Preferences.Default.Remove(legacyPrefKey);
            }
            catch { /* keep legacy readable once */ }
            return legacy;
        }

        return null;
    }

    private static void WriteSecure(string secureKey, string legacyPrefKey, string? value)
    {
        Preferences.Default.Remove(legacyPrefKey);
        try
        {
            if (string.IsNullOrEmpty(value))
                SecureStorage.Default.Remove(secureKey);
            else
                SecureStorage.Default.SetAsync(secureKey, value).GetAwaiter().GetResult();
        }
        catch
        {
            throw new InvalidOperationException("secure_storage_failed");
        }
    }
}
