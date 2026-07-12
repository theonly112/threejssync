import type { PropertyChange } from './protocol';

export interface Vector3Value { x: number; y: number; z: number }
export interface QuaternionValue extends Vector3Value { w: number }

export interface ObjectState {
  position: Vector3Value;
  quaternion: QuaternionValue;
  scale: Vector3Value;
  visible: boolean;
  name: string;
  materialColor: string;
  materialOpacity: number;
  metadata: Record<string, string | number | boolean | null>;
}

export type Validator = (value: unknown) => boolean;
export interface PropertyCodec {
  validate: Validator;
  read: (state: ObjectState) => unknown;
  apply: (state: ObjectState, value: unknown) => void;
}

const finite = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value);
const vector = (value: unknown): value is Vector3Value => {
  const v = value as Vector3Value;
  return !!v && finite(v.x) && finite(v.y) && finite(v.z);
};

export class PropertyRegistry {
  private readonly codecs = new Map<string, PropertyCodec>();

  constructor() {
    this.register('position', { validate: vector, read: s => ({ ...s.position }), apply: (s, v) => s.position = { ...(v as Vector3Value) } });
    this.register('quaternion', {
      validate: value => {
        const q = value as QuaternionValue;
        if (!vector(q) || !finite(q.w)) return false;
        const length = q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w;
        return length > 1e-12 && Math.abs(length - 1) < 0.02;
      },
      read: s => ({ ...s.quaternion }), apply: (s, v) => s.quaternion = { ...(v as QuaternionValue) },
    });
    this.register('scale', { validate: v => vector(v) && Math.abs(v.x) > 1e-9 && Math.abs(v.y) > 1e-9 && Math.abs(v.z) > 1e-9, read: s => ({ ...s.scale }), apply: (s, v) => s.scale = { ...(v as Vector3Value) } });
    this.register('visible', { validate: v => typeof v === 'boolean', read: s => s.visible, apply: (s, v) => s.visible = v as boolean });
    this.register('name', { validate: v => typeof v === 'string' && v.length <= 256, read: s => s.name, apply: (s, v) => s.name = v as string });
    this.register('material.color', { validate: v => typeof v === 'string' && /^#[0-9a-f]{6}$/i.test(v), read: s => s.materialColor, apply: (s, v) => s.materialColor = v as string });
    this.register('material.opacity', { validate: v => finite(v) && v >= 0 && v <= 1, read: s => s.materialOpacity, apply: (s, v) => s.materialOpacity = v as number });
  }

  register(key: string, codec: PropertyCodec): void { this.codecs.set(key, codec); }
  fixedKeys(): string[] { return [...this.codecs.keys()]; }

  get(key: string): PropertyCodec | undefined {
    const fixed = this.codecs.get(key);
    if (fixed) return fixed;
    if (!/^metadata\.[\w.-]{1,128}$/.test(key)) return undefined;
    const metadataKey = key.slice(9);
    return {
      validate: v => (v === null || ['string', 'number', 'boolean'].includes(typeof v)) && JSON.stringify(v).length <= 4096,
      read: s => s.metadata[metadataKey] ?? null,
      apply: (s, v) => s.metadata[metadataKey] = v as string | number | boolean | null,
    };
  }
}

export function defaultState(): ObjectState {
  return {
    position: { x: 0, y: 0, z: 0 }, quaternion: { x: 0, y: 0, z: 0, w: 1 }, scale: { x: 1, y: 1, z: 1 },
    visible: true, name: 'Synchronized cube', materialColor: '#4f8cff', materialOpacity: 1, metadata: {},
  };
}

export function cloneChange(change: PropertyChange): PropertyChange {
  return JSON.parse(JSON.stringify(change)) as PropertyChange;
}

