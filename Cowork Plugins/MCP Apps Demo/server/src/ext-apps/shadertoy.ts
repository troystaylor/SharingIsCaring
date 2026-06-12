/**
 * ShaderToy — port of ext-apps shadertoy-server.
 * Real-time GLSL fragment shader renderer using WebGL 2.0.
 * No external dependencies — pure WebGL, fully self-contained.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { injectBootstrap } from "../shared/widget-bootstrap.js";

const DEFAULT_SHADER = `
void mainImage(out vec4 fragColor, in vec2 fragCoord) {
    vec2 uv = fragCoord / iResolution.xy;
    vec3 col = 0.5 + 0.5 * cos(iTime + uv.xyx + vec3(0, 2, 4));
    fragColor = vec4(col, 1.0);
}`;

export function registerShadertoy(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/shadertoy.html";

  server.resource("ShaderToy UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectBootstrap(shadertoyWidgetHtml()),
    }],
  }));

  server.registerTool(
    "show_shader",
    {
      description: "Render a real-time GLSL fragment shader. Supports standard Shadertoy uniforms (iTime, iResolution, iMouse).",
      inputSchema: {
        shader: z.string().optional().describe("GLSL fragment shader code (uses mainImage convention). If omitted, a colorful default is shown."),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      const shader = args.shader || DEFAULT_SHADER;
      return {
        content: [{ type: "text" as const, text: `ShaderToy: Rendering ${shader.length}-character GLSL shader.` }],
        structuredContent: { shader },
      };
    }
  );
}

function shadertoyWidgetHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>ShaderToy</title>
<style>* { margin:0; padding:0; } body { overflow:hidden; } canvas { width:100%; height:100vh; display:block; }</style>
</head>
<body>
<canvas id="c"></canvas>
<script>
(function() {
  function render(data) {
    const canvas = document.getElementById("c");
    const gl = canvas.getContext("webgl2");
    if (!gl) return;

    canvas.width = canvas.offsetWidth * devicePixelRatio;
    canvas.height = canvas.offsetHeight * devicePixelRatio;
    gl.viewport(0, 0, canvas.width, canvas.height);

    const vs = gl.createShader(gl.VERTEX_SHADER);
    gl.shaderSource(vs, "attribute vec2 p;void main(){gl_Position=vec4(p,0,1);}");
    gl.compileShader(vs);

    const header = \`#version 300 es
precision highp float;
uniform float iTime;
uniform vec3 iResolution;
uniform vec4 iMouse;
out vec4 outColor;
\`;
    const footer = \`
void main() { mainImage(outColor, gl_FragCoord.xy); }
\`;

    const fs = gl.createShader(gl.FRAGMENT_SHADER);
    gl.shaderSource(fs, header + data.shader + footer);
    gl.compileShader(fs);

    if (!gl.getShaderParameter(fs, gl.COMPILE_STATUS)) {
      console.error("Shader error:", gl.getShaderInfoLog(fs));
      return;
    }

    const prog = gl.createProgram();
    gl.attachShader(prog, vs); gl.attachShader(prog, fs);
    gl.linkProgram(prog); gl.useProgram(prog);

    const buf = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, buf);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1,-1,1,-1,-1,1,1,1]), gl.STATIC_DRAW);
    const loc = gl.getAttribLocation(prog, "p");
    gl.enableVertexAttribArray(loc);
    gl.vertexAttribPointer(loc, 2, gl.FLOAT, false, 0, 0);

    const uTime = gl.getUniformLocation(prog, "iTime");
    const uRes = gl.getUniformLocation(prog, "iResolution");
    const start = performance.now();

    function frame() {
      gl.uniform1f(uTime, (performance.now() - start) / 1000);
      gl.uniform3f(uRes, canvas.width, canvas.height, 1);
      gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
      requestAnimationFrame(frame);
    }
    frame();
  }

  window.addEventListener("message", (e) => {
    if (e.data && e.data.structuredContent) render(e.data.structuredContent);
  });
})();
<\/script>
</body>
</html>`;
}
