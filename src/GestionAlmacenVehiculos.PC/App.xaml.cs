using System.Windows;
using GestionAlmacenVehiculos.PC.Localization;

namespace GestionAlmacenVehiculos.PC;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Loc.Instance.Language = PcSettings.Load().Language;
    }
}
