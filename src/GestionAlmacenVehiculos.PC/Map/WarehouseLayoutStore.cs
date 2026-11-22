using System.IO;
using System.Text.Json;

namespace GestionAlmacenVehiculos.PC.Map;

public sealed class WarehouseLayoutStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _dir;

    public WarehouseLayoutStore()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GestionAlmacenVehiculos",
            "layouts");
        Directory.CreateDirectory(_dir);
    }

    private string PathFor(Guid warehouseId) =>
        Path.Combine(_dir, $"{warehouseId:D}.json");

    public WarehouseLayoutDoc Load(Guid warehouseId)
    {
        var path = PathFor(warehouseId);
        if (!File.Exists(path))
        {
            return new WarehouseLayoutDoc { WarehouseId = warehouseId };
        }

        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<WarehouseLayoutDoc>(json, JsonOpts)
                      ?? new WarehouseLayoutDoc { WarehouseId = warehouseId };
            doc.WarehouseId = warehouseId;
            doc.Zones ??= [];
            foreach (var z in doc.Zones)
                z.Cells ??= [];
            return doc;
        }
        catch
        {
            return new WarehouseLayoutDoc { WarehouseId = warehouseId };
        }
    }

    public void Save(WarehouseLayoutDoc doc)
    {
        doc.Version = Math.Max(1, doc.Version);
        var path = PathFor(doc.WarehouseId);
        File.WriteAllText(path, JsonSerializer.Serialize(doc, JsonOpts));
    }

    public static ZoneDef? FindZone(WarehouseLayoutDoc doc, int row, int col) =>
        doc.Zones.FirstOrDefault(z => z.Cells.Any(c => c.Row == row && c.Col == col));

    public static void AssignCell(WarehouseLayoutDoc doc, ZoneDef zone, int row, int col)
    {
        foreach (var z in doc.Zones)
            z.Cells.RemoveAll(c => c.Row == row && c.Col == col);
        if (!zone.Cells.Any(c => c.Row == row && c.Col == col))
            zone.Cells.Add(new CellRef { Row = row, Col = col });
    }

    public static void EraseCell(WarehouseLayoutDoc doc, int row, int col)
    {
        foreach (var z in doc.Zones)
            z.Cells.RemoveAll(c => c.Row == row && c.Col == col);
    }

    public static void ClampToGrid(WarehouseLayoutDoc doc, int rows, int cols)
    {
        foreach (var z in doc.Zones)
            z.Cells.RemoveAll(c => c.Row < 1 || c.Col < 1 || c.Row > rows || c.Col > cols);
    }
}
