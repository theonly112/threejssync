using System;
using System.Threading;
using System.Threading.Tasks;
using CefSharp.Wpf;
using ThreeJsSync.Core;
using ThreeJsSync.Host;

namespace ThreeJsSync.Fetch
{
    internal sealed class FetchTransportModule : ITransportModule
    {
        public string Name => "fetch";
        public string DisplayName => "Intercepted fetch + ExecuteScriptAsync";
        public string Description => "JavaScript POSTs to an in-process CefSharp request handler; .NET pushes through the JavaScript receiver. No server or port is opened.";
        public void ConfigureBrowser(ChromiumWebBrowser browser, Action<string> reportStatus) { }
        public ISyncTransport CreateTransport(ChromiumWebBrowser browser, LocalRequestHandler requestHandler) => new FetchSyncTransport(browser, requestHandler);
        public void Dispose() { }
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
}

