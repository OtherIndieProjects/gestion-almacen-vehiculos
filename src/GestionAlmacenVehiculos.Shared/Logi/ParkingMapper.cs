using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GestionAlmacenVehiculos.Shared.Dtos;

namespace GestionAlmacenVehiculos.Shared.Logi;

/// <summary>Mapea almacenes y stock de Logi Compass a aparcamiento de vehículos.</summary>
public sealed class ParkingMapper
{
    private const string OccupancySeparator = " · ";
    internal const string ParkReasonPrefix = "vehiculos:";
    internal const string UnparkReasonPrefix = "vehiculos-unpark:";
    private const string ClearReason = "vehiculos-clear";
    private const string LegacyParkReasonPrefix = "kngloo:";
    private const string LegacyUnparkReasonPrefix = "kngloo-unpark:";

    private readonly LogiClient _logi;
    private readonly LocalParkHistory _history;

    public ParkingMapper(LogiClient logi, string? historyPath = null)
    {
        _logi = logi;
        _history = new LocalParkHistory(historyPath);
    }

    public LogiClient Client => _logi;

    public async Task<List<WarehouseDto>> GetWarehousesAsync(CancellationToken ct = default)
    {
        var warehouses = await _logi.ListWarehousesAsync(ct);
        var campa = warehouses
            .Where(w => string.Equals(w.SiteKind, "Campa", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var source = campa.Count > 0 ? campa : warehouses;

        var result = new List<WarehouseDto>(source.Count);
        foreach (var wh in source)
        {
            var locations = await _logi.ListLocationsAsync(wh.Id, ct);
            var (rows, cols) = InferGrid(locations);
            result.Add(new WarehouseDto
            {
                Id = wh.Id,
                Code = wh.Code,
                Name = wh.Name,
                SiteKind = wh.SiteKind,
                Rows = rows,
                Columns = cols,
            });
        }

        return result;
    }

    public async Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseRequest req, CancellationToken ct = default)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length < 1 || req.Rows < 1 || req.Columns < 1)
            throw new InvalidOperationException("invalid_warehouse");

