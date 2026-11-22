using GestionAlmacenVehiculos.Mobile.Services;
using GestionAlmacenVehiculos.Shared.Dtos;

namespace GestionAlmacenVehiculos.Mobile.Pages;

public class MainPage : ContentPage
{
    private readonly AppSettings _settings;
    private readonly LocService _loc;
    private readonly ApiClient _api;
    private readonly BrandService _brand;
    private readonly IServiceProvider _services;
    private readonly Image _logo = new() { HeightRequest = 48, Aspect = Aspect.AspectFit, IsVisible = false };
    private readonly Picker _warehousePicker = new();
    private readonly Label _limits = new() { TextColor = Color.FromArgb("#5C6B73"), FontSize = 13 };
    private readonly Label _block = new() { TextColor = Color.FromArgb("#B45309"), FontAttributes = FontAttributes.Bold };
    private readonly Label _whLabel = new();
    private readonly Label _modelLabel = new();
    private readonly Label _binLabel = new();
    private readonly Label _colorLabel = new();
    private readonly Label _rowLabel = new();
    private readonly Label _posLabel = new();
    private readonly Label _gpsLabel = new() { VerticalOptions = LayoutOptions.Center };
    private readonly Label _onlineHint = new() { TextColor = Color.FromArgb("#5C6B73"), FontSize = 12 };
    private readonly Label _titleLabel = new() { FontSize = 28, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#0F766E") };
    private readonly Label _tagLabel = new() { TextColor = Color.FromArgb("#5C6B73") };
    private readonly Entry _model = new() { Keyboard = Keyboard.Text, Placeholder = "Joy07", ReturnType = ReturnType.Next };
    private readonly Entry _bin = new() { Keyboard = Keyboard.Text, ReturnType = ReturnType.Next };
    private readonly Entry _color = new() { Keyboard = Keyboard.Text, ReturnType = ReturnType.Next };
    private readonly Entry _row = new() { Keyboard = Keyboard.Numeric, ReturnType = ReturnType.Next };
    private readonly Entry _pos = new() { Keyboard = Keyboard.Numeric, ReturnType = ReturnType.Go };
    private readonly Switch _gps = new() { IsToggled = true };
    private readonly Label _status = new();
    private readonly Label _historyTitle = new() { FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#0F766E") };
    private readonly Label _historyBox = new() { FontSize = 12, TextColor = Color.FromArgb("#334155") };
    private readonly Button _sendBtn = new();
    private readonly Button _logoutBtn = new() { BackgroundColor = Color.FromArgb("#64748B") };
    private readonly Button _langBtn = new() { BackgroundColor = Colors.Transparent, TextColor = Color.FromArgb("#0F766E") };
    private List<WarehouseDto> _warehouses = [];
    private bool _busy;

    public MainPage(AppSettings settings, LocService loc, ApiClient api, BrandService brand, IServiceProvider services)
    {
        _settings = settings;
        _loc = loc;
        _api = api;
        _brand = brand;
        _services = services;

        _warehousePicker.SelectedIndexChanged += (_, _) =>
        {
            UpdateLimits();
            if (_warehousePicker.SelectedIndex >= 0 && _warehousePicker.SelectedIndex < _warehouses.Count)
                _settings.LastWarehouseId = _warehouses[_warehousePicker.SelectedIndex].Id.ToString("D");
        };

        _model.Completed += (_, _) => _bin.Focus();
        _bin.Completed += (_, _) => _color.Focus();
        _color.Completed += (_, _) => _row.Focus();
        _row.Completed += (_, _) => _pos.Focus();
        _pos.Completed += async (_, _) => await SendAsync();

        _sendBtn.Clicked += async (_, _) => await SendAsync();
        _logoutBtn.Clicked += async (_, _) =>
        {
            await _api.LogoutAsync();
            Application.Current!.MainPage = new NavigationPage(_services.GetRequiredService<LoginPage>());
        };
        _langBtn.Clicked += async (_, _) =>
        {
            _loc.Toggle();
            ApplyLang();
            await ReloadWarehousesAsync();
            await ReloadHistoryAsync();
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 12,
                Children =
                {
                    _logo,
                    _titleLabel,
                    _tagLabel,
                    _langBtn,
                    _block,
                    _whLabel,
                    _warehousePicker,
                    _limits,
                    _modelLabel,
                    _model,
                    _binLabel,
                    _bin,
                    _colorLabel,
                    _color,
                    _rowLabel,
                    _row,
                    _posLabel,
                    _pos,
                    new HorizontalStackLayout
                    {
                        Spacing = 10,
                        Children = { _gps, _gpsLabel }
                    },
                    _sendBtn,
                    _logoutBtn,
                    _status,
                    _onlineHint,
                    _historyTitle,
                    _historyBox,
                }
            }
        };
        ApplyLang();
    }

    private void ApplyLang()
    {
        _brand.Reload();
        var p = _brand.Profile;
        var accent = Color.FromArgb("#0F766E");
        try { if (!string.IsNullOrWhiteSpace(p.Accent)) accent = Color.FromArgb(p.Accent); } catch { /* keep */ }

        _titleLabel.Text = string.IsNullOrWhiteSpace(p.BrandDisplay) ? p.ProductName : p.BrandDisplay;
        _titleLabel.TextColor = accent;
        _tagLabel.Text = p.Tagline;
        _sendBtn.Text = _loc["Send"];
        _sendBtn.BackgroundColor = accent;
        _logoutBtn.Text = _loc["Logout"];
        _langBtn.Text = _loc["Language"];
        _langBtn.TextColor = accent;
        _historyTitle.TextColor = accent;
        _whLabel.Text = _loc["Warehouse"];
        _modelLabel.Text = _loc["Model"];
        _binLabel.Text = _loc["Bin"];
        _colorLabel.Text = _loc["Color"];
        _rowLabel.Text = _loc["Row"];
        _posLabel.Text = _loc["Position"];
        _gpsLabel.Text = _loc["GpsOn"];
        _onlineHint.Text = _loc["OnlineHint"];
        _historyTitle.Text = _loc["History"];
        _block.Text = _loc["FieldPcOnly"];
        if (_warehouses.Count == 0)
            _block.Text = _loc["NoWarehouses"];
        Title = $"{p.ProductName} · {_settings.DisplayName}";

        if (!string.IsNullOrWhiteSpace(_brand.LogoPath) && File.Exists(_brand.LogoPath))
        {
            _logo.Source = ImageSource.FromFile(_brand.LogoPath);
            _logo.IsVisible = true;
        }
        else
        {
            _logo.Source = null;
            _logo.IsVisible = false;
        }

        UpdateLimits();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ReloadWarehousesAsync();
        await ReloadHistoryAsync();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _sendBtn.IsEnabled = !busy;
        _logoutBtn.IsEnabled = !busy;
        _langBtn.IsEnabled = !busy;
        _warehousePicker.IsEnabled = !busy && _warehouses.Count > 0;
    }

    private async Task ReloadWarehousesAsync()
    {
        try
        {
            _warehouses = await _api.GetWarehousesAsync();
            _warehousePicker.Items.Clear();
            foreach (var w in _warehouses)
                _warehousePicker.Items.Add($"{w.Name} ({w.Rows}×{w.Columns})");

            if (_warehouses.Count == 0)
            {
                _block.Text = _loc["NoWarehouses"];
                _block.IsVisible = true;
                _warehousePicker.IsEnabled = false;
            }
            else
            {
                _block.Text = _loc["FieldPcOnly"];
                _block.IsVisible = true;
                _warehousePicker.IsEnabled = !_busy;
                var idx = _warehouses.FindIndex(w => w.Id.ToString("D") == _settings.LastWarehouseId);
                _warehousePicker.SelectedIndex = idx >= 0 ? idx : 0;
            }
            UpdateLimits();
        }
        catch (Exception ex)
        {
            _status.Text = _loc.FormatError(ex);
        }
    }

    private async Task ReloadHistoryAsync()
    {
        try
        {
            var rows = await _api.GetHistoryAsync(null, 8);
            if (rows.Count == 0)
            {
                _historyBox.Text = "—";
                return;
            }

            _historyBox.Text = string.Join("\n", rows.Select(r =>
            {
                var color = string.IsNullOrWhiteSpace(r.Color) ? "" : $" · {r.Color}";
                return $"{r.CreatedAtUtc.ToLocalTime():g}  {r.Model}  BIN {r.Bin}{color}  {r.Row}/{r.Position}";
            }));
        }
        catch
        {
            _historyBox.Text = "—";
        }
    }

    private void UpdateLimits()
    {
        if (_warehousePicker.SelectedIndex < 0 || _warehousePicker.SelectedIndex >= _warehouses.Count)
        {
            _limits.Text = "";
            return;
        }
        var w = _warehouses[_warehousePicker.SelectedIndex];
        _limits.Text = $"{_loc["Limits"]}: {_loc["Row"]} 1–{w.Rows} · {_loc["Position"]} 1–{w.Columns}";
    }

    private async Task SendAsync()
    {
        if (_busy) return;
        try
        {
            if (_warehouses.Count == 0)
            {
                _status.Text = _loc["NoWarehouses"];
                return;
            }
            if (_warehousePicker.SelectedIndex < 0)
            {
                _status.Text = _loc["Warehouse"];
                return;
            }

            var wh = _warehouses[_warehousePicker.SelectedIndex];
            var model = _model.Text?.Trim() ?? "";
            if (model.Length < 1)
            {
                _status.Text = _loc["NeedModel"];
                return;
            }

            var bin = _bin.Text?.Trim() ?? "";
            if (bin.Length < 4)
            {
                _status.Text = _loc["BinMin4"];
                return;
            }
            if (!int.TryParse(_row.Text, out var row) || !int.TryParse(_pos.Text, out var pos) || row < 1 || pos < 1)
            {
                _status.Text = _loc["SlotInvalid"];
                return;
            }
            if (wh.Rows > 0 && (row > wh.Rows || pos > wh.Columns))
            {
                _status.Text = _limits.Text;
                return;
            }

            SetBusy(true);
            _status.Text = _loc["Busy"];

            double? lat = null, lng = null;
            if (_gps.IsToggled)
            {
                try
                {
                    var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                    if (status != PermissionStatus.Granted)
                        status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                    if (status == PermissionStatus.Granted)
                    {
                        var loc = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(8)));
                        if (loc is not null) { lat = loc.Latitude; lng = loc.Longitude; }
                    }
                }
                catch { /* send without GPS */ }
            }

            await _api.SendVehicleAsync(new VehicleUpdateRequest
            {
                WarehouseId = wh.Id,
                Model = model,
                Bin = bin,
                Color = string.IsNullOrWhiteSpace(_color.Text) ? null : _color.Text.Trim(),
                Row = row,
                Position = pos,
                Latitude = lat,
                Longitude = lng
            });
            _settings.LastWarehouseId = wh.Id.ToString("D");
            _status.TextColor = Color.FromArgb("#15803D");
            _status.Text = _loc["Success"];
            _bin.Text = "";
            _row.Text = "";
            _pos.Text = "";
            await ReloadHistoryAsync();
            _bin.Focus();
            try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(40)); } catch { /* optional */ }
        }
        catch (Exception ex)
        {
            _status.TextColor = Color.FromArgb("#B91C1C");
            _status.Text = _loc.FormatError(ex);
            if (string.Equals(ex.Message, "permission_denied", StringComparison.OrdinalIgnoreCase))
            {
                _block.Text = _loc["FieldPcOnly"];
                _block.IsVisible = true;
            }
        }
        finally
        {
            SetBusy(false);
        }
    }
}
