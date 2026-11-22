using ClosedXML.Excel;
using GestionAlmacenVehiculos.Shared.Dtos;

namespace GestionAlmacenVehiculos.PC.Map;

public sealed class LayoutExcelService
{
    public void Export(
        string filePath,
        string warehouseCode,
        string warehouseName,
        int rows,
        int cols,
        WarehouseLayoutDoc layout,
        IReadOnlyList<VehicleUpdateDto> occupancy)
    {
        using var wb = new XLWorkbook();
        var layoutSheet = wb.Worksheets.Add("Layout");
        layoutSheet.Cell(1, 1).Value = "WarehouseCode";
        layoutSheet.Cell(1, 2).Value = "WarehouseName";
        layoutSheet.Cell(1, 3).Value = "Row";
        layoutSheet.Cell(1, 4).Value = "Col";
        layoutSheet.Cell(1, 5).Value = "Zone";
        layoutSheet.Cell(1, 6).Value = "ZoneColor";
        layoutSheet.Cell(1, 7).Value = "Notes";
        layoutSheet.Row(1).Style.Font.Bold = true;

        var r = 2;
        for (var row = 1; row <= rows; row++)
        {
            for (var col = 1; col <= cols; col++)
            {
                var zone = WarehouseLayoutStore.FindZone(layout, row, col);
                layoutSheet.Cell(r, 1).Value = warehouseCode;
                layoutSheet.Cell(r, 2).Value = warehouseName;
                layoutSheet.Cell(r, 3).Value = row;
                layoutSheet.Cell(r, 4).Value = col;
                layoutSheet.Cell(r, 5).Value = zone?.Name ?? "";
                layoutSheet.Cell(r, 6).Value = zone?.ColorHex ?? "";
                layoutSheet.Cell(r, 7).Value = layout.Meta.Notes ?? "";
                r++;
            }
        }
        layoutSheet.Columns().AdjustToContents();

        var occSheet = wb.Worksheets.Add("Ocupacion");
        occSheet.Cell(1, 1).Value = "Row";
        occSheet.Cell(1, 2).Value = "Col";
        occSheet.Cell(1, 3).Value = "Bin";
        occSheet.Cell(1, 4).Value = "Color";
        occSheet.Cell(1, 5).Value = "Model";
        occSheet.Cell(1, 6).Value = "User";
        occSheet.Cell(1, 7).Value = "Time";
        occSheet.Row(1).Style.Font.Bold = true;
        var or = 2;
        foreach (var v in occupancy)
        {
            occSheet.Cell(or, 1).Value = v.Row;
            occSheet.Cell(or, 2).Value = v.Position;
            occSheet.Cell(or, 3).Value = v.Bin;
            occSheet.Cell(or, 4).Value = v.Color ?? "";
            occSheet.Cell(or, 5).Value = v.Model;
            occSheet.Cell(or, 6).Value = v.UserDisplayName;
            occSheet.Cell(or, 7).Value = v.CreatedAtUtc.ToLocalTime().ToString("g");
            or++;
        }
        occSheet.Columns().AdjustToContents();
        wb.SaveAs(filePath);
    }

    public (WarehouseLayoutDoc Layout, int MaxRow, int MaxCol, string? WarehouseCode) ImportLayout(string filePath, Guid warehouseId)
    {
        using var wb = new XLWorkbook(filePath);
        var sheet = wb.Worksheets.FirstOrDefault(w =>
                        string.Equals(w.Name, "Layout", StringComparison.OrdinalIgnoreCase))
                    ?? wb.Worksheets.First();

        var header = sheet.Row(1);
        int Col(string name)
        {
            for (var c = 1; c <= 20; c++)
            {
                var v = header.Cell(c).GetString().Trim();
                if (string.Equals(v, name, StringComparison.OrdinalIgnoreCase))
                    return c;
            }
            return -1;
        }

        var cCode = Col("WarehouseCode");
        var cRow = Col("Row");
        var cCol = Col("Col");
        var cZone = Col("Zone");
        var cColor = Col("ZoneColor");
        var cNotes = Col("Notes");
        if (cRow < 0 || cCol < 0)
            throw new InvalidOperationException("excel_layout_invalid");

        var doc = new WarehouseLayoutDoc { WarehouseId = warehouseId };
        var zonesByName = new Dictionary<string, ZoneDef>(StringComparer.OrdinalIgnoreCase);
        var maxRow = 0;
        var maxCol = 0;
        string? whCode = null;
        string? notes = null;

        var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= last; r++)
        {
            if (!int.TryParse(sheet.Cell(r, cRow).GetString(), out var row) || row < 1) continue;
            if (!int.TryParse(sheet.Cell(r, cCol).GetString(), out var col) || col < 1) continue;
            maxRow = Math.Max(maxRow, row);
            maxCol = Math.Max(maxCol, col);
            if (cCode > 0)
            {
                var code = sheet.Cell(r, cCode).GetString().Trim();
                if (!string.IsNullOrWhiteSpace(code))
                    whCode ??= code;
            }
            if (cNotes > 0 && notes is null)
            {
                var n = sheet.Cell(r, cNotes).GetString().Trim();
                if (!string.IsNullOrWhiteSpace(n))
                    notes = n;
            }

            var zoneName = cZone > 0 ? sheet.Cell(r, cZone).GetString().Trim() : "";
            if (string.IsNullOrWhiteSpace(zoneName))
                continue;

            if (!zonesByName.TryGetValue(zoneName, out var zone))
            {
                var color = cColor > 0 ? sheet.Cell(r, cColor).GetString().Trim() : "";
                if (string.IsNullOrWhiteSpace(color) || !color.StartsWith('#'))
                    color = PickColor(zonesByName.Count);
                zone = new ZoneDef { Name = zoneName, ColorHex = color };
                zonesByName[zoneName] = zone;
                doc.Zones.Add(zone);
            }
            else if (cColor > 0)
            {
                var color = sheet.Cell(r, cColor).GetString().Trim();
                if (!string.IsNullOrWhiteSpace(color) && color.StartsWith('#'))
                    zone.ColorHex = color;
            }

            WarehouseLayoutStore.AssignCell(doc, zone, row, col);
        }

        doc.Meta.Notes = notes ?? "";
        return (doc, maxRow, maxCol, whCode);
    }

    private static string PickColor(int index)
    {
        string[] palette =
        [
            "#0F766E", "#B45309", "#1D4ED8", "#7C3AED", "#BE123C", "#047857", "#A16207", "#334155"
        ];
        return palette[index % palette.Length];
    }
}
