using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ThreeJsSync.Core;

namespace ThreeJsSync.Core.Tests
{
    [TestFixture]
    public sealed class SyncEngineTests
    {
        [Test]
        public void Local_changes_are_coalesced_by_property()
        {
            var state = new SyncedObjectState();
            var engine = new SyncEngine("host", state);
            SyncEnvelope sent = null;
            engine.EnvelopeReady += (_, e) => sent = e.Envelope;

            state.PositionX = 1;
            state.PositionX = 2;
            state.PositionY = 3;
            Assert.That(engine.PendingCount, Is.EqualTo(1));
            Assert.That(engine.Metrics.CoalescedChanges, Is.EqualTo(2));

            Assert.That(engine.FlushPending(), Is.True);
            var changes = sent.Payload.ToObject<PatchPayload>().Changes;
            Assert.That(changes, Has.Count.EqualTo(1));
            Assert.That(changes[0].Value["x"].Value<double>(), Is.EqualTo(2));
        }

        [Test]
        public void Lamport_stamp_has_deterministic_origin_tie_breaker()
        {
            var browser = new LamportStamp { Counter = 7, Origin = "browser" };
            var host = new LamportStamp { Counter = 7, Origin = "host" };
            Assert.That(host.CompareTo(browser), Is.GreaterThan(0));
        }

        [Test]
        public void Remote_changes_do_not_echo()
        {
            var state = new SyncedObjectState();
            var engine = new SyncEngine("host", state);
            var emitted = new List<SyncEnvelope>();
            engine.EnvelopeReady += (_, e) => emitted.Add(e.Envelope);

            engine.Receive(Patch("browser", 1, "visible", new JValue(false), 1));

            Assert.That(state.Visible, Is.False);
            Assert.That(engine.PendingCount, Is.Zero);
            Assert.That(emitted, Has.Count.EqualTo(1));
            Assert.That(emitted[0].Kind, Is.EqualTo(MessageKinds.Ack));
        }

        [Test]
        public void Older_change_is_ignored_but_independent_property_is_applied()
        {
            var state = new SyncedObjectState();
            var engine = new SyncEngine("host", state);
            engine.Receive(Patch("browser", 1, "name", new JValue("new"), 10));
            engine.Receive(Patch("browser", 2, "name", new JValue("old"), 9));
            engine.Receive(Patch("browser", 3, "visible", new JValue(false), 2));
            Assert.That(state.Name, Is.EqualTo("new"));
            Assert.That(state.Visible, Is.False);
            Assert.That(engine.Metrics.IgnoredChanges, Is.EqualTo(1));
        }

        [Test]
        public void Protocol_rejects_oversized_and_unknown_messages()
        {
            Assert.Throws<ProtocolException>(() => Protocol.Deserialize(new string('x', Protocol.MaxMessageBytes + 1)));
            var envelope = Patch("browser", 1, "not.allowed", new JValue(1), 1);
            var engine = new SyncEngine("host", new SyncedObjectState());
            engine.Receive(envelope);
            Assert.That(engine.Metrics.RejectedMessages, Is.EqualTo(1));
        }

        [Test]
        public void Golden_cross_language_envelope_deserializes()
        {
            var json = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "golden-envelope.json"));
            var envelope = Protocol.Deserialize(json);
            var changes = envelope.Payload.ToObject<PatchPayload>().Changes;
            Assert.That(envelope.Sequence, Is.EqualTo(42));
            Assert.That(changes[0].Key, Is.EqualTo("position"));
            Assert.That(changes[1].Value.Value<string>(), Is.EqualTo("#4f8cff"));
        }

        [Test]
        public void Canonical_comparison_treats_equal_json_number_representations_as_equal()
        {
            var browser = JObject.Parse("{\"value\":0,\"stamp\":{\"counter\":2,\"origin\":\"host\"}}");
            var host = new JObject { ["value"] = 0.0, ["stamp"] = new JObject { ["counter"] = 2L, ["origin"] = "host" } };
            Assert.That(CanonicalStateComparer.AreEqual(browser, host), Is.True);
            host["stamp"]["counter"] = 3L;
            Assert.That(CanonicalStateComparer.AreEqual(browser, host), Is.False);
        }

        [Test]
        public void Snapshot_round_trip_converges_checksums()
        {
            var hostState = new SyncedObjectState { PositionX = 4, Name = "Host" };
            var browserState = new SyncedObjectState();
            var host = new SyncEngine("host", hostState);
            var browser = new SyncEngine("browser", browserState);
            host.EnvelopeReady += (_, e) => browser.Receive(e.Envelope);
            browser.EnvelopeReady += (_, e) => host.Receive(e.Envelope);

            browser.SendReady();

            Assert.That(browserState.PositionX, Is.EqualTo(4));
            Assert.That(browserState.Name, Is.EqualTo("Host"));
            Assert.That(browser.ComputeChecksum(), Is.EqualTo(host.ComputeChecksum()));
        }

        private static SyncEnvelope Patch(string origin, long sequence, string key, JToken value, long counter) => new SyncEnvelope
        {
            Kind = MessageKinds.Patch,
            Origin = origin,
            Sequence = sequence,
            CorrelationId = Guid.NewGuid().ToString("N"),
            Payload = JObject.FromObject(new PatchPayload
            {
                Changes = new List<PropertyChange>
                {
                    new PropertyChange { Key = key, Value = value, Stamp = new LamportStamp { Counter = counter, Origin = origin } }
                }
            })
        };
    }
}
