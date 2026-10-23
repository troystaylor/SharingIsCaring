# Cohere

A dual-purpose Power Platform custom connector for the **Cohere API** (v2). One connector serves two audiences:

| Audience | Surface | What you get |
|---|---|---|
| Power Automate, Power Apps | 23 typed REST actions | Chat, Ask, Embed, Rerank, Parse, Transcribe, tokenization, models, batches, datasets and embed jobs, with model and ID dropdowns |
| Copilot Studio | MCP endpoint at `/mcp` | 15 tools covering chat, embeddings, reranking, document parsing, transcription, tokenization, models, batches and datasets |

It covers Cohere's current model families: **Command A+** (chat, tools, vision, reasoning), **Embed v5** (text and images), **Rerank 5** (with Context Focus), **Parse** and **Cohere Transcribe**.

---

## Prerequisites

- A Cohere account and an API key from the [Cohere dashboard](https://dashboard.cohere.com/api-keys). A trial key works, with rate limits.
- A Power Platform environment where you can create custom connectors.
- [Power Platform CLI](https://learn.microsoft.com/power-platform/developer/cli/introduction) (`pac`), signed in to your environment.

---

## Deploy

From this folder:

```powershell
pac auth create --environment <YOUR_ENVIRONMENT_ID>

pac connector create `
    --environment <YOUR_ENVIRONMENT_ID> `
    --api-definition-file apiDefinition.swagger.json `
    --api-properties-file apiProperties.json `
    --script-file script.csx
```

Then create a connection. Enter your Cohere API key in the **API Key** field, without the word `Bearer`.

> `Chat`, `Ask`, `Rerank` and `InvokeMCP` run through `script.csx`. Every other action is passed straight to Cohere.

---

## Power Automate and Power Apps actions

| Action | Cohere endpoint | Notes |
|---|---|---|
| **Ask a model** | `POST /v2/chat` | Simplest way to get text back: a prompt, an optional system prompt, and optional grounding documents. Returns `text`, `citations` and `usage`. |
| **Chat** | `POST /v2/chat` | Full chat request: message history, tools, documents, structured output, thinking. Streaming is off; the response adds a joined `text` field. |
| **Create embeddings** | `POST /v2/embed` | Text, images or mixed inputs. Supports `output_dimension` and multiple `embedding_types`. |
| **Rerank documents** | `POST /v2/rerank` | Adds the original `document` text to each result. With `relevant_spans` enabled (Rerank v5), each span also gets its `text`. |
| **Parse a document image** | `POST /v2/parse` | Markdown or block output from a page image. |
| **Transcribe audio** | `POST /v2/audio/transcriptions` | flac, mp3, mpeg, mpga, ogg or wav. |
| **Tokenize text** / **Detokenize tokens** | `POST /v1/tokenize`, `/v1/detokenize` | Count tokens or convert them back to text. |
| **List models** / **Get a model** | `GET /v1/models`, `/v1/models/{model}` | Powers the model dropdowns in the other actions. |
| **Check API key** | `POST /v1/check-api-key` | Handy for validating a connection in a flow. |
| **List / Get / Create / Cancel batch** | `/v2/batches` | Run a model over an uploaded dataset. |
| **List / Get / Delete dataset** | `/v1/datasets` | Datasets are uploaded in the Cohere dashboard or API; the connector reads and deletes them. |
| **List / Get / Create / Cancel embed job** | `/v1/embed-jobs` | Embed a whole dataset asynchronously. |

Model fields show a dropdown filled from **List models**. You can also type any model name, for example `command-a-plus-05-2026`.

### Current model names

| Use | Model |
|---|---|
| Chat | `command-a-plus-05-2026`, `command-a-03-2025`, `command-r7b-12-2024`, `command-a-translate-08-2025`, `command-a-reasoning-08-2025`, `command-a-vision-07-2025` |
| Embed | `embed-v5.0-pro`, `embed-v5.0-fast` (256 to 2048 dimensions) |
| Rerank | `rerank-v5.0-pro`, `rerank-v5.0-fast` |
| Parse | `parse-v5.0` |
| Transcribe | `cohere-transcribe-03-2026` |

Cohere retires models over time. **List models** shows what your key can use today.

---

## Copilot Studio (MCP)

Add the connector to an agent as a tool. The agent discovers these tools:

| Tool | What it does |
|---|---|
| `cohere_chat` | Prompt in, text out, with optional system prompt and grounding documents |
| `cohere_embed` | Embeddings for a list of texts |
| `cohere_rerank` | Rank documents against a query; optional `top_n`, `min_score` and relevant passages |
| `cohere_parse_document` | Markdown or blocks from a document page image URL or data URI |
| `cohere_transcribe_audio` | Transcribe from an `audio_url` (https) or `audio_base64` |
| `cohere_tokenize`, `cohere_detokenize` | Token counting and conversion |
| `cohere_list_models`, `cohere_get_model` | Discover models and their limits |
| `cohere_check_api_key` | Confirm the connection works |
| `cohere_list_batches`, `cohere_get_batch`, `cohere_create_batch`, `cohere_cancel_batch` | Manage batch jobs |
| `cohere_list_datasets` | Find dataset IDs for batches |

Tool defaults: chat uses `command-a-plus-05-2026`, embed uses `embed-v5.0-pro`, rerank uses `rerank-v5.0-pro`, parse uses `parse-v5.0`, and transcribe uses `cohere-transcribe-03-2026`. Agents can override any of them with the `model` argument.

Try these prompts in the test chat:

- "What Cohere tools do you have?"
- "Rerank these three passages for the query 'refund policy' and keep only the ones scoring above 0.5."
- "Which Cohere models can I use for embeddings?"

---

## Optional: Application Insights telemetry

Telemetry is off by default. To turn it on, open `script.csx`, set your instrumentation key, and change the switch:

```csharp
private const bool APP_INSIGHTS_ENABLED = true;
private const string APP_INSIGHTS_KEY = "<your-instrumentation-key>";
```

Then update the connector:

```powershell
pac connector update --connector-id <YOUR_CONNECTOR_ID> `
    --api-definition-file apiDefinition.swagger.json `
    --api-properties-file apiProperties.json `
    --script-file script.csx
```

Events cover each operation, each MCP tool call and every error. Prompts and responses are not logged.

---

## Limits to know about

- **Parse** accepts page images only (public URL or base64 data URI, up to 20 MB). PDFs are not supported by Cohere yet.
- **Embed** accepts up to 96 texts per call.
- **Rerank** works best with up to 1,000 documents per call. Context Focus (`relevant_spans`) needs a Rerank v5 model.
- **Chat** does not stream through this connector.
- **Transcribe** through MCP needs either an https URL or base64 audio plus a file name with its extension.
- Cohere enforces rate limits per key. A `429` response means you should retry later.

---

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `401 invalid api token` | The key is wrong or revoked. Create a new connection with a valid key and don't include `Bearer`. |
| `403` or "model not available" | Your plan or key can't use that model. Check **List models**. |
| `404 model not found` | The model name is retired or misspelled. Pick one from the dropdown. |
| `400` on **Chat** with `response_format` | Structured output can't be combined with `documents` or `tools`. |
| `400` on **Rerank** with `relevant_spans` | Use a Rerank v5 model. |
| `429 Too Many Requests` | You hit your rate limit. Slow down or move off a trial key. |
| MCP tool returns "Tool execution failed" | The message includes Cohere's own error. Fix the arguments and ask the agent to retry. |
| Model dropdown is empty | **List models** failed. Confirm the connection with **Check API key**. |

---

## Resources

- [Cohere API reference](https://docs.cohere.com/reference/about)
- [Cohere models](https://docs.cohere.com/docs/models)
- [Cohere rate limits](https://docs.cohere.com/docs/rate-limits)

---

**Author:** Troy Taylor ([troy@troystaylor.com](mailto:troy@troystaylor.com), [GitHub](https://github.com/troystaylor))
