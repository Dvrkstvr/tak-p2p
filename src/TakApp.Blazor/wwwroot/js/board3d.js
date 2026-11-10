// The 3D board (Board3D.razor). A view only: it draws the scene the table sends and reports clicks on squares back to .NET,
// which owns every gesture and sends every move to the engine. three.js r170 is vendored in ../lib/three (MIT).
import * as THREE from '../lib/three/three.module.min.js';

const FLAT_H = 0.12;
const MARGIN = 0.6;
const CLICK_SLOP = 7; // px a pointer may travel and still count as a click
const LONG_PRESS_MS = 480;

const COLORS = {
  white: 0xefe4cf,
  black: 0x2b2421,
  accent: 0xf0a83c,
  slide: 0xffc66b,
  road: 0xffcf4a,
  inspect: 0x6aa8ff,
  last: 0xffffff,
  wood: 0xa8774a,
  woodSide: 0x6e4a2c,
};

let shared = null;
function sharedAssets() {
  if (shared) return shared;
  const slab = roundedSlab(0.74, 0.74, FLAT_H - 0.04, 0.1, 0.02);
  const wall = roundedSlab(0.74, 0.17, 0.62, 0.06, 0.02, true);
  const capProfile = [
    [0, 0], [0.31, 0], [0.33, 0.025], [0.32, 0.07], [0.27, 0.2], [0.255, 0.4],
    [0.27, 0.5], [0.3, 0.56], [0.3, 0.6], [0.27, 0.645], [0.12, 0.665], [0, 0.668],
  ].map(([x, y]) => new THREE.Vector2(x, y));
  const cap = new THREE.LatheGeometry(capProfile, 48);
  cap.computeVertexNormals();
  const mat = (color, rough) => new THREE.MeshPhysicalMaterial({ color, roughness: rough, metalness: 0, clearcoat: 0.35, clearcoatRoughness: 0.4 });
  shared = {
    geo: { F: slab, S: wall, C: cap },
    mat: { w: mat(COLORS.white, 0.55), b: mat(COLORS.black, 0.38) },
    disc: new THREE.CircleGeometry(0.1, 32),
    ring: new THREE.RingGeometry(0.25, 0.33, 48),
    plate: new THREE.PlaneGeometry(0.96, 0.96),
    frame: frameGeometry(0.92, 0.07),
  };
  return shared;
}

// A rounded rectangle extruded upward (y) by h; `standing` turns it on its edge (width along x, thickness along z).
function roundedSlab(w, d, h, r, bevel, standing = false) {
  const s = new THREE.Shape();
  const x = -w / 2, y = -d / 2;
  if (standing) {
    // Shape in x/y is the face of the wall (w wide, h tall); extruded by d.
    const sh = new THREE.Shape();
    const hx = -w / 2;
    sh.moveTo(hx + r, 0);
    sh.lineTo(hx + w - r, 0);
    sh.quadraticCurveTo(hx + w, 0, hx + w, r);
    sh.lineTo(hx + w, h - r);
    sh.quadraticCurveTo(hx + w, h, hx + w - r, h);
    sh.lineTo(hx + r, h);
    sh.quadraticCurveTo(hx, h, hx, h - r);
    sh.lineTo(hx, r);
    sh.quadraticCurveTo(hx, 0, hx + r, 0);
    const g = new THREE.ExtrudeGeometry(sh, { depth: d - bevel * 2, bevelEnabled: true, bevelSize: bevel, bevelThickness: bevel, bevelSegments: 3, curveSegments: 8 });
    g.translate(0, bevel, -(d - bevel * 2) / 2);
    g.computeVertexNormals();
    return g;
  }
  s.moveTo(x + r, y);
  s.lineTo(x + w - r, y);
  s.quadraticCurveTo(x + w, y, x + w, y + r);
  s.lineTo(x + w, y + d - r);
  s.quadraticCurveTo(x + w, y + d, x + w - r, y + d);
  s.lineTo(x + r, y + d);
  s.quadraticCurveTo(x, y + d, x, y + d - r);
  s.lineTo(x, y + r);
  s.quadraticCurveTo(x, y, x + r, y);
  const g = new THREE.ExtrudeGeometry(s, { depth: h, bevelEnabled: true, bevelSize: bevel, bevelThickness: bevel, bevelSegments: 3, curveSegments: 8 });
  g.rotateX(-Math.PI / 2);
  g.translate(0, bevel, 0);
  g.computeVertexNormals();
  return g;
}

