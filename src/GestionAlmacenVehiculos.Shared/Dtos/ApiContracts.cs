namespace GestionAlmacenVehiculos.Shared.Dtos;

public sealed class LoginResponse
{
    public string Token { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Username { get; set; } = "";
}

public sealed class WarehouseDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Rows { get; set; }
    public int Columns { get; set; }
    public string SiteKind { get; set; } = "";
}

public sealed class CreateWarehouseRequest
{
    public string Name { get; set; } = "";
    public int Rows { get; set; }
    public int Columns { get; set; }
}

public sealed class VehicleUpdateRequest
{
    public Guid WarehouseId { get; set; }
    public string Model { get; set; } = "";
    public string Bin { get; set; } = "";
    public string? Color { get; set; }
    public int Row { get; set; }
    public int Position { get; set; }
    public string? Notes { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public sealed class VehicleUpdateDto
{
    public Guid Id { get; set; }
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public string Model { get; set; } = "";
    public string Bin { get; set; } = "";
    public string? Color { get; set; }
    public int Row { get; set; }
    public int Position { get; set; }
    public string UserDisplayName { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Notes { get; set; }
    public string? StockWarning { get; set; }
}

public sealed class ActivityLogEntryDto
{
    public DateTime Utc { get; set; }
    public string UserDisplayName { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Detail { get; set; } = "";
    public string? WarehouseName { get; set; }
    public string? Bin { get; set; }
    public string Source { get; set; } = "";
}

public sealed class UserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = [];
}

/// <summary>Rutas de la API interna de Logi Compass.</summary>
public static class LogiApiPaths
{
    public const string Ping = "/api/v1/internal/ping";
    public const string Login = "/api/v1/internal/auth/login";
    public const string Refresh = "/api/v1/internal/auth/refresh";
    public const string Logout = "/api/v1/internal/auth/logout";
    public const string Me = "/api/v1/internal/me";
    public const string Users = "/api/v1/internal/users";
    public const string Warehouses = "/api/v1/internal/warehouses";
    public const string Units = "/api/v1/internal/units";
    public const string UnitsEnsureDefaults = "/api/v1/internal/units/ensure-defaults";
    public const string Materials = "/api/v1/internal/materials";
    public const string Stock = "/api/v1/internal/stock";
    public const string StockMovements = "/api/v1/internal/stock/movements";
    public const string StockPutaway = "/api/v1/internal/stock/putaway";
    public const string StockLocationTransfers = "/api/v1/internal/stock/location-transfers";
    public const string StockAdjustments = "/api/v1/internal/stock/adjustments/post";
    public const string AuditEntries = "/api/v1/internal/audit/entries";
    public const string AuditEntriesExport = "/api/v1/internal/audit/entries/export";

    public static string WarehouseLocations(Guid warehouseId) =>
        $"/api/v1/internal/warehouses/{warehouseId:D}/locations";

    public static string WarehouseLocationById(Guid warehouseId, Guid locationId) =>
        $"/api/v1/internal/warehouses/{warehouseId:D}/locations/{locationId:D}";

    public static string MaterialById(Guid materialId) =>
        $"/api/v1/internal/materials/{materialId:D}";
}
