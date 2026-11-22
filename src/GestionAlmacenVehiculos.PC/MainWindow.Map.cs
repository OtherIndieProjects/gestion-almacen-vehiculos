using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GestionAlmacenVehiculos.PC.Localization;
using GestionAlmacenVehiculos.PC.Map;
using GestionAlmacenVehiculos.PC.Ui;
using GestionAlmacenVehiculos.Shared.Dtos;

namespace GestionAlmacenVehiculos.PC;

public partial class MainWindow
{
    private readonly WarehouseLayoutStore _layoutStore = new();
    private readonly LayoutExcelService _excel = new();
    private WarehouseLayoutDoc _mapLayout = new();
    private MapPaintMode _mapMode = MapPaintMode.View;
    private double _mapZoom = 1.0;
    private bool _mapPainting;
    private ZoneDef? _activeZone;
    private (int Row, int Col)? _selectedCell;
    private readonly Dictionary<(int Row, int Col), Border> _mapCells = new();
    private static readonly string[] ZonePalette =
    [
        "#0F766E", "#B45309", "#1D4ED8", "#7C3AED", "#BE123C", "#047857", "#A16207", "#334155"
    ];

    private void InitMapUi()
    {
        MapZoneColorBox.Items.Clear();
        foreach (var c in ZonePalette)
            MapZoneColorBox.Items.Add(new ZoneColorItem(c));
        MapZoneColorBox.DisplayMemberPath = nameof(ZoneColorItem.Hex);
        MapZoneColorBox.SelectedValuePath = nameof(ZoneColorItem.Hex);
        MapZoneColorBox.SelectedIndex = 0;
        MapZoneNameBox.Text = Loc.Instance["MapZoneDefault"];
    }

    private void ApplyMapLanguage(Loc L)
    {
        MapHintText.Text = L["MapHint"];
        MapModeView.Content = L["MapModeView"];
        MapModePaint.Content = L["MapModePaint"];
        MapModeErase.Content = L["MapModeErase"];
        MapZoneNameBox.ToolTip = L["MapZoneName"];
        MapZoneColorBox.ToolTip = L["MapZoneColor"];
        MapAddZoneButton.Content = L["MapAddZone"];
        MapZoomInButton.Content = L["MapZoomIn"];
        MapZoomOutButton.Content = L["MapZoomOut"];
        MapExportButton.Content = L["MapExportExcel"];
        MapImportButton.Content = L["MapImportExcel"];
        MapRefreshButton.Content = L["Refresh"];
        MapLegendFreeText.Text = L["MapLegendFree"];
        MapLegendOccText.Text = L["MapLegendOcc"];
        MapChartFillTitle.Text = L["MapChartFill"];
        MapChartFreeOccTitle.Text = L["MapChartFreeOcc"];
        MapChartZonesTitle.Text = L["ChartZoneOcc"];
        MapChartModelsTitle.Text = L["ChartStockByModel"];
        MapZonesTitle.Text = L["MapZonesListTitle"];
    }

