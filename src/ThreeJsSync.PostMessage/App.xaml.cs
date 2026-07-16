using System.Windows;
using ThreeJsSync.Host;

namespace ThreeJsSync.PostMessage
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            CefHostRuntime.Initialize("PostMessage");
            base.OnStartup(e);
            MainWindow = new ThreeJsSync.Host.MainWindow(new PostMessageTransportModule());
            MainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e) { CefHostRuntime.Shutdown(); base.OnExit(e); }
    }
}