function frameGeometry(size, width) {
  const o = size / 2, i = o - width;
  const s = new THREE.Shape([new THREE.Vector2(-o, -o), new THREE.Vector2(o, -o), new THREE.Vector2(o, o), new THREE.Vector2(-o, o)]);
  s.holes.push(new THREE.Path([new THREE.Vector2(-i, -i), new THREE.Vector2(-i, i), new THREE.Vector2(i, i), new THREE.Vector2(i, -i)]));
  return new THREE.ShapeGeometry(s);
}

function boardTexture(n, flip) {
  const px = 128;
  const total = n + MARGIN * 2;
  const W = Math.round(total * px);
  const c = document.createElement('canvas');
  c.width = c.height = Math.min(W, 2048);
  const k = c.width / total;
  const g = c.getContext('2d');
  const grad = g.createLinearGradient(0, 0, c.width, c.height);
  grad.addColorStop(0, '#c9965f');
  grad.addColorStop(1, '#a8743f');
  g.fillStyle = grad;
  g.fillRect(0, 0, c.width, c.height);
  // Grain: deterministic wavy lines.
  let seed = 7;
  const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
  g.globalAlpha = 0.08;
  for (let i = 0; i < 90; i++) {
    g.strokeStyle = rnd() > 0.5 ? '#5a3a1e' : '#f3d3a4';
    g.lineWidth = 1 + rnd() * 3;
    g.beginPath();
    const y0 = rnd() * c.height;
    for (let x = 0; x <= c.width; x += 32) g.lineTo(x, y0 + Math.sin(x / (80 + rnd() * 40) + i) * 6);
    g.stroke();
  }
  g.globalAlpha = 1;
  const m = MARGIN * k;
  for (let x = 0; x < n; x++) {
    for (let y = 0; y < n; y++) {
      const col = flip ? n - 1 - x : x;
      const row = flip ? y : n - 1 - y;
      g.fillStyle = (x + y) % 2 === 0 ? 'rgba(60,32,12,0.20)' : 'rgba(255,235,200,0.10)';
      g.fillRect(m + col * k, m + row * k, k, k);
    }
  }
  g.strokeStyle = 'rgba(40,22,8,0.55)';
  g.lineWidth = Math.max(1.5, k * 0.02);
  for (let i = 0; i <= n; i++) {
    g.beginPath(); g.moveTo(m + i * k, m); g.lineTo(m + i * k, m + n * k); g.stroke();
    g.beginPath(); g.moveTo(m, m + i * k); g.lineTo(m + n * k, m + i * k); g.stroke();
  }
  g.fillStyle = 'rgba(45,24,8,0.75)';
  g.font = `600 ${Math.round(k * 0.26)}px ui-sans-serif, system-ui, sans-serif`;
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  for (let i = 0; i < n; i++) {
    const file = String.fromCharCode(97 + (flip ? n - 1 - i : i));
    const rank = String(flip ? i + 1 : n - i);
    g.fillText(file, m + i * k + k / 2, m + n * k + m / 2);
    g.fillText(rank, m / 2, m + i * k + k / 2);
  }
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.anisotropy = 8;
  return tex;
}

function parsePieces(s) {
  const out = [];
  for (let i = 0; i + 1 < s.length; i += 2) out.push({ w: s[i] === 'w', t: s[i + 1] });
  return out;
}

const ease = (t) => 1 - Math.pow(1 - t, 3);

