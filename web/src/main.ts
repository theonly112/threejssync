import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { TransformControls } from 'three/addons/controls/TransformControls.js';
import './style.css';
import { BrowserSyncEngine, percentile } from './syncEngine';
import { createTransport } from './transports';

const root = document.querySelector<HTMLDivElement>('#app')!;
root.innerHTML = `
  <div class="shell">
    <header><div><span class="eyebrow">CEFSharp bridge laboratory</span><h1>Three.js synchronized object</h1></div><div id="status" class="status">Connecting…</div></header>
    <main>
      <section class="viewport"><div id="canvas"></div><div class="hint">Drag the gizmo to publish transform changes</div></section>
      <aside>
        <div class="card"><h2>Browser properties</h2>
          <label>Name <input id="name" type="text" /></label>
          <div class="row"><label>Color <input id="color" type="color" /></label><label>Opacity <input id="opacity" type="range" min="0" max="1" step="0.01" /></label></div>
          <label class="check"><input id="visible" type="checkbox" /> Visible</label>
          <div class="triple" data-vector="position"><label>X<input id="px" type="number" step="0.05" /></label><label>Y<input id="py" type="number" step="0.05" /></label><label>Z<input id="pz" type="number" step="0.05" /></label></div>
          <label>Metadata note <input id="note" type="text" /></label>
        </div>
        <div class="card metrics"><h2>Live metrics</h2><pre id="metrics"></pre></div>
      </aside>
    </main>
  </div>`;

const engine = new BrowserSyncEngine();
const transportName = new URLSearchParams(location.search).get('transport') ?? 'postmessage';
const transport = createTransport(transportName);
const status = document.querySelector<HTMLDivElement>('#status')!;
const metricsElement = document.querySelector<HTMLPreElement>('#metrics')!;

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x0b1020);
const camera = new THREE.PerspectiveCamera(45, 1, 0.1, 100);
camera.position.set(5, 4, 7);
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
renderer.outputColorSpace = THREE.SRGBColorSpace;
document.querySelector('#canvas')!.appendChild(renderer.domElement);
const material = new THREE.MeshStandardMaterial({ color: engine.state.materialColor, transparent: true });
const cube = new THREE.Mesh(new THREE.BoxGeometry(1.6, 1.6, 1.6), material);
scene.add(cube);
scene.add(new THREE.GridHelper(12, 24, 0x29416b, 0x17233c));
scene.add(new THREE.HemisphereLight(0xd9e8ff, 0x16203c, 2.2));
const directional = new THREE.DirectionalLight(0xffffff, 2.4); directional.position.set(4, 6, 3); scene.add(directional);
const orbit = new OrbitControls(camera, renderer.domElement); orbit.enableDamping = true;
const transform = new TransformControls(camera, renderer.domElement); transform.attach(cube); scene.add(transform.getHelper());
let applyingSceneState = false;
transform.addEventListener('dragging-changed', event => orbit.enabled = !event.value);
transform.addEventListener('objectChange', () => {
  if (applyingSceneState) return;
  engine.setLocal('position', { x: cube.position.x, y: cube.position.y, z: cube.position.z });
  engine.setLocal('quaternion', { x: cube.quaternion.x, y: cube.quaternion.y, z: cube.quaternion.z, w: cube.quaternion.w });
  engine.setLocal('scale', { x: cube.scale.x, y: cube.scale.y, z: cube.scale.z });
});

const input = <T extends HTMLInputElement>(id: string) => document.querySelector<T>(`#${id}`)!;
const inputs = { name: input('name'), color: input('color'), opacity: input('opacity'), visible: input('visible'), px: input('px'), py: input('py'), pz: input('pz'), note: input('note') };

