using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GestionAlmacenVehiculos.Shared.Dtos;

namespace GestionAlmacenVehiculos.Shared.Logi;

/// <summary>Cliente HTTP de la API interna de Logi Compass.</summary>
public sealed class LogiClient : IDisposable
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private string _baseUrl = "";
    private string? _accessToken;
    private string? _refreshToken;
    private string _userName = "";

    public LogiClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public string? AccessToken => _accessToken;
    public string? RefreshToken => _refreshToken;
    public string UserName => _userName;
    public Guid UserId { get; private set; }
    public string DisplayName { get; private set; } = "";
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_accessToken);

    /// <summary>Indica si ya se han cargado los permisos de /me.</summary>
    public bool ClaimsKnown { get; private set; }
    public IReadOnlyList<string> Roles { get; private set; } = [];
    public IReadOnlyList<string> Permissions { get; private set; } = [];

    /// <summary>Comprueba un permiso de Logi. Si aún no hay /me, deniega.</summary>
    public bool HasPermission(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission)) return true;
        if (!ClaimsKnown) return false;
        return Permissions.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase));
    }

    public bool HasAnyPermission(params string[] permissions)
    {
        if (permissions is null || permissions.Length == 0) return true;
        if (!ClaimsKnown) return false;
        return permissions.Any(HasPermission);
    }

    public bool CanManageWarehouse => HasAnyPermission(LogiPermissions.WarehouseManage, LogiPermissions.LocationManage);
    public bool CanAdjustStock => HasAnyPermission(LogiPermissions.StockAdjust, LogiPermissions.StockTransfer);
    public bool CanVerifyAudit => HasPermission(LogiPermissions.AuditVerify);
    public bool CanReadUsers => HasPermission(LogiPermissions.UserRead);

    public void Configure(string baseUrl)
    {
        _baseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(_baseUrl))
            throw new InvalidOperationException("server_url_required");
    }

    public void RestoreSession(string baseUrl, string accessToken, string? refreshToken, string userName, Guid userId, string displayName)
    {
        Configure(baseUrl);
        _accessToken = accessToken;
        _refreshToken = refreshToken;
        _userName = userName;
        UserId = userId;
        DisplayName = displayName;
        ClearClaims();
        ApplyAuth();
    }

    public void ClearSession()
    {
        _accessToken = null;
        _refreshToken = null;
        UserId = Guid.Empty;
        DisplayName = "";
        ClearClaims();
        _http.DefaultRequestHeaders.Authorization = null;
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        var res = await _http.GetAsync(Url(LogiApiPaths.Ping), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<LoginResponse> LoginAsync(string userName, string password, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync(
            Url(LogiApiPaths.Login),
            new { userName, password },
            JsonOptions,
            ct);
        if (res.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("login_failed");
        await EnsureSuccess(res, "login_failed", ct);

        var login = await res.Content.ReadFromJsonAsync<LogiLoginDto>(JsonOptions, ct)
            ?? throw new InvalidOperationException("login_failed");

        _accessToken = login.AccessToken;
        _refreshToken = login.RefreshToken;
        _userName = userName.Trim();
        ApplyAuth();

        try
        {
            var me = await _http.GetFromJsonAsync<LogiMeDto>(Url(LogiApiPaths.Me), JsonOptions, ct);
            if (me is not null)
                ApplyMe(me);
            else
            {
                DisplayName = _userName;
                ApplyEmptyClaims();
            }
        }
        catch
        {
            DisplayName = _userName;
            ApplyEmptyClaims();
        }

        return new LoginResponse
        {
            Token = _accessToken ?? "",
            RefreshToken = _refreshToken ?? "",
            UserId = UserId,
            Username = _userName,
            DisplayName = DisplayName,
        };
    }

    public async Task<bool> ValidateSessionAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_accessToken))
            return false;
        ApplyAuth();
        try
        {
            var res = await _http.GetAsync(Url(LogiApiPaths.Me), ct);
            if (res.StatusCode == HttpStatusCode.Unauthorized && await TryRefreshAsync(ct))
            {
                ApplyAuth();
                res = await _http.GetAsync(Url(LogiApiPaths.Me), ct);
            }

            if (!res.IsSuccessStatusCode)
                return false;

            var me = await res.Content.ReadFromJsonAsync<LogiMeDto>(JsonOptions, ct);
            if (me is null) return false;
            ApplyMe(me);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> TryRefreshAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_refreshToken))
            return false;
        try
        {
            var res = await _http.PostAsJsonAsync(
                Url(LogiApiPaths.Refresh),
                new { refreshToken = _refreshToken },
                JsonOptions,
                ct);
            if (!res.IsSuccessStatusCode)
                return false;
            var login = await res.Content.ReadFromJsonAsync<LogiLoginDto>(JsonOptions, ct);
            if (login is null || string.IsNullOrWhiteSpace(login.AccessToken))
                return false;
            _accessToken = login.AccessToken;
            if (!string.IsNullOrWhiteSpace(login.RefreshToken))
                _refreshToken = login.RefreshToken;
            ApplyAuth();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task LogoutServerAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_refreshToken))
            return;
        try
        {
            ApplyAuth();
            await _http.PostAsJsonAsync(
                Url(LogiApiPaths.Logout),
                new { refreshToken = _refreshToken },
                JsonOptions,
                ct);
        }
        catch
        {
        }
    }

    public async Task<List<LogiWarehouseRow>> ListWarehousesAsync(CancellationToken ct = default) =>
        await GetJsonAuthAsync<List<LogiWarehouseRow>>(LogiApiPaths.Warehouses, ct) ?? [];

    public async Task<LogiWarehouseRow> CreateWarehouseAsync(string code, string name, string siteKind = "Campa", CancellationToken ct = default)
    {
        var res = await PostJsonAuthAsync(LogiApiPaths.Warehouses, new { code, name, siteKind }, ct);
        await EnsureSuccess(res, "create_warehouse_failed", ct);
        return await res.Content.ReadFromJsonAsync<LogiWarehouseRow>(JsonOptions, ct)
            ?? throw new InvalidOperationException("create_warehouse_failed");
    }

    public async Task<List<LogiLocationRow>> ListLocationsAsync(Guid warehouseId, CancellationToken ct = default) =>
        await GetJsonAuthAsync<List<LogiLocationRow>>(LogiApiPaths.WarehouseLocations(warehouseId), ct) ?? [];

    public async Task<LogiLocationRow> CreateLocationAsync(
        Guid warehouseId,
        string code,
        string name,
        string kind,
        Guid? parentId,
        CancellationToken ct = default)
    {
        var res = await PostJsonAuthAsync(
            LogiApiPaths.WarehouseLocations(warehouseId),
            new { code, name, kind, parentId },
            ct);
        await EnsureSuccess(res, "create_location_failed", ct);
        return await res.Content.ReadFromJsonAsync<LogiLocationRow>(JsonOptions, ct)
            ?? throw new InvalidOperationException("create_location_failed");
    }

    public async Task<LogiLocationRow> UpdateLocationAsync(
        Guid warehouseId,
        Guid locationId,
        string name,
        bool isActive = true,
        CancellationToken ct = default)
    {
        var res = await SendAuthAsync(
            HttpMethod.Put,
            LogiApiPaths.WarehouseLocationById(warehouseId, locationId),
            new { name, isActive },
            ct);
        await EnsureSuccess(res, "update_location_failed", ct);
        return await res.Content.ReadFromJsonAsync<LogiLocationRow>(JsonOptions, ct)
            ?? throw new InvalidOperationException("update_location_failed");
    }

    public async Task EnsureDefaultUnitsAsync(CancellationToken ct = default)
    {
        try
        {
            await SendAuthAsync(HttpMethod.Post, LogiApiPaths.UnitsEnsureDefaults, null, ct);
        }
        catch
        {
        }
    }

    public async Task<List<LogiUnitRow>> ListUnitsAsync(CancellationToken ct = default) =>
        await GetJsonAuthAsync<List<LogiUnitRow>>(LogiApiPaths.Units, ct) ?? [];

    public async Task<List<LogiMaterialRow>> ListMaterialsAsync(CancellationToken ct = default) =>
        await GetJsonAuthAsync<List<LogiMaterialRow>>(LogiApiPaths.Materials, ct) ?? [];

    public async Task<LogiMaterialRow> CreateMaterialAsync(
        string code,
        string displayName,
        Guid unitId,
        CancellationToken ct = default)
    {
        var res = await PostJsonAuthAsync(
            LogiApiPaths.Materials,
            new
            {
                code,
                displayName,
                description = "-",
                unitId,
                catalogPrice = 0m,
                currency = "EUR",
                requiresLot = false,
            },
            ct);
        await EnsureSuccess(res, "create_material_failed", ct);
        return await res.Content.ReadFromJsonAsync<LogiMaterialRow>(JsonOptions, ct)
            ?? throw new InvalidOperationException("create_material_failed");
    }

    public async Task<LogiMaterialRow> UpdateMaterialAsync(
        Guid materialId,
        string displayName,
        string description,
        decimal catalogPrice,
        string currency,
        bool isActive,
        bool requiresLot = false,
        CancellationToken ct = default)
    {
        var res = await SendAuthAsync(
            HttpMethod.Put,
            LogiApiPaths.MaterialById(materialId),
            new
            {
                displayName,
                description,
                catalogPrice,
                currency,
                isActive,
                requiresLot,
            },
            ct);
        await EnsureSuccess(res, "update_material_failed", ct);
        return await res.Content.ReadFromJsonAsync<LogiMaterialRow>(JsonOptions, ct)
            ?? throw new InvalidOperationException("update_material_failed");
    }

    public async Task<List<LogiStockRow>> ListStockAsync(CancellationToken ct = default) =>
        await GetJsonAuthAsync<List<LogiStockRow>>(LogiApiPaths.Stock, ct) ?? [];

    public async Task<List<LogiMovementRow>> ListMovementsAsync(int limit = 200, CancellationToken ct = default) =>
        await GetJsonAuthAsync<List<LogiMovementRow>>($"{LogiApiPaths.StockMovements}?limit={limit}", ct) ?? [];

    public async Task<LogiAuditPage> ListAuditEntriesAsync(
        int pageSize = 200,
        int page = 1,
        bool includePayloads = true,
        string? table = null,
        string? action = null,
        string? userName = null,
        Guid? userId = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default)
    {
        var size = Math.Clamp(pageSize, 1, 500);
        var qs = new List<string>
        {
            $"page={Math.Max(1, page)}",
            $"pageSize={size}",
            $"includePayloads={(includePayloads ? "true" : "false")}",
        };
        if (!string.IsNullOrWhiteSpace(table))
            qs.Add($"table={Uri.EscapeDataString(table.Trim())}");
        if (!string.IsNullOrWhiteSpace(action))
            qs.Add($"action={Uri.EscapeDataString(action.Trim())}");
        if (!string.IsNullOrWhiteSpace(userName))
            qs.Add($"userName={Uri.EscapeDataString(userName.Trim())}");
        if (userId is Guid uid)
            qs.Add($"userId={uid:D}");
        if (fromUtc is DateTimeOffset from)
            qs.Add($"fromUtc={Uri.EscapeDataString(from.UtcDateTime.ToString("o"))}");
        if (toUtc is DateTimeOffset to)
            qs.Add($"toUtc={Uri.EscapeDataString(to.UtcDateTime.ToString("o"))}");

        var pageDto = await GetJsonAuthAsync<LogiAuditPageResponse>(
            $"{LogiApiPaths.AuditEntries}?{string.Join("&", qs)}", ct);
        if (pageDto is null)
            return new LogiAuditPage([], 0, page, size);
        return new LogiAuditPage(
            pageDto.Items ?? [],
            pageDto.TotalCount,
            pageDto.Page,
            pageDto.PageSize);
    }

    public async Task<(byte[] Bytes, string FileName, string ContentType)> ExportAuditEntriesAsync(
        string format = "csv",
        string? table = null,
        string? action = null,
        string? userName = null,
        Guid? userId = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default)
    {
        var normalized = string.IsNullOrWhiteSpace(format) ? "csv" : format.Trim().ToLowerInvariant();
        if (normalized is not ("csv" or "json"))
            normalized = "csv";

        var qs = new List<string> { $"format={normalized}" };
        if (!string.IsNullOrWhiteSpace(table))
            qs.Add($"table={Uri.EscapeDataString(table.Trim())}");
        if (!string.IsNullOrWhiteSpace(action))
            qs.Add($"action={Uri.EscapeDataString(action.Trim())}");
        if (!string.IsNullOrWhiteSpace(userName))
            qs.Add($"userName={Uri.EscapeDataString(userName.Trim())}");
        if (userId is Guid uid)
            qs.Add($"userId={uid:D}");
        if (fromUtc is DateTimeOffset from)
            qs.Add($"fromUtc={Uri.EscapeDataString(from.UtcDateTime.ToString("o"))}");
        if (toUtc is DateTimeOffset to)
            qs.Add($"toUtc={Uri.EscapeDataString(to.UtcDateTime.ToString("o"))}");

        var res = await SendAuthAsync(
            HttpMethod.Get,
            $"{LogiApiPaths.AuditEntriesExport}?{string.Join("&", qs)}",
            null,
            ct);
        await EnsureSuccess(res, "audit_export_failed", ct);
        var bytes = await res.Content.ReadAsByteArrayAsync(ct);
        var contentType = normalized == "json" ? "application/json" : "text/csv";
        var fileName = normalized == "json" ? "auditoria.json" : "auditoria.csv";
        return (bytes, fileName, contentType);
    }

    public async Task PostPutawayAsync(
        Guid materialId,
        Guid warehouseId,
        Guid destinationLocationId,
        decimal quantity,
        string? reason = null,
        CancellationToken ct = default)
    {
        var res = await SendAuthAsync(
            HttpMethod.Post,
            LogiApiPaths.StockPutaway,
            new
            {
                materialId,
                warehouseId,
                destinationLocationId,
                quantity,
                reason = string.IsNullOrWhiteSpace(reason) ? "putaway" : reason,
            },
            ct,
            idempotency: true);
        await EnsureSuccess(res, "putaway_failed", ct);
    }

    public async Task PostLocationTransferAsync(
        Guid materialId,
        Guid warehouseId,
        Guid sourceLocationId,
        Guid destinationLocationId,
        decimal quantity,
        string? reason = null,
        CancellationToken ct = default)
    {
        var res = await SendAuthAsync(
            HttpMethod.Post,
            LogiApiPaths.StockLocationTransfers,
            new
            {
                materialId,
                warehouseId,
                sourceLocationId,
                destinationLocationId,
                quantity,
                reason = string.IsNullOrWhiteSpace(reason) ? "relocate" : reason,
            },
            ct,
            idempotency: true);
        await EnsureSuccess(res, "location_transfer_failed", ct);
    }

    public async Task PostOpeningAsync(
        Guid materialId,
        Guid warehouseId,
        decimal quantity,
        CancellationToken ct = default) =>
        await PostAdjustmentAsync(materialId, warehouseId, quantity, "Opening", "opening", ct);

    public async Task PostAdjustmentAsync(
        Guid materialId,
        Guid warehouseId,
        decimal signedQuantity,
        string type,
        string reason,
        CancellationToken ct = default)
    {
        var res = await SendAuthAsync(
            HttpMethod.Post,
            LogiApiPaths.StockAdjustments,
            new
            {
                materialId,
                warehouseId,
                signedQuantity,
                type,
                reason,
            },
            ct,
            idempotency: true);
        await EnsureSuccess(res, "adjustment_failed", ct);
    }

    public async Task<List<UserDto>> GetUsersAsync(CancellationToken ct = default)
    {
        var rows = await GetJsonAuthAsync<List<LogiUserDto>>(LogiApiPaths.Users, ct) ?? [];
        return rows.Select(u => new UserDto
        {
            Id = u.Id,
            Username = u.UserName,
            DisplayName = u.UserName,
            IsActive = !u.IsSuspended,
            Roles = u.Roles?.ToList() ?? [],
        }).ToList();
    }

    public void Dispose() => _http.Dispose();

    private void ApplyMe(LogiMeDto me)
    {
        UserId = me.Id;
        DisplayName = me.UserName;
        _userName = me.UserName;
        Roles = me.Roles?.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            ?? [];
        Permissions = me.Permissions?.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            ?? [];
        ClaimsKnown = true;
    }

    /// <summary>Login OK pero /me falló: sin permisos hasta reintentar.</summary>
    private void ApplyEmptyClaims()
    {
        Roles = [];
        Permissions = [];
        ClaimsKnown = true;
    }

    private void ClearClaims()
    {
        ClaimsKnown = false;
        Roles = [];
        Permissions = [];
    }

    private async Task<T?> GetJsonAuthAsync<T>(string path, CancellationToken ct)
    {
        var res = await SendAuthAsync(HttpMethod.Get, path, null, ct);
        await EnsureSuccess(res, "request_failed", ct);
        return await res.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
    }

    private Task<HttpResponseMessage> PostJsonAuthAsync(string path, object body, CancellationToken ct) =>
        SendAuthAsync(HttpMethod.Post, path, body, ct);

    private async Task<HttpResponseMessage> SendAuthAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken ct,
        bool idempotency = false)
    {
        ApplyAuth();
        async Task<HttpResponseMessage> Once()
        {
            using var req = new HttpRequestMessage(method, Url(path));
            if (idempotency)
                req.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
            if (body is not null)
                req.Content = JsonContent.Create(body, options: JsonOptions);
            return await _http.SendAsync(req, ct);
        }

        var res = await Once();
        if (res.StatusCode == HttpStatusCode.Unauthorized && await TryRefreshAsync(ct))
        {
            res.Dispose();
            ApplyAuth();
            res = await Once();
        }

        return res;
    }

    private Uri Url(string path)
    {
        if (string.IsNullOrWhiteSpace(_baseUrl))
            throw new InvalidOperationException("server_url_required");
        return new Uri(new Uri(_baseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));
    }

    private void ApplyAuth()
    {
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(_accessToken)
            ? null
            : new AuthenticationHeaderValue("Bearer", _accessToken);
    }

    private static async Task EnsureSuccess(HttpResponseMessage res, string fallback, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        if (res.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("permission_denied");
        if (res.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException(
                string.Equals(fallback, "login_failed", StringComparison.Ordinal)
                    ? "login_failed"
                    : "session_expired");
        var err = (await res.Content.ReadAsStringAsync(ct)).Trim();
        var useBody = err.Length is > 0 and < 64
            && !err.Contains('{')
            && !err.Contains(' ')
            && !err.Contains('\n');
        throw new InvalidOperationException(useBody ? err : fallback);
    }

    private sealed class LogiLoginDto
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
    }

    private sealed class LogiMeDto
    {
        public Guid Id { get; set; }
        public string UserName { get; set; } = "";
        public List<string>? Roles { get; set; }
        public List<string>? Permissions { get; set; }
    }

    private sealed class LogiUserDto
    {
        public Guid Id { get; set; }
        public string UserName { get; set; } = "";
        public bool IsSuspended { get; set; }
        public List<string>? Roles { get; set; }
    }
}

