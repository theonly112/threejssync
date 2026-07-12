using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CefSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ThreeJsSync.Core;

namespace ThreeJsSync.Host
{
    public partial class MainWindow : Window
    {
        private readonly SyncedObjectState _state = new SyncedObjectState();
        private readonly SyncEngine _engine;
        private readonly BoundHostBridge _boundBridge = new BoundHostBridge();
        private readonly DispatcherTimer _flushTimer;
        private LocalRequestHandler _requestHandler;
        private ISyncTransport _transport;
        private CancellationTokenSource _transportCancellation = new CancellationTokenSource();
        private bool _windowLoaded;
        private bool _browserReady;
        private bool _metadataInitialized;
        private bool _updatingMetadata;

        public MainWindow()
        {
            _engine = new SyncEngine("host", _state);
            InitializeComponent();
            DataContext = _state;

            var webRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web");
            _requestHandler = new LocalRequestHandler(webRoot);
            Browser.RequestHandler = _requestHandler;
            Browser.JavascriptObjectRepository.Settings.JavascriptBindingApiEnabled = true;
            Browser.JavascriptObjectRepository.Settings.JavascriptBindingApiAllowOrigins = new[] { LocalRequestHandler.Origin };
            Browser.ConsoleMessage += (_, e) => _ = Dispatcher.BeginInvoke(new Action(() => ConnectionText.Text = $"Browser console ({e.Line}): {e.Message}"));
            Browser.LoadError += (_, e) => _ = Dispatcher.BeginInvoke(new Action(() => ConnectionText.Text = $"Load error {e.ErrorCode}: {e.ErrorText}"));
            Browser.LoadingStateChanged += async (_, e) =>
            {
                if (e.IsLoading) return;
                await Task.Delay(500);
                var result = await Browser.EvaluateScriptAsync("(document.getElementById('status')?.textContent || '') + '|' + (typeof CefSharp === 'undefined' ? 'undefined' : typeof CefSharp.BindObjectAsync) + '|' + typeof syncHost + '|' + (typeof syncHost === 'object' ? Object.keys(syncHost).join(',') : '') + '|' + location.origin");
                if (!_browserReady && result.Success) _ = Dispatcher.BeginInvoke(new Action(() => ConnectionText.Text = "Bridge diagnostics: " + result.Result));
            };
            Browser.JavascriptObjectRepository.Register("syncHost", _boundBridge, isAsync: true, options: BindingOptions.DefaultBinder);
            Browser.JavascriptObjectRepository.ObjectBoundInJavascript += (_, e) => _ = Dispatcher.BeginInvoke(new Action(() => ConnectionText.Text = "Bound in JavaScript: " + e.ObjectName));
            _boundBridge.StatusChanged += (_, status) => _ = Dispatcher.BeginInvoke(new Action(() => ConnectionText.Text = status));
            Browser.FrameLoadStart += (_, __) => _boundBridge.ClearCallback();

            _engine.EnvelopeReady += Engine_OnEnvelopeReady;
            _engine.MetricsChanged += (_, __) => Dispatcher.BeginInvoke(new Action(UpdateMetrics));
            _engine.ProtocolError += (_, e) => Dispatcher.BeginInvoke(new Action(() => ConnectionText.Text = "Protocol: " + e.Message));
            _state.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != "Metadata.note") return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _updatingMetadata = true;
                    MetadataNote.Text = _state.Metadata.TryGetValue("note", out var value) ? Convert.ToString(value) : string.Empty;
                    _updatingMetadata = false;
                }));
            };

            _flushTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _flushTimer.Tick += (_, __) => _engine.FlushPending();
            _flushTimer.Start();

            Loaded += async (_, __) =>
            {
                var requested = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--transport=", StringComparison.OrdinalIgnoreCase))?.Substring(12).ToLowerInvariant();
                if (requested == "postmessage" || requested == "bound" || requested == "fetch")
                {
                    foreach (ComboBoxItem item in TransportSelector.Items) if (Equals(item.Tag, requested)) item.IsSelected = true;
                }
                _windowLoaded = true;
                _metadataInitialized = true;
                await SwitchTransportAsync(SelectedTransport());
            };
            Closed += OnClosed;
            UpdateMetrics();
        }

        private async void TransportSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_windowLoaded) return;
            await SwitchTransportAsync(SelectedTransport());
        }

        private string SelectedTransport() => (TransportSelector.SelectedItem as ComboBoxItem)?.Tag as string ?? "postmessage";

        private async Task SwitchTransportAsync(string name)
        {
            _browserReady = false;
            ConnectionText.Text = "Connecting via " + name + "…";
            _transportCancellation.Cancel();
            if (_transport != null)
            {
                _transport.MessageReceived -= Transport_OnMessageReceived;
                try { await _transport.StopAsync(CancellationToken.None); } catch { }
                _transport.Dispose();
            }
            _transportCancellation.Dispose();
            _transportCancellation = new CancellationTokenSource();
            switch (name)
            {
                case "bound": _transport = new BoundCallbackSyncTransport(Browser, _boundBridge); break;
                case "fetch": _transport = new FetchSyncTransport(Browser, _requestHandler); break;
                default: _transport = new PostMessageSyncTransport(Browser); break;
            }
            _transport.MessageReceived += Transport_OnMessageReceived;
            await _transport.StartAsync(_transportCancellation.Token);
            Browser.Load(LocalRequestHandler.Origin + "/index.html?transport=" + Uri.EscapeDataString(name));
        }

        private void Transport_OnMessageReceived(object sender, TransportMessageEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var envelope = Protocol.Deserialize(e.Json);
                    _browserReady = true;
                    ConnectionText.Text = "Connected · " + _transport.Name;
                    _engine.Receive(envelope);
                }
                catch (ProtocolException ex)
                {
                    ConnectionText.Text = "Rejected: " + ex.Message;
                }
            }));
        }

        private async void Engine_OnEnvelopeReady(object sender, EnvelopeEventArgs e)
        {
            var transport = _transport;
            if (transport == null) return;
            try { await transport.SendAsync(Protocol.Serialize(e.Envelope), _transportCancellation.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _ = Dispatcher.BeginInvoke(new Action(() => ConnectionText.Text = "Transport: " + ex.Message)); }
        }

        private void MetadataNote_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_metadataInitialized && !_updatingMetadata) _engine.QueueMetadata("note", MetadataNote.Text);
        }

        private void UpdateMetrics()
        {
            var m = _engine.Metrics;
            MetricsText.Text =
                $"sent / received {m.SentMessages} / {m.ReceivedMessages}\n" +
                $"changes sent    {m.SentChanges}\n" +
                $"applied / stale {m.AppliedChanges} / {m.IgnoredChanges}\n" +
                $"rejected        {m.RejectedMessages}\n" +
                $"coalesced       {m.CoalescedChanges}\n" +
                $"pending / max   {_engine.PendingCount} / {m.MaxPendingKeys}\n" +
                $"bytes sent      {m.SentBytes}\n" +
                $"RTT p50/p95/p99 {m.Percentile(.5):F2} / {m.Percentile(.95):F2} / {m.Percentile(.99):F2} ms\n" +
                $"checksum        {_engine.ComputeChecksum().Substring(0, 12)}";
        }

        private async void BenchmarkButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (!_browserReady) { BenchmarkText.Text = "Wait for the browser bridge to connect."; return; }
            BenchmarkButton.IsEnabled = false;
            try
            {
                BenchmarkText.Text = "Warm-up: 5 seconds…";
                await RunWorkloadAsync(TimeSpan.FromSeconds(5));
                var startSent = _engine.Metrics.SentMessages;
                var startReceived = _engine.Metrics.ReceivedMessages;
                var startBytes = _engine.Metrics.SentBytes;
                var startRejected = _engine.Metrics.RejectedMessages;
                var startLatency = _engine.Metrics.RoundTripMilliseconds.Count;

                BenchmarkText.Text = "Benchmark: simultaneous 60 Hz edits for 30 seconds…";
                var elapsed = Stopwatch.StartNew();
                await RunWorkloadAsync(TimeSpan.FromSeconds(30));
                BenchmarkText.Text = "Draining for 2 seconds…";
                await Task.Delay(TimeSpan.FromSeconds(2));
                elapsed.Stop();
                _engine.FlushPending();

                var browserResult = await Browser.EvaluateScriptAsync("JSON.stringify(window.__threeJsSyncMetrics ? window.__threeJsSyncMetrics() : {})");
                var browserMetrics = JObject.Parse(browserResult.Success ? Convert.ToString(browserResult.Result) : "{}");
                var latencies = _engine.Metrics.RoundTripMilliseconds.GetRange(startLatency, _engine.Metrics.RoundTripMilliseconds.Count - startLatency);
                latencies.Sort();
                Func<double, double> p = percentile => latencies.Count == 0 ? 0 : latencies[Math.Max(0, Math.Min(latencies.Count - 1, (int)Math.Ceiling(percentile * latencies.Count) - 1))];
                var converged = CanonicalStateComparer.AreEqual(browserMetrics["canonicalState"], _engine.GetCanonicalState());
                var passed = _engine.Metrics.RejectedMessages == startRejected && _engine.PendingCount == 0 && converged;
                BenchmarkText.Text =
                    $"{(passed ? "PASS" : "CHECK")} · {_transport.Name}\n" +
                    $"elapsed {elapsed.Elapsed.TotalSeconds:F1}s · sent {_engine.Metrics.SentMessages - startSent} · received {_engine.Metrics.ReceivedMessages - startReceived}\n" +
                    $"host RTT p50/p95/p99 {p(.5):F2}/{p(.95):F2}/{p(.99):F2} ms · bytes {_engine.Metrics.SentBytes - startBytes:N0}\n" +
                    $"converged {converged} · host checksum {_engine.ComputeChecksum().Substring(0, 16)}\n" +
                    $"browser metrics {CompactBrowserMetrics(browserMetrics)}";
            }
            catch (Exception ex) { BenchmarkText.Text = "Benchmark failed: " + ex.Message; }
            finally { BenchmarkButton.IsEnabled = true; }
        }

        private async Task RunWorkloadAsync(TimeSpan duration)
        {
            Browser.ExecuteScriptAsync("window.__threeJsSyncStartBenchmark", duration.TotalMilliseconds);
            var completion = new TaskCompletionSource<bool>();
            var started = Stopwatch.StartNew();
            var tick = 0;
            var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, __) =>
            {
                _state.MaterialOpacity = 0.5 + Math.Sin(started.Elapsed.TotalMilliseconds / 130) * 0.45;
                if (++tick % 10 == 0) _engine.SendPing();
                if (started.Elapsed >= duration) { timer.Stop(); completion.TrySetResult(true); }
            };
            timer.Start();
            await completion.Task;
        }

        private static string CompactBrowserMetrics(JObject value)
        {
            try
            {
                return $"RTT {value["p50"]:F2}/{value["p95"]:F2}/{value["p99"]:F2} ms · rejected {value["rejectedMessages"]}";
            }
            catch { return value.ToString(Formatting.None); }
        }

        private async void OnClosed(object sender, EventArgs e)
        {
            _flushTimer.Stop();
            _transportCancellation.Cancel();
            if (_transport != null) { try { await _transport.StopAsync(CancellationToken.None); } catch { } _transport.Dispose(); }
            _boundBridge.ClearCallback();
            _engine.Dispose();
            Browser.Dispose();
            _transportCancellation.Dispose();
        }
    }
}
