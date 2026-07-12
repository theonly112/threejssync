import { describe, expect, it, vi } from 'vitest';
import { compareStamp, envelope, parseEnvelope, type PropertyChange } from '../src/protocol';
import { BrowserSyncEngine } from '../src/syncEngine';
import golden from '../../protocol/golden-envelope.json';

describe('browser synchronization engine', () => {
  it('uses a deterministic Lamport origin tie-breaker', () => {
    expect(compareStamp({ counter: 3, origin: 'host' }, { counter: 3, origin: 'browser' })).toBeGreaterThan(0);
  });

  it('coalesces repeated changes per property', () => {
    const engine = new BrowserSyncEngine();
    const sent: string[] = []; engine.onSend = json => { sent.push(json); };
    engine.setLocal('position', { x: 1, y: 0, z: 0 });
    engine.setLocal('position', { x: 2, y: 0, z: 0 });
    expect(engine.metrics.coalescedChanges).toBe(1);
    expect(engine.flush()).toBe(true);
    expect((JSON.parse(sent[0]).payload.changes as PropertyChange[])).toHaveLength(1);
  });

  it('applies independent fields and ignores an older same-field patch', () => {
    const engine = new BrowserSyncEngine();
    const patch = (key: string, value: unknown, counter: number, sequence: number) => envelope('patch', sequence, { changes: [{ key, value, stamp: { counter, origin: 'host' } }] });
    engine.receive(patch('name', 'new', 4, 1)); engine.receive(patch('name', 'old', 3, 2)); engine.receive(patch('visible', false, 1, 3));
    expect(engine.state.name).toBe('new'); expect(engine.state.visible).toBe(false); expect(engine.metrics.ignoredChanges).toBe(1);
  });

  it('rejects malformed envelopes and invalid property values', () => {
    expect(() => parseEnvelope('{}')).toThrow();
    const engine = new BrowserSyncEngine(); engine.setLocal('material.opacity', 2); expect(engine.metrics.rejectedMessages).toBe(1);
  });

  it('reads the shared golden envelope', () => {
    const parsed = parseEnvelope(JSON.stringify(golden));
    expect(parsed.sequence).toBe(42);
    expect((parsed.payload as { changes: PropertyChange[] }).changes[1].value).toBe('#4f8cff');
  });

  it('does not emit a patch while applying remote state', () => {
    const engine = new BrowserSyncEngine(); const send = vi.fn(); engine.onSend = send;
    engine.receive(envelope('snapshot', 1, { changes: [{ key: 'visible', value: false, stamp: { counter: 2, origin: 'host' } }] }));
    expect(engine.state.visible).toBe(false); expect(engine.flush()).toBe(false);
    expect(send).toHaveBeenCalledTimes(1); // acknowledgement only
  });
});
