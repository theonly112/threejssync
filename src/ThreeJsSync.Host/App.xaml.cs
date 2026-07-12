using System.Windows;
using CefSharp;
using CefSharp.Wpf;

namespace ThreeJsSync.Host
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            CefSharpSettings.ConcurrentTaskExecution = true;
            var settings = new CefSettings
            {
                CachePath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "ThreeJsSync", "CefCache"),
                LogSeverity = LogSeverity.Warning
            };
            settings.CefCommandLineArgs["disable-background-timer-throttling"] = "1";
            Cef.Initialize(settings);
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Cef.Shutdown();
            base.OnExit(e);
        }
    }
}
