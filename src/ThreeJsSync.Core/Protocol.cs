using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace ThreeJsSync.Core
{
    public static class Protocol
    {
        public const int Version = 1;
        public const int MaxMessageBytes = 64 * 1024;
        public const string DefaultObjectId = "demo-cube";

        public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };

        public static string Serialize(SyncEnvelope envelope) => JsonConvert.SerializeObject(envelope, JsonSettings);

        public static SyncEnvelope Deserialize(string json)
        {
            if (json == null) throw new ProtocolException("Message is null.");
            if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxMessageBytes)
                throw new ProtocolException("Message exceeds 64 KiB.");
            try
            {
                var envelope = JsonConvert.DeserializeObject<SyncEnvelope>(json, JsonSettings);
                if (envelope == null) throw new ProtocolException("Message is empty.");
                ValidateEnvelope(envelope);
                return envelope;
            }
            catch (JsonException ex)
            {
                throw new ProtocolException("Malformed JSON message.", ex);
            }
        }

        public static void ValidateEnvelope(SyncEnvelope envelope)
        {
            if (envelope.ProtocolVersion != Version) throw new ProtocolException("Unsupported protocol version.");
            if (string.IsNullOrWhiteSpace(envelope.Kind) || !MessageKinds.All.Contains(envelope.Kind))
                throw new ProtocolException("Unknown message kind.");
            if (envelope.ObjectId != DefaultObjectId) throw new ProtocolException("Unexpected object id.");
            if (envelope.Origin != "host" && envelope.Origin != "browser")
                throw new ProtocolException("Unexpected origin.");
            if (envelope.Sequence < 0) throw new ProtocolException("Sequence cannot be negative.");
        }
    }

    public static class MessageKinds
    {
        public const string Ready = "ready";
        public const string Snapshot = "snapshot";
        public const string Patch = "patch";
        public const string Ack = "ack";
        public const string Ping = "ping";
        public const string Pong = "pong";
        public const string Error = "error";
        public static readonly HashSet<string> All = new HashSet<string>
        {
            Ready, Snapshot, Patch, Ack, Ping, Pong, Error
        };
    }

    public sealed class SyncEnvelope
    {
        public int ProtocolVersion { get; set; } = Protocol.Version;
        public string Kind { get; set; }
        public string ObjectId { get; set; } = Protocol.DefaultObjectId;
        public string Origin { get; set; }
        public long Sequence { get; set; }
        public string CorrelationId { get; set; }
        public JToken Payload { get; set; }
    }

    public sealed class PatchPayload
    {
        public IList<PropertyChange> Changes { get; set; } = new List<PropertyChange>();
    }

    public sealed class PropertyChange
    {
        public string Key { get; set; }
        public JToken Value { get; set; }
        public LamportStamp Stamp { get; set; }
    }

    public sealed class LamportStamp : IComparable<LamportStamp>
    {
        public long Counter { get; set; }
        public string Origin { get; set; }

        public int CompareTo(LamportStamp other)
        {
            if (other == null) return 1;
            var counter = Counter.CompareTo(other.Counter);
            return counter != 0 ? counter : string.CompareOrdinal(Origin, other.Origin);
        }

        public LamportStamp Copy() => new LamportStamp { Counter = Counter, Origin = Origin };
    }

    public sealed class AckPayload
    {
        public long Sequence { get; set; }
        public string Checksum { get; set; }
    }

    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message) : base(message) { }
        public ProtocolException(string message, Exception inner) : base(message, inner) { }
    }
}

