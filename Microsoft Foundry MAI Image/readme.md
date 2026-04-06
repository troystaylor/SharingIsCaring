# Microsoft Foundry MAI Image

## Overview

Generate photorealistic images using MAI-Image-2 and other Microsoft Foundry image generation models. This connector provides both REST operations for Power Automate flows and MCP protocol support for Copilot Studio agents.

MAI-Image-2 is ranked top 3 on the Arena.ai text-to-image leaderboard. It delivers 2x faster generation with accurate skin tones, natural lighting, and clear in-image text rendering — built for photographers, designers, and visual storytellers.

## Prerequisites

- A Microsoft Foundry resource with an image generation model deployed (MAI-Image-2 or gpt-image-1.5)
- Resource name and API key from the Azure portal

## Connection Setup

1. **Resource Name** — Your Microsoft Foundry resource name (just the name, not the full URL)
2. **API Key** — The API key from your Microsoft Foundry resource

## Operations

### Generate Photorealistic Image

Generate images from text prompts. Parameters:

| Parameter | Required | Description |
|-----------|----------|-------------|
| Prompt | Yes | Text description of the desired image (max 32,000 chars) |
| Model | No | Model name (e.g., MAI-Image-2, gpt-image-1.5) |
| Size | No | 1024x1024 (square), 1536x1024 (landscape), 1024x1536 (portrait), auto |
| Quality | No | low, medium, high, auto |
| Style | No | vivid (bold colors) or natural (photorealistic) |
| Response Format | No | url (temporary download link) or b64_json (base64 data) |
| Output Format | No | png, jpeg, webp |

### Chat Completion

Borrowed from the parent Microsoft Foundry connector. Use to describe, interpret, or discuss generated images within a Copilot Studio MCP agent conversation.

### Invoke MCP

Model Context Protocol endpoint for Copilot Studio integration. Exposes `generate_image` and `chat_completion` tools via JSON-RPC 2.0.

## MCP Tools

| Tool | Description |
|------|-------------|
| `generate_image` | Generate photorealistic images from text prompts |
| `chat_completion` | Send a chat message to describe or discuss images (borrowed) |

## Pricing

- **MAI-Image-2**: $5 per 1M text input tokens + $33 per 1M image output tokens
- **gpt-image-1.5**: Varies by quality (low ~$0.009, medium ~$0.034, high ~$0.133 per 1024x1024)

## Use Cases

- Marketing content and campaign imagery
- Product visualization and mockups
- Branded asset creation (social media, ads, banners)
- Presentation graphics with in-image text
- Creative concept exploration

## Known Limitations

- Image URLs expire after 60 minutes (use b64_json for persistent storage)
- Base64 responses may be large (~1MB+ for high quality PNG)
- Maximum 10 images per request
- Maximum 32,000 character prompt
