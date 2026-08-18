// three/ModelView.ts — the 3D preview: one canvas with a scene, lights, orbit controls and GLB models held in
// named slots, plus a synchroniser that keeps several canvases looking from the same place.
//
// This is imperative on purpose. three.js owns its canvas, its GPU resources and when they are freed; React
// owns where the canvas sits. The two meet in useModelView, and nowhere else.
//
// Rendering happens on demand — when the camera moves, the size changes or a material does — rather than in a
// continuous loop, so a still model costs nothing.
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';

const loader = new GLTFLoader();

export interface MeshStatistics {
  vertices: number;
  triangles: number;
}

interface LoadedModel {
  object: THREE.Object3D;
  statistics: MeshStatistics;
}

export interface LoadOptions {
  /** Which slot to put it in; the overlay mode keeps two at once. */
  slot?: string;
  /** Point the camera at it once it is in. */
  frame?: boolean;
  /** Show it straight away, rather than leaving it hidden for a blend. */
  show?: boolean;
}

export function isWebglAvailable(): boolean {
  try {
    const canvas = document.createElement('canvas');
    return !!(canvas.getContext('webgl2') ?? canvas.getContext('webgl'));
  } catch {
    return false;
  }
}

/** Material properties as they were before any blending touched them, so they can be put back. */
interface OriginalMaterial {
  opacity: number;
  transparent: boolean;
  depthWrite: boolean;
}

const originals = new WeakMap<THREE.Material, OriginalMaterial>();

export class ModelView {
  /** Told when the user moves this view's camera, so the others can follow. */
  onCameraMoved: ((view: ModelView) => void) | null = null;

  private readonly renderer: THREE.WebGLRenderer;
  private readonly scene = new THREE.Scene();
  private readonly camera = new THREE.PerspectiveCamera(35, 1, 0.01, 100);
  private readonly controls: OrbitControls;
  private readonly resize: ResizeObserver;

  private readonly models = new Map<string, LoadedModel>();
  private shown: string | null = null;
  private wireframe = false;
  /** True while this view is being moved by the synchroniser, so it does not echo the move back. */
  private following = false;
  private disposed = false;

