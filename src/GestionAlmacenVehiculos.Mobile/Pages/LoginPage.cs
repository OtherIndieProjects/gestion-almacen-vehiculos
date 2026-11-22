using GestionAlmacenVehiculos.Mobile.Services;
using GestionAlmacenVehiculos.Shared.Branding;

namespace GestionAlmacenVehiculos.Mobile.Pages;

public class LoginPage : ContentPage
{
    private readonly AppSettings _settings;
    private readonly LocService _loc;
    private readonly ApiClient _api;
    private readonly BrandService _brand;
    private readonly IServiceProvider _services;

    private readonly Image _logo = new() { HeightRequest = 64, Aspect = Aspect.AspectFit, IsVisible = false };
    private readonly Entry _hostIp = new() { Keyboard = Keyboard.Url, Placeholder = "192.168.1.10", ReturnType = ReturnType.Next };
    private readonly Entry _hostPort = new() { Keyboard = Keyboard.Numeric, Text = "5088", ReturnType = ReturnType.Next };
    private readonly Entry _user = new() { ReturnType = ReturnType.Next };
    private readonly Entry _pass = new() { IsPassword = true, ReturnType = ReturnType.Go };
    private readonly Entry _brandName = new() { Placeholder = "Vehicle" };
    private readonly Label _title = new() { FontSize = 32, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#0F766E") };
    private readonly Label _tag = new() { TextColor = Color.FromArgb("#5C6B73") };
    private readonly Label _brandSection = new() { FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#0F766E") };
    private readonly Label _hostLabel = new();
    private readonly Label _portLabel = new();
    private readonly Label _userLabel = new();
    private readonly Label _passLabel = new();
    private readonly Label _hint = new() { TextColor = Color.FromArgb("#5C6B73"), FontSize = 12 };
    private readonly Label _status = new();
    private readonly Button _langBtn = new() { BackgroundColor = Colors.Transparent, TextColor = Color.FromArgb("#0F766E") };
    private readonly Button _testBtn = new() { BackgroundColor = Color.FromArgb("#334155") };
    private readonly Button _loginBtn = new();
    private readonly Button _pickLogoBtn = new() { BackgroundColor = Color.FromArgb("#334155") };
    private readonly Button _saveBrandBtn = new();
    private readonly Button _resetBrandBtn = new() { BackgroundColor = Color.FromArgb("#64748B") };
    private string? _pendingLogoPath;
    private bool _bootstrapped;
    private bool _busy;

    public LoginPage(AppSettings settings, LocService loc, ApiClient api, BrandService brand, IServiceProvider services)
    {
        _settings = settings;
        _loc = loc;
        _api = api;
        _brand = brand;
        _services = services;

        _hostIp.Completed += (_, _) => _hostPort.Focus();
        _hostPort.Completed += (_, _) => _user.Focus();
        _user.Completed += (_, _) => _pass.Focus();
        _pass.Completed += async (_, _) => await LoginAsync();

        _langBtn.Clicked += (_, _) => { _loc.Toggle(); ApplyLang(); ApplyBrandUi(); };
        _testBtn.Clicked += async (_, _) => await TestConnectionAsync();
        _loginBtn.Clicked += async (_, _) => await LoginAsync();
        _pickLogoBtn.Clicked += async (_, _) => await PickLogoAsync();
        _saveBrandBtn.Clicked += (_, _) => SaveBrand();
        _resetBrandBtn.Clicked += (_, _) =>
        {
            _pendingLogoPath = null;
            _brand.ResetToVehicle();
            ApplyBrandUi();
            _status.TextColor = Color.FromArgb("#15803D");
            _status.Text = _loc["BrandSaved"];
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 28,
                Spacing = 10,
                Children =
                {
                    _logo, _title, _tag, _langBtn,
                    _brandSection, _brandName, _pickLogoBtn, _saveBrandBtn, _resetBrandBtn,
                    _hostLabel, _hostIp,
                    _portLabel, _hostPort,
                    _userLabel, _user,
                    _passLabel, _pass,
                    _testBtn, _loginBtn,
                    _status, _hint
                }
            }
        };
        ApplyLang();
        ApplyBrandUi();
        Appearing += async (_, _) => await OnAppearingAsync();
    }

    private async Task OnAppearingAsync()
    {
        if (_bootstrapped) return;
        _bootstrapped = true;

        _hostIp.Text = _settings.HostIp;
        _hostPort.Text = _settings.HostPort.ToString();
        _user.Text = _settings.Username;
        _pass.Text = await _settings.GetPasswordAsync() ?? "";
        ApplyBrandUi();

        if (!_settings.HasHostConfigured)
        {
            _status.Text = _loc["NeedHost"];
            return;
        }

        if (!TryApplyHostFromUi(out _))
            return;

        if (!string.IsNullOrWhiteSpace(_settings.Token))
        {
            try
            {
                if (await _api.ValidateSessionAsync())
                {
                    await GoMainAsync();
                    return;
                }
            }
            catch { /* caer a login */ }
            _settings.ClearSession();
        }

        var savedUser = _settings.Username;
        var savedPass = await _settings.GetPasswordAsync();
        if (!string.IsNullOrWhiteSpace(savedUser) && !string.IsNullOrWhiteSpace(savedPass))
        {
            try
            {
                if (await _api.HealthAsync())
                {
                    await _api.LoginAsync(savedUser, savedPass);
                    await GoMainAsync();
                }
            }
            catch
            {
                _status.Text = _loc["NeedLogin"];
            }
        }
    }

    private void ApplyLang()
    {
        _langBtn.Text = _loc["Language"];
        _hostLabel.Text = _loc["HostIp"];
        _portLabel.Text = _loc["HostPort"];
        _userLabel.Text = _loc["Username"];
        _passLabel.Text = _loc["Password"];
        _testBtn.Text = _loc["CheckUrl"];
        _loginBtn.Text = _loc["Login"];
        _hint.Text = _loc["HostHint"];
        _brandSection.Text = _loc["BrandSection"];
        _pickLogoBtn.Text = _loc["BrandPickLogo"];
        _saveBrandBtn.Text = _loc["BrandSave"];
        _resetBrandBtn.Text = _loc["BrandReset"];
        _brandName.Placeholder = _loc["BrandProduct"];
    }

    private void ApplyBrandUi()
    {
        var p = _brand.Profile;
        var accent = TryColor(p.Accent, Color.FromArgb("#0F766E"));
        _title.Text = string.IsNullOrWhiteSpace(p.BrandDisplay) ? p.ProductName : p.BrandDisplay;
        _title.TextColor = accent;
        _tag.Text = p.Tagline;
        Title = p.AppTitle;
        _brandName.Text = p.ProductName;
        _langBtn.TextColor = accent;
        _brandSection.TextColor = accent;
        _loginBtn.BackgroundColor = accent;
        _saveBrandBtn.BackgroundColor = accent;

        var logo = _pendingLogoPath ?? _brand.LogoPath;
        if (!string.IsNullOrWhiteSpace(logo) && File.Exists(logo))
        {
            _logo.Source = ImageSource.FromFile(logo);
            _logo.IsVisible = true;
        }
        else
        {
            _logo.Source = null;
            _logo.IsVisible = false;
        }
    }

    private static Color TryColor(string? hex, Color fallback)
    {
        try { return string.IsNullOrWhiteSpace(hex) ? fallback : Color.FromArgb(hex); }
        catch { return fallback; }
    }

    private async Task PickLogoAsync()
    {
        try
        {
            var result = await MediaPicker.Default.PickPhotoAsync();
            if (result is null) return;
            _pendingLogoPath = result.FullPath;
            ApplyBrandUi();
        }
        catch (Exception ex)
        {
            _status.TextColor = Color.FromArgb("#B91C1C");
            _status.Text = _loc.FormatError(ex);
        }
    }

    private void SaveBrand()
    {
        var name = (_brandName.Text ?? "").Trim();
        if (name.Length < 1) name = BrandDefaults.ProductName;
        var profile = _brand.Profile.Clone();
        profile.ProductName = name;
        profile.BrandDisplay = name.ToUpperInvariant();
        profile.AppTitle = name;
        profile.Tagline = string.IsNullOrWhiteSpace(profile.Tagline)
            ? BrandDefaults.TaglineMobile
            : profile.Tagline;
        try
        {
            _brand.Save(profile, _pendingLogoPath);
            _pendingLogoPath = null;
            ApplyBrandUi();
            _status.TextColor = Color.FromArgb("#15803D");
            _status.Text = _loc["BrandSaved"];
        }
        catch (Exception ex)
        {
            _status.TextColor = Color.FromArgb("#B91C1C");
            _status.Text = _loc.FormatError(ex);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _testBtn.IsEnabled = !busy;
        _loginBtn.IsEnabled = !busy;
        _langBtn.IsEnabled = !busy;
        _pickLogoBtn.IsEnabled = !busy;
        _saveBrandBtn.IsEnabled = !busy;
        _resetBrandBtn.IsEnabled = !busy;
    }

    private bool TryApplyHostFromUi(out string error)
    {
        error = "";
        var ip = _hostIp.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(ip))
        {
            error = _loc["NeedHost"];
            return false;
        }
        if (!int.TryParse(_hostPort.Text?.Trim(), out var port) || port is < 1 or > 65535)
        {
            error = _loc["HostPort"];
            return false;
        }
        _settings.ApplyHost(ip, port);
        return true;
    }

    private async Task TestConnectionAsync()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            if (!TryApplyHostFromUi(out var err))
            {
                _status.Text = err;
                return;
            }
            _status.Text = _loc["Busy"];
            var ok = await _api.HealthAsync();
            if (ok)
            {
                _status.TextColor = Color.FromArgb("#15803D");
                _status.Text = $"{_loc["Connected"]} ({_settings.BaseUrl})";
            }
            else
            {
                _status.TextColor = Color.FromArgb("#B91C1C");
                _status.Text = _loc["Error"];
            }
        }
        catch (Exception ex)
        {
            _status.TextColor = Color.FromArgb("#B91C1C");
            _status.Text = _loc.FormatError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoginAsync()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            if (!TryApplyHostFromUi(out var err))
            {
                _status.TextColor = Color.FromArgb("#B45309");
                _status.Text = err;
                return;
            }

            var username = _user.Text?.Trim() ?? "";
            var password = _pass.Text ?? "";
            if (username.Length < 1 || password.Length < 1)
            {
                _status.TextColor = Color.FromArgb("#B45309");
                _status.Text = _loc["NeedLogin"];
                return;
            }

            _status.TextColor = Color.FromArgb("#5C6B73");
            _status.Text = _loc["Busy"];
            await _api.LoginAsync(username, password);
            await _settings.SetPasswordAsync(password);
            await GoMainAsync();
        }
        catch (Exception ex)
        {
            _status.TextColor = Color.FromArgb("#B91C1C");
            _status.Text = _loc.FormatError(ex);
            _pass.Focus();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private Task GoMainAsync()
    {
        Application.Current!.MainPage = new NavigationPage(_services.GetRequiredService<MainPage>());
        return Task.CompletedTask;
    }
}
