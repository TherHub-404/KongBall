// Headless visual check for a .glb: loads it in three.js inside Chromium, screenshots the model at
// rest and at the peak-motion frame of each animation clip (front + side view), and measures the
// largest per-bone rotation over each clip's full duration. No Unity Editor involved — the AGENTS.md
// pattern this project already names for verifying model scale/pose without opening one.
//
// Usage: npm install && node preview.mjs <path-to.glb> [outDir]
import puppeteer from 'puppeteer';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import http from 'node:http';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

const glbPath = process.argv[2];
const outDir = process.argv[3] || path.join(__dirname, 'out');
if (!glbPath) {
    console.error('usage: node preview.mjs <path-to.glb> [outDir]');
    process.exit(1);
}
await mkdir(outDir, { recursive: true });

const threeRoot = path.join(__dirname, 'node_modules/three');
const glbBytes = await readFile(glbPath);

const html = `<!doctype html><html><head><meta charset="utf-8">
<script type="importmap">{"imports":{"three":"/three/build/three.module.js","three/addons/":"/three/examples/jsm/"}}</script>
</head><body><script type="module" src="/main.js"></script></body></html>`;

const mainJs = `
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';

async function run() {
    const res = await fetch('/model.glb');
    const buf = await res.arrayBuffer();

    const renderer = new THREE.WebGLRenderer({ antialias: true });
    renderer.setSize(800, 800);
    document.body.appendChild(renderer.domElement);

    const scene = new THREE.Scene();
    scene.background = new THREE.Color(0x2b2b2b);
    scene.add(new THREE.HemisphereLight(0xffffff, 0x444444, 2.2));
    const dir = new THREE.DirectionalLight(0xffffff, 2.0);
    dir.position.set(3, 6, 4);
    scene.add(dir);
    scene.add(new THREE.GridHelper(4, 8));

    const loader = new GLTFLoader();
    const gltf = await new Promise((resolve, reject) => loader.parse(buf, '', resolve, reject));
    scene.add(gltf.scene);

    const box = new THREE.Box3().setFromObject(gltf.scene);
    const size = box.getSize(new THREE.Vector3());
    const center = box.getCenter(new THREE.Vector3());

    const boneNames = [];
    gltf.scene.traverse((o) => { if (o.isBone) boneNames.push(o.name); });

    const camera = new THREE.PerspectiveCamera(40, 1, 0.05, 100);
    const dist = Math.max(size.x, size.y, size.z) * 1.8 + 0.5;
    camera.position.set(center.x + dist * 0.6, center.y + size.y * 0.4, center.z + dist);
    camera.lookAt(center);

    // A pure side view (looking down -X) — a forward/backward kick or lunge foreshortens to nearly
    // nothing from the default 3/4-front angle, so this is the view that actually shows it.
    const sideCamera = new THREE.PerspectiveCamera(40, 1, 0.05, 100);
    sideCamera.position.set(center.x + dist * 1.2, center.y + size.y * 0.3, center.z);
    sideCamera.lookAt(center);

    const shots = [];
    renderer.render(scene, camera);
    shots.push({ name: 'rest', dataUrl: renderer.domElement.toDataURL('image/png') });

    // Bind-pose bone rotations, to diff against every sampled frame of every clip — a numeric
    // "did anything actually move" check, since a screenshot at one arbitrary frame can miss motion
    // concentrated elsewhere in the clip, or motion on an axis that barely changes the silhouette.
    const bones = [];
    gltf.scene.traverse((o) => { if (o.isBone) bones.push(o); });
    const bindQuat = new Map(bones.map((b) => [b.name, b.quaternion.clone()]));

    const clipReports = [];
    if (gltf.animations.length > 0) {
        const mixer = new THREE.AnimationMixer(gltf.scene);
        for (const clip of gltf.animations) {
            const action = mixer.clipAction(clip);
            action.play();

            let maxDeg = 0, maxBone = null, maxT = 0;
            const samples = 20;
            for (let i = 0; i <= samples; i++) {
                const t = (clip.duration * i) / samples;
                mixer.setTime(t);
                for (const b of bones) {
                    const bind = bindQuat.get(b.name);
                    const deg = THREE.MathUtils.radToDeg(2 * Math.acos(Math.min(1, Math.abs(bind.dot(b.quaternion)))));
                    if (deg > maxDeg) { maxDeg = deg; maxBone = b.name; maxT = t; }
                }
            }

            mixer.setTime(Math.min(clip.duration * 0.5, clip.duration));
            renderer.render(scene, camera);
            shots.push({ name: 'anim_' + clip.name, dataUrl: renderer.domElement.toDataURL('image/png') });

            // Also shoot the actual frame where the biggest rotation happens, so a mid-clip
            // screenshot can't miss motion that peaks elsewhere — from both the default angle and a
            // pure side view, since a forward/backward motion foreshortens from the front.
            mixer.setTime(maxT);
            renderer.render(scene, camera);
            shots.push({ name: 'peak_' + clip.name, dataUrl: renderer.domElement.toDataURL('image/png') });
            renderer.render(scene, sideCamera);
            shots.push({ name: 'peak_side_' + clip.name, dataUrl: renderer.domElement.toDataURL('image/png') });

            clipReports.push({ clip: clip.name, duration: clip.duration, maxRotationDeg: maxDeg, maxBone, maxT });

            mixer.stopAllAction();
            mixer.uncacheClip(clip);
        }
    }

    window.__report = {
        size: { x: size.x, y: size.y, z: size.z },
        center: { x: center.x, y: center.y, z: center.z },
        rootNodeCount: gltf.scene.children.length,
        boneCount: boneNames.length,
        boneNames,
        animationNames: gltf.animations.map((a) => a.name),
        clipReports,
        shots,
    };
    window.__done = true;
}
run().catch((e) => { window.__error = String(e && e.stack || e); window.__done = true; });
`;