class Board3D {
  constructor(host, dotnet, options) {
    this.host = host;
    this.dotnet = dotnet;
    this.autoRotate = !!options?.autoRotate;
    this.interactive = options?.interactive !== false;
    const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, powerPreference: 'high-performance' });
    if (!renderer.getContext()) throw new Error('no WebGL context');
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.05;
    renderer.setClearColor(0x000000, 0);
    this.renderer = renderer;
    this.canvas = renderer.domElement;
    this.canvas.className = 'board3d-canvas';
    host.appendChild(this.canvas);
    this.labels = document.createElement('div');
    this.labels.className = 'board3d-labels';
    host.appendChild(this.labels);

    this.scene = new THREE.Scene();
    this.camera = new THREE.PerspectiveCamera(36, 1, 0.1, 200);
    this.scene.add(new THREE.HemisphereLight(0xfff1dc, 0x302418, 1.1));
    const key = new THREE.DirectionalLight(0xffffff, 2.4);
    key.position.set(4.5, 10, 6);
    key.castShadow = true;
    key.shadow.mapSize.set(2048, 2048);
    key.shadow.bias = -0.0004;
    key.shadow.normalBias = 0.02;
    key.shadow.radius = 4;
    this.key = key;
    this.scene.add(key);
    this.scene.add(key.target);
    const fill = new THREE.DirectionalLight(0xa9c2ff, 0.45);
    fill.position.set(-6, 4, -4);
    this.scene.add(fill);

    this.table = new THREE.Mesh(new THREE.PlaneGeometry(80, 80), new THREE.ShadowMaterial({ opacity: 0.28 }));
    this.table.rotation.x = -Math.PI / 2;
    this.table.position.y = -0.445;
    this.table.receiveShadow = true;
    this.scene.add(this.table);

    this.boardGroup = new THREE.Group();
    this.pieceGroup = new THREE.Group();
    this.markGroup = new THREE.Group();
    this.scene.add(this.boardGroup, this.markGroup, this.pieceGroup);
    this.hoverPlate = new THREE.Mesh(sharedAssets().plate, new THREE.MeshBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.13, depthWrite: false }));
    this.hoverPlate.rotation.x = -Math.PI / 2;
    this.hoverPlate.visible = false;
    this.scene.add(this.hoverPlate);

    this.n = 0;
    this.flip = false;
    this.prev = new Map();
    this.anims = [];
    this.handMeshes = [];
    this.labelItems = [];
    this.view = { theta: 0, phi: 0.82, radius: 10 };
    this.raycaster = new THREE.Raycaster();
    this.pointers = new Map();
    this.frames = 0;
    this.ready = false;
    this.dirty = true;
    this.raf = 0;

    this.onResize = () => { this.resize(); };
    this.ro = new ResizeObserver(this.onResize);
    this.ro.observe(host);
    this.bindPointer();
    this.canvas.addEventListener('webglcontextlost', (e) => {
      e.preventDefault();
      this.dotnet?.invokeMethodAsync('OnContextLost').catch(() => {});
    });
    window.__tak3d = this;
    this.resize();
  }

  // ---------- scene ----------
  update(json) {
    const s = JSON.parse(json);
    if (s.n !== this.n || s.flip !== this.flip) {
      this.n = s.n;
      this.flip = s.flip;
      this.buildBoard();
      this.prev = new Map();
      this.resetView(true);
    }
    this.buildPieces(s);
    this.buildMarks(s);
    this.ready = true;
    this.kick();
  }

  center(coord) {
    const n = this.n;
    const x = coord.charCodeAt(0) - 97;
    const y = parseInt(coord.slice(1), 10) - 1;
    const sx = x - (n - 1) / 2, sz = (n - 1) / 2 - y;
    return this.flip ? new THREE.Vector3(-sx, 0, -sz) : new THREE.Vector3(sx, 0, sz);
  }

  coordAt(px, pz) {
    const n = this.n;
    let x = px, z = pz;
    if (this.flip) { x = -x; z = -z; }
    const file = Math.round(x + (n - 1) / 2);
    const rank = Math.round((n - 1) / 2 - z);
    if (file < 0 || file >= n || rank < 0 || rank >= n) return null;
    return String.fromCharCode(97 + file) + (rank + 1);
  }

  buildBoard() {
    this.disposeChildren(this.boardGroup);
    const n = this.n;
    const total = n + MARGIN * 2;
    const slabGeo = roundedSlab(total, total, 0.36, 0.22, 0.04);
    slabGeo.translate(0, -0.44, 0);
    const slab = new THREE.Mesh(slabGeo, new THREE.MeshStandardMaterial({ color: COLORS.woodSide, roughness: 0.75 }));
    slab.castShadow = true;
    slab.receiveShadow = true;
    this.boardGroup.add(slab);
    const topMat = new THREE.MeshStandardMaterial({ map: boardTexture(n, this.flip), roughness: 0.62 });
    const top = new THREE.Mesh(new THREE.PlaneGeometry(total - 0.08, total - 0.08), topMat);
    top.rotation.x = -Math.PI / 2;
    top.position.y = 0.001;
    top.receiveShadow = true;
    top.userData.board = true;
    this.boardGroup.add(top);
    this.boardTop = top;
    const half = total / 2 + 1;
    const cam = this.key.shadow.camera;
    cam.left = cam.bottom = -half;
    cam.right = cam.top = half;
    cam.near = 1;
    cam.far = 30;
    cam.updateProjectionMatrix();
  }

  buildPieces(s) {
    const a = sharedAssets();
    this.disposeChildren(this.pieceGroup, false);
    this.handMeshes = [];
    this.anims = [];
    const now = performance.now();
    const next = new Map();
    this.clearLabels();
    for (const [coord, pstr, flags, dropped] of s.sq) {
      next.set(coord, pstr);
      if (!pstr) continue;
      const pieces = parsePieces(pstr);
      const before = this.prev.get(coord) ?? '';
      // Tiles that were not there before fall into place.
      let same = 0;
      while (same * 2 < before.length && same * 2 < pstr.length && before.substr(same * 2, 2) === pstr.substr(same * 2, 2)) same++;
      const c = this.center(coord);
      this.addStack(pieces, c, 0, (i, mesh) => {
        if (this.prev.size > 0 && i >= same) this.anims.push({ mesh, from: mesh.position.y + 0.9, to: mesh.position.y, t0: now + (i - same) * 45, dur: 300 });
      }, coord);
      if (pieces.length > 2) this.addLabel(`h${coord}`, String(pieces.length), c.clone().setY(pieces.length * FLAT_H + 0.75), 'height');
      if (dropped > 0) this.addLabel(`d${coord}`, `+${dropped}`, c.clone().add(new THREE.Vector3(0.36, 0.05, -0.36)), 'drop');
    }
    if (s.hand && s.hand.p) {
      const pieces = parsePieces(s.hand.p);
      const c = this.center(s.hand.c);
      const groundH = parsePieces(s.sq.find((q) => q[0] === s.hand.c)?.[1] ?? '').length * FLAT_H;
      this.addStack(pieces, c, groundH + 0.45, (i, mesh) => {
        mesh.userData.hover = mesh.position.y;
        this.handMeshes.push(mesh);
      }, s.hand.c);
    }
    if (s.origin && s.carry > 0) {
      this.addLabel('carry', `×${s.carry}`, this.center(s.origin).add(new THREE.Vector3(-0.36, 0.05, -0.36)), 'carry');
    }
    this.prev = next;
  }

  addStack(pieces, c, baseY, each, coord) {
    const a = sharedAssets();
    let y = baseY;
    pieces.forEach((p, i) => {
      const top = i === pieces.length - 1;
      const type = top ? p.t : 'F';
      const mesh = new THREE.Mesh(a.geo[type], a.mat[p.w ? 'w' : 'b']);
      mesh.castShadow = true;
      mesh.receiveShadow = true;
      mesh.position.set(c.x, y, c.z);
      if (type === 'S') mesh.rotation.y = Math.PI / 4;
      if (type === 'F') mesh.rotation.y = ((i * 37) % 9 - 4) * 0.006; // a hand-placed look
      mesh.userData.coord = coord;
      this.pieceGroup.add(mesh);
      each(i, mesh);
      y += FLAT_H;
    });
  }

  buildMarks(s) {
    const a = sharedAssets();
    this.disposeChildren(this.markGroup, false);
    const plate = (c, color, opacity, y = 0.004) => {
      const m = new THREE.Mesh(a.plate, new THREE.MeshBasicMaterial({ color, transparent: true, opacity, depthWrite: false }));
      m.rotation.x = -Math.PI / 2;
      m.position.set(c.x, y, c.z);
      this.markGroup.add(m);
      return m;
    };
    this.pulses = [];
    for (const [coord, pstr, flags] of s.sq) {
      if (!flags) continue;
      const c = this.center(coord);
      const h = (pstr.length / 2) * FLAT_H;
      if (flags.includes('R')) plate(c, COLORS.road, 0.55, 0.003);
      if (flags.includes('S')) plate(c, COLORS.accent, 0.42, 0.005);
      if (flags.includes('I')) plate(c, COLORS.inspect, 0.38, 0.006);
      if (flags.includes('L')) {
        const f = new THREE.Mesh(a.frame, new THREE.MeshBasicMaterial({ color: COLORS.last, transparent: true, opacity: 0.6, depthWrite: false }));
        f.rotation.x = -Math.PI / 2;
        f.position.set(c.x, 0.007, c.z);
        this.markGroup.add(f);
      }
      if (flags.includes('P')) {
        const d = new THREE.Mesh(a.disc, new THREE.MeshBasicMaterial({ color: COLORS.accent, transparent: true, opacity: 0.8, depthWrite: false }));
        d.rotation.x = -Math.PI / 2;
        d.position.set(c.x, 0.008, c.z);
        this.markGroup.add(d);
      }
      if (flags.includes('T') || flags.includes('N')) {
        const r = new THREE.Mesh(a.ring, new THREE.MeshBasicMaterial({ color: COLORS.slide, transparent: true, opacity: 0.95, depthWrite: false, side: THREE.DoubleSide }));
        r.rotation.x = -Math.PI / 2;
        r.position.set(c.x, h + 0.012, c.z);
        this.markGroup.add(r);
        this.pulses.push(r);
      }
    }
  }

  // ---------- labels (HTML badges that follow the camera) ----------
  clearLabels() {
    this.labels.textContent = '';
    this.labelItems = [];
  }

  addLabel(key, text, pos, kind) {
    const el = document.createElement('span');
    el.className = `board3d-label ${kind}`;
    el.textContent = text;
    el.dataset.key = key;
    this.labels.appendChild(el);
    this.labelItems.push({ el, pos });
  }

  placeLabels() {
    const w = this.canvas.clientWidth, h = this.canvas.clientHeight;
    const v = new THREE.Vector3();
    for (const { el, pos } of this.labelItems) {
      v.copy(pos).project(this.camera);
      el.style.transform = `translate(-50%, -50%) translate(${((v.x + 1) / 2) * w}px, ${((1 - v.y) / 2) * h}px)`;
      el.style.visibility = v.z < 1 ? 'visible' : 'hidden';
    }
  }

  // ---------- camera ----------
  fitRadius() {
    const total = this.n + MARGIN * 2 + 0.6;
    const aspect = Math.max(0.3, this.canvas.clientWidth / Math.max(1, this.canvas.clientHeight));
    const vfov = (this.camera.fov * Math.PI) / 180;
    const hfov = 2 * Math.atan(Math.tan(vfov / 2) * aspect);
    const byW = (total / 2) / Math.tan(hfov / 2);
    const byH = (total * 0.62) / Math.tan(vfov / 2);
    return Math.max(byW * 1.04, byH * 0.9);
  }

  resetView(silent) {
    const portrait = this.canvas.clientWidth < this.canvas.clientHeight * 0.9;
    this.view = { theta: this.flip ? Math.PI : 0, phi: portrait ? 0.6 : 0.78, radius: this.fitRadius() };
    if (!silent) this.kick();
  }

  topView() {
    this.view = { theta: this.flip ? Math.PI : 0, phi: 0.02, radius: this.fitRadius() * 0.98 };
    this.kick();
  }

  applyCamera() {
    const { theta, phi, radius } = this.view;
    this.camera.position.set(radius * Math.sin(phi) * Math.sin(theta), radius * Math.cos(phi), radius * Math.sin(phi) * Math.cos(theta));
    this.camera.lookAt(0, -0.15, 0);
  }

  resize() {
    const w = this.host.clientWidth, h = this.host.clientHeight;
    if (!w || !h) return;
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
    if (this.n) this.view.radius = Math.min(Math.max(this.view.radius, this.fitRadius() * 0.5), this.fitRadius() * 2.2);
    this.kick();
  }

  // ---------- input ----------
  bindPointer() {
    const el = this.canvas;
    el.style.touchAction = 'none';
    el.addEventListener('contextmenu', (e) => e.preventDefault());
    el.addEventListener('pointerdown', (e) => {
      el.setPointerCapture(e.pointerId);
      this.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY, x0: e.clientX, y0: e.clientY, t0: performance.now(), button: e.button });
      if (this.pointers.size === 2) {
        const [p, q] = [...this.pointers.values()];
        this.pinch = { d: Math.hypot(p.x - q.x, p.y - q.y), r: this.view.radius };
        this.cancelLongPress();
      } else if (e.pointerType !== 'mouse') {
        this.longPress = setTimeout(() => {
          const p = this.pointers.get(e.pointerId);
          if (p && !p.moved) {
            p.consumed = true;
            this.report('OnSquareRightClick', e.clientX, e.clientY);
          }
        }, LONG_PRESS_MS);
      }
    });
    el.addEventListener('pointermove', (e) => {
      const p = this.pointers.get(e.pointerId);
      if (!p) {
        this.hover(e.clientX, e.clientY);
        return;
      }
      const dx = e.clientX - p.x, dy = e.clientY - p.y;
      p.x = e.clientX; p.y = e.clientY;
      if (Math.hypot(p.x - p.x0, p.y - p.y0) > CLICK_SLOP) { p.moved = true; this.cancelLongPress(); }
      if (this.pointers.size === 2 && this.pinch) {
        const [a, b] = [...this.pointers.values()];
        const d = Math.hypot(a.x - b.x, a.y - b.y);
        this.view.radius = this.clampRadius(this.pinch.r * (this.pinch.d / Math.max(1, d)));
        this.kick();
      } else if (p.moved && this.interactive) {
        this.view.theta -= dx * 0.0075;
        this.view.phi = Math.min(1.3, Math.max(0.02, this.view.phi - dy * 0.006));
        this.autoRotate = false;
        this.kick();
      }
    });
    const end = (e) => {
      const p = this.pointers.get(e.pointerId);
      this.pointers.delete(e.pointerId);
      if (this.pointers.size < 2) this.pinch = null;
      this.cancelLongPress();
      if (!p || p.moved || p.consumed || e.type === 'pointercancel' || !this.interactive) return;
      if (p.button === 2) this.report('OnSquareRightClick', e.clientX, e.clientY);
      else if (p.button === 0) this.report('OnSquareClick', e.clientX, e.clientY);
    };
    el.addEventListener('pointerup', end);
    el.addEventListener('pointercancel', end);
    el.addEventListener('pointerleave', () => { this.hoverPlate.visible = false; this.kick(); });
    el.addEventListener('wheel', (e) => {
      if (!this.interactive) return;
      e.preventDefault();
      this.view.radius = this.clampRadius(this.view.radius * Math.exp(e.deltaY * 0.0012));
      this.kick();
    }, { passive: false });
  }

  cancelLongPress() {
    if (this.longPress) clearTimeout(this.longPress);
    this.longPress = 0;
  }

  clampRadius(r) {
    const fit = this.fitRadius();
    return Math.min(fit * 2.2, Math.max(fit * 0.5, r));
  }

  pick(clientX, clientY) {
    const rect = this.canvas.getBoundingClientRect();
    const ndc = new THREE.Vector2(((clientX - rect.left) / rect.width) * 2 - 1, -((clientY - rect.top) / rect.height) * 2 + 1);
    this.raycaster.setFromCamera(ndc, this.camera);
    const hit = this.raycaster.intersectObjects(this.pieceGroup.children, false)[0];
    if (hit?.object.userData.coord) return hit.object.userData.coord;
    const ray = this.raycaster.ray;
    if (Math.abs(ray.direction.y) < 1e-6) return null;
    const t = -ray.origin.y / ray.direction.y;
    if (t < 0) return null;
    const p = ray.origin.clone().addScaledVector(ray.direction, t);
    return this.coordAt(p.x, p.z);
  }

  hover(x, y) {
    if (!this.interactive || !this.n) return;
    const c = this.pick(x, y);
    if (c) {
      const v = this.center(c);
      this.hoverPlate.position.set(v.x, 0.009, v.z);
    }
    if (this.hoverPlate.visible !== !!c || (c && c !== this.hovered)) {
      this.hoverPlate.visible = !!c;
      this.canvas.style.cursor = c ? 'pointer' : 'grab';
      this.kick();
    }
    this.hovered = c;
  }

  report(method, x, y) {
    const c = this.pick(x, y);
    if (!this.dotnet) return;
    const call = c ? this.dotnet.invokeMethodAsync(method, c) : this.dotnet.invokeMethodAsync('OnBackgroundClick');
    call.catch(() => { /* the view is gone */ });
  }

  // ---------- loop ----------
  kick() {
    this.dirty = true;
    if (!this.raf) this.raf = requestAnimationFrame((t) => this.frame(t));
  }

  frame(t) {
    this.raf = 0;
    const now = performance.now();
    let busy = false;
    if (this.autoRotate) { this.view.theta += 0.0022; busy = true; }
    this.anims = this.anims.filter((a) => {
      const k = Math.min(1, Math.max(0, (now - a.t0) / a.dur));
      a.mesh.position.y = a.from + (a.to - a.from) * ease(k);
      return k < 1;
    });
    if (this.anims.length) busy = true;
    if (this.handMeshes.length) {
      const bob = Math.sin(now / 280) * 0.04;
      for (const m of this.handMeshes) m.position.y = m.userData.hover + bob;
      busy = true;
    }
    if (this.pulses?.length) {
      const s = 1 + Math.sin(now / 220) * 0.06;
      for (const r of this.pulses) r.scale.set(s, s, s);
      busy = true;
    }
    if (this.dirty || busy) {
      this.applyCamera();
      this.renderer.render(this.scene, this.camera);
      this.placeLabels();
      this.frames++;
      this.dirty = false;
    }
    if (busy) this.kick();
  }

  // ---------- test hook and teardown ----------
  /** Client coordinates of the top of the stack on `coord` (tests click there). */
  screenOf(coord) {
    this.applyCamera();
    this.camera.updateMatrixWorld();
    const c = this.center(coord);
    const h = (this.prev.get(coord)?.length ?? 0) / 2;
    c.y = h * FLAT_H;
    c.project(this.camera);
    const r = this.canvas.getBoundingClientRect();
    return { x: r.left + ((c.x + 1) / 2) * r.width, y: r.top + ((1 - c.y) / 2) * r.height };
  }

  disposeChildren(group, deep = true) {
    for (const child of [...group.children]) {
      group.remove(child);
      if (child.material && !Object.values(sharedAssets().mat).includes(child.material)) {
        child.material.map?.dispose();
        child.material.dispose();
      }
      if (deep && child.geometry && !Object.values(sharedAssets().geo).includes(child.geometry)) child.geometry.dispose();
    }
  }

  dispose() {
    cancelAnimationFrame(this.raf);
    this.cancelLongPress();
    this.ro.disconnect();
    this.disposeChildren(this.boardGroup);
    this.disposeChildren(this.pieceGroup, false);
    this.disposeChildren(this.markGroup, false);
    this.renderer.dispose();
    this.renderer.forceContextLoss?.();
    this.canvas.remove();
    this.labels.remove();
    this.dotnet = null;
    if (window.__tak3d === this) window.__tak3d = null;
  }
}

/** Creates the 3D board in `host`, or returns null when this browser cannot draw WebGL (the page then shows 2D). */
export function create(host, dotnet, options) {
  try {
    return new Board3D(host, dotnet, options);
  } catch (e) {
    console.warn('3D board unavailable:', e?.message ?? e);
    return null;
  }
}
