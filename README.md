# ThreeJsSync

A runnable WPF prototype for low-latency, two-way state synchronization between a three.js object in CefSharp and a .NET Framework host object. One application compares three transports while using the same state model, conflict rules, batching, and benchmark.

## What is included

- WPF host targeting .NET Framework 4.7.2 and x64
- CefSharp.Wpf 149.0.60 and a locally bundled three.js 0.185.1 frontend
- Host and browser property editors plus three.js `TransformControls`
- Field-level last-write-wins using Lamport stamps
- Per-frame, property-keyed coalescing with no unbounded message queue
- Full snapshot recovery after navigation or transport changes
- Three transport implementations:

| Transport | Browser → host | Host → browser | Notes |
| --- | --- | --- | --- |
| `postmessage` | `CefSharp.PostMessage` | `ExecuteScriptAsync` | Recommended default: small API and lifecycle surface |
| `bound` | Async bound method | Persistent `IJavascriptCallback` | Native IPC in both directions; callback must be replaced after navigation |
| `fetch` | Intercepted same-origin `fetch` | `ExecuteScriptAsync` | Familiar web API, but pays request/response overhead |

No listener, local port, or external network access is used at runtime. The application intercepts the fixed origin `https://threejssync.local`, serves bundled assets, and handles the fetch endpoint inside CefSharp.

## Prerequisites

- Windows with .NET Framework 4.7.2 or newer
- Visual C++ 2022 x64 runtime (required by current CefSharp releases)
- Node.js 22+ and npm
- A .NET SDK capable of building SDK-style `net472` projects; `global.json` selects the installed .NET 10.0.300 SDK

## Build and run

```powershell
cd web
npm ci
npm test
cd ..
dotnet test tests\ThreeJsSync.Core.Tests\ThreeJsSync.Core.Tests.csproj -c Debug
dotnet build ThreeJsSync.sln -c Debug -p:Platform=x64
.\src\ThreeJsSync.Host\bin\x64\Debug\net472\win-x64\ThreeJsSync.Host.exe
```

The .NET host build runs the Vite production build and copies `web/dist` into the executable output. The application does not need Node.js or internet access after it is built.

For an automated startup smoke test of a specific bridge, pass one of:

```powershell
ThreeJsSync.Host.exe --transport=postmessage
ThreeJsSync.Host.exe --transport=bound
ThreeJsSync.Host.exe --transport=fetch
```

The selector in the application disposes the current bridge, reloads the browser, and initializes the selected bridge from a fresh host snapshot.

## Synchronization model

The canonical wire contract is described by [`protocol/protocol.schema.json`](protocol/protocol.schema.json). Every envelope carries a protocol version, kind, object ID, origin, sequence, optional correlation ID, and payload. The supported message kinds are `ready`, `snapshot`, `patch`, `ack`, `ping`, `pong`, and `error`.

State is synchronized by typed keys rather than by reflecting a three.js `Object3D`:

- `position`, `quaternion`, `scale`
- `visible`, `name`
- `material.color`, `material.opacity`
- `metadata.<key>`

Each property has its own `{ counter, origin }` Lamport stamp. A larger counter wins; equal counters use the origin string as a deterministic tie-breaker. Independent properties therefore merge without conflict. Remote application is suppressed from local change capture, so acknowledgements cannot create echo loops.

The host flushes dirty keys on a 16 ms dispatcher timer, and the browser flushes in `requestAnimationFrame`. Updating one property repeatedly replaces its pending value instead of growing a queue. Messages over 64 KiB and invalid fields, numbers, colors, quaternions, scales, object IDs, or protocol versions are rejected.

To add an application property, register matching `PropertyCodec` implementations in the C# and TypeScript `PropertyRegistry` instances. A codec validates, reads, and applies one typed value. Add a shared golden JSON fixture when introducing a new wire representation.

## Benchmark

Click **Run 5 s warm-up + 30 s benchmark**. Both peers generate approximately 60 updates per second on different properties while correlated pings and patch acknowledgements measure round-trip time without clock synchronization. After a two-second drain, the result reports:

- sent/received messages and changes
- bytes and coalesced changes
- p50, p95, and p99 RTT from both peers
- rejects and maximum pending keys
- canonical state convergence

A run passes only when there are no new rejects, pending work is drained, and the browser and host canonical states—including per-property stamps—are equal. Latency is reported rather than compared with a machine-dependent hard threshold.

During the final implementation smoke test, the PostMessage prototype completed the full workload with 2,298 measured messages each way, zero rejects, exact canonical convergence, host p95 RTT of 5.45 ms, and browser p95 RTT of 12.20 ms. Treat these numbers as a development-machine result, not a portable performance guarantee.

## Project map

- `src/ThreeJsSync.Core`: protocol, state model, validation, merge engine, metrics, and transport contract
- `src/ThreeJsSync.Host`: WPF application, CefSharp bridges, local resource handling, and benchmark runner
- `web`: three.js editor, browser merge engine, transport adapters, and Vitest tests
- `tests/ThreeJsSync.Core.Tests`: .NET merge/protocol tests
- `protocol`: JSON schema and cross-language fixture

## Production guidance

Start with PostMessage + script injection unless measurements in the target application favor another bridge. Keep script injection to one stable receive function and never use `EvaluateScriptAsync` for state updates. The bound-object prototype requires `CefSharpSettings.ConcurrentTaskExecution = true` for its task-returning methods and must dispose its callback on navigation. The fetch handler runs on CEF's IO thread, so it only validates/copies the request and dispatches state work to WPF asynchronously.
