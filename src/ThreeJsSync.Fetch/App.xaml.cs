using System.Windows;
using ThreeJsSync.Host;

namespace ThreeJsSync.Fetch
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            CefHostRuntime.Initialize("Fetch");
            base.OnStartup(e);
            MainWindow = new ThreeJsSync.Host.MainWindow(new FetchTransportModule());
            MainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e) { CefHostRuntime.Shutdown(); base.OnExit(e); }
    }
}

