using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GestionAlmacenVehiculos.PC.Localization;
using GestionAlmacenVehiculos.PC.Ui;
using GestionAlmacenVehiculos.Shared.Branding;
using GestionAlmacenVehiculos.Shared.Dtos;

namespace GestionAlmacenVehiculos.PC;

public partial class MainWindow : Window
{
    private readonly PcRuntime _rt = PcRuntime.Instance;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly ObservableCollection<WarehouseRowVm> _warehouses = [];
    private readonly ObservableCollection<UserRowVm> _users = [];
    private readonly ObservableCollection<VehicleRowVm> _current = [];
    private readonly ObservableCollection<VehicleRowVm> _history = [];
    private readonly ObservableCollection<ActivityRowVm> _registros = [];
    private bool _refreshing;
    private bool _busy;
    private DispatcherTimer? _actionToastTimer;
    private string? _pendingLogoPath;

    public MainWindow()
    {
        InitializeComponent();
        WarehousesGrid.ItemsSource = _warehouses;
        UsersGrid.ItemsSource = _users;
        CurrentGrid.ItemsSource = _current;
        HistoryGrid.ItemsSource = _history;
        RegistrosGrid.ItemsSource = _registros;
        HistoryFilter.TextChanged += (_, _) => ScheduleFilterRefresh();
        RegistrosFilter.TextChanged += (_, _) => ScheduleFilterRefresh();
        RegistrosKindBox.SelectionChanged += (_, _) =>
        {
            if (!IsLoaded) return;
            _ = RefreshDataAsync();
        };
        _refreshTimer.Tick += async (_, _) => await RefreshDataAsync();
        InitMapUi();
        ApplyBrandToChrome();
        ApplyLanguage();
        ShowPanel(PanelConnection);
    }

    private void ApplyBrandToChrome()
    {
        var brand = _rt.Brand;
        BrandTheme.Apply(Application.Current, brand);
        SidebarRoot.Background = BrandTheme.SidebarGradient(brand);
        BrandText.Text = brand.BrandDisplay;
        TaglineText.Text = brand.Tagline;
        SetLogoImage(BrandLogoImage, _rt.BrandLogoPath);
        LoadBrandEditorFields();
        UpdateWindowTitle();
    }

    private void LoadBrandEditorFields()
    {
        var b = _rt.Brand;
        BrandProductBox.Text = b.ProductName;
        BrandDisplayBox.Text = b.BrandDisplay;
        BrandAppTitleBox.Text = b.AppTitle;
        BrandTaglineBox.Text = b.Tagline;
        BrandAccentBox.Text = b.Accent;
        BrandAccentDeepBox.Text = b.AccentDeep;
        SetLogoImage(BrandPreviewImage, _pendingLogoPath ?? _rt.BrandLogoPath);
        BrandPathHintText.Text = $"{Loc.Instance["BrandPathHint"]}\n{_rt.BrandWriteDirectory}";
    }

