using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ThreeJsSync.Core
{
    public sealed class SyncEngine : IDisposable
    {
        private readonly string _origin;
        private readonly SyncedObjectState _state;
        private readonly PropertyRegistry _registry;
        private readonly Dictionary<string, PropertyChange> _pending = new Dictionary<string, PropertyChange>(StringComparer.Ordinal);
        private readonly Dictionary<string, LamportStamp> _stamps = new Dictionary<string, LamportStamp>(StringComparer.Ordinal);
        private readonly Dictionary<string, Stopwatch> _pendingAcks = new Dictionary<string, Stopwatch>(StringComparer.Ordinal);
        private bool _applyingRemote;
        private long _clock;
        private long _sequence;

        public SyncEngine(string origin, SyncedObjectState state, PropertyRegistry registry = null)
        {
            if (origin != "host" && origin != "browser") throw new ArgumentException("Origin must be host or browser.", nameof(origin));
            _origin = origin;
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _registry = registry ?? new PropertyRegistry();
            Metrics = new SyncMetrics();
            foreach (var key in _registry.FixedKeys) _stamps[key] = new LamportStamp { Counter = 0, Origin = origin };
            _state.PropertyChanged += OnStatePropertyChanged;
        }

        public event EventHandler<EnvelopeEventArgs> EnvelopeReady;
        public event EventHandler MetricsChanged;
        public event EventHandler<ProtocolErrorEventArgs> ProtocolError;

        public SyncedObjectState State => _state;
        public SyncMetrics Metrics { get; }
        public int PendingCount => _pending.Count;

        public void QueueMetadata(string key, object value) => _state.SetMetadata(key, value);

        public bool FlushPending()
        {
            if (_pending.Count == 0) return false;
            var changes = _pending.Values.OrderBy(c => c.Key, StringComparer.Ordinal).ToList();
            _pending.Clear();
            var correlationId = Guid.NewGuid().ToString("N");
            var envelope = Create(MessageKinds.Patch, JToken.FromObject(new PatchPayload { Changes = changes }, JsonSerializer.Create(Protocol.JsonSettings)), correlationId);
            _pendingAcks[correlationId] = Stopwatch.StartNew();
            Emit(envelope);
            Metrics.SentChanges += changes.Count;
            RaiseMetrics();
            return true;
        }

        public void SendReady() => Emit(Create(MessageKinds.Ready, new JObject()));

        public void SendPing()
        {
            var correlationId = Guid.NewGuid().ToString("N");
            _pendingAcks[correlationId] = Stopwatch.StartNew();
            Emit(Create(MessageKinds.Ping, new JObject(), correlationId));
        }

        public void Receive(SyncEnvelope envelope)
        {
            try
            {
                Protocol.ValidateEnvelope(envelope);
                Metrics.ReceivedMessages++;
                _clock = Math.Max(_clock, HighestCounter(envelope.Payload));
                switch (envelope.Kind)
                {
                    case MessageKinds.Ready:
                        EmitSnapshot();
                        break;
                    case MessageKinds.Snapshot:
                        ApplyChanges(ParseChanges(envelope), true);
                        Acknowledge(envelope);
                        break;
                    case MessageKinds.Patch:
                        ApplyChanges(ParseChanges(envelope), false);
                        Acknowledge(envelope);
                        break;
                    case MessageKinds.Ack:
                    case MessageKinds.Pong:
                        CompleteRoundTrip(envelope.CorrelationId);
                        break;
                    case MessageKinds.Ping:
                        Emit(Create(MessageKinds.Pong, new JObject(), envelope.CorrelationId));
                        break;
                    case MessageKinds.Error:
                        Metrics.RejectedMessages++;
                        break;
                }
                RaiseMetrics();
            }
            catch (Exception ex) when (ex is ProtocolException || ex is JsonException || ex is InvalidOperationException)
            {
                Metrics.RejectedMessages++;
                ProtocolError?.Invoke(this, new ProtocolErrorEventArgs(ex.Message));
                RaiseMetrics();
            }
        }

        public void ReceiveJson(string json)
        {
            try { Receive(Protocol.Deserialize(json)); }
            catch (ProtocolException ex)
            {
                Metrics.RejectedMessages++;
                ProtocolError?.Invoke(this, new ProtocolErrorEventArgs(ex.Message));
                RaiseMetrics();
            }
        }

        public string ComputeChecksum()
        {
            var data = GetCanonicalState();
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(data.ToString(Formatting.None)));
                return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        public JObject GetCanonicalState()
        {
            var data = new JObject();
            foreach (var pair in _stamps.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (!_registry.TryGet(pair.Key, out var codec)) continue;
                data[pair.Key] = new JObject
                {
                    ["value"] = codec.Read(_state),
                    ["counter"] = pair.Value.Counter,
                    ["origin"] = pair.Value.Origin
                };
            }
            return data;
        }

        public IReadOnlyDictionary<string, LamportStamp> GetStamps() => _stamps.ToDictionary(p => p.Key, p => p.Value.Copy());

        private void OnStatePropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            if (_applyingRemote) return;
            var key = _registry.KeyForStateProperty(args.PropertyName);
            if (key == null || !_registry.TryGet(key, out var codec)) return;
            var value = codec.Read(_state);
            if (!codec.Validate(value))
            {
                Metrics.RejectedMessages++;
                ProtocolError?.Invoke(this, new ProtocolErrorEventArgs("Local value rejected for " + key));
                RaiseMetrics();
                return;
            }
            var stamp = new LamportStamp { Counter = ++_clock, Origin = _origin };
            _stamps[key] = stamp;
            if (_pending.ContainsKey(key)) Metrics.CoalescedChanges++;
            _pending[key] = new PropertyChange { Key = key, Value = value.DeepClone(), Stamp = stamp.Copy() };
            Metrics.MaxPendingKeys = Math.Max(Metrics.MaxPendingKeys, _pending.Count);
            RaiseMetrics();
        }

        private void ApplyChanges(IList<PropertyChange> changes, bool force)
        {
            foreach (var change in changes)
            {
                if (change == null || string.IsNullOrWhiteSpace(change.Key) || change.Stamp == null || change.Stamp.Origin == null)
                    throw new ProtocolException("Patch contains an incomplete change.");
                if (change.Stamp.Origin != "host" && change.Stamp.Origin != "browser") throw new ProtocolException("Invalid stamp origin.");
                if (!_registry.TryGet(change.Key, out var codec)) throw new ProtocolException("Unknown property: " + change.Key);
                if (!codec.Validate(change.Value)) throw new ProtocolException("Invalid value for: " + change.Key);
                if (!force && _stamps.TryGetValue(change.Key, out var current) && change.Stamp.CompareTo(current) <= 0)
                {
                    Metrics.IgnoredChanges++;
                    continue;
                }
                _applyingRemote = true;
                try { codec.Apply(_state, change.Value); }
                finally { _applyingRemote = false; }
                _stamps[change.Key] = change.Stamp.Copy();
                _clock = Math.Max(_clock, change.Stamp.Counter);
                Metrics.AppliedChanges++;
            }
        }

        private IList<PropertyChange> ParseChanges(SyncEnvelope envelope)
        {
            var payload = envelope.Payload?.ToObject<PatchPayload>(JsonSerializer.Create(Protocol.JsonSettings));
            if (payload?.Changes == null) throw new ProtocolException("Patch payload is missing changes.");
            return payload.Changes;
        }

        private void EmitSnapshot()
        {
            var keys = new HashSet<string>(_registry.FixedKeys, StringComparer.Ordinal);
            foreach (var key in _state.Metadata.Keys) keys.Add("metadata." + key);
            var changes = new List<PropertyChange>();
            foreach (var key in keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                if (!_registry.TryGet(key, out var codec)) continue;
                if (!_stamps.TryGetValue(key, out var stamp)) stamp = _stamps[key] = new LamportStamp { Counter = 0, Origin = _origin };
                changes.Add(new PropertyChange { Key = key, Value = codec.Read(_state), Stamp = stamp.Copy() });
            }
            Emit(Create(MessageKinds.Snapshot, JToken.FromObject(new PatchPayload { Changes = changes }, JsonSerializer.Create(Protocol.JsonSettings))));
        }

        private void Acknowledge(SyncEnvelope envelope)
        {
            var payload = JToken.FromObject(new AckPayload { Sequence = envelope.Sequence, Checksum = ComputeChecksum() }, JsonSerializer.Create(Protocol.JsonSettings));
            Emit(Create(MessageKinds.Ack, payload, envelope.CorrelationId));
        }

        private void CompleteRoundTrip(string correlationId)
        {
            if (string.IsNullOrEmpty(correlationId) || !_pendingAcks.TryGetValue(correlationId, out var stopwatch)) return;
            stopwatch.Stop();
            _pendingAcks.Remove(correlationId);
            Metrics.RoundTripMilliseconds.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        private SyncEnvelope Create(string kind, JToken payload, string correlationId = null) => new SyncEnvelope
        {
            Kind = kind,
            Origin = _origin,
            ObjectId = Protocol.DefaultObjectId,
            Sequence = ++_sequence,
            CorrelationId = correlationId,
            Payload = payload
        };

        private void Emit(SyncEnvelope envelope)
        {
            Metrics.SentMessages++;
            Metrics.SentBytes += Encoding.UTF8.GetByteCount(Protocol.Serialize(envelope));
            EnvelopeReady?.Invoke(this, new EnvelopeEventArgs(envelope));
        }

        private static long HighestCounter(JToken payload)
        {
            var changes = payload?["changes"] as JArray;
            return changes == null || changes.Count == 0 ? 0 : changes.Select(c => c?["stamp"]?["counter"]?.Value<long>() ?? 0).Max();
        }

        private void RaiseMetrics() => MetricsChanged?.Invoke(this, EventArgs.Empty);

        public void Dispose() => _state.PropertyChanged -= OnStatePropertyChanged;
    }

    public sealed class SyncMetrics
    {
        public long SentMessages { get; internal set; }
        public long ReceivedMessages { get; internal set; }
        public long SentChanges { get; internal set; }
        public long AppliedChanges { get; internal set; }
        public long IgnoredChanges { get; internal set; }
        public long RejectedMessages { get; internal set; }
        public long CoalescedChanges { get; internal set; }
        public long SentBytes { get; internal set; }
        public int MaxPendingKeys { get; internal set; }
        public List<double> RoundTripMilliseconds { get; } = new List<double>();

        public double Percentile(double percentile)
        {
            if (RoundTripMilliseconds.Count == 0) return 0;
            var ordered = RoundTripMilliseconds.OrderBy(v => v).ToArray();
            var index = (int)Math.Ceiling(percentile * ordered.Length) - 1;
            return ordered[Math.Max(0, Math.Min(index, ordered.Length - 1))];
        }
    }

    public sealed class EnvelopeEventArgs : EventArgs
    {
        public EnvelopeEventArgs(SyncEnvelope envelope) => Envelope = envelope;
        public SyncEnvelope Envelope { get; }
    }

    public sealed class ProtocolErrorEventArgs : EventArgs
    {
        public ProtocolErrorEventArgs(string message) => Message = message;
        public string Message { get; }
    }
}
