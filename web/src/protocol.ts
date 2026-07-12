export const PROTOCOL_VERSION = 1;
export const OBJECT_ID = 'demo-cube';
export const MAX_MESSAGE_BYTES = 64 * 1024;

export type MessageKind = 'ready' | 'snapshot' | 'patch' | 'ack' | 'ping' | 'pong' | 'error';
export type Origin = 'host' | 'browser';

export interface LamportStamp {
  counter: number;
  origin: Origin;
}

export interface PropertyChange {
  key: string;
  value: unknown;
  stamp: LamportStamp;
}

export interface PatchPayload {
  changes: PropertyChange[];
}

export interface SyncEnvelope {
  protocolVersion: number;
  kind: MessageKind;
  objectId: string;
  origin: Origin;
  sequence: number;
  correlationId?: string;
  payload: unknown;
}

export function parseEnvelope(json: string): SyncEnvelope {
  if (new TextEncoder().encode(json).length > MAX_MESSAGE_BYTES) throw new Error('Message exceeds 64 KiB.');
  const value = JSON.parse(json) as Partial<SyncEnvelope>;
  const kinds: MessageKind[] = ['ready', 'snapshot', 'patch', 'ack', 'ping', 'pong', 'error'];
  if (value.protocolVersion !== PROTOCOL_VERSION) throw new Error('Unsupported protocol version.');
  if (!value.kind || !kinds.includes(value.kind)) throw new Error('Unknown message kind.');
  if (value.objectId !== OBJECT_ID) throw new Error('Unexpected object id.');
  if (value.origin !== 'host' && value.origin !== 'browser') throw new Error('Unexpected origin.');
  if (!Number.isSafeInteger(value.sequence) || value.sequence! < 0) throw new Error('Invalid sequence.');
  return value as SyncEnvelope;
}

export function compareStamp(left: LamportStamp, right?: LamportStamp): number {
  if (!right) return 1;
  if (left.counter !== right.counter) return left.counter - right.counter;
  return left.origin.localeCompare(right.origin);
}

export function envelope(kind: MessageKind, sequence: number, payload: unknown, correlationId?: string): SyncEnvelope {
  return { protocolVersion: PROTOCOL_VERSION, kind, objectId: OBJECT_ID, origin: 'browser', sequence, correlationId, payload };
}

