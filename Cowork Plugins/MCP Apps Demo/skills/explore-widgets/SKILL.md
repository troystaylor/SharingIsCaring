---
name: explore-widgets
description: >
  Show interactive widgets, charts, 3D scenes, and visualizations.
  Use when the user says: "show me a chart", "display a widget",
  "render something visual", "show a demo", "QR code", "3D scene",
  "heatmap", "scatter plot", "budget chart", "scenario model",
  "basic demo", "what widgets are available"
---

# Explore Widgets

You help users discover and view interactive MCP Apps widgets.

## Workflow

1. If the user asks what's available, list the widget tools:
   - `basic_demo` — simple data-flow demo
   - `generate_qr` — QR code from text/URL
   - `allocate_budget` — donut chart budget allocator
   - `segment_customers` — scatter chart customer segmentation
   - `show_cohort_heatmap` — retention heatmap
   - `model_scenario` — SaaS revenue projector
   - `show_map` — interactive map with OpenStreetMap
   - `explore_wiki` — Wikipedia article network graph
   - `show_3d_scene` — Three.js 3D scene with orbit controls
   - `show_shader` — real-time GLSL fragment shader
   - `show_sheet_music` — ABC notation sheet music renderer
   - `show_system_monitor` — real-time CPU and memory metrics
   - `transcribe_audio` — live speech-to-text via Web Speech API
   - `show_video` — video player with sample content
   - `show_pdf` — PDF document viewer

2. If the user names a specific widget, call the matching tool directly.

3. Present the text summary from the tool response. The widget renders automatically alongside.

## Notes

- All synthetic-data widgets show a demo data disclaimer
- Widgets render inline in the conversation as interactive iframes
- If the widget doesn't render, the text response still provides the data
