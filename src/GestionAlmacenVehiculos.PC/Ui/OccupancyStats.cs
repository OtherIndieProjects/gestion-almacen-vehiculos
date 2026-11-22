using GestionAlmacenVehiculos.Shared.Dtos;

namespace GestionAlmacenVehiculos.PC.Ui;

public sealed record ModelStockRow(string Model, int Count);

public sealed class OccupancySnapshot
{
    public int Parked { get; init; }
    public int Capacity { get; init; }
    public int Free => Math.Max(0, Capacity - Parked);
    public double FillPercent => Capacity <= 0 ? 0 : 100.0 * Parked / Capacity;
    public int DistinctModels { get; init; }
    public IReadOnlyList<ModelStockRow> ByModel { get; init; } = [];
}

public static class OccupancyStats
{
    public static OccupancySnapshot Compute(
        IEnumerable<VehicleUpdateDto> current,
        IEnumerable<(Guid Id, int Rows, int Columns)> warehouses,
        Guid? warehouseFilter = null)
    {
        var list = current
            .Where(v => warehouseFilter is null || v.WarehouseId == warehouseFilter)
            .ToList();

        var caps = warehouses
            .Where(w => warehouseFilter is null || w.Id == warehouseFilter)
            .Sum(w => Math.Max(0, w.Rows) * Math.Max(0, w.Columns));

        var byModel = list
            .GroupBy(v => string.IsNullOrWhiteSpace(v.Model) ? "—" : v.Model.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new ModelStockRow(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Model, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new OccupancySnapshot
        {
            Parked = list.Count,
            Capacity = caps,
            DistinctModels = byModel.Count,
            ByModel = byModel,
        };
    }

    /// <summary>Modelos más frecuentes para el gráfico.</summary>
    public static IReadOnlyList<ModelStockRow> TopModels(IReadOnlyList<ModelStockRow> all, int top = 8, string? othersLabel = null)
    {
        if (all.Count <= top) return all;
        var head = all.Take(top - 1).ToList();
        var rest = all.Skip(top - 1).Sum(x => x.Count);
        head.Add(new ModelStockRow(othersLabel ?? "Otros", rest));
        return head;
    }
}
