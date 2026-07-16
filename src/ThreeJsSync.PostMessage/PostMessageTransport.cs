using System;
using System.Threading;
using System.Threading.Tasks;
using CefSharp;
using CefSharp.Wpf;
using ThreeJsSync.Core;
using ThreeJsSync.Host;

namespace ThreeJsSync.PostMessage
{
    internal sealed class PostMessageTransportModule : ITransportModule
    {
        public string Name => "postmessage";
        public string DisplayName => "PostMessage + ExecuteScriptAsync";
        public string Description => "JavaScript publishes with CefSharp.PostMessage; .NET pushes through one stable JavaScript receive function.";
        public void ConfigureBrowser(ChromiumWebBrowser browser, Action<string> reportStatus) { }
        public ISyncTransport CreateTransport(ChromiumWebBrowser browser, LocalRequestHandler requestHandler) => new PostMessageSyncTransport(browser);
        public void Dispose() { }
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
}

