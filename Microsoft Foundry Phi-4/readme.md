# Microsoft Foundry Phi-4

## Overview

The Microsoft Foundry Phi-4 connector integrates three models from Microsoft's Phi-4 family into Power Platform:

- **Phi-4-Reasoning-Vision-15B** — compact multimodal reasoning model excelling at math, science, and UI understanding
- **Phi-4-multimodal-instruct** — processes text, images, and audio simultaneously (5.6B params, 23 languages)
- **Phi-4-mini-instruct** — lightweight text-only chat model (3.8B params, 131K context, 23 languages)

All three models are self-sufficient — no borrowed tools are needed. They produce human-readable outputs natively.

## Prerequisites

1. An Azure subscription with access to Microsoft Foundry
2. Deploy one or more Phi-4 models from the Foundry Model Catalog:
   - [Phi-4-Reasoning-Vision-15B](https://ai.azure.com/explore/models/Phi-4-Reasoning-Vision-15B/version/1/registry/azureml-phi-prod) — for vision reasoning
   - [Phi-4-multimodal-instruct](https://ai.azure.com/explore/models/Phi-4-multimodal-instruct/version/1/registry/azureml) — for multimodal chat
   - [Phi-4-mini-instruct](https://ai.azure.com/explore/models/Phi-4-mini-instruct/version/1/registry/azureml) — for text chat
3. Note the **Resource Name** (e.g., `my-foundry-resource` from `https://my-foundry-resource.services.ai.azure.com`) and **API Key** from the deployment

## Connection Setup

| Parameter | Description | Example |
|---|---|---|
| **Resource Name** | The Azure AI Services resource name | `my-foundry-resource` |
| **API Key** | API key from the Foundry portal | (from Keys & Endpoint) |

## Operations

### Reason With Vision

Send an image and text prompt to Phi-4-Reasoning-Vision-15B for visual reasoning. Returns step-by-step reasoning and a final answer.

| Parameter | Required | Description |
|---|---|---|
| Prompt | Yes | The question or instruction about the image |
| Image URL | Yes | URL or base64 data URI of the image to analyze |
| System Prompt | No | Optional system instructions |
| Temperature | No | Sampling temperature (default: 0.7) |
| Max Tokens | No | Maximum tokens to generate (default: 4096) |

**Returns:**

| Field | Description |
|---|---|
| Reasoning | Step-by-step reasoning from the model |
| Answer | The final answer after reasoning |
| Model | Model identifier |
| Usage | Token usage statistics |

**Example use cases:** Document analysis with visual reasoning, invoice/form interpretation, screenshot-based UI testing, visual math problem solving, diagram interpretation.

### Chat Multimodal

Send text with optional images and audio to Phi-4-multimodal-instruct. Processes multiple input types simultaneously.

| Parameter | Required | Description |
|---|---|---|
| Prompt | Yes | The text message or question |
| Image URL | No | URL or base64 data URI of an image |
| Audio URL | No | URL or base64 data URI of an audio file |
| System Prompt | No | Optional system instructions |
| Temperature | No | Sampling temperature (default: 0.7) |
| Max Tokens | No | Maximum tokens to generate (default: 4096) |

**Returns:** Standard chat completion response with choices, message content, and usage statistics.

**Example use cases:** Multimodal document processing (voice annotation + image + text), real-time translation with image context, audio transcription with visual context.

### Chat Mini

Send a text chat completion request to Phi-4-mini-instruct. Lightweight model with fast inference.

| Parameter | Required | Description |
|---|---|---|
| Messages | Yes | Array of message objects with `role` (system/user/assistant) and `content` |
| Temperature | No | Sampling temperature (default: 0.7) |
| Top P | No | Nucleus sampling probability mass (default: 1.0) |
| Max Tokens | No | Maximum tokens to generate (default: 4096) |

**Returns:** Standard chat completion response with choices, message content, and usage statistics.

**Example use cases:** Quick text Q&A, content generation, code assistance, lightweight agent backends, function-calling scenarios.

## MCP Protocol Support

This connector includes an MCP (Model Context Protocol) endpoint for Copilot Studio integration with three tools:

| Tool | Description |
|---|---|
| `reason_with_vision` | Visual reasoning with image + text input |
| `chat_multimodal` | Multimodal chat with optional image and audio |
| `chat_mini` | Lightweight text-only chat |

## About the Phi-4 Models

| Model | Parameters | Context | Languages | Specialization |
|---|---|---|---|---|
| Phi-4-Reasoning-Vision-15B | 15B | 128K | en | Math, science, UI, visual reasoning |
| Phi-4-multimodal-instruct | 5.6B | 131K | 23 | Speech + vision + text simultaneously |
| Phi-4-mini-instruct | 3.8B | 131K | 23 | Fast text-only inference |

- **Developer:** Microsoft Research
- **License:** MIT
- **Status:** Public Preview (multimodal, mini), Experiment (Reasoning-Vision)

## Known Limitations

- Vision reasoning model outputs may include `<think>` tags — the connector extracts reasoning content automatically
- Audio input format for multimodal model may vary by deployment configuration
- All models are small language models — they may not match larger models on complex tasks
- Image and audio inputs must be accessible via URL or provided as base64 data URIs
- Only one image and one audio file can be sent per multimodal request through the simplified interface; use Chat Mini with raw messages for more complex multi-turn conversations

## Authors

- Troy Taylor, troy@troystaylor.com, https://github.com/troystaylor
