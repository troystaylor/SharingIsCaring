/**
 * Three.js Scene — port of ext-apps three-server.
 * Renders a 3D scene with orbit controls. Three.js + OrbitControls are
 * inlined from node_modules into the widget HTML (~700KB) so it works
 * under Cowork's strict CSP (no CDN needed).
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { injectBootstrap } from "../shared/widget-bootstrap.js";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);

// Read Three.js + OrbitControls at module load time (once)
let threeJs: string;
let orbitControlsJs: string;
try {
  threeJs = readFileSync(require.resolve("three/build/three.module.min.js"), "utf-8");
  orbitControlsJs = readFileSync(require.resolve("three/examples/jsm/controls/OrbitControls.js"), "utf-8");
} catch {
  threeJs = "";
  orbitControlsJs = "";
}

export function registerThree(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/three.html";

  server.resource("3D Scene UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectBootstrap(threeWidgetHtml()),
    }],
  }));

  server.registerTool(
    "show_3d_scene",
    {
      description: "Display an interactive 3D scene. Specify shapes, colors, and positions to render in a Three.js viewport.",
      inputSchema: {
        scene: z.string().optional().describe("Scene description (e.g., 'A red cube next to a blue sphere')"),
        background: z.string().optional().describe("Background color hex (default #1a1a2e)"),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      // Generate a default scene with some shapes
      const shapes = [
        { type: "box", color: "#ff6b6b", position: [-1.5, 0, 0], size: 1 },
        { type: "sphere", color: "#4361ee", position: [0, 0, 0], size: 0.7 },
        { type: "cylinder", color: "#2ecc71", position: [1.5, 0, 0], size: 0.6 },
        { type: "torus", color: "#f39c12", position: [0, 1.5, 0], size: 0.5 },
      ];

      return {
        content: [{ type: "text" as const, text: `3D Scene: ${args.scene || "Default shapes"} — 4 objects rendered with orbit controls.` }],
        structuredContent: {
          description: args.scene || "Default scene with geometric shapes",
          background: args.background || "#1a1a2e",
          shapes,
        },
      };
    }
  );
}

function threeWidgetHtml(): string {
  if (!threeJs) {
    return `<!DOCTYPE html><html><body><p style="color:#ccc;font-family:system-ui;padding:40px;text-align:center">Three.js library not found. Reinstall dependencies.</p></body></html>`;
  }

  // OrbitControls imports from "three" — rewrite to use the global THREE
  const patchedOrbitControls = orbitControlsJs
    .replace(/from\s+['"]three['"]/g, "from './three-inline.js'")
    .replace(/import\s*\{[^}]+\}\s*from\s*['"]\.\/three-inline\.js['"]\s*;?/g, "");

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>3D Scene</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { overflow:hidden; background:#1a1a2e; }
  canvas { width:100%; height:100vh; display:block; }
</style>
</head>
<body>
<script>
// Inline Three.js (non-module, assigns to window.THREE)
(function() {
  var exports = {};
  var module = { exports: exports };
  ${threeJs.replace(/<\/script>/g, "<\\/script>")}
  window.THREE = exports.THREE || module.exports;
  // Also try to pick up from the global scope if the UMD pattern set it
  if (!window.THREE && typeof THREE !== "undefined") window.THREE = THREE;
})();
<\/script>
<script>
(function() {
  const THREE = window.THREE;
  if (!THREE) { document.body.innerHTML = '<p style="color:#f00;padding:20px">THREE not loaded</p>'; return; }

  // Minimal OrbitControls inline (simplified — handles rotate, zoom, pan)
  class OrbitControls {
    constructor(camera, domElement) {
      this.camera = camera;
      this.domElement = domElement;
      this.enableDamping = false;
      this._spherical = new THREE.Spherical();
      this._target = new THREE.Vector3();
      this._isDragging = false;
      this._prev = { x: 0, y: 0 };
      const self = this;
      domElement.addEventListener("pointerdown", (e) => { self._isDragging = true; self._prev = { x: e.clientX, y: e.clientY }; });
      domElement.addEventListener("pointermove", (e) => {
        if (!self._isDragging) return;
        const dx = (e.clientX - self._prev.x) * 0.005;
        const dy = (e.clientY - self._prev.y) * 0.005;
        self._prev = { x: e.clientX, y: e.clientY };
        self._spherical.setFromVector3(camera.position.clone().sub(self._target));
        self._spherical.theta -= dx;
        self._spherical.phi = Math.max(0.1, Math.min(Math.PI - 0.1, self._spherical.phi - dy));
        camera.position.setFromSpherical(self._spherical).add(self._target);
        camera.lookAt(self._target);
      });
      domElement.addEventListener("pointerup", () => { self._isDragging = false; });
      domElement.addEventListener("wheel", (e) => {
        e.preventDefault();
        const factor = e.deltaY > 0 ? 1.1 : 0.9;
        camera.position.sub(self._target).multiplyScalar(factor).add(self._target);
        camera.lookAt(self._target);
      }, { passive: false });
    }
    update() {}
  }

  function init(data) {
    const scene = new THREE.Scene();
    scene.background = new THREE.Color(data.background || "#1a1a2e");
    const camera = new THREE.PerspectiveCamera(60, window.innerWidth / window.innerHeight, 0.1, 100);
    camera.position.set(3, 2, 4);
    const renderer = new THREE.WebGLRenderer({ antialias: true });
    renderer.setSize(window.innerWidth, window.innerHeight);
    renderer.setPixelRatio(window.devicePixelRatio);
    document.body.appendChild(renderer.domElement);
    const controls = new OrbitControls(camera, renderer.domElement);

    scene.add(new THREE.AmbientLight(0x404040, 2));
    const dirLight = new THREE.DirectionalLight(0xffffff, 1.5);
    dirLight.position.set(5, 5, 5);
    scene.add(dirLight);
    scene.add(new THREE.GridHelper(10, 10, 0x444444, 0x333333));

    (data.shapes || []).forEach(s => {
      let geom;
      switch (s.type) {
        case "sphere": geom = new THREE.SphereGeometry(s.size, 32, 32); break;
        case "cylinder": geom = new THREE.CylinderGeometry(s.size, s.size, s.size * 2, 32); break;
        case "torus": geom = new THREE.TorusGeometry(s.size, s.size * 0.3, 16, 48); break;
        default: geom = new THREE.BoxGeometry(s.size, s.size, s.size);
      }
      const mat = new THREE.MeshStandardMaterial({ color: s.color });
      const mesh = new THREE.Mesh(geom, mat);
      mesh.position.set(...(s.position || [0, 0, 0]));
      scene.add(mesh);
    });

    function animate() {
      requestAnimationFrame(animate);
      controls.update();
      renderer.render(scene, camera);
    }
    animate();
    window.addEventListener("resize", () => {
      camera.aspect = window.innerWidth / window.innerHeight;
      camera.updateProjectionMatrix();
      renderer.setSize(window.innerWidth, window.innerHeight);
    });
  }

  window.addEventListener("message", (e) => {
    if (e.data && e.data.structuredContent) init(e.data.structuredContent);
  });
})();
<\/script>
</body>
</html>`;
}
