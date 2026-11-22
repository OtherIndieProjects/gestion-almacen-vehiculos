using GestionAlmacenVehiculos.Mobile.Services;
using Microsoft.Extensions.Logging;

namespace GestionAlmacenVehiculos.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton<AppSettings>();
        builder.Services.AddSingleton<LocService>();
        builder.Services.AddSingleton<BrandService>();
        builder.Services.AddSingleton<ApiClient>();
        builder.Services.AddTransient<Pages.LoginPage>();
        builder.Services.AddTransient<Pages.MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
