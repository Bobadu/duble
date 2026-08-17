// view3d.js — podglad 3D na three.js: Widok3D (jeden canvas: scena, swiatla, OrbitControls, modele GLB w "slotach"
// — tryb A/B trzyma dwa modele i przelacza widocznosc) i Synchronizator (wspolna kamera kilku widokow).
// Render na zadanie (bez ciaglej petli): przy zmianie kamery, rozmiaru, materialu.
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';

const loader = new GLTFLoader();

export function webglDostepny() {
  try { const c = document.createElement('canvas'); return !!(c.getContext('webgl2') || c.getContext('webgl')); } catch { return false; }
}

export class Widok3D {
  constructor(kontener) {
    this.kontener = kontener;
    this.onZmianaKamery = null;   // (widok) => void — Synchronizator
    this.modele = {};             // slot -> { obiekt, statystyki }
    this.widoczny = null;         // slot pokazywany
    this._wire = false; this._cichy = false; this._zniszczony = false;

    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, powerPreference: 'high-performance' });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.renderer.setClearColor(0x000000, 0);
    kontener.append(this.renderer.domElement);

    this.scena = new THREE.Scene();
    this.kamera = new THREE.PerspectiveCamera(35, 1, 0.01, 100);
    this.kamera.position.set(0, 0.2, 2.2);
    this.scena.add(new THREE.HemisphereLight(0xffffff, 0x3a3a3a, 1.15));
    const kier = new THREE.DirectionalLight(0xffffff, 0.9);
    kier.position.set(0.5, 1, 1.5);
    this.kamera.add(kier);          // swiatlo jedzie z kamera: model zawsze oswietlony od przodu
    this.scena.add(this.kamera);
    const wypelnienie = new THREE.DirectionalLight(0xffffff, 0.35);
    wypelnienie.position.set(-1, -0.5, -1);
    this.scena.add(wypelnienie);

    this.kontrolki = new OrbitControls(this.kamera, this.renderer.domElement);
    this.kontrolki.enableDamping = false;
    this.kontrolki.screenSpacePanning = true;
    this.kontrolki.addEventListener('change', () => { this.render(); if (!this._cichy) this.onZmianaKamery?.(this); });

    this._ro = new ResizeObserver(() => this.dopasujRozmiar());
    this._ro.observe(kontener);
    this.dopasujRozmiar();
  }

  get model() { return this.widoczny ? this.modele[this.widoczny]?.obiekt : null; }
  get statystyki() { return (this.widoczny && this.modele[this.widoczny]?.statystyki) || { wierzcholki: 0, trojkaty: 0 }; }

  dopasujRozmiar() {
    if (this._zniszczony) return;
    const w = Math.max(1, this.kontener.clientWidth), h = Math.max(1, this.kontener.clientHeight);
    this.renderer.setSize(w, h, false);
    this.kamera.aspect = w / h; this.kamera.updateProjectionMatrix();
    this.render();
  }

  render() { if (!this._zniszczony) this.renderer.render(this.scena, this.kamera); }

  /** Laduje GLB do slotu (podmienia poprzedni w tym slocie). dopasuj = ustaw kamere po zaladowaniu; pokaz = od razu widoczny. */
  async zaladuj(url, { slot = 'glowny', dopasuj = true, pokaz = true } = {}) {
    const gltf = await loader.loadAsync(url);
    if (this._zniszczony) return;
    this.usunModel(slot);
    let wierz = 0, troj = 0;
    gltf.scene.traverse(o => {
      if (o.isMesh) {
        const g = o.geometry;
        wierz += g.attributes.position?.count || 0;
        troj += g.index ? g.index.count / 3 : (g.attributes.position?.count || 0) / 3;
        if (o.material) { o.material.wireframe = this._wire; o.material.needsUpdate = true; }
        o.frustumCulled = false;
      }
    });
    this.modele[slot] = { obiekt: gltf.scene, statystyki: { wierzcholki: wierz, trojkaty: Math.round(troj) } };
    this.scena.add(gltf.scene);
    if (pokaz) this.pokaz(slot); else gltf.scene.visible = false;
    if (dopasuj) this.dopasujKamere();
    this.render();
  }

  pokaz(slot) {
    this.widoczny = slot;
    for (const [s, m] of Object.entries(this.modele)) { this._krycie(s, s === slot ? 1 : 0, true); m.obiekt.visible = s === slot; }
    this.render();
  }

  /** Krycie modelu w slocie (0..1). `glowny` = ten model zapisuje glebie (zaslania swoje wnetrze i drugi model). */
  _krycie(slot, alfa, glowny = true) {
    const m = this.modele[slot]; if (!m) return;
    m.obiekt.visible = alfa > 0.004;
    m.obiekt.traverse(o => {
      if (!o.isMesh || !o.material) return;
      o.renderOrder = glowny ? 0 : 1;
      const mats = Array.isArray(o.material) ? o.material : [o.material];
      for (const mt of mats) {
        if (!mt) continue;
        if (mt.__krycie0 === undefined) { mt.__krycie0 = mt.opacity ?? 1; mt.__przez0 = !!mt.transparent; mt.__zapisZ0 = mt.depthWrite !== false; }
        const a = mt.__krycie0 * alfa;
        mt.opacity = a;
        mt.transparent = mt.__przez0 || a < 0.996;
        mt.depthWrite = mt.__zapisZ0 && (glowny || a > 0.996);
        mt.needsUpdate = true;
      }
    });
  }

  /** Przenikanie miedzy dwoma slotami: t=0 tylko A, t=1 tylko B, pomiedzy — slabszy model przeswituje jak duch
   *  po silniejszym (silniejszy zapisuje glebie, wiec nie widac przez niego wnetrza siatki). */
  mieszaj(t, slotA = 'A', slotB = 'B') {
    t = Math.max(0, Math.min(1, Number(t) || 0));
    const aGlowny = 1 - t >= t;
    this._krycie(slotA, 1 - t, aGlowny);
    this._krycie(slotB, t, !aGlowny);
    this.widoczny = aGlowny ? slotA : slotB;
    this.render();
  }

  usunModel(slot = null) {
    const sloty = slot ? [slot] : Object.keys(this.modele);
    for (const s of sloty) {
      const m = this.modele[s]; if (!m) continue;
      this.scena.remove(m.obiekt);
      m.obiekt.traverse(o => {
        if (o.isMesh) {
          o.geometry?.dispose();
          const mats = Array.isArray(o.material) ? o.material : [o.material];
          for (const mt of mats) { if (!mt) continue; for (const k of ['map', 'normalMap', 'roughnessMap', 'metalnessMap', 'emissiveMap', 'aoMap']) mt[k]?.dispose?.(); mt.dispose(); }
        }
      });
      delete this.modele[s];
      if (this.widoczny === s) this.widoczny = null;
    }
  }

  /** Kamera na +Z (przod postaci) w odleglosci dopasowanej do rozmiaru widocznego modelu (albo wszystkich). */
  dopasujKamere() {
    const box = new THREE.Box3();
    const cel = this.model; if (cel) box.setFromObject(cel); else for (const m of Object.values(this.modele)) box.expandByObject(m.obiekt);
    if (box.isEmpty()) return;
    const srodek = box.getCenter(new THREE.Vector3()), rozm = box.getSize(new THREE.Vector3());
    const maks = Math.max(rozm.x, rozm.y, rozm.z, 0.05);
    const dyst = maks / (2 * Math.tan((this.kamera.fov * Math.PI / 180) / 2)) * 1.35;
    this._cichy = true;
    this.kamera.position.set(srodek.x, srodek.y + rozm.y * 0.1, srodek.z + dyst);
    this.kamera.near = Math.max(0.005, dyst / 100); this.kamera.far = dyst * 50; this.kamera.updateProjectionMatrix();
    this.kontrolki.target.copy(srodek);
    this.kontrolki.minDistance = dyst * 0.12; this.kontrolki.maxDistance = dyst * 8;
    this.kontrolki.update();
    this._cichy = false;
    this.render();
    this.onZmianaKamery?.(this);
  }

  ustawWireframe(b) {
    this._wire = !!b;
    for (const m of Object.values(this.modele)) m.obiekt.traverse(o => { if (o.isMesh && o.material) { const mats = Array.isArray(o.material) ? o.material : [o.material]; for (const mt of mats) { mt.wireframe = this._wire; mt.needsUpdate = true; } } });
    this.render();
  }

  /** Przejmij kamere z innego widoku (bez emitowania zmiany dalej). */
  przyjmijKamere(inny) {
    this._cichy = true;
    this.kamera.position.copy(inny.kamera.position);
    this.kamera.quaternion.copy(inny.kamera.quaternion);
    this.kamera.near = inny.kamera.near; this.kamera.far = inny.kamera.far; this.kamera.zoom = inny.kamera.zoom;
    this.kamera.updateProjectionMatrix();
    this.kontrolki.target.copy(inny.kontrolki.target);
    this.kontrolki.minDistance = inny.kontrolki.minDistance; this.kontrolki.maxDistance = inny.kontrolki.maxDistance;
    this.kontrolki.update();
    this._cichy = false;
    this.render();
  }

  zniszcz() {
    this._zniszczony = true;
    this._ro.disconnect();
    this.kontrolki.dispose();
    this.usunModel();
    this.renderer.dispose();
    try { this.renderer.forceContextLoss(); } catch { }
    this.renderer.domElement.remove();
  }
}

/** Wspolna kamera: zmiana w jednym widoku -> kopiowana do pozostalych (gdy wlaczone). */
export class Synchronizator {
  constructor() { this.widoki = []; this.wlaczony = true; this._trwa = false; }
  dodaj(w) { this.widoki.push(w); w.onZmianaKamery = (zr) => this.rozglos(zr); }
  wlacz(b) { this.wlaczony = !!b; if (this.wlaczony && this.widoki.length) this.rozglos(this.widoki[0]); }
  rozglos(zrodlo) {
    if (!this.wlaczony || this._trwa) return;
    this._trwa = true;
    try { for (const w of this.widoki) if (w !== zrodlo) w.przyjmijKamere(zrodlo); } finally { this._trwa = false; }
  }
  zniszcz() { for (const w of this.widoki) w.zniszcz(); this.widoki = []; }
}