        var code = MakeWarehouseCode(name);
        var created = await _logi.CreateWarehouseAsync(code, name, "Campa", ct);
        await EnsureGridAsync(created.Id, req.Rows, req.Columns, ct);
        var locations = await _logi.ListLocationsAsync(created.Id, ct);
        var (rows, cols) = InferGrid(locations);
        return new WarehouseDto
        {
            Id = created.Id,
            Code = created.Code,
            Name = created.Name,
            SiteKind = created.SiteKind,
            Rows = rows,
            Columns = cols,
        };
    }

    public async Task EnsureGridAsync(Guid warehouseId, int rows, int columns, CancellationToken ct = default)
    {
        if (rows < 1 || columns < 1)
            throw new InvalidOperationException("invalid_grid");

        var locations = await _logi.ListLocationsAsync(warehouseId, ct);
        var root = locations.FirstOrDefault(l =>
            string.Equals(l.Kind, "Root", StringComparison.OrdinalIgnoreCase)
            || string.Equals(l.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase));

        Guid? parentForRows = root?.Id;

        for (var r = 1; r <= rows; r++)
        {
            var rowCode = RowCode(r);
            var rowNode = locations.FirstOrDefault(l =>
                string.Equals(l.Kind, "Row", StringComparison.OrdinalIgnoreCase)
                && string.Equals(l.Code, rowCode, StringComparison.OrdinalIgnoreCase));

            if (rowNode is null)
            {
                rowNode = await _logi.CreateLocationAsync(
                    warehouseId, rowCode, $"Fila {r}", "Row", parentForRows, ct);
                locations.Add(rowNode);
            }

            for (var c = 1; c <= columns; c++)
            {
                var posCode = PositionCode(r, c);
                if (locations.Any(l =>
                        string.Equals(l.Kind, "Position", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(l.Code, posCode, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var pos = await _logi.CreateLocationAsync(
                    warehouseId, posCode, DefaultPositionName(r, c), "Position", rowNode.Id, ct);
                locations.Add(pos);
            }
        }
    }

    public async Task<VehicleUpdateDto> ParkAsync(VehicleUpdateRequest req, CancellationToken ct = default)
    {
        var modelDisplay = (req.Model ?? "").Trim();
        var model = NormalizeModel(modelDisplay);
        var bin = (req.Bin ?? "").Trim().ToUpperInvariant();
        var color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim();

        if (model.Length < 1)
            throw new InvalidOperationException("model_required");
        if (bin.Length < 4)
            throw new InvalidOperationException("bin_min_4");
        if (req.Row < 1 || req.Position < 1)
            throw new InvalidOperationException("invalid_slot");

        var warehouses = await _logi.ListWarehousesAsync(ct);
        var warehouse = warehouses.FirstOrDefault(w => w.Id == req.WarehouseId)
            ?? throw new InvalidOperationException("warehouse_not_found");

        var locations = await _logi.ListLocationsAsync(req.WarehouseId, ct);
        var (maxRows, maxCols) = InferGrid(locations);
        if (maxRows < req.Row || maxCols < req.Position)
        {
            await EnsureGridAsync(req.WarehouseId, Math.Max(maxRows, req.Row), Math.Max(maxCols, req.Position), ct);
            locations = await _logi.ListLocationsAsync(req.WarehouseId, ct);
        }

        var posCode = PositionCode(req.Row, req.Position);
        var position = locations.FirstOrDefault(l =>
            string.Equals(l.Kind, "Position", StringComparison.OrdinalIgnoreCase)
            && string.Equals(l.Code, posCode, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("position_not_found");

        var root = FindRoot(locations)
            ?? throw new InvalidOperationException("root_not_found");

        var now = DateTime.UtcNow;
        var material = await EnsureMaterialAsync(model, modelDisplay, ct);

        await FreePositionAsync(req.WarehouseId, position, root.Id, locations, ct);
        await FreeBinElsewhereAsync(req.WarehouseId, bin, position.Id, root.Id, locations, ct);

        var parkReason = ParkReason.Serialize(
            bin, color, req.Row, req.Position, _logi.UserName, now, req.Latitude, req.Longitude, req.Notes);

        await SyncStockOrThrowAsync(material.Id, req.WarehouseId, position.Id, root.Id, parkReason, ct);

        await _logi.UpdateLocationAsync(
            req.WarehouseId,
            position.Id,
            OccupancyName(bin, color),
            position.IsActive,
            ct);

        var dto = new VehicleUpdateDto
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            WarehouseName = warehouse.Name,
            Model = string.IsNullOrWhiteSpace(material.DisplayName) ? model : material.DisplayName,
            Bin = bin,
            Color = color,
            Row = req.Row,
            Position = req.Position,
            UserDisplayName = _logi.UserName,
            CreatedAtUtc = now,
            Latitude = req.Latitude,
            Longitude = req.Longitude,
            Notes = req.Notes,
        };
        _history.Append(dto);
        return dto;
    }

    public async Task<VehicleUpdateDto> UnparkAsync(
        Guid warehouseId,
        int row,
        int position,
        CancellationToken ct = default)
    {
        if (row < 1 || position < 1)
            throw new InvalidOperationException("invalid_slot");

        var warehouses = await _logi.ListWarehousesAsync(ct);
        var warehouse = warehouses.FirstOrDefault(w => w.Id == warehouseId)
            ?? throw new InvalidOperationException("warehouse_not_found");

        var locations = await _logi.ListLocationsAsync(warehouseId, ct);
        var posCode = PositionCode(row, position);
        var pos = locations.FirstOrDefault(l =>
            string.Equals(l.Kind, "Position", StringComparison.OrdinalIgnoreCase)
            && string.Equals(l.Code, posCode, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("position_not_found");

        var root = FindRoot(locations)
            ?? throw new InvalidOperationException("root_not_found");

        var (bin, color) = ParseOccupancyName(pos.Name);
        var materials = (await _logi.ListMaterialsAsync(ct)).ToDictionary(m => m.Id);
        var stockAtPos = (await _logi.ListStockAsync(ct))
            .Where(s => s.WarehouseId == warehouseId && s.LocationId == pos.Id && s.Quantity > 0)
            .ToList();

        string model = "";
        if (stockAtPos.Count > 0 && materials.TryGetValue(stockAtPos[0].MaterialId, out var mat))
            model = !string.IsNullOrWhiteSpace(mat.DisplayName) ? mat.DisplayName : mat.Code;

        var now = DateTime.UtcNow;
        var unparkReason = ParkReason.SerializeUnpark(bin, color, row, position, _logi.UserName, now);

        foreach (var line in stockAtPos)
        {
            await _logi.PostLocationTransferAsync(
                line.MaterialId,
                warehouseId,
                pos.Id,
                root.Id,
                line.Quantity,
                unparkReason,
                ct);
        }

        await _logi.UpdateLocationAsync(
            warehouseId,
            pos.Id,
            DefaultPositionName(row, position),
            pos.IsActive,
            ct);

        var dto = new VehicleUpdateDto
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            WarehouseName = warehouse.Name,
            Model = model,
            Bin = bin,
            Color = color,
            Row = row,
            Position = position,
            UserDisplayName = _logi.UserName,
            CreatedAtUtc = now,
            Notes = "unpark",
        };
        _history.Append(dto);
        return dto;
    }

    public async Task<List<VehicleUpdateDto>> GetCurrentAsync(CancellationToken ct = default)
    {
        var warehouses = (await _logi.ListWarehousesAsync(ct)).ToDictionary(w => w.Id);
        var materials = (await _logi.ListMaterialsAsync(ct)).ToDictionary(m => m.Id);
        var stock = await _logi.ListStockAsync(ct);
        var parkEvents = await LoadParkEventsAsync(500, ct);
        var locationCache = new Dictionary<Guid, List<LogiLocationRow>>();
        var result = new List<VehicleUpdateDto>();

        foreach (var line in stock.Where(s => s.Quantity > 0 && s.LocationId is not null))
        {
            if (!warehouses.TryGetValue(line.WarehouseId, out var warehouse))
                continue;
            if (!materials.TryGetValue(line.MaterialId, out var material))
                continue;

            if (!locationCache.TryGetValue(line.WarehouseId, out var locs))
            {
                locs = await _logi.ListLocationsAsync(line.WarehouseId, ct);
                locationCache[line.WarehouseId] = locs;
            }

            var position = locs.FirstOrDefault(l =>
                l.Id == line.LocationId
                && string.Equals(l.Kind, "Position", StringComparison.OrdinalIgnoreCase));
            if (position is null)
                continue;

            var (row, col) = ParseSlot(position.Code);
            if (row < 1 || col < 1)
                continue;

            var (bin, color) = ParseOccupancyName(position.Name);
            var model = !string.IsNullOrWhiteSpace(material.DisplayName)
                ? material.DisplayName
                : material.Code;

            var ev = parkEvents.FirstOrDefault(e =>
                e.WarehouseId == line.WarehouseId
                && e.MaterialId == line.MaterialId
                && e.Payload.R == row
                && e.Payload.P == col
                && !e.IsUnpark);

            result.Add(new VehicleUpdateDto
            {
                Id = line.LocationId!.Value,
                WarehouseId = warehouse.Id,
                WarehouseName = warehouse.Name,
                Model = model,
                Bin = string.IsNullOrWhiteSpace(bin) ? (ev?.Payload.B ?? "") : bin,
                Color = color ?? ev?.Payload.C,
                Row = row,
                Position = col,
                UserDisplayName = ev?.Payload.U ?? "",
                CreatedAtUtc = ev?.PostedUtc ?? DateTime.UtcNow,
                Latitude = ev?.Payload.Lat,
                Longitude = ev?.Payload.Lon,
                Notes = ev?.Payload.N,
            });
        }

        return result
            .OrderBy(v => v.WarehouseName)
            .ThenBy(v => v.Row)
            .ThenBy(v => v.Position)
            .ToList();
    }

    /// <summary>Historial de aparcamientos y, si hay permiso, auditoría de Logi.</summary>
    public async Task<(List<ActivityLogEntryDto> Entries, bool AuditLimited)> GetActivityLogAsync(
        string? filter = null,
        string? kindFilter = null,
        int take = 300,
        CancellationToken ct = default)
    {
        take = take is < 1 or > 1000 ? 300 : take;
        var entries = new List<ActivityLogEntryDto>();
        var users = (await _logi.GetUsersAsync(ct)).ToDictionary(u => u.Id);
        var warehouses = (await _logi.ListWarehousesAsync(ct)).ToDictionary(w => w.Id);
        var materials = (await _logi.ListMaterialsAsync(ct)).ToDictionary(m => m.Id);

        foreach (var ev in await LoadParkEventsAsync(Math.Max(take, 200), ct))
        {
            materials.TryGetValue(ev.MaterialId, out var mat);
            warehouses.TryGetValue(ev.WarehouseId, out var wh);
            var model = mat is null
                ? ""
                : (!string.IsNullOrWhiteSpace(mat.DisplayName) ? mat.DisplayName : mat.Code);
            var kind = ev.IsUnpark ? "unpark" : "park";
            var bin = ev.Payload.B ?? "";
            var detail = $"{model} · F{ev.Payload.R}-P{ev.Payload.P}"
                + (string.IsNullOrWhiteSpace(ev.Payload.C) ? "" : $" · {ev.Payload.C}");
            entries.Add(new ActivityLogEntryDto
            {
                Utc = ev.PostedUtc,
                UserDisplayName = ev.Payload.U ?? "",
                Kind = kind,
                Detail = detail,
                WarehouseName = wh?.Name,
                Bin = bin,
                Source = "stock",
            });
        }

        var auditLimited = false;
        try
        {
            var audit = await _logi.ListAuditEntriesAsync(
                pageSize: Math.Min(take, 500),
                page: 1,
                includePayloads: true,
                ct: ct);
            foreach (var a in audit.Items)
            {
                var mapped = MapAuditEntry(a, users);
                if (mapped is null) continue;
                entries.Add(mapped);
            }
        }
        catch (InvalidOperationException ex) when (
            string.Equals(ex.Message, "permission_denied", StringComparison.OrdinalIgnoreCase))
        {
            auditLimited = true;
        }

        IEnumerable<ActivityLogEntryDto> q = entries;
        if (!string.IsNullOrWhiteSpace(kindFilter)
            && !string.Equals(kindFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            var k = kindFilter.Trim();
            q = q.Where(e => string.Equals(e.Kind, k, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var f = filter.Trim();
            q = q.Where(e =>
                e.Detail.Contains(f, StringComparison.OrdinalIgnoreCase)
                || e.UserDisplayName.Contains(f, StringComparison.OrdinalIgnoreCase)
                || (e.Bin?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false)
                || (e.WarehouseName?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false)
                || e.Kind.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        return (q.OrderByDescending(e => e.Utc).Take(take).ToList(), auditLimited);
    }

    private static ActivityLogEntryDto? MapAuditEntry(
        LogiAuditEntryRow a,
        IReadOnlyDictionary<Guid, UserDto> users)
    {
        var table = (a.TableName ?? "").Trim();
        string kind;
        string label;
        if (string.Equals(table, "auth_sessions", StringComparison.OrdinalIgnoreCase))
        {
            kind = string.Equals(a.Action, "LOGIN", StringComparison.OrdinalIgnoreCase) ? "login"
                : string.Equals(a.Action, "LOGOUT", StringComparison.OrdinalIgnoreCase) ? "logout"
                : "auth";
            label = a.Action;
        }
        else if (string.Equals(table, "warehouses", StringComparison.OrdinalIgnoreCase))
        {
            kind = "warehouse";
            label = $"{a.Action} warehouse";
        }
        else if (string.Equals(table, "location_nodes", StringComparison.OrdinalIgnoreCase))
        {
            kind = "location";
            label = $"{a.Action} location";
        }
        else if (string.Equals(table, "materials", StringComparison.OrdinalIgnoreCase))
        {
            kind = "material";
            label = $"{a.Action} material";
        }
        else if (string.Equals(table, "units", StringComparison.OrdinalIgnoreCase)
                 || table.StartsWith("unit", StringComparison.OrdinalIgnoreCase))
        {
            kind = "audit";
            label = $"{a.Action} {table}";
        }
        else
        {
            if (table.Contains("stock", StringComparison.OrdinalIgnoreCase)
                || table.Contains("refresh", StringComparison.OrdinalIgnoreCase)
                || table.Contains("idempotency", StringComparison.OrdinalIgnoreCase))
                return null;
            kind = "audit";
            label = $"{a.Action} {table}";
        }

        var userName = !string.IsNullOrWhiteSpace(a.UserName)
            ? a.UserName!.Trim()
            : "";
        if (string.IsNullOrWhiteSpace(userName) && a.UserId is Guid uid && users.TryGetValue(uid, out var u))
            userName = string.IsNullOrWhiteSpace(u.DisplayName) ? u.Username : u.DisplayName;
        else if (string.IsNullOrWhiteSpace(userName) && a.UserId is Guid id)
            userName = id.ToString("D")[..8];

        var detail = $"{label} · {a.RecordId}";
        var payloadSnippet = ClipJsonSnippet(a.NewJson) ?? ClipJsonSnippet(a.OldJson);
        if (!string.IsNullOrWhiteSpace(payloadSnippet))
            detail += $" · {payloadSnippet}";
        if (!string.IsNullOrWhiteSpace(a.Ip))
            detail += $" · IP {a.Ip}";

        return new ActivityLogEntryDto
        {
            Utc = a.Utc.UtcDateTime,
            UserDisplayName = userName,
            Kind = kind,
            Detail = detail,
            Source = "audit",
        };
    }

    private static string? ClipJsonSnippet(string? json, int max = 120)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var t = json.Trim().Replace('\r', ' ').Replace('\n', ' ');
        while (t.Contains("  ", StringComparison.Ordinal))
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        if (t.Length <= max) return t;
        return t[..max] + "…";
    }

    public async Task<List<VehicleUpdateDto>> GetHistoryAsync(string? binFilter = null, int take = 300, CancellationToken ct = default)
    {
        take = take is < 1 or > 1000 ? 300 : take;
        var materials = (await _logi.ListMaterialsAsync(ct)).ToDictionary(m => m.Id);
        var warehouses = (await _logi.ListWarehousesAsync(ct)).ToDictionary(w => w.Id);
        var events = await LoadParkEventsAsync(Math.Max(take, 200), ct);
        var result = new List<VehicleUpdateDto>();

        foreach (var ev in events)
        {
            materials.TryGetValue(ev.MaterialId, out var mat);
            warehouses.TryGetValue(ev.WarehouseId, out var wh);
            var model = mat is null
                ? ""
                : (!string.IsNullOrWhiteSpace(mat.DisplayName) ? mat.DisplayName : mat.Code);
            var bin = ev.Payload.B ?? "";
            var color = ev.Payload.C;

            if (!string.IsNullOrWhiteSpace(binFilter))
            {
                var f = binFilter.Trim();
                if (bin.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0
                    && model.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
            }

            result.Add(new VehicleUpdateDto
            {
                Id = ev.MovementId,
                WarehouseId = ev.WarehouseId,
                WarehouseName = wh?.Name ?? ev.WarehouseId.ToString("D"),
                Model = model,
                Bin = bin,
                Color = color,
                Row = ev.Payload.R,
                Position = ev.Payload.P,
                UserDisplayName = ev.Payload.U ?? "",
                CreatedAtUtc = ev.PostedUtc,
                Latitude = ev.Payload.Lat,
                Longitude = ev.Payload.Lon,
                Notes = ev.IsUnpark ? "unpark" : ev.Payload.N,
            });
        }

        if (result.Count == 0)
            return _history.List(binFilter, take);

        return result
            .OrderByDescending(v => v.CreatedAtUtc)
            .Take(take)
            .ToList();
    }

    private async Task<List<ParkEvent>> LoadParkEventsAsync(int limit, CancellationToken ct)
    {
        var movements = await _logi.ListMovementsAsync(Math.Clamp(limit, 50, 1000), ct);
        var list = new List<ParkEvent>();
        foreach (var mv in movements.OrderByDescending(m => m.PostedUtc))
        {
            var reason = mv.Reason ?? "";
            var unparkPrefix = MatchPrefix(reason, UnparkReasonPrefix, LegacyUnparkReasonPrefix);
            var parkPrefix = unparkPrefix is null
                ? MatchPrefix(reason, ParkReasonPrefix, LegacyParkReasonPrefix)
                : null;
            var isUnpark = unparkPrefix is not null;
            var isPark = parkPrefix is not null;
            if (!isPark && !isUnpark)
                continue;

            if (isPark && mv.SignedQuantity <= 0)
                continue;

            var usedPrefix = isUnpark ? unparkPrefix! : parkPrefix!;
            var json = reason[usedPrefix.Length..];
            var payload = ParkReason.Parse(json);
            list.Add(new ParkEvent(
                mv.Id,
                mv.MaterialId,
                mv.WarehouseId,
                mv.PostedUtc.UtcDateTime,
                payload,
                isUnpark));
        }

        return list;
    }

    private static string? MatchPrefix(string reason, params string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (reason.StartsWith(prefix, StringComparison.Ordinal))
                return prefix;
        }
        return null;
    }

    private async Task<LogiMaterialRow> EnsureMaterialAsync(
        string modelCode,
        string modelDisplay,
        CancellationToken ct)
    {
        var materials = await _logi.ListMaterialsAsync(ct);
        var existing = materials.FirstOrDefault(m =>
            string.Equals(m.Code, modelCode, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing;

        var unitId = await ResolveUnitIdAsync(ct);
        var displayName = string.IsNullOrWhiteSpace(modelDisplay) ? modelCode : modelDisplay;
        return await _logi.CreateMaterialAsync(modelCode, displayName, unitId, ct);
    }

    private async Task FreePositionAsync(
        Guid warehouseId,
        LogiLocationRow position,
        Guid rootId,
        List<LogiLocationRow> locations,
        CancellationToken ct)
    {
        var stock = (await _logi.ListStockAsync(ct))
            .Where(s =>
                s.WarehouseId == warehouseId
                && s.LocationId == position.Id
                && s.Quantity > 0)
            .ToList();

        foreach (var line in stock)
        {
            await _logi.PostLocationTransferAsync(
                line.MaterialId,
                warehouseId,
                position.Id,
                rootId,
                line.Quantity,
                ClearReason,
                ct);
        }

        var (row, col) = ParseSlot(position.Code);
        if (row < 1 || col < 1)
            return;

        var updated = await _logi.UpdateLocationAsync(
            warehouseId,
            position.Id,
            DefaultPositionName(row, col),
            position.IsActive,
            ct);
        var idx = locations.FindIndex(l => l.Id == position.Id);
        if (idx >= 0)
            locations[idx] = updated;
        else
            position.Name = updated.Name;
    }

    private async Task FreeBinElsewhereAsync(
        Guid warehouseId,
        string bin,
        Guid keepPositionId,
        Guid rootId,
        List<LogiLocationRow> locations,
        CancellationToken ct)
    {
        var matches = locations
            .Where(l =>
                l.Id != keepPositionId
                && string.Equals(l.Kind, "Position", StringComparison.OrdinalIgnoreCase)
                && NameStartsWithBin(l.Name, bin))
            .ToList();

        foreach (var pos in matches)
            await FreePositionAsync(warehouseId, pos, rootId, locations, ct);
    }

    private async Task<Guid> ResolveUnitIdAsync(CancellationToken ct)
    {
        await _logi.EnsureDefaultUnitsAsync(ct);
        var units = await _logi.ListUnitsAsync(ct);
        var unit = units.FirstOrDefault(u =>
            string.Equals(u.Code, "UN", StringComparison.OrdinalIgnoreCase)
            || string.Equals(u.Code, "EA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(u.Code, "UDS", StringComparison.OrdinalIgnoreCase))
            ?? units.FirstOrDefault()
            ?? throw new InvalidOperationException("no_units");
        return unit.Id;
    }

    private async Task SyncStockOrThrowAsync(
        Guid materialId,
        Guid warehouseId,
        Guid destinationLocationId,
        Guid rootId,
        string parkReason,
        CancellationToken ct)
    {
        var stock = (await _logi.ListStockAsync(ct))
            .Where(s => s.MaterialId == materialId && s.WarehouseId == warehouseId && s.Quantity > 0)
            .ToList();

        if (stock.Any(s => s.LocationId == destinationLocationId))
        {
            await _logi.PostLocationTransferAsync(
                materialId, warehouseId, destinationLocationId, rootId, 1m, ClearReason, ct);
            stock = (await _logi.ListStockAsync(ct))
                .Where(s => s.MaterialId == materialId && s.WarehouseId == warehouseId && s.Quantity > 0)
                .ToList();
        }

        if (!stock.Any(s => s.LocationId == rootId && s.Quantity >= 1m))
        {
            try
            {
                await _logi.PostOpeningAsync(materialId, warehouseId, 1m, ct);
            }
            catch
            {
                await _logi.PostAdjustmentAsync(
                    materialId, warehouseId, 1m, "Adjustment", "park", ct);
            }
        }

        await _logi.PostPutawayAsync(
            materialId, warehouseId, destinationLocationId, 1m, parkReason, ct);

        var verify = (await _logi.ListStockAsync(ct))
            .Any(s =>
                s.MaterialId == materialId
                && s.WarehouseId == warehouseId
                && s.LocationId == destinationLocationId
                && s.Quantity > 0);
        if (!verify)
            throw new InvalidOperationException("stock_sync_failed");
    }

    internal static (int Rows, int Columns) InferGrid(IReadOnlyList<LogiLocationRow> locations)
    {
        var rows = locations.Count(l =>
            l.IsActive && string.Equals(l.Kind, "Row", StringComparison.OrdinalIgnoreCase));
        if (rows == 0) return (0, 0);

        var cols = locations
            .Where(l => l.IsActive && string.Equals(l.Kind, "Position", StringComparison.OrdinalIgnoreCase))
            .GroupBy(l => l.ParentId)
            .Select(g => g.Count())
            .DefaultIfEmpty(0)
            .Max();
        return (rows, cols);
    }

    internal static string RowCode(int row) =>
        $"R{row.ToString("D3", CultureInfo.InvariantCulture)}";

    internal static string PositionCode(int row, int col) =>
        $"{RowCode(row)}-P{col.ToString("D3", CultureInfo.InvariantCulture)}";

    internal static string DefaultPositionName(int row, int col) =>
        $"Fila {row} Puesto {col}";

    internal static string OccupancyName(string bin, string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return bin;
        return bin + OccupancySeparator + color.Trim();
    }

    internal static (string Bin, string? Color) ParseOccupancyName(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0)
            return ("", null);

        if (n.StartsWith("Fila ", StringComparison.OrdinalIgnoreCase))
            return ("", null);

        var sep = n.IndexOf(OccupancySeparator, StringComparison.Ordinal);
        if (sep < 0)
            return (n, null);

        var bin = n[..sep].Trim();
        var color = n[(sep + OccupancySeparator.Length)..].Trim();
        return (bin, string.IsNullOrWhiteSpace(color) ? null : color);
    }

    internal static bool NameStartsWithBin(string? name, string bin)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0 || string.IsNullOrWhiteSpace(bin))
            return false;
        if (n.Equals(bin, StringComparison.OrdinalIgnoreCase))
            return true;
        return n.StartsWith(bin + OccupancySeparator, StringComparison.OrdinalIgnoreCase);
    }

    internal static (int Row, int Col) ParseSlot(string code)
    {
        var m = Regex.Match(code ?? "", @"^R(\d+)-P(\d+)$", RegexOptions.IgnoreCase);
        if (!m.Success) return (0, 0);
        return (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    private static LogiLocationRow? FindRoot(IReadOnlyList<LogiLocationRow> locations) =>
        locations.FirstOrDefault(l =>
            string.Equals(l.Kind, "Root", StringComparison.OrdinalIgnoreCase)
            || string.Equals(l.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeModel(string? model)
    {
        var s = (model ?? "").Trim();
        if (s.Length == 0) return "";
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
                sb.Append(ch);
            if (sb.Length >= 40) break;
        }
        return sb.ToString();
    }

    private static string MakeWarehouseCode(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            if (sb.Length >= 12) break;
        }

        if (sb.Length < 2)
            sb.Append(Guid.NewGuid().ToString("N")[..6].ToUpperInvariant());
        return sb.ToString();
    }

    private sealed record ParkEvent(
        Guid MovementId,
        Guid MaterialId,
        Guid WarehouseId,
        DateTime PostedUtc,
        ParkReason Payload,
        bool IsUnpark);
}

internal sealed class ParkReason
{
    public string? B { get; set; }
    public string? C { get; set; }
    public int R { get; set; }
    public int P { get; set; }
    public string? U { get; set; }
    public string? Ts { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public string? N { get; set; }

    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string Serialize(
        string bin,
        string? color,
        int row,
        int col,
        string? user,
        DateTime utc,
        double? lat,
        double? lon,
        string? notes)
    {
        string Clip(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var t = value.Trim();
            return t.Length <= max ? t : t[..max];
        }

        for (var noteMax = 80; noteMax >= 0; noteMax -= 20)
        {
            var payload = new ParkReason
            {
                B = Clip(bin, 40),
                C = string.IsNullOrWhiteSpace(color) ? null : Clip(color, 30),
                R = row,
                P = col,
                U = string.IsNullOrWhiteSpace(user) ? null : Clip(user, 40),
                Ts = utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                Lat = lat,
                Lon = lon,
                N = string.IsNullOrWhiteSpace(notes) ? null : Clip(notes, noteMax),
            };
            var body = JsonSerializer.Serialize(payload, WriteOpts);
            var full = ParkingMapper.ParkReasonPrefix + body;
            if (full.Length <= 500)
                return full;
        }

        var minimal = ParkingMapper.ParkReasonPrefix + JsonSerializer.Serialize(new ParkReason
        {
            B = Clip(bin, 20),
            R = row,
            P = col,
            U = string.IsNullOrWhiteSpace(user) ? null : Clip(user, 20),
            Ts = utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
        }, WriteOpts);
        return minimal.Length <= 500 ? minimal : minimal[..500];
    }

    public static string SerializeUnpark(
        string bin,
        string? color,
        int row,
        int col,
        string? user,
        DateTime utc)
    {
        var body = JsonSerializer.Serialize(new ParkReason
        {
            B = string.IsNullOrWhiteSpace(bin) ? null : bin.Trim(),
            C = string.IsNullOrWhiteSpace(color) ? null : color.Trim(),
            R = row,
            P = col,
            U = string.IsNullOrWhiteSpace(user) ? null : user.Trim(),
            Ts = utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            N = "unpark",
        }, WriteOpts);
        var full = ParkingMapper.UnparkReasonPrefix + body;
        return full.Length <= 500 ? full : full[..500];
    }

    public static ParkReason Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ParkReason();
        try
        {
            return JsonSerializer.Deserialize<ParkReason>(json, ReadOpts) ?? new ParkReason();
        }
        catch
        {
            return new ParkReason();
        }
    }
}

/// <summary>Caché local opcional del historial de aparcamientos.</summary>
public sealed class LocalParkHistory
{
    private readonly string _path;
    private readonly object _gate = new();

    public LocalParkHistory(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GestionAlmacenVehiculos",
            "park-history.json");
    }

    public void Append(VehicleUpdateDto dto)
    {
        lock (_gate)
        {
            var list = LoadUnlocked();
            list.Insert(0, dto);
            if (list.Count > 2000) list = list.Take(2000).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = false }));
        }
    }

    public List<VehicleUpdateDto> List(string? binFilter, int take)
    {
        lock (_gate)
        {
            IEnumerable<VehicleUpdateDto> q = LoadUnlocked();
            if (!string.IsNullOrWhiteSpace(binFilter))
            {
                var f = binFilter.Trim();
                q = q.Where(v =>
                    v.Bin.Contains(f, StringComparison.OrdinalIgnoreCase)
                    || v.Model.Contains(f, StringComparison.OrdinalIgnoreCase));
            }

            return q.Take(take).ToList();
        }
    }

    private List<VehicleUpdateDto> LoadUnlocked()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            return JsonSerializer.Deserialize<List<VehicleUpdateDto>>(File.ReadAllText(_path)) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
