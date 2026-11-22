namespace GestionAlmacenVehiculos.PC.Map;

public sealed class WarehouseLayoutDoc
{
    public Guid WarehouseId { get; set; }
    public int Version { get; set; } = 1;
    public List<ZoneDef> Zones { get; set; } = [];
    public LayoutMeta Meta { get; set; } = new();
}

public sealed class LayoutMeta
{
    public double CellW { get; set; } = 56;
    public double CellH { get; set; } = 44;
    public string Notes { get; set; } = "";
}

public sealed class ZoneDef
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string ColorHex { get; set; } = "#0F766E";
    public List<CellRef> Cells { get; set; } = [];
}

public sealed class CellRef
{
    public int Row { get; set; }
    public int Col { get; set; }
}

public enum MapPaintMode
{
    View,
    Paint,
    Erase,
}