/// <summary>Nombres de permisos de Logi Compass (/me).</summary>
public static class LogiPermissions
{
    public const string WarehouseManage = "Warehouse.Manage";
    public const string LocationManage = "Location.Manage";
    public const string StockAdjust = "Stock.Adjust";
    public const string StockTransfer = "Stock.Transfer";
    public const string StockRead = "Stock.Read";
    public const string AuditVerify = "Audit.Verify";
    public const string UserRead = "User.Read";
}

public sealed class LogiWarehouseRow
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string SiteKind { get; set; } = "";
}

public sealed class LogiLocationRow
{
    public Guid Id { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? ParentId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class LogiUnitRow
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class LogiMaterialRow
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid UnitId { get; set; }
    public decimal CatalogPrice { get; set; }
    public string Currency { get; set; } = "EUR";
    public bool IsActive { get; set; } = true;
    public string? ExternalId { get; set; }
    public bool RequiresLot { get; set; }
}

public sealed class LogiStockRow
{
    public Guid MaterialId { get; set; }
    public string MaterialCode { get; set; } = "";
    public Guid WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = "";
    public decimal Quantity { get; set; }
    public Guid? LocationId { get; set; }
    public string? LocationCode { get; set; }
}

public sealed class LogiMovementRow
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public Guid MaterialId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal SignedQuantity { get; set; }
    public DateTimeOffset PostedUtc { get; set; }
    public string? Reason { get; set; }
}

public sealed class LogiAuditEntryRow
{
    public long Sequence { get; set; }
    public string TableName { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string Action { get; set; } = "";
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Ip { get; set; }
    public DateTimeOffset Utc { get; set; }
    public string? OldJson { get; set; }
    public string? NewJson { get; set; }
}

public sealed class LogiAuditPageResponse
{
    public List<LogiAuditEntryRow>? Items { get; set; }
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public sealed record LogiAuditPage(
    IReadOnlyList<LogiAuditEntryRow> Items,
    int TotalCount,
    int Page,
    int PageSize);
