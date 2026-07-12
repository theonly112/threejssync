export type MessageHandler = (json: string) => void;

export interface SyncTransport {
  readonly name: string;
  connect(handler: MessageHandler): Promise<void>;
  send(json: string): Promise<void>;
  dispose(): Promise<void>;
}

declare global {
  interface Window {
    CefSharp?: { PostMessage(message: unknown): void; BindObjectAsync(...names: string[]): Promise<void> };
    syncHost?: { publish(json: string): Promise<void>; subscribe(callback: (json: string) => void): Promise<void>; unsubscribe(): Promise<void> };
    __threeJsSyncReceive?: (json: string) => void;
    __threeJsSyncStartBenchmark?: (durationMs: number) => void;
    __threeJsSyncMetrics?: () => unknown;
  }
}

abstract class ScriptReceiveTransport implements SyncTransport {
  abstract readonly name: string;
  protected handler: MessageHandler = () => undefined;
  async connect(handler: MessageHandler): Promise<void> { this.handler = handler; window.__threeJsSyncReceive = handler; }
  abstract send(json: string): Promise<void>;
  async dispose(): Promise<void> { delete window.__threeJsSyncReceive; }
}

export class PostMessageTransport extends ScriptReceiveTransport {
  readonly name = 'postmessage';
  async send(json: string): Promise<void> {
    if (!window.CefSharp?.PostMessage) throw new Error('CefSharp.PostMessage is unavailable.');
    window.CefSharp.PostMessage(json);
  }
}

export class FetchScriptTransport extends ScriptReceiveTransport {
  readonly name = 'fetch';
  async send(json: string): Promise<void> {
    const response = await fetch('/sync/patch', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: json, cache: 'no-store' });
    if (!response.ok) throw new Error(`Fetch transport failed: ${response.status}`);
    const reply = await response.text();
    if (reply.trim()) this.handler(reply);
  }
}

export class BoundCallbackTransport implements SyncTransport {
  readonly name = 'bound';
  async connect(handler: MessageHandler): Promise<void> {
    if (!window.CefSharp?.BindObjectAsync) throw new Error('CefSharp binding is unavailable.');
    await window.CefSharp.BindObjectAsync('syncHost');
    if (!window.syncHost) throw new Error('syncHost was not bound.');
    await window.syncHost.subscribe(handler);
  }
  async send(json: string): Promise<void> { if (!window.syncHost) throw new Error('syncHost is unavailable.'); await window.syncHost.publish(json); }
  async dispose(): Promise<void> { if (window.syncHost) await window.syncHost.unsubscribe(); }
}

export function createTransport(name: string): SyncTransport {
  if (name === 'fetch') return new FetchScriptTransport();
  if (name === 'bound') return new BoundCallbackTransport();
  return new PostMessageTransport();
}
