using System;
using System.Threading;
using System.Threading.Tasks;
using CefSharp;
using CefSharp.Wpf;
using ThreeJsSync.Core;
using ThreeJsSync.Host;

namespace ThreeJsSync.BoundCallback
{
    internal sealed class BoundCallbackTransportModule : ITransportModule
    {
        private readonly BoundHostBridge _bridge = new BoundHostBridge();
        public string Name => "bound";
        public string DisplayName => "Async bound object + IJavascriptCallback";
        public string Description => "JavaScript publishes through an async bound .NET object; .NET pushes through a persistent JavaScript callback.";

        public void ConfigureBrowser(ChromiumWebBrowser browser, Action<string> reportStatus)
        {
            browser.JavascriptObjectRepository.Settings.JavascriptBindingApiEnabled = true;
            browser.JavascriptObjectRepository.Settings.JavascriptBindingApiAllowOrigins = new[] { LocalRequestHandler.Origin };
            browser.JavascriptObjectRepository.Register("syncHost", _bridge, isAsync: true, options: BindingOptions.DefaultBinder);
            browser.JavascriptObjectRepository.ObjectBoundInJavascript += (_, e) => reportStatus("Bound in JavaScript: " + e.ObjectName);
            _bridge.StatusChanged += (_, status) => reportStatus(status);
            browser.FrameLoadStart += (_, __) => _bridge.ClearCallback();
        }

        public ISyncTransport CreateTransport(ChromiumWebBrowser browser, LocalRequestHandler requestHandler) => new BoundCallbackSyncTransport(browser, _bridge);
        public void Dispose() => _bridge.ClearCallback();
    }

    internal sealed class BoundCallbackSyncTransport : SyncTransportBase
    {
        private readonly BoundHostBridge _bridge;
        public BoundCallbackSyncTransport(ChromiumWebBrowser browser, BoundHostBridge bridge) : base(browser) => _bridge = bridge;
        public override string Name => "bound";
        public override Task StartAsync(CancellationToken cancellationToken) { _bridge.MessageReceived += OnMessage; return Task.CompletedTask; }
        public override async Task SendAsync(string json, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); await _bridge.SendAsync(json); }
        public override Task StopAsync(CancellationToken cancellationToken) { _bridge.MessageReceived -= OnMessage; _bridge.ClearCallback(); return Task.CompletedTask; }
        private void OnMessage(object sender, string json) => RaiseMessage(json);
    }

    public sealed class BoundHostBridge
    {
        private readonly object _gate = new object();
        private IJavascriptCallback _callback;
        public event EventHandler<string> MessageReceived;
        public event EventHandler<string> StatusChanged;

        public Task Publish(string json) { StatusChanged?.Invoke(this, "bound publish received"); MessageReceived?.Invoke(this, json); return Task.CompletedTask; }
        public Task Subscribe(IJavascriptCallback callback) { lock (_gate) { _callback?.Dispose(); _callback = callback; } StatusChanged?.Invoke(this, "bound callback subscribed"); return Task.CompletedTask; }
        public Task Unsubscribe() { ClearCallback(); return Task.CompletedTask; }

        public async Task SendAsync(string json)
        {
            IJavascriptCallback callback;
            lock (_gate) callback = _callback;
            if (callback == null || callback.IsDisposed || !callback.CanExecute) return;
            var response = await callback.ExecuteAsync(json);
            if (!response.Success) ClearCallback();
        }

        public void ClearCallback() { lock (_gate) { _callback?.Dispose(); _callback = null; } }
    }
}