    private static void SetLogoImage(System.Windows.Controls.Image image, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            image.Source = null;
            image.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            image.Source = bmp;
            image.Visibility = Visibility.Visible;
        }
        catch
        {
            image.Source = null;
            image.Visibility = Visibility.Collapsed;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            _ = RefreshDataAsync();
        }
    }

    private void WhCreateFields_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CreateWarehouse_Click(sender, e);
        }
    }

    private DispatcherTimer? _filterDebounce;

    private void ScheduleFilterRefresh()
    {
        if (!IsLoaded) return;
        _filterDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _filterDebounce.Stop();
        _filterDebounce.Tick -= FilterDebounce_Tick;
        _filterDebounce.Tick += FilterDebounce_Tick;
        _filterDebounce.Start();
    }

    private async void FilterDebounce_Tick(object? sender, EventArgs e)
    {
        _filterDebounce?.Stop();
        await RefreshDataAsync();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ServerUrlBox.Text = _rt.Settings.ServerUrl;
        UsernameBox.Text = _rt.Settings.Username;
        UpdateSessionStatus();
        if (_rt.Client.IsAuthenticated)
        {
            try
            {
                if (await _rt.Client.ValidateSessionAsync())
                {
                    _rt.PersistSession();
                    await RefreshDataAsync();
                }
                else
                {
                    _rt.Client.ClearSession();
                    _rt.Settings.ClearSession();
                    _rt.Settings.Save();
                    UpdateSessionStatus();
                }
            }
            catch { /* ignore */ }
        }
        _refreshTimer.Start();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _refreshTimer.Stop();
        SaveSettingsFromUi(true);
    }

    private void ApplyLanguage()
    {
        var L = Loc.Instance;
        BrandText.Text = _rt.Brand.BrandDisplay;
        TaglineText.Text = _rt.Brand.Tagline;
        LangButton.Content = L["Language"];
        NavConnection.Content = L["NavConnection"];
        NavBrand.Content = L["NavBrand"];
        NavWarehouses.Content = L["NavWarehouses"];
        NavCurrent.Content = L["NavCurrent"];
        NavMap.Content = L["NavMap"];
        NavHistory.Content = L["NavHistory"];
        NavRegistros.Content = L["NavRegistros"];
        NavUsers.Content = L["NavUsers"];
        BrandHintText.Text = L["BrandHint"];
        LblBrandProduct.Text = L["BrandProduct"];
        LblBrandDisplay.Text = L["BrandDisplay"];
        LblBrandAppTitle.Text = L["BrandAppTitle"];
        LblBrandTagline.Text = L["BrandTagline"];
        LblBrandAccent.Text = L["BrandAccent"];
        LblBrandAccentDeep.Text = L["BrandAccentDeep"];
        BrandPickLogoButton.Content = L["BrandPickLogo"];
        BrandClearLogoButton.Content = L["BrandClearLogo"];
        BrandSaveButton.Content = L["BrandSave"];
        BrandResetButton.Content = L["BrandReset"];
        BrandOpenFolderButton.Content = L["BrandOpenFolder"];
        BrandPathHintText.Text = $"{L["BrandPathHint"]}\n{_rt.BrandWriteDirectory}";
        ApplyMapLanguage(L);
        LblServerUrl.Text = L["ServerUrl"];
        LblUsername.Text = L["Username"];
        LblPassword.Text = L["Password"];
        TestServerButton.Content = L["CheckUrl"];
        LoginButton.Content = L["Login"];
        LogoutButton.Content = L["Logout"];
        SaveSettingsButton.Content = L["SaveSettings"];
        ServerHintText.Text = L["ServerHint"];
        ClaimsPendingText.Text = L["ClaimsPending"];
        NoWarehousesHint.Text = L["NoWarehouses"];
        LblWhName.Text = L["WarehouseName"];
        LblWhRows.Text = L["Rows"];
        LblWhCols.Text = L["Columns"];
        WhName.ToolTip = L["WarehouseName"];
        WhRows.ToolTip = L["Rows"];
        WhCols.ToolTip = L["Columns"];
        CreateWhButton.Content = L["CreateWarehouse"];
        RefreshWhButton.Content = L["Refresh"];
        RefreshUsersButton.Content = L["Refresh"];
        RefreshCurrentButton.Content = L["Refresh"];
        RefreshHistoryButton.Content = L["Refresh"];
        UsersHintText.Text = L["UsersHint"];
        HistoryFilter.ToolTip = L["FilterBin"];
        LblHistoryFilter.Text = L["FilterBin"];
        LblRegistrosFilter.Text = L["RegistrosFilter"];
        LblRegistrosKind.Text = L["RegistrosKind"];
        KpiParkedLabel.Text = L["KpiParked"];
        KpiFillLabel.Text = L["KpiFill"];
        KpiFreeLabel.Text = L["KpiFree"];
        KpiModelsLabel.Text = L["KpiModels"];
        ChartStockByModelTitle.Text = L["ChartStockByModel"];
        ParkFormHint.Text = L["ParkFormHint"];
        ParkButton.Content = L["Park"];
        UnparkButton.Content = L["Unpark"];
        LblParkWarehouse.Text = L["ParkWarehouse"];
        LblParkModel.Text = L["ParkModel"];
        LblParkBin.Text = L["ParkBin"];
        LblParkColor.Text = L["ParkColor"];
        LblParkRow.Text = L["ParkRow"];
        LblParkPos.Text = L["ParkPos"];
        LblParkNotes.Text = L["ParkNotes"];
        ParkWarehouseBox.ToolTip = L["ParkWarehouse"];
        ParkModelBox.ToolTip = L["ParkModel"];
        ParkBinBox.ToolTip = L["ParkBin"];
        ParkColorBox.ToolTip = L["ParkColor"];
        ParkRowBox.ToolTip = L["ParkRow"];
        ParkPosBox.ToolTip = L["ParkPos"];
        ParkNotesBox.ToolTip = L["ParkNotes"];
        EmptyCurrentHint.Text = L["EmptyCurrent"];
        EmptyHistoryHint.Text = L["EmptyHistory"];
        EmptyRegistrosHint.Text = L["EmptyRegistros"];
        EmptyUsersHint.Text = L["EmptyUsers"];
        RegistrosHintText.Text = L["RegistrosHint"];
        RegistrosAuditLimitedText.Text = L["RegistrosAuditLimited"];
        RegistrosFilter.ToolTip = L["RegistrosFilter"];
        RegistrosKindBox.ToolTip = L["RegistrosKind"];
        RefreshRegistrosButton.Content = L["Refresh"];
        ExportRegistrosCsvButton.Content = L["ExportCsv"];
        ExportRegistrosJsonButton.Content = L["ExportJson"];
        ApplyGridHeaders(L);
        FillRegistrosKinds(L);
        UpdatePageTitle();
        UpdateSessionStatus();
        UpdateEmptyHints();
    }

    private void UpdateWindowTitle()
    {
        var brand = _rt.Brand;
        Title = _rt.Client.IsAuthenticated && !string.IsNullOrWhiteSpace(_rt.Client.DisplayName)
            ? $"{brand.ProductName} · {_rt.Client.DisplayName}"
            : brand.AppTitle;
    }

    private void ApplyGridHeaders(Loc L)
    {
        SetHeaders(WarehousesGrid, L["ColCode"], L["ColName"], L["ColRows"], L["ColColumns"], L["ColGrid"], L["ColSite"]);
        SetHeaders(CurrentGrid, L["ColWarehouse"], L["ColModel"], L["ColBin"], L["ColColor"], L["ColRow"], L["ColPos"], L["ColUser"], L["ColTime"], L["ColGps"]);
        SetHeaders(HistoryGrid, L["ColWarehouse"], L["ColModel"], L["ColBin"], L["ColColor"], L["ColRow"], L["ColPos"], L["ColUser"], L["ColTime"], L["ColGps"]);
        SetHeaders(RegistrosGrid, L["ColTime"], L["ColUser"], L["ColKind"], L["ColDetail"], L["ColWarehouse"], L["ColBin"], L["ColSource"]);
        SetHeaders(UsersGrid, L["ColUsername"], L["ColDisplayName"], L["ColRoles"], L["ColActive"]);
    }

    private static void SetHeaders(DataGrid grid, params string[] headers)
    {
        for (var i = 0; i < headers.Length && i < grid.Columns.Count; i++)
            grid.Columns[i].Header = headers[i];
    }

    private void FillRegistrosKinds(Loc L)
    {
        var selected = (RegistrosKindBox.SelectedItem as ComboBoxItem)?.Tag as string;
        RegistrosKindBox.Items.Clear();
        void Add(string tag, string label) =>
            RegistrosKindBox.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
        Add("all", L["KindAll"]);
        Add("park", L["KindPark"]);
        Add("unpark", L["KindUnpark"]);
        Add("login", L["KindLogin"]);
        Add("logout", L["KindLogout"]);
        Add("warehouse", L["KindWarehouse"]);
        Add("material", L["KindMaterial"]);
        Add("location", L["KindLocation"]);
        RegistrosKindBox.SelectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(selected))
        {
            for (var i = 0; i < RegistrosKindBox.Items.Count; i++)
            {
                if (RegistrosKindBox.Items[i] is ComboBoxItem c
                    && string.Equals(c.Tag as string, selected, StringComparison.OrdinalIgnoreCase))
                {
                    RegistrosKindBox.SelectedIndex = i;
                    break;
                }
            }
        }
    }

    private void UpdatePageTitle()
    {
        var L = Loc.Instance;
        if (NavBrand.IsChecked == true) PageTitle.Text = L["NavBrand"];
        else if (NavWarehouses.IsChecked == true) PageTitle.Text = L["NavWarehouses"];
        else if (NavCurrent.IsChecked == true) PageTitle.Text = L["NavCurrent"];
        else if (NavMap.IsChecked == true) PageTitle.Text = L["NavMap"];
        else if (NavHistory.IsChecked == true) PageTitle.Text = L["NavHistory"];
        else if (NavRegistros.IsChecked == true) PageTitle.Text = L["NavRegistros"];
        else if (NavUsers.IsChecked == true) PageTitle.Text = L["NavUsers"];
        else PageTitle.Text = L["NavConnection"];
    }

    private void LangButton_Click(object sender, RoutedEventArgs e)
    {
        Loc.Instance.Language = Loc.Instance.IsChinese ? "es" : "zh";
        _rt.Settings.Language = Loc.Instance.Language;
        _rt.Settings.Save();
        ApplyLanguage();
        _ = RefreshDataAsync();
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (NavBrand.IsChecked == true) ShowPanel(PanelBrand);
        else if (NavWarehouses.IsChecked == true) ShowPanel(PanelWarehouses);
        else if (NavCurrent.IsChecked == true) ShowPanel(PanelCurrent);
        else if (NavMap.IsChecked == true) ShowPanel(PanelMap);
        else if (NavHistory.IsChecked == true) ShowPanel(PanelHistory);
        else if (NavRegistros.IsChecked == true) ShowPanel(PanelRegistros);
        else if (NavUsers.IsChecked == true) ShowPanel(PanelUsers);
        else ShowPanel(PanelConnection);
        UpdatePageTitle();
        if (NavBrand.IsChecked == true)
            LoadBrandEditorFields();
        else
            _ = RefreshDataAsync();
        if (NavMap.IsChecked == true)
            ReloadMapFromSelection();
    }

    private void ShowPanel(UIElement panel)
    {
        PanelConnection.Visibility = Visibility.Collapsed;
        PanelBrand.Visibility = Visibility.Collapsed;
        PanelWarehouses.Visibility = Visibility.Collapsed;
        PanelCurrent.Visibility = Visibility.Collapsed;
        PanelMap.Visibility = Visibility.Collapsed;
        PanelHistory.Visibility = Visibility.Collapsed;
        PanelRegistros.Visibility = Visibility.Collapsed;
        PanelUsers.Visibility = Visibility.Collapsed;
        panel.Visibility = Visibility.Visible;
    }

    private void BrandPickLogo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|PNG|*.png|JPEG|*.jpg;*.jpeg",
        };
        if (dlg.ShowDialog() != true) return;
        _pendingLogoPath = dlg.FileName;
        SetLogoImage(BrandPreviewImage, _pendingLogoPath);
    }

    private void BrandClearLogo_Click(object sender, RoutedEventArgs e)
    {
        _pendingLogoPath = null;
        try { _rt.ClearBrandLogo(); } catch { /* ignore */ }
        ApplyBrandToChrome();
        ShowStatusOk(Loc.Instance["BrandSaved"]);
    }

    private void BrandSave_Click(object sender, RoutedEventArgs e)
    {
        var profile = new BrandProfile
        {
            ProductName = BrandProductBox.Text ?? "",
            BrandDisplay = BrandDisplayBox.Text ?? "",
            AppTitle = BrandAppTitleBox.Text ?? "",
            Tagline = BrandTaglineBox.Text ?? "",
            Accent = BrandAccentBox.Text ?? "",
            AccentDeep = BrandAccentDeepBox.Text ?? "",
            LogoFileName = _rt.Brand.LogoFileName,
        };
        try
        {
            _rt.SaveBrand(profile, _pendingLogoPath);
            _pendingLogoPath = null;
            ApplyBrandToChrome();
            ShowStatusOk(Loc.Instance["BrandSaved"]);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Instance.FormatError(ex), Title);
        }
    }

    private void BrandReset_Click(object sender, RoutedEventArgs e)
    {
        _pendingLogoPath = null;
        var defaults = BrandStore.CreateDefault();
        try
        {
            _rt.SaveBrand(defaults);
            try { _rt.ClearBrandLogo(); } catch { /* ignore */ }
            _rt.SaveBrand(BrandStore.CreateDefault());
            ApplyBrandToChrome();
            ShowStatusOk(Loc.Instance["BrandSaved"]);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Instance.FormatError(ex), Title);
        }
    }

    private void BrandOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_rt.BrandWriteDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _rt.BrandWriteDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Instance.FormatError(ex), Title);
        }
    }

    private void SaveSettingsFromUi(bool persist)
    {
        _rt.Settings.ServerUrl = ServerUrlBox.Text.Trim().TrimEnd('/');
        _rt.Settings.Username = UsernameBox.Text.Trim();
        if (persist) _rt.Settings.Save();
    }

    private void UpdateSessionStatus()
    {
        var L = Loc.Instance;
        UpdateWindowTitle();

        if (_rt.Client.IsAuthenticated)
        {
            var roles = _rt.Client.Roles;
            var rolesSummary = roles.Count > 0
                ? string.Join(", ", roles.Take(4)) + (roles.Count > 4 ? "…" : "")
                : "—";
            var sessionLine = _busy
                ? $"{L["LoggedIn"]}: {_rt.Client.DisplayName} · {L["Busy"]}"
                : $"{L["LoggedIn"]}: {_rt.Client.DisplayName}";
            ApiStatusText.Text = sessionLine;
            ApiStatusText.Foreground = (Brush)FindResource("OnlineBrush");

            SessionBanner.Visibility = Visibility.Visible;
            SessionBannerUser.Text = $"{_rt.Client.DisplayName} ({_rt.Client.UserName})";
            SessionBannerRoles.Text = $"{L["SessionRoles"]}: {rolesSummary}";

            SidebarSessionChip.Visibility = Visibility.Visible;
            SidebarSessionUser.Text = _rt.Client.DisplayName;
            SidebarSessionRoles.Text = rolesSummary;
        }
        else
        {
            ApiStatusText.Text = L["LoggedOut"];
            ApiStatusText.Foreground = (Brush)FindResource("OfflineBrush");
            SessionBanner.Visibility = Visibility.Collapsed;
            SessionBannerUser.Text = "";
            SessionBannerRoles.Text = "";
            SidebarSessionChip.Visibility = Visibility.Collapsed;
            SidebarSessionUser.Text = "";
            SidebarSessionRoles.Text = "";
        }

        ClaimsPendingText.Visibility =
            _rt.Client.IsAuthenticated && !_rt.Client.ClaimsKnown
                ? Visibility.Visible
                : Visibility.Collapsed;

        ApplyPermissionGates();
    }

    private void ApplyPermissionGates()
    {
        var L = Loc.Instance;
        var auth = _rt.Client.IsAuthenticated && !_busy;
        var claimsReady = auth && _rt.Client.ClaimsKnown;
        var canWh = claimsReady && _rt.Client.CanManageWarehouse;
        var canStock = claimsReady && _rt.Client.CanAdjustStock;
        var canAudit = claimsReady && _rt.Client.CanVerifyAudit;
        var canUsers = claimsReady && _rt.Client.CanReadUsers;

        CreateWhButton.IsEnabled = canWh;
        ParkButton.IsEnabled = canStock;
        UnparkButton.IsEnabled = canStock;
        ExportRegistrosCsvButton.IsEnabled = canAudit;
        ExportRegistrosJsonButton.IsEnabled = canAudit;
        RefreshUsersButton.IsEnabled = canUsers;

        ParkModelBox.IsEnabled = !_busy;
        ParkBinBox.IsEnabled = !_busy;
        ParkColorBox.IsEnabled = !_busy;
        ParkRowBox.IsEnabled = !_busy;
        ParkPosBox.IsEnabled = !_busy;
        ParkNotesBox.IsEnabled = !_busy;
        ParkWarehouseBox.IsEnabled = !_busy;
        MapModeView.IsEnabled = !_busy;
        MapModePaint.IsEnabled = !_busy;
        MapModeErase.IsEnabled = !_busy;
        MapAddZoneButton.IsEnabled = !_busy;
        MapZoneNameBox.IsEnabled = !_busy;
        MapZoneColorBox.IsEnabled = !_busy;

        CreateWhButton.ToolTip = canWh ? null : L["NeedPermission"];
        ParkButton.ToolTip = canStock ? L["Park"] : L["NeedPermission"];
        UnparkButton.ToolTip = canStock ? L["Unpark"] : L["NeedPermission"];
        ExportRegistrosCsvButton.ToolTip = canAudit ? L["ExportCsv"] : L["NeedPermission"];
        ExportRegistrosJsonButton.ToolTip = canAudit ? L["ExportJson"] : L["NeedPermission"];
        RefreshUsersButton.ToolTip = canUsers ? L["Refresh"] : L["NeedPermission"];

        var missing = new List<string>();
        if (claimsReady)
        {
            if (!_rt.Client.CanManageWarehouse) missing.Add(L["PermWarehouse"]);
            if (!_rt.Client.CanAdjustStock) missing.Add(L["PermStock"]);
            if (!_rt.Client.CanVerifyAudit) missing.Add(L["PermAudit"]);
            if (!_rt.Client.CanReadUsers) missing.Add(L["PermUsers"]);
        }

        var hint = missing.Count > 0
            ? $"{L["NeedPermission"]}: {string.Join(" · ", missing)}"
            : null;

        if (hint is not null)
        {
            PermissionHintText.Visibility = Visibility.Visible;
            PermissionHintText.Text = hint;
            PermissionHintText.ToolTip = L["PermissionDeniedHint"];
            HeaderPermissionHint.Visibility = Visibility.Visible;
            HeaderPermissionHint.Text = hint;
            HeaderPermissionHint.ToolTip = L["PermissionDeniedHint"];
        }
        else
        {
            PermissionHintText.Visibility = Visibility.Collapsed;
            PermissionHintText.ToolTip = null;
            HeaderPermissionHint.Visibility = Visibility.Collapsed;
            HeaderPermissionHint.ToolTip = null;
        }
    }

    private void ShowStatusOk(string message) => ShowActionStatus(message, ok: true);

    private void ShowStatusError(string message) => ShowActionStatus(message, ok: false);

    private void ShowActionStatus(string message, bool ok)
    {
        ActionStatusText.Text = message;
        ActionStatusText.Foreground = (Brush)FindResource(ok ? "OnlineBrush" : "AccentDeepBrush");
        ActionStatusText.Visibility = Visibility.Visible;

        _actionToastTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _actionToastTimer.Stop();
        _actionToastTimer.Tick -= ActionToast_Tick;
        _actionToastTimer.Tick += ActionToast_Tick;
        _actionToastTimer.Start();
    }

    private void ActionToast_Tick(object? sender, EventArgs e)
    {
        _actionToastTimer?.Stop();
        ActionStatusText.Visibility = Visibility.Collapsed;
        ActionStatusText.Text = "";
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        TestServerButton.IsEnabled = !busy;
        LoginButton.IsEnabled = !busy;
        LogoutButton.IsEnabled = !busy;
        SaveSettingsButton.IsEnabled = !busy;
        RefreshWhButton.IsEnabled = !busy;
        RefreshCurrentButton.IsEnabled = !busy;
        RefreshHistoryButton.IsEnabled = !busy;
        RefreshRegistrosButton.IsEnabled = !busy;
        MapExportButton.IsEnabled = !busy;
        MapImportButton.IsEnabled = !busy;
        MapRefreshButton.IsEnabled = !busy;
        UpdateSessionStatus();
    }

    private bool TryBeginBusy()
    {
        if (_busy) return false;
        SetBusy(true);
        return true;
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Login_Click(sender, e);
        }
    }

    private async void TestServer_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBeginBusy()) return;
        try
        {
            SaveSettingsFromUi(true);
            _rt.Client.Configure(_rt.Settings.ServerUrl);
            var ok = await _rt.Client.PingAsync();
            if (ok) ShowStatusOk(Loc.Instance["Connected"]);
            else ShowStatusError(Loc.Instance["Error"]);
        }
        catch (Exception ex)
        {
            ShowStatusError(Loc.Instance.FormatError(ex));
            MessageBox.Show(Loc.Instance.FormatError(ex), Title);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBeginBusy()) return;
        try
        {
            SaveSettingsFromUi(false);
            _rt.Client.Configure(_rt.Settings.ServerUrl);
            var user = UsernameBox.Text.Trim();
            var pass = PasswordBox.Password;
            if (user.Length < 1 || pass.Length < 1)
            {
                MessageBox.Show(Loc.Instance["NeedLogin"], Title);
                return;
            }

            await _rt.Client.LoginAsync(user, pass);
            try { _rt.PersistSession(); }
            catch (Exception ex)
            {
                ShowStatusError(Loc.Instance.FormatError(ex));
            }
            PasswordBox.Password = "";
            UpdateSessionStatus();
            ShowStatusOk(Loc.Instance["Connected"]);
            await RefreshDataAsync(force: true);
        }
        catch (Exception ex)
        {
            ShowStatusError(Loc.Instance.FormatError(ex));
            MessageBox.Show(Loc.Instance.FormatError(ex), Title);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBeginBusy()) return;
        try
        {
            try { await _rt.Client.LogoutServerAsync(); } catch { /* ignore */ }
            _rt.Client.ClearSession();
            _rt.Settings.ClearSession();
            _rt.Settings.Save();
            _warehouses.Clear();
            _users.Clear();
            _current.Clear();
            _history.Clear();
            _registros.Clear();
            UpdateEmptyHints();
            UpdateSessionStatus();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi(true);
        try { _rt.Client.Configure(_rt.Settings.ServerUrl); } catch { /* ignore */ }
        ShowStatusOk(Loc.Instance["SettingsSaved"]);
    }

    private async void CreateWarehouse_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!_rt.Client.IsAuthenticated)
        {
            MessageBox.Show(Loc.Instance["NeedLogin"], Title);
            return;
        }
        if (!_rt.Client.CanManageWarehouse)
        {
            MessageBox.Show(Loc.Instance["NeedPermission"], Title);
            return;
        }

        var name = WhName.Text.Trim();
        if (name.Length < 1 || !int.TryParse(WhRows.Text, out var rows) || !int.TryParse(WhCols.Text, out var cols) || rows < 1 || cols < 1)
        {
            MessageBox.Show(Loc.Instance["InvalidWarehouse"], Title);
            return;
        }

        if (!TryBeginBusy()) return;
        try
        {
            await _rt.Mapper.CreateWarehouseAsync(new CreateWarehouseRequest
            {
                Name = name,
                Rows = rows,
                Columns = cols,
            });
            WhName.Text = "";
            WhRows.Text = "";
            WhCols.Text = "";
            ShowStatusOk(Loc.Instance["CreateWhOk"]);
            await RefreshDataAsync(force: true);
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

    private async void RefreshAll_Click(object sender, RoutedEventArgs e) => await RefreshDataAsync();

    private async void ExportRegistrosCsv_Click(object sender, RoutedEventArgs e) =>
        await ExportRegistrosAsync("csv");

    private async void ExportRegistrosJson_Click(object sender, RoutedEventArgs e) =>
        await ExportRegistrosAsync("json");

    private async Task ExportRegistrosAsync(string format)
    {
        if (_busy) return;
        if (!_rt.Client.IsAuthenticated)
        {
            MessageBox.Show(Loc.Instance["NeedLogin"], Title);
            return;
        }
        if (!_rt.Client.CanVerifyAudit)
        {
            MessageBox.Show(Loc.Instance["NeedPermission"], Title);
            return;
        }

        if (!TryBeginBusy()) return;
        try
        {
            var (bytes, fileName, _) = await _rt.Client.ExportAuditEntriesAsync(format);
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = fileName,
                Filter = format == "json"
                    ? "JSON (*.json)|*.json|All|*.*"
                    : "CSV (*.csv)|*.csv|All|*.*",
            };
            if (dialog.ShowDialog() != true)
                return;
            await System.IO.File.WriteAllBytesAsync(dialog.FileName, bytes);
            ShowStatusOk(Loc.Instance["ExportOk"]);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Loc.Instance["ExportFailed"]}\n{Loc.Instance.FormatError(ex)}", Title);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshDataAsync(bool force = false)
    {
        if (!_rt.Client.IsAuthenticated)
        {
            UpdateSessionStatus();
            UpdateEmptyHints();
            return;
        }
        if ((!force && _busy) || _refreshing) return;
        _refreshing = true;
        try
        {
            var L = Loc.Instance;
            var warehouses = await _rt.Mapper.GetWarehousesAsync();
            _warehouses.Clear();
            foreach (var w in warehouses)
            {
                _warehouses.Add(new WarehouseRowVm
                {
                    Id = w.Id,
                    Code = w.Code,
                    Name = w.Name,
                    Rows = w.Rows,
                    Columns = w.Columns,
                    GridText = $"{w.Rows}×{w.Columns}",
                    SiteKind = w.SiteKind,
                });
            }
            NoWarehousesHint.Visibility = _warehouses.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var prevWh = ParkWarehouseBox.SelectedValue as Guid?;
            ParkWarehouseBox.ItemsSource = _warehouses.ToList();
            if (prevWh is Guid keep && _warehouses.Any(w => w.Id == keep))
                ParkWarehouseBox.SelectedValue = keep;
            else if (_warehouses.Count > 0)
                ParkWarehouseBox.SelectedIndex = 0;
            SyncMapWarehousePicker();

            _users.Clear();
            if (_rt.Client.CanReadUsers)
            {
                try
                {
                    var users = await _rt.Client.GetUsersAsync();
                    foreach (var u in users)
                    {
                        _users.Add(new UserRowVm
                        {
                            Username = u.Username,
                            DisplayName = u.DisplayName,
                            RolesText = u.Roles.Count > 0 ? string.Join(", ", u.Roles) : "—",
                            ActiveText = u.IsActive ? L["Active"] : L["Inactive"],
                        });
                    }
                }
                catch (Exception ex)
                {
                    ShowStatusError(L.FormatError(ex));
                }
            }

            _current.Clear();
            foreach (var v in await _rt.Mapper.GetCurrentAsync())
                _current.Add(ToVm(v));

            var filter = HistoryFilter.Text?.Trim() ?? "";
            _history.Clear();
            foreach (var v in await _rt.Mapper.GetHistoryAsync(filter, 300))
                _history.Add(ToVm(v));

            var regFilter = RegistrosFilter.Text?.Trim() ?? "";
            var kind = (RegistrosKindBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
            var (log, auditLimited) = await _rt.Mapper.GetActivityLogAsync(regFilter, kind, 300);
            _registros.Clear();
            foreach (var e in log)
            {
                var kindLabel = L.LocalizeKind(e.Kind);
                var detail = e.Kind is "park" or "unpark"
                    ? $"{kindLabel} · {e.Detail}"
                    : e.Detail;
                _registros.Add(new ActivityRowVm
                {
                    TimeLocal = e.Utc.ToLocalTime().ToString("g"),
                    UserDisplayName = e.UserDisplayName,
                    Kind = kindLabel,
                    Detail = detail,
                    WarehouseName = e.WarehouseName ?? "—",
                    Bin = e.Bin ?? "—",
                    Source = e.Source,
                });
            }
            RegistrosAuditLimitedText.Visibility = auditLimited ? Visibility.Visible : Visibility.Collapsed;

            UpdateEmptyHints();
            UpdateKpisAndCharts();
            if (!_busy) UpdateSessionStatus();
            if (NavMap.IsChecked == true)
            {
                RebuildMapCanvas();
                UpdateMapCharts();
            }
        }
        catch (Exception ex)
        {
            ShowStatusError(Loc.Instance["Error"] + ": " + Loc.Instance.FormatError(ex));
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateEmptyHints()
    {
        EmptyCurrentHint.Visibility = _current.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CurrentGrid.Visibility = _current.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyHistoryHint.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryGrid.Visibility = _history.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyRegistrosHint.Visibility = _registros.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RegistrosGrid.Visibility = _registros.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_rt.Client.IsAuthenticated && _rt.Client.ClaimsKnown && !_rt.Client.CanReadUsers)
        {
            EmptyUsersHint.Text = Loc.Instance["UsersNoPermission"];
            EmptyUsersHint.Visibility = Visibility.Visible;
            UsersGrid.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmptyUsersHint.Text = Loc.Instance["EmptyUsers"];
            EmptyUsersHint.Visibility = _users.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UsersGrid.Visibility = _users.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void ParkWarehouseBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateKpisAndCharts();
    }

    private void UpdateKpisAndCharts()
    {
        Guid? filter = ParkWarehouseBox.SelectedValue as Guid?;
        var dtoCurrent = _current.Select(v => new VehicleUpdateDto
        {
            WarehouseId = v.WarehouseId,
            Model = v.Model,
            Bin = v.Bin,
            Color = v.Color,
            Row = v.Row,
            Position = v.Position,
        }).ToList();

        var snap = OccupancyStats.Compute(
            dtoCurrent,
            _warehouses.Select(w => (w.Id, w.Rows, w.Columns)),
            filter);

        if (filter is not null && snap.Capacity == 0 && _warehouses.Count > 0)
        {
            snap = OccupancyStats.Compute(
                dtoCurrent,
                _warehouses.Select(w => (w.Id, w.Rows, w.Columns)),
                null);
        }

        KpiParkedValue.Text = snap.Parked.ToString();
        KpiFillValue.Text = $"{snap.FillPercent:0.#}%";
        KpiFreeValue.Text = snap.Free.ToString();
        KpiModelsValue.Text = snap.DistinctModels.ToString();
        ApplyKpiFillWarn(snap.FillPercent);
        RenderModelChart(EstadoPlotModels, OccupancyStats.TopModels(snap.ByModel, othersLabel: Loc.Instance["ChartOtros"]));
    }

    private void ApplyKpiFillWarn(double fillPercent)
    {
        Brush fg;
        Brush bg;
        if (fillPercent >= 90)
        {
            fg = (Brush)FindResource("DangerBrush");
            bg = new SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 226, 226));
        }
        else if (fillPercent >= 75)
        {
            fg = (Brush)FindResource("WarnBrush");
            bg = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 237, 213));
        }
        else
        {
            fg = (Brush)FindResource("AccentDeepBrush");
            bg = (Brush)FindResource("SurfaceMutedBrush");
        }

        KpiFillValue.Foreground = fg;
        KpiFillCard.Background = bg;
    }

    private void RenderModelChart(ScottPlot.WPF.WpfPlot plot, IReadOnlyList<ModelStockRow> rows)
    {
        plot.Plot.Clear();
        PlotTheme.Apply(plot);
        var L = Loc.Instance;
        if (rows.Count == 0)
        {
            plot.Plot.Title(L["EmptyModelStock"]);
            plot.Refresh();
            return;
        }

        var positions = new double[rows.Count];
        var values = new double[rows.Count];
        var labels = new string[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            positions[i] = i + 1;
            values[i] = rows[i].Count;
            labels[i] = rows[i].Model.Length > 10 ? rows[i].Model[..9] + "…" : rows[i].Model;
        }

        var bars = plot.Plot.Add.Bars(positions, values);
        bars.Color = PlotTheme.Occ;
        plot.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(positions, labels);
        plot.Plot.Axes.SetLimitsY(0, Math.Max(values.Max(), 1) * 1.25);
        plot.Plot.Title(L["ChartStockByModel"]);
        plot.Refresh();
    }

    private static VehicleRowVm ToVm(VehicleUpdateDto v) => new()
    {
        WarehouseId = v.WarehouseId,
        WarehouseName = v.WarehouseName,
        Model = v.Model,
        Bin = v.Bin,
        Color = v.Color ?? "—",
        Row = v.Row,
        Position = v.Position,
        UserDisplayName = v.UserDisplayName,
        CreatedAtUtc = v.CreatedAtUtc,
        TimeLocal = v.CreatedAtUtc.ToLocalTime().ToString("g"),
        Latitude = v.Latitude,
        Longitude = v.Longitude,
        GpsText = v.Latitude.HasValue && v.Longitude.HasValue ? $"{v.Latitude:F5}, {v.Longitude:F5}" : "—"
    };

    private void CurrentGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CurrentGrid.SelectedItem is not VehicleRowVm row) return;
        FillParkForm(row);
    }

    private void FillParkForm(VehicleRowVm row)
    {
        if (_warehouses.Any(w => w.Id == row.WarehouseId))
            ParkWarehouseBox.SelectedValue = row.WarehouseId;
        ParkModelBox.Text = row.Model;
        ParkBinBox.Text = row.Bin;
        ParkColorBox.Text = row.Color == "—" ? "" : row.Color;
        ParkRowBox.Text = row.Row.ToString();
        ParkPosBox.Text = row.Position.ToString();
        ParkNotesBox.Text = "";
    }

    private async void Park_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!_rt.Client.IsAuthenticated)
        {
            MessageBox.Show(Loc.Instance["NeedLogin"], Title);
            return;
        }
        if (!_rt.Client.CanAdjustStock)
        {
            MessageBox.Show(Loc.Instance["NeedPermission"], Title);
            return;
        }

        if (ParkWarehouseBox.SelectedValue is not Guid warehouseId
            || !int.TryParse(ParkRowBox.Text, out var row) || row < 1
            || !int.TryParse(ParkPosBox.Text, out var pos) || pos < 1
            || string.IsNullOrWhiteSpace(ParkModelBox.Text)
            || (ParkBinBox.Text?.Trim().Length ?? 0) < 4)
        {
            MessageBox.Show(Loc.Instance["ParkInvalid"], Title);
            return;
        }

        if (!TryBeginBusy()) return;
        try
        {
            await _rt.Mapper.ParkAsync(new VehicleUpdateRequest
            {
                WarehouseId = warehouseId,
                Model = (ParkModelBox.Text ?? "").Trim(),
                Bin = (ParkBinBox.Text ?? "").Trim(),
                Color = string.IsNullOrWhiteSpace(ParkColorBox.Text) ? null : ParkColorBox.Text.Trim(),
                Row = row,
                Position = pos,
                Notes = string.IsNullOrWhiteSpace(ParkNotesBox.Text) ? null : ParkNotesBox.Text.Trim(),
            });
            ShowStatusOk(Loc.Instance["ParkOk"]);
            await RefreshDataAsync(force: true);
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

    private async void Unpark_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!_rt.Client.IsAuthenticated)
        {
            MessageBox.Show(Loc.Instance["NeedLogin"], Title);
            return;
        }
        if (!_rt.Client.CanAdjustStock)
        {
            MessageBox.Show(Loc.Instance["NeedPermission"], Title);
            return;
        }

        if (ParkWarehouseBox.SelectedValue is not Guid warehouseId
            || !int.TryParse(ParkRowBox.Text, out var row) || row < 1
            || !int.TryParse(ParkPosBox.Text, out var pos) || pos < 1)
        {
            MessageBox.Show(Loc.Instance["UnparkInvalid"], Title);
            return;
        }

        var L = Loc.Instance;
        var bin = (ParkBinBox.Text ?? "").Trim();
        var binPart = bin.Length > 0 ? $" ({bin})" : "";
        var confirm = MessageBox.Show(
            string.Format(L["UnparkConfirm"], row, pos, binPart),
            L["Confirm"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        if (!TryBeginBusy()) return;
        try
        {
            await _rt.Mapper.UnparkAsync(warehouseId, row, pos);
            ShowStatusOk(L["UnparkOk"]);
            await RefreshDataAsync(force: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(L.FormatError(ex), Title);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void CurrentGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CurrentGrid.SelectedItem is not VehicleRowVm row) return;
        FillParkForm(row);
        if (row.Latitude is null || row.Longitude is null) return;
        var url = $"https://www.google.com/maps?q={row.Latitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},{row.Longitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private sealed class WarehouseRowVm
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public int Rows { get; set; }
        public int Columns { get; set; }
        public string GridText { get; set; } = "";
        public string SiteKind { get; set; } = "";
    }

    private sealed class UserRowVm
    {
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string RolesText { get; set; } = "";
        public string ActiveText { get; set; } = "";
    }

    private sealed class VehicleRowVm
    {
        public Guid WarehouseId { get; set; }
        public string WarehouseName { get; set; } = "";
        public string Model { get; set; } = "";
        public string Bin { get; set; } = "";
        public string Color { get; set; } = "";
        public int Row { get; set; }
        public int Position { get; set; }
        public string UserDisplayName { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; }
        public string TimeLocal { get; set; } = "";
        public string GpsText { get; set; } = "";
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    private sealed class ActivityRowVm
    {
        public string TimeLocal { get; set; } = "";
        public string UserDisplayName { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Detail { get; set; } = "";
        public string WarehouseName { get; set; } = "";
        public string Bin { get; set; } = "";
        public string Source { get; set; } = "";
    }
}
