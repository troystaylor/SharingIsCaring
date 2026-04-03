# Azure AI Content Safety

## Overview

The Azure AI Content Safety connector provides comprehensive content moderation using Azure AI Content Safety. It covers harm detection (Hate, SelfHarm, Sexual, Violence), prompt injection shielding, protected material detection, groundedness checking, task adherence for agents, custom categories, and full blocklist management. 17 operations + 11 MCP tools.

Pairs with any AI connector (Foundry OptiMind, Phi-4, etc.) to validate LLM outputs before returning to users, or use standalone to moderate user-generated content.

## Prerequisites

1. An Azure subscription
2. An [Azure AI Content Safety](https://azure.microsoft.com/products/ai-services/ai-content-safety) resource (or any Azure AI Services multi-service resource)
3. Note the **Resource Name** and **API Key** from Keys and Endpoint

## Connection Setup

| Parameter | Description | Example |
|---|---|---|
| **Resource Name** | The Azure AI Services resource name | `my-safety-resource` |
| **API Key** | Subscription key from Keys and Endpoint | (from Azure Portal) |

## Operations

### Check Text Safety

Simplified safety check — returns a clear `is_safe` true/false result.

| Parameter | Required | Description |
|---|---|---|
| Text | Yes | The text to check (max 10,000 characters) |
| Threshold | No | Max allowed severity (0-6). Default: 2. Content at or above this is flagged unsafe. |
| Blocklist Names | No | Custom blocklists to check against |

**Returns:**

| Field | Description |
|---|---|
| Is Safe | `true` if no category exceeds the threshold and no blocklist matches |
| Highest Category | The category with the highest severity (Hate, SelfHarm, Sexual, Violence, or None) |
| Highest Severity | The highest severity score found (0-6) |
| Threshold | The threshold used |
| Blocklist Hit | `true` if any blocklist items matched |
| Categories | All four category scores: `{ Hate: 0, SelfHarm: 0, Sexual: 0, Violence: 0 }` |

### Check Image Safety

Simplified image safety check — same `is_safe` pattern.

| Parameter | Required | Description |
|---|---|---|
| Image Content (Base64) | * | Base64-encoded image (provide this OR Image URL) |
| Image URL | * | Azure Blob Storage URL (provide this OR Image Content) |
| Threshold | No | Max allowed severity (0-6). Default: 2. |

### Analyze Text

Full analysis with detailed severity scores and blocklist match details.

| Parameter | Required | Description |
|---|---|---|
| Text | Yes | The text to analyze |
| Categories | No | Specific categories to check (omit for all four) |
| Blocklist Names | No | Custom blocklists to check |
| Halt on Blocklist Hit | No | If true, skip harm analysis when blocklist matches |
| Output Type | No | `FourSeverityLevels` (0,2,4,6) or `EightSeverityLevels` (0-7) |

### Analyze Image

Full image analysis with detailed severity scores.

| Parameter | Required | Description |
|---|---|---|
| Image | Yes | Object with `content` (base64) or `blobUrl` (Azure Blob Storage URL) |
| Categories | No | Specific categories to check |

### Shield Prompt

Detect prompt injection attacks in user prompts and documents. Identifies both direct jailbreak attempts (user trying to bypass safety rules) and indirect attacks (malicious instructions embedded in documents). Use before passing user input to an LLM.

| Parameter | Required | Description |
|---|---|---|
| User Prompt | * | The user prompt to check (provide this and/or Documents) |
| Documents | * | Array of documents to check for indirect injection attacks |

**Returns:** `{ userPromptAnalysis: { attackDetected: true/false }, documentsAnalysis: [{ attackDetected: true/false }] }`

### Detect Protected Material (Text)

Check if AI-generated text contains known protected material such as song lyrics, articles, recipes, or selected web content. Use on LLM outputs before returning to users to avoid copyright issues. GA.

| Parameter | Required | Description |
|---|---|---|
| Text | Yes | The AI-generated text to check (min 110, max 10,000 characters) |

**Returns:** `{ protectedMaterialAnalysis: { detected: true/false } }`

### Detect Protected Material (Code)

Check if AI-generated code matches known code from public GitHub repositories. Preview — code index is current through April 2023.

| Parameter | Required | Description |
|---|---|---|
| Code | Yes | The AI-generated code to check |

**Returns:** `{ protectedMaterialAnalysis: { detected, codeCitations: [{ license, sourceUrl }] } }`

### Detect Groundedness

Check if an LLM response is grounded (factually consistent) with provided source materials. Detects hallucinations and fabricated information. Preview feature.

| Parameter | Required | Description |
|---|---|---|
| Text | Yes | The LLM-generated text to check (max 7,500 characters) |
| Grounding Sources | Yes | Source documents the text should be grounded in (max 55,000 chars total) |
| Domain | No | `Generic` (default) or `Medical` |
| Task | No | `QnA` (default) or `Summarization` |
| QnA Query | No | The user's original question (for QnA tasks) |
| Enable Reasoning | No | Enable detailed explanations (requires Azure OpenAI resource) |

**Returns:** `{ ungroundedDetected, ungroundedPercentage, ungroundedDetails: [{ text, reason }] }`

### Detect Task Adherence

Check if an AI agent's tool calls are aligned with the user's intent. Detects misaligned, unintended, or premature tool invocations in agent workflows. Preview feature.

| Parameter | Required | Description |
|---|---|---|
| Tools | Yes | Array of tool definitions available to the agent |
| Messages | Yes | Array of conversation messages (user, assistant, tool interactions) |

**Returns:** `{ taskRiskDetected: true/false, details: "explanation of detected risk" }`

### Analyze Custom Category (Rapid)

Check text against a custom-defined category using a name, definition, and optional few-shot examples. Define new safety rules on the fly without training a model. Preview feature.

| Parameter | Required | Description |
|---|---|---|
| Text | Yes | The text to analyze (max 1,000 characters) |
| Category Name | Yes | Name of the custom category (e.g., 'PoliticalContent') |
| Definition | Yes | Description of what this category represents |
| Sample Texts | No | Array of `{ text, label }` examples for few-shot learning |

**Returns:** `{ customCategoryAnalysis: { detected: true/false } }`

### Create or Update Blocklist

Create a new custom text blocklist or update an existing one.

| Parameter | Required | Description |
|---|---|---|
| Blocklist Name | Yes | Name (alphanumeric, dashes, underscores) |
| Description | No | Description of the blocklist |

### Add Blocklist Items

Add terms to a custom blocklist.

| Parameter | Required | Description |
|---|---|---|
| Blocklist Name | Yes | The blocklist to add items to |
| Items | Yes | Array of `{ text, description }` objects (text max 128 characters) |

### List Blocklists

List all custom text blocklists in the resource.

### List Blocklist Items

List all items in a specific blocklist.

| Parameter | Required | Description |
|---|---|---|
| Blocklist Name | Yes | The blocklist to list items from |

### Remove Blocklist Items

Remove items from a blocklist by their IDs.

| Parameter | Required | Description |
|---|---|---|
| Blocklist Name | Yes | The blocklist to remove items from |
| Item IDs | Yes | Array of blocklist item IDs to remove |

### Delete Blocklist

Delete an entire text blocklist.

| Parameter | Required | Description |
|---|---|---|
| Blocklist Name | Yes | The blocklist to delete |

## Severity Levels

| Score | Meaning |
|---|---|
| 0 | Safe — no harmful content detected |
| 2 | Low severity — mildly concerning |
| 4 | Medium severity — clearly harmful |
| 6 | High severity — severely harmful |

When using `EightSeverityLevels` output type, intermediate scores (1, 3, 5, 7) provide finer granularity.

## Example Workflows

### Validate LLM Output
1. Generate response with Microsoft Foundry OptiMind/Phi-4
2. **CheckTextSafety** with threshold 2
3. If `is_safe` = false → return a safe fallback message instead

### Content Moderation Pipeline
1. User submits a comment in Power Apps
2. **CheckTextSafety** with custom blocklist for org-specific terms
3. If safe → publish; if unsafe → flag for human review

### Image Upload Screening
1. User uploads image via Power Apps
2. Convert to base64 → **CheckImageSafety** with threshold 2
3. If safe → store in SharePoint; if unsafe → reject with message

## MCP Protocol Support

This connector includes an MCP endpoint for Copilot Studio with 11 tools:

| Tool | Description |
|---|---|
| `check_text_safety` | Simple safe/unsafe text check with configurable threshold |
| `check_image_safety` | Simple safe/unsafe image check |
| `analyze_text` | Full text analysis with all severity scores |
| `analyze_image` | Full image analysis with all severity scores |
| `shield_prompt` | Detect prompt injection/jailbreak attacks |
| `detect_protected_material` | Check for copyrighted text in LLM output |
| `detect_protected_code` | Check for known GitHub code in LLM output |
| `detect_groundedness` | Check if LLM response is grounded in sources |
| `detect_task_adherence` | Check if agent tool calls align with user intent |
| `analyze_custom_category` | Check text against a custom-defined category |
| (blocklist management) | Available via REST operations only |

## Known Limitations

- Text analysis limited to 10,000 characters per request
- Image analysis limited to 2048x2048 pixels, 4MB max, 50x50 min
- Image analysis accepts base64 or Azure Blob Storage URLs only (not public HTTP URLs)
- Blocklist item text limited to 128 characters
- The `blobUrl` for images must be an Azure Blob Storage URL with appropriate access (SAS token or public access)
- Protected material text detection requires minimum 110 characters of input
- Protected material code index is current through April 2023 only
- Groundedness detection supports max 7,500 character text and 55,000 character grounding sources
- Custom categories (rapid) limited to 1,000 character input
- Task adherence is preview and requires `2025-09-15-preview` API version
- Groundedness, protected code, and custom categories use preview API versions

## Authors

- Troy Taylor, troy@troystaylor.com, https://github.com/troystaylor