  constructor(private readonly container: HTMLElement) {
    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, powerPreference: 'high-performance' });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.renderer.setClearColor(0x000000, 0);
    container.append(this.renderer.domElement);

    this.camera.position.set(0, 0.2, 2.2);
    this.scene.add(new THREE.HemisphereLight(0xffffff, 0x3a3a3a, 1.15));

    // the key light rides with the camera, so the model is lit from wherever it is being looked at
    const key = new THREE.DirectionalLight(0xffffff, 0.9);
    key.position.set(0.5, 1, 1.5);
    this.camera.add(key);
    this.scene.add(this.camera);

    const fill = new THREE.DirectionalLight(0xffffff, 0.35);
    fill.position.set(-1, -0.5, -1);
    this.scene.add(fill);

    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = false;
    this.controls.screenSpacePanning = true;
    this.controls.addEventListener('change', () => {
      this.render();
      if (!this.following) this.onCameraMoved?.(this);
    });

    this.resize = new ResizeObserver(() => this.fitToContainer());
    this.resize.observe(container);
    this.fitToContainer();
  }

  get statistics(): MeshStatistics {
    const shown = this.shown === null ? undefined : this.models.get(this.shown);
    return shown?.statistics ?? { vertices: 0, triangles: 0 };
  }

  statisticsOf(slot: string): MeshStatistics | null {
    return this.models.get(slot)?.statistics ?? null;
  }

  render(): void {
    if (!this.disposed) this.renderer.render(this.scene, this.camera);
  }

  fitToContainer(): void {
    if (this.disposed) return;

    const width = Math.max(1, this.container.clientWidth);
    const height = Math.max(1, this.container.clientHeight);

    this.renderer.setSize(width, height, false);
    this.camera.aspect = width / height;
    this.camera.updateProjectionMatrix();
    this.render();
  }

  /** Loads a GLB into a slot, replacing whatever was there. */
  async load(url: string, { slot = 'main', frame = true, show = true }: LoadOptions = {}): Promise<MeshStatistics | null> {
    const gltf = await loader.loadAsync(url);
    if (this.disposed) return null;

    this.remove(slot);

    let vertices = 0;
    let triangles = 0;
    gltf.scene.traverse((node) => {
      if (!(node instanceof THREE.Mesh)) return;

      const geometry = node.geometry as THREE.BufferGeometry;
      vertices += geometry.attributes.position?.count ?? 0;
      triangles += geometry.index ? geometry.index.count / 3 : (geometry.attributes.position?.count ?? 0) / 3;

      for (const material of materialsOf(node)) setWireframe(material, this.wireframe);
      // a garment is one connected thing; culling its parts costs more than it saves
      node.frustumCulled = false;
    });

    const statistics = { vertices, triangles: Math.round(triangles) };
    this.models.set(slot, { object: gltf.scene, statistics });
    this.scene.add(gltf.scene);

    if (show) this.show(slot);
    else gltf.scene.visible = false;
    if (frame) this.frameCamera();

    this.render();
    return statistics;
  }

  show(slot: string): void {
    this.shown = slot;
    for (const [name, model] of this.models) {
      this.setOpacity(name, name === slot ? 1 : 0, true);
      model.object.visible = name === slot;
    }
    this.render();
  }

  /**
   * Blends between two slots: 0 is all A, 1 is all B. The stronger of the two writes depth, so the weaker one
   * shows through it like a ghost instead of revealing the inside of its own mesh.
   */
  blend(amount: number, a = 'A', b = 'B'): void {
    const t = Math.max(0, Math.min(1, amount));
    const aLeads = 1 - t >= t;

    this.setOpacity(a, 1 - t, aLeads);
    this.setOpacity(b, t, !aLeads);
    this.shown = aLeads ? a : b;
    this.render();
  }

  remove(slot?: string): void {
    for (const name of slot ? [slot] : [...this.models.keys()]) {
      const model = this.models.get(name);
      if (!model) continue;

      this.scene.remove(model.object);
      model.object.traverse((node) => {
        if (!(node instanceof THREE.Mesh)) return;
        node.geometry.dispose();
        for (const material of materialsOf(node)) {
          for (const map of ['map', 'normalMap', 'roughnessMap', 'metalnessMap', 'emissiveMap', 'aoMap'] as const) {
            const texture = (material as unknown as Record<string, THREE.Texture | null>)[map];
            texture?.dispose();
          }
          material.dispose();
        }
      });

      this.models.delete(name);
      if (this.shown === name) this.shown = null;
    }
  }

  /** Puts the camera in front of the model, far enough back to see all of it. */
  frameCamera(): void {
    const box = new THREE.Box3();
    const target = this.shown ? this.models.get(this.shown)?.object : undefined;
    if (target) box.setFromObject(target);
    else for (const model of this.models.values()) box.expandByObject(model.object);
    if (box.isEmpty()) return;

    const centre = box.getCenter(new THREE.Vector3());
    const size = box.getSize(new THREE.Vector3());
    const largest = Math.max(size.x, size.y, size.z, 0.05);
    const distance = (largest / (2 * Math.tan(((this.camera.fov * Math.PI) / 180) / 2))) * 1.35;

    this.following = true;
    this.camera.position.set(centre.x, centre.y + size.y * 0.1, centre.z + distance);
    this.camera.near = Math.max(0.005, distance / 100);
    this.camera.far = distance * 50;
    this.camera.updateProjectionMatrix();

    this.controls.target.copy(centre);
    this.controls.minDistance = distance * 0.12;
    this.controls.maxDistance = distance * 8;
    this.controls.update();
    this.following = false;

    this.render();
    this.onCameraMoved?.(this);
  }

  setWireframe(on: boolean): void {
    this.wireframe = on;
    for (const model of this.models.values())
      model.object.traverse((node) => {
        if (!(node instanceof THREE.Mesh)) return;
        for (const material of materialsOf(node)) setWireframe(material, on);
      });
    this.render();
  }

  /** Copies another view's camera without telling anyone, which is what stops a synchronising loop. */
  followCamera(other: ModelView): void {
    this.following = true;
    this.camera.position.copy(other.camera.position);
    this.camera.quaternion.copy(other.camera.quaternion);
    this.camera.near = other.camera.near;
    this.camera.far = other.camera.far;
    this.camera.zoom = other.camera.zoom;
    this.camera.updateProjectionMatrix();

    this.controls.target.copy(other.controls.target);
    this.controls.minDistance = other.controls.minDistance;
    this.controls.maxDistance = other.controls.maxDistance;
    this.controls.update();
    this.following = false;

    this.render();
  }

  dispose(): void {
    this.disposed = true;
    this.resize.disconnect();
    this.controls.dispose();
    this.remove();
    this.renderer.dispose();
    try {
      this.renderer.forceContextLoss();
    } catch {
      // some drivers refuse; the canvas is going away regardless
    }
    this.renderer.domElement.remove();
  }

  /** `leading` means this model writes depth: it hides its own insides, and whatever is behind it. */
  private setOpacity(slot: string, alpha: number, leading: boolean): void {
    const model = this.models.get(slot);
    if (!model) return;

    model.object.visible = alpha > 0.004;
    model.object.traverse((node) => {
      if (!(node instanceof THREE.Mesh)) return;
      node.renderOrder = leading ? 0 : 1;

      for (const material of materialsOf(node)) {
        let original = originals.get(material);
        if (!original) {
          original = { opacity: material.opacity, transparent: material.transparent, depthWrite: material.depthWrite };
          originals.set(material, original);
        }

        const opacity = original.opacity * alpha;
        material.opacity = opacity;
        material.transparent = original.transparent || opacity < 0.996;
        material.depthWrite = original.depthWrite && (leading || opacity > 0.996);
        material.needsUpdate = true;
      }
    });
  }
}

/** Keeps several views looking from the same place: a move in one is copied to the rest. */
export class CameraSync {
  private readonly views: ModelView[] = [];
  private enabled = true;
  private broadcasting = false;

  add(view: ModelView): void {
    this.views.push(view);
    view.onCameraMoved = (source) => this.broadcast(source);
  }

  setEnabled(enabled: boolean): void {
    this.enabled = enabled;
    const first = this.views[0];
    if (enabled && first) this.broadcast(first);
  }

  broadcast(source: ModelView): void {
    if (!this.enabled || this.broadcasting) return;
    this.broadcasting = true;
    try {
      for (const view of this.views) if (view !== source) view.followCamera(source);
    } finally {
      this.broadcasting = false;
    }
  }

  clear(): void {
    this.views.length = 0;
  }
}

function materialsOf(mesh: THREE.Mesh): THREE.Material[] {
  return Array.isArray(mesh.material) ? mesh.material : [mesh.material];
}

/** Only the materials that can draw as a mesh have a wireframe; a GLB may hold others. */
function setWireframe(material: THREE.Material, on: boolean): void {
  if (!('wireframe' in material)) return;
  (material as THREE.Material & { wireframe: boolean }).wireframe = on;
  material.needsUpdate = true;
}
