using System;
using System.Threading;
using System.Threading.Tasks;
using CefSharp;
using CefSharp.Wpf;
using ThreeJsSync.Core;

namespace ThreeJsSync.Host
{
    internal abstract class SyncTransportBase : ISyncTransport
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

    internal sealed class PostMessageSyncTransport : SyncTransportBase
    {
        public PostMessageSyncTransport(ChromiumWebBrowser browser) : base(browser) { }
        public override string Name => "postmessage";
        public override Task StartAsync(CancellationToken cancellationToken) { Browser.JavascriptMessageReceived += OnMessage; return Task.CompletedTask; }
        public override Task SendAsync(string json, CancellationToken cancellationToken) => SendByScriptAsync(json, cancellationToken);
        public override Task StopAsync(CancellationToken cancellationToken) { Browser.JavascriptMessageReceived -= OnMessage; return Task.CompletedTask; }
        private void OnMessage(object sender, JavascriptMessageReceivedEventArgs e) { if (e.Message is string json) RaiseMessage(json); }
    }

    internal sealed class FetchSyncTransport : SyncTransportBase
    {
        private readonly LocalRequestHandler _requestHandler;
        public FetchSyncTransport(ChromiumWebBrowser browser, LocalRequestHandler requestHandler) : base(browser) => _requestHandler = requestHandler;
        public override string Name => "fetch";
        public override Task StartAsync(CancellationToken cancellationToken) { _requestHandler.FetchMessage += OnFetchMessage; return Task.CompletedTask; }
        public override Task SendAsync(string json, CancellationToken cancellationToken) => SendByScriptAsync(json, cancellationToken);
        public override Task StopAsync(CancellationToken cancellationToken) { _requestHandler.FetchMessage -= OnFetchMessage; return Task.CompletedTask; }
        private void OnFetchMessage(object sender, string json) => RaiseMessage(json);
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
