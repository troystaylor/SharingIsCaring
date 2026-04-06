# Microsoft Foundry MAI Speech

## Overview

Transcribe and translate audio using MAI-Transcribe-1 and other Microsoft Foundry speech-to-text models. This connector provides both REST operations for Power Automate flows and MCP protocol support for Copilot Studio agents.

MAI-Transcribe-1 delivers state-of-the-art accuracy (3.9% average WER), 2.5x faster batch transcription than Azure Fast, and #1 ranking on FLEURS in 11 core languages. Designed for messy, real-world audio environments.

## Prerequisites

- A Microsoft Foundry resource with a speech-to-text model deployed (MAI-Transcribe-1 or Whisper)
- Resource name and API key from the Azure portal

## Connection Setup

1. **Resource Name** — Your Microsoft Foundry resource name (just the name, not the full URL)
2. **API Key** — The API key from your Microsoft Foundry resource

## Operations

### Transcribe Audio

Transcribe audio files into text. Parameters:

| Parameter | Required | Description |
|-----------|----------|-------------|
| Deployment ID | Yes | Model deployment name (e.g., MAI-Transcribe-1) |
| Audio File | Yes | Audio file (mp3, mp4, mpeg, mpga, m4a, wav, webm). Max 25 MB |
| Language | No | ISO-639-1 code (e.g., en, es, fr). Auto-detected if omitted |
| Prompt | No | Guide the model's style or continue a previous segment |
| Response Format | No | json, text, srt, verbose_json, vtt |
| Temperature | No | Sampling temperature (0-1). Default: 0 |

### Translate Audio to English

Translate audio from any supported language into English text. Same parameters as Transcribe Audio (except Language).

### Chat Completion

Use to summarize, analyze, or discuss transcription results within a Copilot Studio MCP agent conversation — extract key topics, generate meeting minutes, identify action items.

### Invoke MCP

Model Context Protocol endpoint for Copilot Studio integration. Exposes `transcribe_audio` and `chat_completion` tools via JSON-RPC 2.0.

## MCP Tools

| Tool | Description |
|------|-------------|
| `transcribe_audio` | Transcribe audio from a URL to text. Optional `deployment_id` parameter (defaults to MAI-Transcribe-1) |
| `chat_completion` | Send a chat message to summarize or discuss transcriptions |

## Pricing

- **MAI-Transcribe-1**: $0.36 per hour of audio
- **Whisper**: Varies by deployment

## Use Cases

- Meeting transcription and minutes generation
- Call recording analysis and compliance review
- Voice memo processing
- Multilingual content transcription (25+ languages)
- Podcast and media transcription
- Subtitle generation (SRT/VTT output formats)

## Known Limitations

- Audio files must be 25 MB or smaller
- Supported formats: mp3, mp4, mpeg, mpga, m4a, wav, webm
- For files larger than 25 MB, use Azure Speech batch transcription API
- MCP `transcribe_audio` tool downloads audio from URL — files must be publicly accessible or use a SAS URL