const routes = {
    '/': { type: 'text/html', body: html },
    '/main.js': { type: 'text/javascript', body: mainJs },
    '/model.glb': { type: 'model/gltf-binary', body: glbBytes },
};

const server = http.createServer(async (req, res) => {
    const route = routes[req.url];
    if (route) {
        res.writeHead(200, { 'Content-Type': route.type });
        res.end(route.body);
        return;
    }
    if (req.url.startsWith('/three/')) {
        try {
            const filePath = path.join(threeRoot, req.url.slice('/three/'.length));
            const body = await readFile(filePath);
            res.writeHead(200, { 'Content-Type': 'text/javascript' });
            res.end(body);
        } catch {
            res.writeHead(404);
            res.end();
        }
        return;
    }
    res.writeHead(404);
    res.end();
});
await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
const port = server.address().port;

const browser = await puppeteer.launch({ headless: true });
const page = await browser.newPage();
await page.setViewport({ width: 800, height: 800 });
page.on('console', (msg) => console.log('[page]', msg.text()));
page.on('pageerror', (err) => console.error('[pageerror]', err));

await page.goto(`http://127.0.0.1:${port}/`);
await page.waitForFunction('window.__done === true', { timeout: 30000 });

const error = await page.evaluate(() => window.__error);
if (error) {
    console.error('Render error:', error);
    await browser.close();
    server.close();
    process.exit(1);
}

const report = await page.evaluate(() => window.__report);

for (const shot of report.shots) {
    const b64 = shot.dataUrl.replace(/^data:image\/png;base64,/, '');
    await writeFile(path.join(outDir, `${shot.name}.png`), Buffer.from(b64, 'base64'));
}
delete report.shots;

console.log(JSON.stringify(report, null, 2));
await writeFile(path.join(outDir, 'report.json'), JSON.stringify(report, null, 2));

await browser.close();
server.close();
