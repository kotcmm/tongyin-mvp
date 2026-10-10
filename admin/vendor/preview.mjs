import * as THREE from './three.module.min.js';
import { GLTFLoader } from './loaders/GLTFLoader.js';

// This is a browser-only rendering step. The model and render frames are kept
// local until the administrator uploads the resulting PNG files.
const ANGLES = [
  { label: '正面', yaw: 0 },
  { label: '侧面', yaw: Math.PI / 2 },
  { label: '背面', yaw: Math.PI }
];

function imageBlob(canvas) {
  return new Promise((resolve, reject) => {
    canvas.toBlob(blob => blob ? resolve(blob) : reject(new Error('浏览器无法导出 PNG 图片')), 'image/png');
  });
}

function disposeModel(root) {
  const geometries = new Set();
  const materials = new Set();
  const textures = new Set();
  root.traverse(node => {
    if (node.geometry) geometries.add(node.geometry);
    if (node.material) {
      for (const mat of Array.isArray(node.material) ? node.material : [node.material]) {
        if (!mat) continue;
        materials.add(mat);
        for (const value of Object.values(mat)) {
          if (value && value.isTexture) textures.add(value);
        }
      }
    }
  });
  geometries.forEach(g => g.dispose());
  materials.forEach(m => m.dispose());
  textures.forEach(t => t.dispose());
}

/**
 * @param {ArrayBuffer} arrayBuffer - Self-contained GLB uploaded or fetched by admin.
 * @param {(message:string)=>void} onProgress
 * @returns {Promise<{label:string,file:File}[]>}
 */
export async function renderWhiteModelPreviews(arrayBuffer, onProgress = () => {}) {
  if (!(arrayBuffer instanceof ArrayBuffer) || arrayBuffer.byteLength < 12 ||
      new DataView(arrayBuffer).getUint32(0, true) !== 0x46546c67) {
    throw new Error('文件不是有效 GLB，请确认选择的是 .glb 而不是 .stl / .obj');
  }
  let renderer;
  let root;
  try {
    onProgress('正在载入 3D 模型…');
    let gltf;
    try {
      gltf = await new Promise((resolve, reject) =>
        new GLTFLoader().parse(arrayBuffer, '', resolve, reject));
    } catch (err) {
      throw new Error('无法解析此 GLB（可能使用了未支持的压缩/外部贴图）：' + (err.message || err));
    }
    root = gltf.scene;
    root.updateMatrixWorld(true);
    const bounds = new THREE.Box3().setFromObject(root);
    const sizes = bounds.getSize(new THREE.Vector3());
    const center = bounds.getCenter(new THREE.Vector3());
    const maxSize = Math.max(sizes.x, sizes.y, sizes.z);
    if (!Number.isFinite(maxSize) || maxSize <= 0.000001) {
      throw new Error('模型没有可渲染的几何体');
    }

    const whiteMaterial = new THREE.MeshStandardMaterial({
      color: 0xf5f6f2, metalness: 0, roughness: 0.88, side: THREE.DoubleSide
    });
    let meshCount = 0;
    root.traverse(obj => {
      if (!obj.isMesh) return;
      meshCount++;
      obj.material = whiteMaterial;
      obj.castShadow = false;
      obj.receiveShadow = false;
    });
    if (!meshCount) throw new Error('模型中没有可见网格');

    const scene = new THREE.Scene();
    scene.background = new THREE.Color(0xe9eee9);
    // Normalize size and center to make different GLBs use consistent cameras.
    const holder = new THREE.Group();
    holder.add(root);
    holder.scale.setScalar(1 / maxSize);
    holder.position.copy(center).multiplyScalar(-1 / maxSize);
    scene.add(holder);
    scene.add(new THREE.HemisphereLight(0xffffff, 0x9ca99f, 2.4));
    const key = new THREE.DirectionalLight(0xffffff, 3.0);
    key.position.set(3, 5, 7);
    scene.add(key);
    const fill = new THREE.DirectionalLight(0xdfe8e6, 1.4);
    fill.position.set(-5, 3, -4);
    scene.add(fill);
    const rim = new THREE.DirectionalLight(0xffffff, 1.1);
    rim.position.set(2, 2, -5);
    scene.add(rim);

    const dimension = 1.55 * sizes.length() / maxSize;
    const half = Math.max(0.85, dimension / 2);
    const camera = new THREE.OrthographicCamera(-half, half, half, -half, 0.01, 100);
    camera.up.set(0, 1, 0);

    try {
      renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false, preserveDrawingBuffer: true });
    } catch (err) {
      throw new Error('无法创建 WebGL 渲染器，请在支持 WebGL 的 Chrome/Edge 中打开管理后台。' + (err.message || ''));
    }
    renderer.setPixelRatio(1);
    renderer.setSize(768, 768, false);
    renderer.setClearColor(0xe9eee9, 1);
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.55;

    const files = [];
    for (let i = 0; i < ANGLES.length; i++) {
      const view = ANGLES[i];
      onProgress('正在渲染' + view.label + '（' + (i + 1) + '/3）…');
      const distance = 3.5;
      camera.position.set(Math.sin(view.yaw) * distance, 0.5, Math.cos(view.yaw) * distance);
      camera.lookAt(0, 0, 0);
      camera.updateProjectionMatrix();
      renderer.render(scene, camera);
      const blob = await imageBlob(renderer.domElement);
      files.push({ label: view.label, file: new File([blob], 'tongyin-' + (i + 1) + '.png', { type: 'image/png' }) });
    }
    return files;
  } finally {
    if (root) disposeModel(root);
    if (renderer) {
      renderer.dispose();
      renderer.forceContextLoss();
    }
  }
}