function renderState(): void {
  const s = engine.state;
  applyingSceneState = true;
  try {
    cube.position.set(s.position.x, s.position.y, s.position.z);
    cube.quaternion.set(s.quaternion.x, s.quaternion.y, s.quaternion.z, s.quaternion.w);
    cube.scale.set(s.scale.x, s.scale.y, s.scale.z); cube.visible = s.visible; cube.name = s.name;
    material.color.set(s.materialColor); material.opacity = s.materialOpacity; material.visible = s.visible;
  } finally { applyingSceneState = false; }
  inputs.name.value = s.name; inputs.color.value = s.materialColor; inputs.opacity.value = String(s.materialOpacity); inputs.visible.checked = s.visible;
  inputs.px.value = s.position.x.toFixed(2); inputs.py.value = s.position.y.toFixed(2); inputs.pz.value = s.position.z.toFixed(2);
  inputs.note.value = String(s.metadata.note ?? '');
}

function renderMetrics(): void {
  const m = engine.metrics;
  metricsElement.textContent = [
    `transport       ${transport.name}`, `sent / received ${m.sentMessages} / ${m.receivedMessages}`,
    `applied / stale ${m.appliedChanges} / ${m.ignoredChanges}`, `rejected        ${m.rejectedMessages}`,
    `coalesced       ${m.coalescedChanges}`, `max pending     ${m.maxPendingKeys}`, `bytes sent      ${m.sentBytes}`,
    `RTT p50/p95/p99 ${percentile(m.roundTrips, .5).toFixed(2)} / ${percentile(m.roundTrips, .95).toFixed(2)} / ${percentile(m.roundTrips, .99).toFixed(2)} ms`,
  ].join('\n');
}
engine.onState = renderState; engine.onMetrics = renderMetrics; engine.onSend = json => transport.send(json);

inputs.name.addEventListener('input', () => engine.setLocal('name', inputs.name.value));
inputs.color.addEventListener('input', () => engine.setLocal('material.color', inputs.color.value));
inputs.opacity.addEventListener('input', () => engine.setLocal('material.opacity', Number(inputs.opacity.value)));
inputs.visible.addEventListener('change', () => engine.setLocal('visible', inputs.visible.checked));
inputs.note.addEventListener('input', () => engine.setLocal('metadata.note', inputs.note.value));
for (const key of ['px', 'py', 'pz'] as const) inputs[key].addEventListener('input', () => engine.setLocal('position', { x: Number(inputs.px.value), y: Number(inputs.py.value), z: Number(inputs.pz.value) }));

function resize(): void { const container = document.querySelector<HTMLElement>('#canvas')!; const { clientWidth, clientHeight } = container; renderer.setSize(clientWidth, clientHeight, false); camera.aspect = clientWidth / Math.max(1, clientHeight); camera.updateProjectionMatrix(); }
new ResizeObserver(resize).observe(document.querySelector('#canvas')!);
function frame(): void { requestAnimationFrame(frame); engine.flush(); orbit.update(); renderer.render(scene, camera); }

window.__threeJsSyncStartBenchmark = (durationMs: number) => {
  const started = performance.now(); let frameNumber = 0;
  const step = () => {
    const elapsed = performance.now() - started;
    engine.setLocal('position', { ...engine.state.position, y: Math.sin(elapsed / 120) * 1.5 });
    if (++frameNumber % 10 === 0) engine.ping();
    if (elapsed < durationMs) requestAnimationFrame(step);
  };
  requestAnimationFrame(step);
};
window.__threeJsSyncMetrics = () => ({ ...engine.metrics, p50: percentile(engine.metrics.roundTrips, .5), p95: percentile(engine.metrics.roundTrips, .95), p99: percentile(engine.metrics.roundTrips, .99), canonicalState: engine.canonicalState() });

void transport.connect(json => engine.receiveJson(json)).then(() => { status.textContent = `Connected · ${transport.name}`; status.classList.add('connected'); engine.ready(); }).catch(error => { status.textContent = String(error); status.classList.add('error'); });
window.addEventListener('beforeunload', () => void transport.dispose());
renderState(); renderMetrics(); resize(); frame();
