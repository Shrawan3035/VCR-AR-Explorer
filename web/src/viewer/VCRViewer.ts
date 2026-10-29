/**
 * VCRViewer — framework-free three.js viewer for the generated VCR_System.glb.
 *
 * Responsibilities: loading (with progress / errors), orbit-pan-zoom camera, tap/click picking
 * that ignores drags, single-component highlight, ghost / isolate modes, exploded view,
 * numbered HTML callouts, conceptual refrigerant-flow particles and fan rotation.
 * React talks to it through the small public API at the bottom of the class.
 */
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';
import { components, flowSegments, stateColor, type ComponentInfo } from '../data';

export type VisibilityMode = 'all' | 'ghost' | 'isolate';

export interface ViewerCallbacks {
  onSelect?: (id: string | null) => void;
  onProgress?: (fraction: number) => void;
  onLoad?: () => void;
  onError?: (message: string) => void;
}

interface Part {
  info: ComponentInfo;
  root: THREE.Object3D;
  base: THREE.Vector3;
  meshes: THREE.Mesh[];
  materials: { mat: THREE.MeshStandardMaterial; opacity: number; transparent: boolean }[];
  anchorLocal: THREE.Vector3; // label anchor relative to the component root
  label: HTMLButtonElement;
}

const HIGHLIGHT = new THREE.Color('#3fb6d3');
const HOVER = new THREE.Color('#c8773a');
const DEFAULT_TARGET = new THREE.Vector3(0, 0.4, 0);
const DEFAULT_CAM = new THREE.Vector3(0.95, 0.85, 1.55);

export class VCRViewer {
  readonly renderer: THREE.WebGLRenderer;
  readonly scene = new THREE.Scene();
  readonly camera: THREE.PerspectiveCamera;
  readonly controls: OrbitControls;
  private container: HTMLElement;
  private overlay: HTMLDivElement;
  private cb: ViewerCallbacks;
  private parts = new Map<string, Part>();
  private model: THREE.Object3D | null = null;
  private rotors: THREE.Object3D[] = [];
  private raycaster = new THREE.Raycaster();
  private pointerDown: { x: number; y: number; t: number } | null = null;
  private selected: string | null = null;
  private hovered: string | null = null;
  private mode: VisibilityMode = 'all';
  private explodeT = 0;
  private showLabels = true;
  private flowOn = true;
  private flowFocus: string | null = null;
  private flowMesh: THREE.InstancedMesh | null = null;
  private flowPath: { p: THREE.Vector3; t: THREE.Vector3; seg: number; s: number }[] = [];
  private flowLen = 0;
  private flowOffset = 0;
  private heatLabels: { el: HTMLDivElement; pos: THREE.Vector3; comp: string }[] = [];
  private clock = new THREE.Clock();
  private camAnim: { from: THREE.Vector3; to: THREE.Vector3; tFrom: THREE.Vector3; tTo: THREE.Vector3; t: number } | null = null;
  private resizeObs: ResizeObserver;
  private disposed = false;
  private tmp = new THREE.Vector3();
  private grid!: THREE.GridHelper;

