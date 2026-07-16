using System;
using System.Threading;
using System.Threading.Tasks;
using CefSharp;
using CefSharp.Wpf;
using ThreeJsSync.Core;

namespace ThreeJsSync.Host
{
    public interface ITransportModule : IDisposable
    {
        string Name { get; }
        string DisplayName { get; }
        string Description { get; }
        void ConfigureBrowser(ChromiumWebBrowser browser, Action<string> reportStatus);
        ISyncTransport CreateTransport(ChromiumWebBrowser browser, LocalRequestHandler requestHandler);
    }

    public abstract class SyncTransportBase : ISyncTransport
    {
        protected readonly ChromiumWebBrowser Browser;
        protected SyncTransportBase(ChromiumWebBrowser browser) => Browser = browser;
        public abstract string Name { get; }
        public event EventHandler<TransportMessageEventArgs> MessageReceived;
        protected void RaiseMessage(string json) => MessageReceived?.Invoke(this, new TransportMessageEventArgs(json));
        public abstract Task StartAsync(CancellationToken cancellationToken);
        public abstract Task SendAsync(string json, CancellationToken cancellationToken);
        public abstract Task StopAsync(CancellationToken cancellationToken);
        public virtual void Dispose() { }

        protected Task SendByScriptAsync(string json, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Browser.IsBrowserInitialized) return Task.CompletedTask;
            Browser.ExecuteScriptAsync("window.__threeJsSyncReceive", json);
            return Task.CompletedTask;
        }
    }

    public static class CefHostRuntime
    {
        public static void Initialize(string applicationName, bool concurrentTaskExecution = false)
        {
            CefSharpSettings.ConcurrentTaskExecution = concurrentTaskExecution;
            var settings = new CefSharp.Wpf.CefSettings
            {
                CachePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThreeJsSync", applicationName, "CefCache"),
                LogSeverity = LogSeverity.Warning
            };
            settings.CefCommandLineArgs["disable-background-timer-throttling"] = "1";
            Cef.Initialize(settings);
        }

        public static void Shutdown() => Cef.Shutdown();
    }
}
