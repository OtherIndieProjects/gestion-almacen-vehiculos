using GestionAlmacenVehiculos.Shared.Dtos;
using GestionAlmacenVehiculos.Shared.Logi;

namespace GestionAlmacenVehiculos.Mobile.Services;

/// <summary>Cliente móvil sobre Logi Compass.</summary>
public sealed class ApiClient
{
    private readonly AppSettings _settings;
    private readonly LogiClient _logi = new();
    private ParkingMapper? _mapper;

    public ApiClient(AppSettings settings) => _settings = settings;

    private ParkingMapper EnsureConfigured()
    {
        if (!_settings.HasHostConfigured)
            throw new InvalidOperationException("host_not_configured");
        _logi.Configure(_settings.BaseUrl);
        if (!string.IsNullOrWhiteSpace(_settings.Token)
            && Guid.TryParse(_settings.UserId, out var userId))
        {
            _logi.RestoreSession(
                _settings.BaseUrl,
                _settings.Token,
                _settings.RefreshToken,
                _settings.Username,
                userId,
                _settings.DisplayName);
        }

        _mapper ??= new ParkingMapper(_logi, Path.Combine(FileSystem.AppDataDirectory, "park-history.json"));
        return _mapper;
    }

    public async Task<bool> HealthAsync(CancellationToken ct = default)
    {
        EnsureConfigured();
        return await _logi.PingAsync(ct);
    }

    public async Task<bool> ValidateSessionAsync(CancellationToken ct = default)
    {
        EnsureConfigured();
        var ok = await _logi.ValidateSessionAsync(ct);
        if (ok)
        {
            _settings.Token = _logi.AccessToken;
            _settings.RefreshToken = _logi.RefreshToken;
            _settings.UserId = _logi.UserId.ToString("D");
            _settings.DisplayName = _logi.DisplayName;
            _settings.Username = _logi.UserName;
        }
        return ok;
    }

    public async Task<LoginResponse> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        EnsureConfigured();
        var res = await _logi.LoginAsync(username, password, ct);
        _settings.Token = res.Token;
        _settings.RefreshToken = res.RefreshToken;
        _settings.UserId = res.UserId.ToString("D");
        _settings.DisplayName = res.DisplayName;
        _settings.Username = res.Username;
        return res;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            EnsureConfigured();
            await _logi.LogoutServerAsync(ct);
        }
        catch { /* ignore */ }
        _logi.ClearSession();
        _settings.ClearSession();
    }

    public async Task<List<WarehouseDto>> GetWarehousesAsync(CancellationToken ct = default)
    {
        var mapper = EnsureConfigured();
        return await mapper.GetWarehousesAsync(ct);
    }

    public async Task<VehicleUpdateDto> SendVehicleAsync(VehicleUpdateRequest req, CancellationToken ct = default)
    {
        var mapper = EnsureConfigured();
        return await mapper.ParkAsync(req, ct);
    }

    public async Task<List<VehicleUpdateDto>> GetCurrentAsync(CancellationToken ct = default)
    {
        var mapper = EnsureConfigured();
        return await mapper.GetCurrentAsync(ct);
    }

    public async Task<List<VehicleUpdateDto>> GetHistoryAsync(string? binFilter = null, int take = 50, CancellationToken ct = default)
    {
        var mapper = EnsureConfigured();
        return await mapper.GetHistoryAsync(binFilter, take, ct);
    }
}
