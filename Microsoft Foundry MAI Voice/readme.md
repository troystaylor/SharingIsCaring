# Microsoft Foundry MAI Voice

## Overview

Generate natural, realistic speech using MAI-Voice-1 and other Microsoft Foundry text-to-speech models. This connector provides both REST operations for Power Automate flows and MCP protocol support for Copilot Studio agents.

MAI-Voice-1 delivers top-tier voice generation with emotional range, speaker identity preservation, and custom voice cloning from just seconds of audio. 60 seconds of audio generated in 1 second.

## Prerequisites

- A Microsoft Foundry resource with Speech Services enabled
- Resource name, API key, and region from the Azure portal

## Connection Setup

1. **Resource Name** — Your Microsoft Foundry resource name (just the name, not the full URL)
2. **API Key** — The API key from your Microsoft Foundry resource
3. **Region** — Azure region where your resource is deployed (e.g., eastus2, westus2)

## Operations

### Synthesize Speech

Convert text to speech audio using SSML (Speech Synthesis Markup Language).

| Parameter | Required | Description |
|-----------|----------|-------------|
| Output Format | Yes | Audio format (MP3, WAV, OGG at various quality levels) |
| SSML | Yes | SSML document specifying text, voice, language, and prosody |

**Example SSML:**
```xml
<speak version='1.0' xml:lang='en-US'>
  <voice xml:lang='en-US' name='en-US-JennyNeural'>
    Hello! This is a sample of text to speech.
  </voice>
</speak>
```

### List Available Voices

Get all available TTS voices for the configured region, including name, locale, gender, and supported speaking styles.

### Chat Completion

Borrowed from the parent Microsoft Foundry connector. Use to generate SSML markup, suggest voice selections, or write scripts for narration within a Copilot Studio MCP agent conversation.

### Invoke MCP

Model Context Protocol endpoint for Copilot Studio integration. Exposes `synthesize_speech`, `list_voices`, and `chat_completion` tools via JSON-RPC 2.0.

## MCP Tools

| Tool | Description |
|------|-------------|
| `synthesize_speech` | Convert text to speech audio. Provide text and voice name. |
| `list_voices` | Get available voices, optionally filtered by locale. |
| `chat_completion` | Generate SSML, suggest voices, or discuss speech parameters (borrowed). |

## Pricing

- **MAI-Voice-1**: $22 per 1M characters
- **Standard Neural voices**: Varies by region

## Use Cases

- Voice agent responses in Copilot Studio
- Audio content generation for podcasts and narration
- Accessibility — convert text content to speech
- Multilingual voice output
- Branded voice experiences with custom voice cloning
- Subtitle-to-audio conversion

## Audio Output Formats

| Format | Use Case |
|--------|----------|
| `audio-24khz-96kbitrate-mono-mp3` | General purpose (default) |
| `audio-48khz-192kbitrate-mono-mp3` | High quality |
| `riff-24khz-16bit-mono-pcm` | Uncompressed WAV |
| `ogg-24khz-16bit-mono-opus` | Web streaming |

## Known Limitations

- Audio output is capped at 10 minutes per request
- SSML body length is limited by the Speech Services API
- Custom voice cloning requires separate enrollment through Azure Speech Studio
- MCP `synthesize_speech` tool generates audio but cannot return binary data — use the REST `SynthesizeSpeech` operation to retrieve audio files directly
