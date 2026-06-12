# MCP Apps Demo for Copilot Cowork

A comprehensive demo plugin showcasing [MCP Apps](https://github.com/modelcontextprotocol/ext-apps) interactive widgets in [Microsoft 365 Copilot Cowork (Frontier)](https://learn.microsoft.com/en-us/microsoft-365/copilot/cowork/mcp-apps-support). Demonstrates all four Cowork interaction patterns: text tools, elicitation forms, read-only widgets, and interactive widgets with bidirectional communication.

## What's Included

### Custom Business Demos (with elicitation forms)

| Tool | Widget | Elicitation |
|------|--------|-------------|
| `show_sales_dashboard` | KPI cards, revenue chart, pipeline bars, deal table | Date range, region |
| `show_it_dashboard` | Severity donut, SLA gauge, incident table | Department, severity |
| `show_kanban` | Draggable card columns (To Do / In Progress / Done) | Project name |
| `show_weather` | Current conditions, temperature chart, daily cards | City, forecast days |
| `convert_units` | **No widget** — text-only elicitation demo | Value, from-unit, to-unit |

### Ext-Apps Ports

| Tool | Description |
|------|-------------|
| `generate_qr` | QR code from text/URL |
| `basic_demo` | MCP Apps data-flow hello world |
| `allocate_budget` | Donut chart budget allocator with sliders |
| `segment_customers` | Scatter chart — 50 customers, 4 segments |
| `show_cohort_heatmap` | Monthly retention heatmap |
| `model_scenario` | SaaS revenue projector with line chart |
| `show_map` | Interactive OpenStreetMap (requires CDN) |
| `explore_wiki` | Wikipedia article network graph (requires CDN) |
| `show_3d_scene` | Three.js 3D scene with orbit controls (inlined) |
| `show_shader` | Real-time GLSL fragment shader (pure WebGL) |
| `show_sheet_music` | ABC notation renderer (requires CDN) |
| `show_system_monitor` | CPU per-core + memory usage bars |
| `transcribe_audio` | Live speech-to-text via Web Speech API |
| `show_video` | Video player (requires CDN for media) |
| `show_pdf` | PDF viewer (requires CDN for PDF.js) |

### Classic Games

| Tool | Description |
|------|-------------|
| `play_snake` | Arrow keys to move, eat food, grow |
| `play_2048` | Slide tiles, combine matching numbers |
| `play_minesweeper` | Click to reveal, right-click to flag |
| `play_tetris` | Falling blocks — arrow keys + space to drop |

All mock-data tools display a demo data disclaimer in both the widget footer and the tool response text.

## Architecture

```
MCP Apps Demo/
├── manifest.json                    # M365 devPreview manifest
├── color.png / outline.png          # Fluent UI Apps icons
├── package.ps1                      # Plugin validation & packaging
├── skills/                          # 4 Cowork skills
│   ├── explore-widgets/SKILL.md
│   ├── manage-tasks/SKILL.md
│   ├── analyze-data/SKILL.md
│   └── play-games/SKILL.md
├── server/                          # Node.js MCP server
│   ├── package.json
│   ├── tsconfig.json
│   ├── Dockerfile
│   ├── src/
│   │   ├── index.ts                 # Express + Streamable HTTP transport
│   │   ├── server.ts                # Tool/resource registration
│   │   ├── shared/
│   │   │   ├── disclaimer.ts        # Demo data disclaimer injection
│   │   │   ├── elicitation.ts       # Elicitation helper
│   │   │   └── widget-bootstrap.ts  # Inline MCP Apps handshake
│   │   ├── custom/                  # Business demo tools + widgets
│   │   └── ext-apps/                # Ext-apps ports + games
│   └── widgets/                     # Widget HTML (inline in TS)
└── infra/                           # Azure Bicep (azd)
    ├── main.bicep
    ├── main.parameters.json
    └── modules/resources.bicep
```

## Prerequisites

- [Node.js](https://nodejs.org/) 22+
- [Azure Developer CLI (azd)](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/install-azd)
- An Azure subscription
- [Frontier preview program](https://adoption.microsoft.com/copilot/frontier-program/) access for Cowork
- M365 Admin access to sideload apps

## Deploy

### 1. Install dependencies

```bash
cd server
npm install
```

### 2. Run locally

```bash
npm run dev
# Server starts at http://localhost:8080
# Health: http://localhost:8080/health
# MCP:   http://localhost:8080/mcp
```

### 3. Deploy to Azure

```bash
# From project root
azd init
azd env set AZURE_LOCATION westus2
azd up

# After first deploy, bind ACR registry:
az containerapp registry set \
  -g rg-mcp-apps-demo \
  -n mcp-apps-demo \
  --server <your-acr>.azurecr.io \
  --identity system

# Redeploy:
azd deploy server --no-prompt
```

### 4. Update manifest

Replace `{{YOUR_CONTAINER_APP_FQDN}}` in `manifest.json` with your Container App's FQDN (e.g., `mcp-apps-demo.randomname-abc123.westus2.azurecontainerapps.io`).

### 5. Package and sideload

```powershell
.\package.ps1            # Validates and creates .zip
# Upload at M365 Admin Center → Agents → All Agents → Add Agent
```

## How It Works

### Widget Handshake

Cowork renders widgets in sandboxed iframes. Each widget includes an inline bootstrap script that implements the MCP Apps JSON-RPC 2.0 handshake without any external SDK (Cowork's CSP blocks CDN imports):

1. Widget → Host: `ui/initialize` request via `postMessage`
2. Host → Widget: Response with capabilities
3. Widget → Host: `ui/notifications/initialized`
4. Host → Widget: `ui/notifications/tool-result` with `structuredContent`

The bootstrap re-dispatches tool result data as a standard `MessageEvent` so each widget's render function works unchanged.

### Interaction Patterns

| Pattern | Example | How |
|---------|---------|-----|
| **Text only** | `convert_units` | No `_meta.ui` — returns text, uses elicitation for input |
| **Read-only widget** | `show_sales_dashboard` | `_meta.ui.resourceUri` + `structuredContent` |
| **Interactive widget** | `show_kanban` | Widget calls `window.__mcpCallTool()` for `move_card` |
| **App-only tools** | `refresh_sales` | `visibility: ["app"]` — agent can't call, only widget can |

### Auto-Resize

The bootstrap uses a `ResizeObserver` on `document.body` with 200ms debounce to report content height via `ui/notifications/size-changed`. Width uses `window.innerWidth` (host-controlled) to avoid feedback loops.

## Known Limitations

- **CDN-dependent widgets**: Map (Leaflet), Wiki Explorer (force-graph), Sheet Music (abcjs), Video, PDF (PDF.js) require CDN access which Cowork's iframe CSP may block
- **No outbound HTTPS from Container Apps**: The server cannot call external APIs (e.g., Open-Meteo). All data is mock/demo
- **Widget → server tool calls**: `__mcpCallTool()` may not work for all tool types in Cowork's current preview

## Credits

- [MCP Apps Extension (SEP-1865)](https://github.com/modelcontextprotocol/ext-apps) — protocol specification
- [Microsoft Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) — app icons (MIT)
- [Cowork MCP Apps Plugin Author Guide](https://learn.microsoft.com/en-us/microsoft-365/copilot/cowork/mcp-apps-support) — platform docs