  constructor(container: HTMLElement, cb: ViewerCallbacks = {}) {
    this.container = container;
    this.cb = cb;
    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, preserveDrawingBuffer: true });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.05;
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.renderer.domElement.className = 'viewer-canvas';
    this.renderer.domElement.setAttribute('aria-label', '3D model of the refrigeration rig. Drag to rotate, scroll or pinch to zoom, tap a part to learn about it.');
    this.renderer.domElement.setAttribute('role', 'img');
    container.appendChild(this.renderer.domElement);

    this.overlay = document.createElement('div');
    this.overlay.className = 'viewer-overlay';
    container.appendChild(this.overlay);

    this.camera = new THREE.PerspectiveCamera(35, 1, 0.02, 30);
    this.camera.position.copy(DEFAULT_CAM);
    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.target.copy(DEFAULT_TARGET);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.08;
    this.controls.minDistance = 0.35;
    this.controls.maxDistance = 5;
    this.controls.maxPolarAngle = Math.PI * 0.62;
    this.controls.screenSpacePanning = true;
    this.controls.addEventListener('start', () => (this.camAnim = null));

    const pmrem = new THREE.PMREMGenerator(this.renderer);
    this.scene.environment = pmrem.fromScene(new RoomEnvironment(), 0.04).texture;
    pmrem.dispose();

    this.scene.add(new THREE.HemisphereLight(0xf6f3ea, 0x1a2333, 0.6));
    const sun = new THREE.DirectionalLight(0xffffff, 1.6);
    sun.position.set(1.2, 2.2, 1.6);
    sun.castShadow = true;
    sun.shadow.mapSize.set(1024, 1024);
    Object.assign(sun.shadow.camera, { left: -1, right: 1, top: 1, bottom: -1, near: 0.5, far: 6 });
    sun.shadow.bias = -0.0005;
    this.scene.add(sun);
    const ground = new THREE.Mesh(new THREE.PlaneGeometry(6, 6), new THREE.ShadowMaterial({ opacity: 0.18 }));
    ground.rotation.x = -Math.PI / 2;
    ground.receiveShadow = true;
    this.scene.add(ground);
    const grid = (this.grid = new THREE.GridHelper(4, 40, 0x8a93a3, 0xc9ccd2));
    (grid.material as THREE.Material).opacity = 0.25;
    (grid.material as THREE.Material).transparent = true;
    grid.position.y = 0.0005;
    this.scene.add(grid);

    const el = this.renderer.domElement;
    el.addEventListener('pointerdown', this.onPointerDown);
    el.addEventListener('pointerup', this.onPointerUp);
    el.addEventListener('pointermove', this.onPointerMove);
    el.addEventListener('pointerleave', () => this.setHover(null));

    this.resizeObs = new ResizeObserver(() => this.resize());
    this.resizeObs.observe(container);
    this.resize();
    this.renderer.setAnimationLoop(this.tick);
  }

  // ------------------------------------------------------------------ loading
  async load(url: string) {
    const loader = new GLTFLoader();
    try {
      const gltf = await loader.loadAsync(url, (e) => {
        if (e.total) this.cb.onProgress?.(e.loaded / e.total);
      });
      if (this.disposed) return;
      this.model = gltf.scene;
      this.scene.add(this.model);
      this.model.updateMatrixWorld(true);
      this.indexComponents();
      this.buildFlow();
      this.buildHeatLabels();
      this.applyVisuals();
      this.cb.onLoad?.();
    } catch (err) {
      console.error(err);
      this.cb.onError?.(
        `The 3D model could not be loaded (${err instanceof Error ? err.message : String(err)}). Check your connection and reload, or use the component library instead.`,
      );
    }
  }

  private indexComponents() {
    const missing: string[] = [];
    for (const info of components) {
      const root = this.model!.getObjectByName(info.node);
      if (!root) {
        missing.push(info.node);
        continue;
      }
      const meshes: THREE.Mesh[] = [];
      const materials: Part['materials'] = [];
      root.traverse((o) => {
        if ((o as THREE.Mesh).isMesh) {
          const m = o as THREE.Mesh;
          m.castShadow = true;
          m.receiveShadow = true;
          // clone materials so highlighting one component never affects another
          const mat = (m.material as THREE.MeshStandardMaterial).clone();
          m.material = mat;
          materials.push({ mat, opacity: mat.opacity, transparent: mat.transparent });
          m.userData.componentId = info.id;
          meshes.push(m);
        }
        if (o.name.endsWith('_Rotor')) this.rotors.push(o);
      });
      const box = new THREE.Box3().setFromObject(root);
      const anchorWorld = new THREE.Vector3((box.min.x + box.max.x) / 2, box.max.y, (box.min.z + box.max.z) / 2);
      if (info.line) box.getCenter(anchorWorld);
      const anchorLocal = root.worldToLocal(anchorWorld.clone());

      const label = document.createElement('button');
      label.type = 'button';
      label.className = 'callout' + (info.main ? ' callout-main' : '');
      label.innerHTML = `<span class="callout-num">${info.number}</span><span class="callout-name">${info.name}</span>`;
      label.setAttribute('aria-label', `${info.number}. ${info.name}`);
      label.addEventListener('click', (e) => {
        e.stopPropagation();
        this.select(info.id, true);
      });
      label.addEventListener('pointerenter', () => this.setHover(info.id));
      label.addEventListener('pointerleave', () => this.setHover(null));
      this.overlay.appendChild(label);
      this.parts.set(info.id, { info, root, base: root.position.clone(), meshes, materials, anchorLocal, label });
    }
    if (missing.length) console.warn('Model is missing component nodes:', missing);
  }

  // ------------------------------------------------------------------ flow animation
  private buildFlow() {
    // concatenate all segments into one closed loop sampled at even spacing
    const pts: { p: THREE.Vector3; seg: number }[] = [];
    flowSegments.forEach((s, i) => s.points.forEach((p) => pts.push({ p: new THREE.Vector3(...p), seg: i })));
    let s = 0;
    this.flowPath = pts.map((q, i) => {
      if (i > 0) s += q.p.distanceTo(pts[i - 1].p);
      const next = pts[Math.min(i + 1, pts.length - 1)].p;
      return { p: q.p, t: next.clone().sub(q.p).normalize(), seg: q.seg, s };
    });
    this.flowLen = s;
    const count = Math.floor(this.flowLen / 0.045);
    const geo = new THREE.ConeGeometry(0.0075, 0.022, 8);
    geo.rotateX(Math.PI / 2); // point along +Z
    const mat = new THREE.MeshBasicMaterial({ depthTest: false, transparent: true, opacity: 0.95, toneMapped: false });
    this.flowMesh = new THREE.InstancedMesh(geo, mat, count);
    this.flowMesh.renderOrder = 10;
    this.flowMesh.frustumCulled = false;
    for (let i = 0; i < count; i++) this.flowMesh.setColorAt(i, new THREE.Color('#ffffff'));
    this.scene.add(this.flowMesh);
  }

  private sampleFlow(d: number) {
    d = ((d % this.flowLen) + this.flowLen) % this.flowLen;
    const path = this.flowPath;
    let lo = 0,
      hi = path.length - 1;
    while (hi - lo > 1) {
      const mid = (lo + hi) >> 1;
      if (path[mid].s <= d) lo = mid;
      else hi = mid;
    }
    const a = path[lo],
      b = path[hi];
    const f = b.s > a.s ? (d - a.s) / (b.s - a.s) : 0;
    return { p: a.p.clone().lerp(b.p, f), t: b.p.clone().sub(a.p).normalize(), seg: a.seg };
  }

  private updateFlow(dt: number) {
    const mesh = this.flowMesh;
    if (!mesh) return;
    mesh.visible = this.flowOn && this.explodeT < 0.05;
    if (!mesh.visible) return;
    this.flowOffset += dt * 0.12;
    const m = new THREE.Matrix4();
    const q = new THREE.Quaternion();
    const z = new THREE.Vector3(0, 0, 1);
    const col = new THREE.Color();
    const spacing = this.flowLen / mesh.count;
    for (let i = 0; i < mesh.count; i++) {
      const smp = this.sampleFlow(i * spacing + this.flowOffset);
      const seg = flowSegments[smp.seg];
      const focus = !this.flowFocus || seg.component === this.flowFocus;
      q.setFromUnitVectors(z, smp.t);
      const sc = focus ? (this.flowFocus ? 1.35 : 1) : 0.6;
      m.compose(smp.p, q, new THREE.Vector3(sc, sc, sc));
      mesh.setMatrixAt(i, m);
      col.set(stateColor[seg.state]);
      if (!focus) col.lerp(new THREE.Color('#9aa3b2'), 0.7);
      mesh.setColorAt(i, col);
    }
    mesh.instanceMatrix.needsUpdate = true;
    if (mesh.instanceColor) mesh.instanceColor.needsUpdate = true;
  }

  private buildHeatLabels() {
    const mk = (comp: string, text: string, cls: string) => {
      const p = this.parts.get(comp);
      if (!p) return;
      const el = document.createElement('div');
      el.className = `heat-badge ${cls}`;
      el.textContent = text;
      el.setAttribute('aria-hidden', 'true');
      this.overlay.appendChild(el);
      const box = new THREE.Box3().setFromObject(p.root);
      const c = box.getCenter(new THREE.Vector3());
      c.x += comp === 'condenser' ? -0.24 : 0.24;
      this.heatLabels.push({ el, pos: c, comp });
    };
    mk('condenser', 'Q out · heat rejected', 'heat-out');
    mk('evaporator', 'Q in · heat absorbed', 'heat-in');
  }

  // ------------------------------------------------------------------ input
  private ndc(e: PointerEvent) {
    const r = this.renderer.domElement.getBoundingClientRect();
    return new THREE.Vector2(((e.clientX - r.left) / r.width) * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1);
  }

  private pick(e: PointerEvent): string | null {
    if (!this.model) return null;
    this.raycaster.setFromCamera(this.ndc(e), this.camera);
    const targets: THREE.Object3D[] = [];
    this.parts.forEach((p) => p.root.visible && targets.push(p.root));
    const hits = this.raycaster.intersectObjects(targets, true);
    for (const h of hits) {
      const id = h.object.userData.componentId as string | undefined;
      if (!id) continue;
      // ghosted parts can't be picked so you can click "through" them
      if (this.mode === 'ghost' && this.selected && id !== this.selected) continue;
      return id;
    }
    return null;
  }

  private onPointerDown = (e: PointerEvent) => {
    this.pointerDown = { x: e.clientX, y: e.clientY, t: performance.now() };
  };

  private onPointerUp = (e: PointerEvent) => {
    const d = this.pointerDown;
    this.pointerDown = null;
    if (!d) return;
    const moved = Math.hypot(e.clientX - d.x, e.clientY - d.y);
    const slop = e.pointerType === 'touch' ? 12 : 6;
    if (moved > slop || performance.now() - d.t > 600) return; // it was a drag, not a tap
    const id = this.pick(e);
    this.select(id, true);
  };

  private onPointerMove = (e: PointerEvent) => {
    if (e.pointerType !== 'mouse' || e.buttons) return;
    this.setHover(this.pick(e));
  };

  private setHover(id: string | null) {
    if (id === this.hovered) return;
    this.hovered = id;
    this.renderer.domElement.style.cursor = id ? 'pointer' : 'grab';
    this.applyVisuals();
  }

  // ------------------------------------------------------------------ visuals
  private applyVisuals() {
    this.parts.forEach((p, id) => {
      const sel = id === this.selected;
      const others = this.selected && !sel;
      p.root.visible = !(others && this.mode === 'isolate');
      const ghost = !!others && this.mode === 'ghost';
      const lineFade = p.info.line ? Math.max(0, 1 - this.explodeT * 2.5) : 1;
      if (p.info.line) p.root.visible = p.root.visible && lineFade > 0.02;
      for (const m of p.materials) {
        const a = (ghost ? 0.12 : 1) * lineFade * m.opacity;
        m.mat.transparent = m.transparent || a < 0.999;
        m.mat.opacity = a;
        m.mat.depthWrite = a > 0.5;
        m.mat.emissive.copy(sel ? HIGHLIGHT : id === this.hovered ? HOVER : new THREE.Color(0));
        m.mat.emissiveIntensity = sel ? 0.55 : 0.35;
        m.mat.needsUpdate = true;
      }
      p.label.classList.toggle('is-selected', sel);
      p.label.classList.toggle('is-dim', !!others);
    });
  }

  private updateLabels() {
    const w = this.container.clientWidth,
      h = this.container.clientHeight;
    const camDir = this.camera.getWorldDirection(new THREE.Vector3());
    this.parts.forEach((p) => {
      const show = this.showLabels && p.root.visible && (!p.info.line || this.explodeT < 0.2 || p.info.id === this.selected);
      const world = p.root.localToWorld(this.tmp.copy(p.anchorLocal));
      const behind = world.clone().sub(this.camera.position).dot(camDir) < 0;
      const v = world.project(this.camera);
      if (!show || behind) {
        p.label.style.display = 'none';
        return;
      }
      p.label.style.display = '';
      p.label.style.transform = `translate(${((v.x + 1) / 2) * w}px, ${((1 - v.y) / 2) * h}px) translate(-50%, -100%)`;
    });
    for (const hl of this.heatLabels) {
      const vis = this.flowOn && this.explodeT < 0.05 && (!this.selected || this.mode === 'all' || this.selected === hl.comp);
      const v = hl.pos.clone().project(this.camera);
      hl.el.style.display = vis ? '' : 'none';
      hl.el.style.transform = `translate(${((v.x + 1) / 2) * w}px, ${((1 - v.y) / 2) * h}px) translate(-50%, -50%)`;
    }
  }

  private tick = () => {
    const dt = Math.min(this.clock.getDelta(), 0.05);
    if (this.camAnim) {
      const a = this.camAnim;
      a.t = Math.min(1, a.t + dt / 0.7);
      const e = 1 - Math.pow(1 - a.t, 3);
      this.camera.position.lerpVectors(a.from, a.to, e);
      this.controls.target.lerpVectors(a.tFrom, a.tTo, e);
      if (a.t >= 1) this.camAnim = null;
    }
    this.controls.update();
    if (this.flowOn) for (const r of this.rotors) r.rotation.z += dt * 9;
    this.updateFlow(dt);
    if (this.selected) {
      const p = this.parts.get(this.selected);
      const k = 0.45 + 0.2 * Math.sin(performance.now() / 260);
      p?.materials.forEach((m) => (m.mat.emissiveIntensity = k));
    }
    this.renderer.render(this.scene, this.camera);
    this.updateLabels();
  };

  private resize() {
    const w = Math.max(1, this.container.clientWidth),
      h = Math.max(1, this.container.clientHeight);
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w / h;
    // keep the whole rig in frame on narrow portrait screens
    this.camera.fov = w / h < 0.8 ? 50 : 35;
    this.camera.updateProjectionMatrix();
  }

  private animateCamera(pos: THREE.Vector3, target: THREE.Vector3) {
    this.camAnim = { from: this.camera.position.clone(), to: pos, tFrom: this.controls.target.clone(), tTo: target, t: 0 };
  }

  // ------------------------------------------------------------------ public API
  select(id: string | null, notify = false) {
    if (id && !this.parts.has(id)) id = null;
    this.selected = id;
    this.applyVisuals();
    if (notify) this.cb.onSelect?.(id);
  }

  getSelected() {
    return this.selected;
  }

  setMode(mode: VisibilityMode) {
    this.mode = mode;
    this.applyVisuals();
  }

  setExplode(t: number) {
    this.explodeT = THREE.MathUtils.clamp(t, 0, 1);
    this.parts.forEach((p) => {
      const [x, y, z] = p.info.explode;
      p.root.position.copy(p.base).add(new THREE.Vector3(x, y, z).multiplyScalar(this.explodeT));
    });
    this.applyVisuals();
  }

  setLabels(on: boolean) {
    this.showLabels = on;
  }

  setFlow(on: boolean) {
    this.flowOn = on;
  }

  setFlowFocus(componentId: string | null) {
    this.flowFocus = componentId;
  }

  focus(id: string) {
    const p = this.parts.get(id);
    if (!p) return;
    const box = new THREE.Box3().setFromObject(p.root);
    const c = box.getCenter(new THREE.Vector3());
    const size = box.getSize(new THREE.Vector3()).length();
    const dir = this.camera.position.clone().sub(this.controls.target).normalize();
    const dist = THREE.MathUtils.clamp(size * 2.4, 0.5, 2.2);
    this.animateCamera(c.clone().add(dir.multiplyScalar(dist)), c);
  }

  setGrid(on: boolean) {
    this.grid.visible = on;
  }

  /** Jump the camera immediately (used by deep links and the capture script). */
  setCamera(pos: [number, number, number], target: [number, number, number]) {
    this.camAnim = null;
    this.camera.position.set(...pos);
    this.controls.target.set(...target);
    this.controls.update();
  }

  /** Screen-space (CSS px) label anchors and centres of every component, for poster callouts. */
  getScreenAnchors() {
    this.renderer.render(this.scene, this.camera);
    const w = this.container.clientWidth,
      h = this.container.clientHeight;
    const proj = (v: THREE.Vector3) => {
      const p = v.clone().project(this.camera);
      return [Math.round(((p.x + 1) / 2) * w), Math.round(((1 - p.y) / 2) * h)];
    };
    const out: Record<string, { anchor: number[]; centre: number[]; visible: boolean }> = {};
    this.parts.forEach((p, id) => {
      const box = new THREE.Box3().setFromObject(p.root);
      out[id] = { anchor: proj(p.root.localToWorld(p.anchorLocal.clone())), centre: proj(box.getCenter(new THREE.Vector3())), visible: p.root.visible };
    });
    return { width: w, height: h, parts: out };
  }

  resetView() {
    this.animateCamera(DEFAULT_CAM.clone(), DEFAULT_TARGET.clone());
  }

  zoom(factor: number) {
    const off = this.camera.position.clone().sub(this.controls.target).multiplyScalar(factor);
    const len = THREE.MathUtils.clamp(off.length(), this.controls.minDistance, this.controls.maxDistance);
    this.animateCamera(this.controls.target.clone().add(off.setLength(len)), this.controls.target.clone());
  }

  dispose() {
    this.disposed = true;
    this.renderer.setAnimationLoop(null);
    this.resizeObs.disconnect();
    this.controls.dispose();
    this.renderer.dispose();
    this.renderer.domElement.remove();
    this.overlay.remove();
  }
}
