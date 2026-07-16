using System.Windows;
using ThreeJsSync.Host;

namespace ThreeJsSync.BoundCallback
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            CefHostRuntime.Initialize("BoundCallback", concurrentTaskExecution: true);
            base.OnStartup(e);
            MainWindow = new ThreeJsSync.Host.MainWindow(new BoundCallbackTransportModule());
            MainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e) { CefHostRuntime.Shutdown(); base.OnExit(e); }
    }
}

