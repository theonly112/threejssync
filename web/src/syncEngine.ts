import { compareStamp, envelope, parseEnvelope, type LamportStamp, type PatchPayload, type PropertyChange, type SyncEnvelope } from './protocol';
import { cloneChange, defaultState, PropertyRegistry, type ObjectState } from './state';

export interface BrowserMetrics {
  sentMessages: number; receivedMessages: number; sentChanges: number; appliedChanges: number;
  ignoredChanges: number; rejectedMessages: number; coalescedChanges: number; sentBytes: number;
  maxPendingKeys: number; roundTrips: number[];
}

export class BrowserSyncEngine {
  readonly state: ObjectState = defaultState();
  readonly metrics: BrowserMetrics = { sentMessages: 0, receivedMessages: 0, sentChanges: 0, appliedChanges: 0, ignoredChanges: 0, rejectedMessages: 0, coalescedChanges: 0, sentBytes: 0, maxPendingKeys: 0, roundTrips: [] };
  readonly registry = new PropertyRegistry();
  private readonly stamps = new Map<string, LamportStamp>();
  private readonly pending = new Map<string, PropertyChange>();
  private readonly pendingAcks = new Map<string, number>();
  private clock = 0;
  private sequence = 0;
  onSend: (json: string) => void | Promise<void> = () => undefined;
  onState: () => void = () => undefined;
  onMetrics: () => void = () => undefined;

  constructor() { for (const key of this.registry.fixedKeys()) this.stamps.set(key, { counter: 0, origin: 'browser' }); }

  setLocal(key: string, value: unknown): void {
    const codec = this.registry.get(key);
    if (!codec || !codec.validate(value)) { this.metrics.rejectedMessages++; this.onMetrics(); return; }
    codec.apply(this.state, value);
    const stamp: LamportStamp = { counter: ++this.clock, origin: 'browser' };
    this.stamps.set(key, stamp);
    if (this.pending.has(key)) this.metrics.coalescedChanges++;
    this.pending.set(key, cloneChange({ key, value, stamp }));
    this.metrics.maxPendingKeys = Math.max(this.metrics.maxPendingKeys, this.pending.size);
    this.onState(); this.onMetrics();
  }

  flush(): boolean {
    if (!this.pending.size) return false;
    const changes = [...this.pending.values()].sort((a, b) => a.key.localeCompare(b.key));
    this.pending.clear();
    const id = crypto.randomUUID().replaceAll('-', '');
    this.pendingAcks.set(id, performance.now());
    this.emit(envelope('patch', ++this.sequence, { changes }, id));
    this.metrics.sentChanges += changes.length;
    return true;
  }

  ready(): void { this.emit(envelope('ready', ++this.sequence, {})); }
  ping(): void { const id = crypto.randomUUID().replaceAll('-', ''); this.pendingAcks.set(id, performance.now()); this.emit(envelope('ping', ++this.sequence, {}, id)); }

  receiveJson(json: string): void {
    try { this.receive(parseEnvelope(json)); }
    catch { this.metrics.rejectedMessages++; this.onMetrics(); }
  }

  receive(message: SyncEnvelope): void {
    this.metrics.receivedMessages++;
    switch (message.kind) {
      case 'snapshot': this.apply((message.payload as PatchPayload).changes, true); this.ack(message); break;
      case 'patch': this.apply((message.payload as PatchPayload).changes, false); this.ack(message); break;
      case 'ack': case 'pong': this.completeRoundTrip(message.correlationId); break;
      case 'ping': this.emit(envelope('pong', ++this.sequence, {}, message.correlationId)); break;
      case 'error': this.metrics.rejectedMessages++; break;
    }
    this.onMetrics();
  }

  canonicalState(): Record<string, unknown> {
    const result: Record<string, unknown> = {};
    for (const key of [...this.stamps.keys()].sort()) {
      const codec = this.registry.get(key); const stamp = this.stamps.get(key);
      if (codec && stamp) result[key] = { value: codec.read(this.state), counter: stamp.counter, origin: stamp.origin };
    }
    return result;
  }

  private apply(changes: PropertyChange[], force: boolean): void {
    if (!Array.isArray(changes)) throw new Error('Missing changes.');
    for (const change of changes) {
      const codec = this.registry.get(change.key);
      if (!codec || !change.stamp || !codec.validate(change.value)) { this.metrics.rejectedMessages++; continue; }
      this.clock = Math.max(this.clock, change.stamp.counter);
      if (!force && compareStamp(change.stamp, this.stamps.get(change.key)) <= 0) { this.metrics.ignoredChanges++; continue; }
      codec.apply(this.state, change.value); this.stamps.set(change.key, { ...change.stamp }); this.metrics.appliedChanges++;
    }
    this.onState();
  }

  private ack(message: SyncEnvelope): void { this.emit(envelope('ack', ++this.sequence, { sequence: message.sequence }, message.correlationId)); }
  private completeRoundTrip(id?: string): void { if (!id) return; const start = this.pendingAcks.get(id); if (start === undefined) return; this.pendingAcks.delete(id); this.metrics.roundTrips.push(performance.now() - start); }
  private emit(message: SyncEnvelope): void { const json = JSON.stringify(message); this.metrics.sentMessages++; this.metrics.sentBytes += new TextEncoder().encode(json).length; void this.onSend(json); this.onMetrics(); }
}

export function percentile(values: number[], p: number): number {
  if (!values.length) return 0;
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.max(0, Math.ceil(p * sorted.length) - 1)];
}
