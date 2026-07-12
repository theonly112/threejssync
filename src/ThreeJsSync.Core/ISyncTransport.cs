using System;
using System.Threading;
using System.Threading.Tasks;

namespace ThreeJsSync.Core
{
    public interface ISyncTransport : IDisposable
    {
        string Name { get; }
        event EventHandler<TransportMessageEventArgs> MessageReceived;
        Task StartAsync(CancellationToken cancellationToken);
        Task SendAsync(string json, CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
    }

    public sealed class TransportMessageEventArgs : EventArgs
    {
        public TransportMessageEventArgs(string json) => Json = json;
        public string Json { get; }
    }
}

