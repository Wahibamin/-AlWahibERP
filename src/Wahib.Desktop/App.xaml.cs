using System.Windows;
namespace Wahib.Desktop;
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.Exception.Message,"الوهيب ERP - تنبيه",MessageBoxButton.OK,MessageBoxImage.Error);
            ex.Handled=true;
        };
        new MainWindow().Show();
    }
}