    private void MapWarehouseBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ReloadMapFromSelection();
    }

    private void MapMode_Checked(object sender, RoutedEventArgs e)
    {
        if (MapModePaint?.IsChecked == true) _mapMode = MapPaintMode.Paint;
        else if (MapModeErase?.IsChecked == true) _mapMode = MapPaintMode.Erase;
        else _mapMode = MapPaintMode.View;
    }

    private void MapAddZone_Click(object sender, RoutedEventArgs e)
    {
        var name = (MapZoneNameBox.Text ?? "").Trim();
        if (name.Length < 1)
            name = string.Format(Loc.Instance["MapZoneDefaultN"], _mapLayout.Zones.Count + 1);
        var color = SelectedZoneColorHex() ?? ZonePalette[_mapLayout.Zones.Count % ZonePalette.Length];
        var zone = new ZoneDef { Name = name, ColorHex = color };
        _mapLayout.Zones.Add(zone);
        _activeZone = zone;
        PersistMapLayout();
        RefreshMapZonesList();
        MapModePaint.IsChecked = true;
        _mapMode = MapPaintMode.Paint;
        RebuildMapCanvas();
    }

    private void MapZonesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MapZonesList.SelectedItem is ZoneListItem item)
        {
            _activeZone = item.Zone;
            MapZoneNameBox.Text = item.Zone.Name;
            SelectZoneColor(item.Zone.ColorHex);
        }
    }

    private string? SelectedZoneColorHex() =>
        MapZoneColorBox.SelectedItem switch
        {
            ZoneColorItem z => z.Hex,
            string s => s,
            _ => MapZoneColorBox.SelectedValue as string,
        };

    private void SelectZoneColor(string hex)
    {
        for (var i = 0; i < MapZoneColorBox.Items.Count; i++)
        {
            if (MapZoneColorBox.Items[i] is ZoneColorItem z
                && string.Equals(z.Hex, hex, StringComparison.OrdinalIgnoreCase))
            {
                MapZoneColorBox.SelectedIndex = i;
                return;
            }
        }
        MapZoneColorBox.Items.Add(new ZoneColorItem(hex));
        MapZoneColorBox.SelectedIndex = MapZoneColorBox.Items.Count - 1;
    }

    private void MapZoomIn_Click(object sender, RoutedEventArgs e)
    {
        _mapZoom = Math.Min(2.2, _mapZoom + 0.15);
        RebuildMapCanvas();
    }

    private void MapZoomOut_Click(object sender, RoutedEventArgs e)
    {
        _mapZoom = Math.Max(0.55, _mapZoom - 0.15);
        RebuildMapCanvas();
    }

    private void ReloadMapFromSelection()
    {
        if (MapWarehouseBox.SelectedValue is not Guid id)
        {
            MapCanvas.Children.Clear();
            _mapCells.Clear();
            return;
        }

        _mapLayout = _layoutStore.Load(id);
        var wh = _warehouses.FirstOrDefault(w => w.Id == id);
        if (wh is not null)
            WarehouseLayoutStore.ClampToGrid(_mapLayout, wh.Rows, wh.Columns);
        _activeZone = _mapLayout.Zones.FirstOrDefault();
        RefreshMapZonesList();
        RebuildMapCanvas();
        UpdateMapCharts();
    }

    private void PersistMapLayout()
    {
        if (_mapLayout.WarehouseId == Guid.Empty && MapWarehouseBox.SelectedValue is Guid id)
            _mapLayout.WarehouseId = id;
        if (_mapLayout.WarehouseId != Guid.Empty)
            _layoutStore.Save(_mapLayout);
    }

    private void RefreshMapZonesList()
    {
        MapZonesList.Items.Clear();
        foreach (var z in _mapLayout.Zones)
            MapZonesList.Items.Add(new ZoneListItem(z));
        if (_activeZone is not null)
        {
            foreach (ZoneListItem item in MapZonesList.Items)
            {
                if (item.Zone.Id == _activeZone.Id)
                {
                    MapZonesList.SelectedItem = item;
                    break;
                }
            }
        }

        MapLegendZones.ItemsSource = _mapLayout.Zones
            .Select(z => new ZoneLegendChip(z.Name, z.ColorHex))
            .ToList();
    }

    private void RebuildMapCanvas()
    {
        MapCanvas.Children.Clear();
        _mapCells.Clear();
        var L = Loc.Instance;
        if (MapWarehouseBox.SelectedValue is not Guid id)
        {
            MapSelectionText.Text = L["MapNeedWarehouse"];
            return;
        }

        var wh = _warehouses.FirstOrDefault(w => w.Id == id);
        if (wh is null || wh.Rows < 1 || wh.Columns < 1)
        {
            MapSelectionText.Text = L["MapNoGrid"];
            return;
        }

        var cellW = (_mapLayout.Meta.CellW > 0 ? _mapLayout.Meta.CellW : 56) * _mapZoom;
        var cellH = (_mapLayout.Meta.CellH > 0 ? _mapLayout.Meta.CellH : 44) * _mapZoom;
        const double pad = 28;
        MapCanvas.Width = pad + wh.Columns * cellW + 16;
        MapCanvas.Height = pad + wh.Rows * cellH + 16;

        for (var c = 1; c <= wh.Columns; c++)
        {
            var lbl = new TextBlock
            {
                Text = c.ToString(),
                FontSize = 10,
                Foreground = (Brush)FindResource("InkMutedBrush"),
            };
            Canvas.SetLeft(lbl, pad + (c - 1) * cellW + cellW / 2 - 4);
            Canvas.SetTop(lbl, 4);
            MapCanvas.Children.Add(lbl);
        }

        var occ = _current.Where(v => v.WarehouseId == id)
            .ToDictionary(v => (v.Row, v.Position));

        for (var row = 1; row <= wh.Rows; row++)
        {
            var rowLbl = new TextBlock
            {
                Text = row.ToString(),
                FontSize = 10,
                Foreground = (Brush)FindResource("InkMutedBrush"),
            };
            Canvas.SetLeft(rowLbl, 6);
            Canvas.SetTop(rowLbl, pad + (row - 1) * cellH + cellH / 2 - 7);
            MapCanvas.Children.Add(rowLbl);

            for (var col = 1; col <= wh.Columns; col++)
            {
                var zone = WarehouseLayoutStore.FindZone(_mapLayout, row, col);
                var occupied = occ.TryGetValue((row, col), out var vehicle);
                var selected = _selectedCell is { } s && s.Row == row && s.Col == col;
                Brush bg;
                if (zone is not null)
                    bg = SafeBrush(zone.ColorHex, (Brush)FindResource("SurfaceMutedBrush"));
                else if (occupied)
                    bg = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 15, 118, 110));
                else
                    bg = (Brush)FindResource("PaperBrush");

                var cellStack = new Grid();
                if (occupied && zone is not null)
                {
                    cellStack.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 15, 118, 110)),
                    });
                }
                cellStack.Children.Add(new TextBlock
                {
                    Text = occupied ? Trunc(vehicle!.Bin, 8) : (zone is null ? "" : Trunc(zone.Name, 6)),
                    FontSize = Math.Max(9, 10 * _mapZoom),
                    Foreground = occupied ? Brushes.White : (Brush)FindResource("InkBrush"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                });
                if (occupied && zone is not null)
                {
                    cellStack.Children.Add(new Border
                    {
                        Width = 9,
                        Height = 9,
                        CornerRadius = new CornerRadius(4),
                        Background = SafeBrush(zone.ColorHex, (Brush)FindResource("AccentBrush")),
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 3, 3, 0),
                        BorderBrush = Brushes.White,
                        BorderThickness = new Thickness(1),
                        ToolTip = zone.Name,
                    });
                }

                var border = new Border
                {
                    Width = cellW - 3,
                    Height = cellH - 3,
                    Background = bg,
                    BorderBrush = selected
                        ? (Brush)FindResource("AccentDeepBrush")
                        : (Brush)FindResource("LineBrush"),
                    BorderThickness = new Thickness(selected ? 2.5 : 1),
                    CornerRadius = new CornerRadius(4),
                    Cursor = Cursors.Hand,
                    Tag = (row, col),
                    ToolTip = occupied
                        ? $"{vehicle!.Bin} · {vehicle.Model}" + (string.IsNullOrWhiteSpace(vehicle.Color) || vehicle.Color == "—" ? "" : $" · {vehicle.Color}")
                        : (zone?.Name ?? $"{row}/{col}"),
                    Child = cellStack,
                };
                Canvas.SetLeft(border, pad + (col - 1) * cellW);
                Canvas.SetTop(border, pad + (row - 1) * cellH);
                MapCanvas.Children.Add(border);
                _mapCells[(row, col)] = border;
            }
        }
    }

    private static string Trunc(string s, int n) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..(n - 1)] + "…");

    private static string SanitizeFileToken(string name)
    {
        var t = string.IsNullOrWhiteSpace(name) ? "Vehicle" : name.Trim();
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            t = t.Replace(c, '_');
        return t.Length == 0 ? "Vehicle" : t;
    }

    private static Brush SafeBrush(string? hex, Brush fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try { return (Brush)new BrushConverter().ConvertFromString(hex)!; }
        catch { return fallback; }
    }

    private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (HitCell(e.GetPosition(MapCanvas)) is not { } cell) return;
        _mapPainting = _mapMode is MapPaintMode.Paint or MapPaintMode.Erase;
        ApplyMapCell(cell.Row, cell.Col);
        if (_mapPainting)
            MapCanvas.CaptureMouse();
    }

    private void MapCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_mapPainting || e.LeftButton != MouseButtonState.Pressed) return;
        if (HitCell(e.GetPosition(MapCanvas)) is not { } cell) return;
        ApplyMapCell(cell.Row, cell.Col);
    }

    private void MapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mapPainting)
        {
            _mapPainting = false;
            MapCanvas.ReleaseMouseCapture();
            PersistMapLayout();
            UpdateMapCharts();
        }
    }

    private void MapCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_mapPainting) return;
        _mapPainting = false;
        MapCanvas.ReleaseMouseCapture();
        PersistMapLayout();
        UpdateMapCharts();
    }

    private (int Row, int Col)? HitCell(Point p)
    {
        foreach (var (key, border) in _mapCells)
        {
            var left = Canvas.GetLeft(border);
            var top = Canvas.GetTop(border);
            if (p.X >= left && p.X <= left + border.Width && p.Y >= top && p.Y <= top + border.Height)
                return key;
        }
        return null;
    }

    private void ApplyMapCell(int row, int col)
    {
        var L = Loc.Instance;
        MapSelectionText.Text = string.Format(L["MapSelected"], row, col);

        if (_mapMode == MapPaintMode.View)
        {
            _selectedCell = (row, col);
            RebuildMapCanvas();
            if (MapWarehouseBox.SelectedValue is Guid whId)
            {
                if (_warehouses.Any(w => w.Id == whId))
                    ParkWarehouseBox.SelectedValue = whId;
                ParkRowBox.Text = row.ToString();
                ParkPosBox.Text = col.ToString();
                if (_current.FirstOrDefault(v => v.WarehouseId == whId && v.Row == row && v.Position == col) is { } occ)
                {
                    ParkModelBox.Text = occ.Model;
                    ParkBinBox.Text = occ.Bin;
                    ParkColorBox.Text = occ.Color == "—" ? "" : occ.Color;
                    ParkNotesBox.Text = "";
                }
            }
            return;
        }

        if (_mapMode == MapPaintMode.Erase)
        {
            WarehouseLayoutStore.EraseCell(_mapLayout, row, col);
            RebuildMapCanvas();
            return;
        }

        if (_activeZone is null)
        {
            MapAddZone_Click(this, new RoutedEventArgs());
            if (_activeZone is null) return;
        }

        var name = (MapZoneNameBox.Text ?? "").Trim();
        if (name.Length > 0) _activeZone.Name = name;
        if (SelectedZoneColorHex() is { } color) _activeZone.ColorHex = color;

        WarehouseLayoutStore.AssignCell(_mapLayout, _activeZone, row, col);
        RebuildMapCanvas();
    }

    private void UpdateMapCharts()
    {
        var L = Loc.Instance;
        if (MapWarehouseBox.SelectedValue is not Guid id)
        {
            ClearPlots(L["EmptyMapCharts"]);
            return;
        }

        var wh = _warehouses.FirstOrDefault(w => w.Id == id);
        if (wh is null || wh.Rows < 1 || wh.Columns < 1)
        {
            ClearPlots(L["MapNoGrid"]);
            return;
        }

        var occHere = _current.Where(v => v.WarehouseId == id).ToList();
        var total = wh.Rows * wh.Columns;
        var occupied = occHere.Count;
        var free = Math.Max(0, total - occupied);
        var fill = total == 0 ? 0 : 100.0 * occupied / total;
        var occSet = occHere.Select(v => (v.Row, v.Position)).ToHashSet();

        MapPlotFill.Plot.Clear();
        PlotTheme.Apply(MapPlotFill);
        var bar = MapPlotFill.Plot.Add.Bar(position: 1, value: fill);
        bar.Color = PlotTheme.Occ;
        MapPlotFill.Plot.Axes.SetLimitsY(0, 100);
        MapPlotFill.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            new double[] { 1 }, new string[] { L["MapChartFill"] });
        MapPlotFill.Plot.Title($"{fill:0.#}%");
        MapPlotFill.Refresh();

        MapPlotFreeOcc.Plot.Clear();
        PlotTheme.Apply(MapPlotFreeOcc);
        var freeBar = MapPlotFreeOcc.Plot.Add.Bar(position: 1, value: free);
        freeBar.Color = PlotTheme.Free;
        var occBar = MapPlotFreeOcc.Plot.Add.Bar(position: 2, value: occupied);
        occBar.Color = PlotTheme.Occ;
        MapPlotFreeOcc.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            new double[] { 1, 2 }, new string[] { L["MapLegendFree"], L["MapLegendOcc"] });
        MapPlotFreeOcc.Plot.Axes.SetLimitsY(0, Math.Max(total, 1) * 1.15);
        MapPlotFreeOcc.Refresh();

        MapPlotZones.Plot.Clear();
        PlotTheme.Apply(MapPlotZones);
        if (_mapLayout.Zones.Count == 0)
        {
            MapPlotZones.Plot.Title("—");
            MapPlotZones.Refresh();
        }
        else
        {
            var positions = new double[_mapLayout.Zones.Count];
            var values = new double[_mapLayout.Zones.Count];
            var labels = new string[_mapLayout.Zones.Count];
            for (var i = 0; i < _mapLayout.Zones.Count; i++)
            {
                var z = _mapLayout.Zones[i];
                positions[i] = i + 1;
                values[i] = z.Cells.Count(c => occSet.Contains((c.Row, c.Col)));
                labels[i] = Trunc(z.Name, 8);
                var zoneBar = MapPlotZones.Plot.Add.Bar(position: positions[i], value: values[i]);
                zoneBar.Color = PlotTheme.FromHex(z.ColorHex, PlotTheme.Accent);
            }
            MapPlotZones.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(positions, labels);
            MapPlotZones.Plot.Axes.SetLimitsY(0, Math.Max(values.DefaultIfEmpty(0).Max(), 1) * 1.2);
            MapPlotZones.Plot.Title(L["ChartZoneOcc"]);
            MapPlotZones.Refresh();
        }

        var dto = occHere.Select(v => new VehicleUpdateDto
        {
            WarehouseId = v.WarehouseId,
            Model = v.Model,
            Row = v.Row,
            Position = v.Position,
        });
        var snap = OccupancyStats.Compute(dto, [(id, wh.Rows, wh.Columns)], id);
        RenderModelChart(MapPlotModels, OccupancyStats.TopModels(snap.ByModel, 6, L["ChartOtros"]));
    }

    private void ClearPlots(string? title = null)
    {
        foreach (var plot in new[] { MapPlotFill, MapPlotFreeOcc, MapPlotZones, MapPlotModels })
        {
            plot.Plot.Clear();
            PlotTheme.Apply(plot);
            if (!string.IsNullOrWhiteSpace(title))
                plot.Plot.Title(title);
            plot.Refresh();
        }
    }

    private async void MapExportExcel_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (MapWarehouseBox.SelectedValue is not Guid id)
        {
            MessageBox.Show(Loc.Instance["MapNeedWarehouse"], Title);
            return;
        }
        var wh = _warehouses.FirstOrDefault(w => w.Id == id);
        if (wh is null || wh.Rows < 1)
        {
            MessageBox.Show(Loc.Instance["MapNoGrid"], Title);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"{SanitizeFileToken(_rt.Brand.ProductName)}_Layout_{wh.Code}_{DateTime.Now:yyyyMMdd}.xlsx",
            Filter = "Excel (*.xlsx)|*.xlsx",
        };
        if (dialog.ShowDialog() != true) return;

        if (!TryBeginBusy()) return;
        try
        {
            PersistMapLayout();
            var occ = _current.Where(v => v.WarehouseId == id)
                .Select(v => new VehicleUpdateDto
                {
                    WarehouseId = v.WarehouseId,
                    WarehouseName = v.WarehouseName,
                    Model = v.Model,
                    Bin = v.Bin,
                    Color = v.Color == "—" ? null : v.Color,
                    Row = v.Row,
                    Position = v.Position,
                    UserDisplayName = v.UserDisplayName,
                    CreatedAtUtc = v.CreatedAtUtc == default ? DateTime.UtcNow : v.CreatedAtUtc,
                }).ToList();
            try
            {
                occ = (await _rt.Mapper.GetCurrentAsync())
                    .Where(x => x.WarehouseId == id)
                    .ToList();
            }
            catch { /* use VM snapshot */ }

            _excel.Export(dialog.FileName, wh.Code, wh.Name, wh.Rows, wh.Columns, _mapLayout, occ);
            ShowStatusOk(Loc.Instance["MapExportOk"]);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Instance.FormatError(ex), Title);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void MapImportExcel_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (MapWarehouseBox.SelectedValue is not Guid id)
        {
            MessageBox.Show(Loc.Instance["MapNeedWarehouse"], Title);
            return;
        }
        var wh = _warehouses.FirstOrDefault(w => w.Id == id);
        if (wh is null || wh.Rows < 1)
        {
            MessageBox.Show(Loc.Instance["MapNoGrid"], Title);
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
        };
        if (dialog.ShowDialog() != true) return;

        if (!TryBeginBusy()) return;
        try
        {
            var (layout, maxRow, maxCol, _) = _excel.ImportLayout(dialog.FileName, id);
            if (maxRow > wh.Rows || maxCol > wh.Columns)
            {
                if (!_rt.Client.CanManageWarehouse)
                {
                    MessageBox.Show(Loc.Instance["NeedPermission"], Title);
                    return;
                }
                await _rt.Mapper.EnsureGridAsync(id, Math.Max(wh.Rows, maxRow), Math.Max(wh.Columns, maxCol));
                await RefreshDataAsync(force: true);
                wh = _warehouses.FirstOrDefault(w => w.Id == id) ?? wh;
            }

            WarehouseLayoutStore.ClampToGrid(layout, Math.Max(wh.Rows, maxRow), Math.Max(wh.Columns, maxCol));
            _mapLayout = layout;
            _mapLayout.WarehouseId = id;
            PersistMapLayout();
            _activeZone = _mapLayout.Zones.FirstOrDefault();
            RefreshMapZonesList();
            RebuildMapCanvas();
            UpdateMapCharts();
            ShowStatusOk(Loc.Instance["MapImportOk"]);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Instance.FormatError(ex), Title);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SyncMapWarehousePicker()
    {
        var prev = MapWarehouseBox.SelectedValue as Guid?;
        MapWarehouseBox.ItemsSource = _warehouses.ToList();
        if (prev is Guid keep && _warehouses.Any(w => w.Id == keep))
            MapWarehouseBox.SelectedValue = keep;
        else if (_warehouses.Count > 0)
            MapWarehouseBox.SelectedIndex = 0;
    }

    private sealed class ZoneListItem(ZoneDef zone)
    {
        public ZoneDef Zone { get; } = zone;
        public override string ToString() => $"{Zone.Name} ({Zone.Cells.Count})";
    }

    private sealed class ZoneColorItem
    {
        public ZoneColorItem(string hex)
        {
            Hex = hex;
            try { Brush = (Brush)new BrushConverter().ConvertFromString(hex)!; }
            catch { Brush = Brushes.Gray; }
        }

        public string Hex { get; }
        public Brush Brush { get; }
    }

    private sealed class ZoneLegendChip
    {
        public ZoneLegendChip(string name, string hex)
        {
            Name = name;
            try { Brush = (Brush)new BrushConverter().ConvertFromString(hex)!; }
            catch { Brush = Brushes.Gray; }
        }

        public string Name { get; }
        public Brush Brush { get; }
    }
}
