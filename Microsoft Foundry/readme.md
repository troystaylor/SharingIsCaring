# Microsoft Foundry

## Overview

A comprehensive custom connector for Microsoft Foundry (formerly Azure AI Foundry), providing access to:
- **Model Inference API** - Chat completions, text embeddings, image embeddings
- **AI Agents API** - Assistants, threads, messages, runs (Assistants v2)
- **Foundry Tools** - Content Safety (text & image), Image Analysis (Vision)
- **AI Evaluation** - Quality metrics (groundedness, relevance) and safety evaluation
- **Speech Services** - Audio transcription and translation via OpenAI Whisper
- **Translator** - Text translation, transliteration, and language detection
- **Language Services** - Sentiment analysis, entity recognition, key phrases, PII detection
- **Document Intelligence** - Document analysis and text extraction
- **Text-to-Speech** - Convert text to natural-sounding speech audio
- **Speaker Recognition** - Voice biometrics for speaker verification and enrollment
- **Custom Vision** - Image classification and object detection with custom models
- **Personalizer** - Contextual personalization and recommendation ranking
- **AI Search** - Full-text, semantic, and vector search over Azure AI Search indexes
- **OCR/Read API** - Extract printed and handwritten text from images
- **Batch Transcription** - Large-scale audio transcription jobs
- **MCP Protocol** - Model Context Protocol support for Copilot Studio integration
- **Application Insights** - Built-in telemetry support

## Prerequisites

- An Azure subscription with a Microsoft Foundry resource
- A deployed model on the Microsoft Foundry endpoint
- An API key from the Microsoft Foundry resource (or Entra ID authentication configured)

## Supported Operations

### Model Inference (api-version: 2024-05-01-preview)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **Chat Completion** | POST | `/models/chat/completions` | Generate chat completion responses using Foundry Models (serverless deployments). Requires `model` parameter in request body. |
| **Chat Completion (Azure OpenAI)** | POST | `/openai/deployments/{deployment}/chat/completions` | Generate chat completion responses using Azure OpenAI deployments. Use this for traditional deployments at `.cognitiveservices.azure.com`. |
| **Get Text Embeddings** | POST | `/models/embeddings` | Generate embedding vectors for text inputs. Useful for semantic search, clustering, and similarity comparison. |
| **Get Image Embeddings** | POST | `/models/images/embeddings` | Generate embedding vectors for images (base64-encoded). Supports multimodal models with optional text input. |
| **Get Model Info** | GET | `/models/info` | Retrieve information about the deployed model including name, type, and provider. |

#### Chat Completion Operations - Which to Use?

| Deployment Type | Endpoint | Operation | Notes |
|----------------|----------|-----------|-------|
| **Foundry Models** (Serverless) | `.services.ai.azure.com` | Chat Completion | Deploy via CLI: `az cognitiveservices account deployment create`. Requires `model` in request body. |
| **Azure OpenAI** (Traditional) | `.cognitiveservices.azure.com` | Chat Completion (Azure OpenAI) | Deploy via Azure Portal or CLI. Specify deployment name in path parameter. |

### AI Agents / Assistants API (api-version: 2025-04-01-preview)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **List Assistants** | GET | `/openai/assistants` | Returns a list of AI assistants. |
| **Create Assistant** | POST | `/openai/assistants` | Create an AI assistant with a model and instructions. |
| **Get Assistant** | GET | `/openai/assistants/{id}` | Retrieves an assistant by ID. |
| **Delete Assistant** | DELETE | `/openai/assistants/{id}` | Deletes an assistant. |
| **Create Thread** | POST | `/openai/threads` | Create a conversation thread. |
| **Get Thread** | GET | `/openai/threads/{id}` | Retrieves a thread by ID. |
| **Delete Thread** | DELETE | `/openai/threads/{id}` | Deletes a thread. |
| **List Messages** | GET | `/openai/threads/{id}/messages` | Returns messages from a thread. |
| **Create Message** | POST | `/openai/threads/{id}/messages` | Create a message in a thread. |
| **List Runs** | GET | `/openai/threads/{id}/runs` | Returns runs from a thread. |
| **Create Run** | POST | `/openai/threads/{id}/runs` | Create a run to execute an assistant on a thread. |
| **Get Run** | GET | `/openai/threads/{id}/runs/{run_id}` | Retrieves a run by ID. |
| **Create Thread and Run** | POST | `/openai/threads/runs` | Create a thread and run it in one request. |

### Foundry Tools - Content Safety (api-version: 2024-09-01)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **Analyze Text** | POST | `/contentsafety/text:analyze` | Analyze text for harmful content (Hate, SelfHarm, Sexual, Violence). Returns severity levels 0-6. |
| **Analyze Image** | POST | `/contentsafety/image:analyze` | Analyze images for harmful content. Supports base64 or blob URL input. |

**Severity Levels:**
- 0 = Safe
- 2 = Low
- 4 = Medium
- 6 = High

### Foundry Tools - Image Analysis / Vision (api-version: 2023-02-01-preview)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **Analyze Image (Vision)** | POST | `/imageanalysis:analyze` | Comprehensive image analysis including captions, tags, objects, OCR text, smart crops, and people detection. |

**Available Features:**
- `caption` - Brief description of the image
- `denseCaptions` - Multiple captions for different regions
- `tags` - Content tags with confidence scores
- `objects` - Detected objects with bounding boxes
- `read` - OCR text extraction
- `smartCrops` - Recommended crop regions
- `people` - People detection with bounding boxes

### AI Evaluation (api-version: 2022-11-01-preview)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **Submit Evaluation** | POST | `/evaluation/annotations:submit` | Submit content for AI-assisted evaluation. Returns operation ID for async results. |
| **Get Evaluation Result** | GET | `/evaluation/operations/{id}` | Retrieve evaluation results by operation ID. |

**Evaluation Types:**
- **Quality Metrics** (Score 1-5):
  - `groundedness` - Is the response grounded in provided context?
  - `relevance` - Is the response relevant to the query?
  - `coherence` - Is the response well-structured?
  - `fluency` - Is the response natural and fluent?
  - `similarity` - How similar is the response to ground truth?

- **Safety Metrics** (Severity 0-6):
  - `hate` - Hate speech detection
  - `violence` - Violence content detection
  - `selfharm` - Self-harm content detection
  - `sexual` - Sexual content detection

### Speech Services (api-version: 2024-10-21)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **Transcribe Audio** | POST | `/openai/deployments/{deployment_id}/audio/transcriptions` | Transcribe audio to text using OpenAI Whisper model. |
| **Translate Audio** | POST | `/openai/deployments/{deployment_id}/audio/translations` | Translate audio to English using OpenAI Whisper model. |

**Supported Audio Formats:**
- `mp3`, `mp4`, `mpeg`, `mpga`, `m4a`, `wav`, `webm`

### Translator (api-version: 3.0)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **Translate Text** | POST | `/translate` | Translate text from one language to another. Supports 100+ languages. |
| **Transliterate Text** | POST | `/transliterate` | Convert text from one script to another (e.g., Cyrillic to Latin). |
| **Detect Language** | POST | `/detect` | Detect the language of input text. |

**Translator Features:**
- Auto-detect source language
- Multiple target languages in one request
- Script transliteration
- Language detection with alternatives

### Language Services (api-version: 2024-11-01)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **Analyze Text (Language)** | POST | `/language/:analyze-text` | Comprehensive text analysis for sentiment, entities, key phrases, and PII. |

**Analysis Types:**
- `SentimentAnalysis` - Detect positive, negative, neutral sentiment
- `EntityRecognition` - Extract named entities (people, places, organizations)
- `KeyPhraseExtraction` - Extract key phrases and topics
- `PiiEntityRecognition` - Detect and redact personal information
- `EntityLinking` - Link entities to Wikipedia articles
- `LanguageDetection` - Detect document language

### Document Intelligence (api-version: 2024-11-30)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **Analyze Document** | POST | `/documentintelligence/documentModels/{modelId}:analyze` | Extract text, tables, and structure from documents. |
| **Get Document Analysis Result** | GET | `/documentintelligence/documentModels/{modelId}/analyzeResults/{resultId}` | Retrieve document analysis results. |

**Available Models:**
- `prebuilt-read` - OCR text extraction
- `prebuilt-layout` - Tables, paragraphs, and structure
- `prebuilt-document` - Key-value pairs and entities
- `prebuilt-invoice` - Invoice field extraction
- `prebuilt-receipt` - Receipt field extraction
- `prebuilt-businessCard` - Business card extraction

### Question Answering (api-version: 2021-10-01)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **Query Knowledge Base** | POST | `/language/:query-knowledgebases` | Answer questions using a custom question answering project. Returns matching answers with confidence scores. |

**Features:**
- Multi-turn conversations with follow-up prompts
- Confidence score filtering
- Unstructured content support
- Metadata filtering

### Conversational Language Understanding (CLU) (api-version: 2023-04-01)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **Analyze Conversation** | POST | `/language/:analyze-conversations` | Detect user intents and extract entities from conversational text. |

**Use Cases:**
- Intent routing in chatbots
- Entity extraction (dates, numbers, custom entities)
- Multi-intent detection
- Slot filling for task automation

### Anomaly Detector (api-version: v1.1)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **Detect Last Point Anomaly** | POST | `/anomalydetector/timeseries/last/detect` | Detect if the latest data point is an anomaly. Ideal for real-time monitoring. |
| **Detect Entire Series Anomalies** | POST | `/anomalydetector/timeseries/entire/detect` | Detect anomalies across an entire time series. Best for batch analysis. |

**Response includes:**
- `isAnomaly` - Boolean indicating anomaly
- `expectedValue` - Predicted normal value
- `upperMargin` / `lowerMargin` - Threshold bounds
- `severity` - Anomaly severity score

### Health Text Analytics (api-version: 2023-04-01)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **Analyze Health Text** | POST | `/language/analyze-text/jobs` | Extract healthcare entities from clinical text. |
| **Get Health Analysis Result** | GET | `/language/analyze-text/jobs/{jobId}` | Retrieve healthcare analysis results. |

**Healthcare Entity Categories:**
- `MedicationName` - Drug names
- `Dosage` - Medication dosages
- `Condition` - Medical conditions
- `BodyStructure` - Anatomical references
- `TreatmentName` - Treatments and procedures
- `SymptomOrSign` - Symptoms
- `Direction` - Anatomical directions
- `Time` - Time expressions

**Relations detected:**
- DosageOfMedication
- FrequencyOfMedication
- TimeOfMedication
- RouteOfMedication

### Face Detection (api-version: v1.0)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **Detect Faces** | POST | `/face/detect` | Detect human faces in images and optionally analyze attributes. |

**Face Attributes (optional):**
- `age` - Estimated age
- `gender` - Gender
- `glasses` - None, ReadingGlasses, Sunglasses, SwimmingGoggles
- `emotion` - Anger, contempt, disgust, fear, happiness, neutral, sadness, surprise
- `smile` - Smile intensity (0-1)
- `headPose` - Pitch, roll, yaw angles
- `facialHair` - Beard, moustache, sideburns
- `makeup` - Eye and lip makeup detection

### Text-to-Speech (Speech Services)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **Synthesize Speech** | POST | `/speechtotext/v3.2/transcriptions:transcribe` | Convert text to natural-sounding speech audio using SSML. |

**Output Formats:**
- MP3: `audio-16khz-128kbitrate-mono-mp3`, `audio-24khz-96kbitrate-mono-mp3`, `audio-48khz-192kbitrate-mono-mp3`
- WAV: `riff-16khz-16bit-mono-pcm`, `riff-24khz-16bit-mono-pcm`, `riff-48khz-16bit-mono-pcm`
- OGG: `ogg-16khz-16bit-mono-opus`, `ogg-24khz-16bit-mono-opus`, `ogg-48khz-16bit-mono-opus`

**Example SSML:**
```xml
<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="en-US">
  <voice name="en-US-JennyNeural">
    Hello, this is a text-to-speech example.
  </voice>
</speak>
```

### Speaker Recognition (api-version: 2021-09-05)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **Create Speaker Profile** | POST | `/speaker-recognition/verification/text-independent/profiles` | Create a new speaker verification profile for voice biometrics. |
| **Verify Speaker** | POST | `/speaker-recognition/verification/text-independent/profiles/{profileId}:verify` | Verify a speaker's identity against an enrolled voice profile. |
| **Enroll Speaker** | POST | `/speaker-recognition/identification/text-independent/profiles/{profileId}/enrollments` | Add voice enrollment audio to a speaker profile for training. |

**Workflow:**
1. Create a speaker profile with locale
2. Enroll the speaker with multiple audio samples (WAV format)
3. Once enrolled, verify the speaker against the profile

### Custom Vision (api-version: v3.0)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **Classify Image** | POST | `/customvision/v3.0/Prediction/{projectId}/classify/iterations/{publishedName}/image` | Classify an image using a published Custom Vision classification model. |
| **Detect Objects** | POST | `/customvision/v3.0/Prediction/{projectId}/detect/iterations/{publishedName}/image` | Detect objects in an image using a published Custom Vision object detection model. |

**Prerequisites:**
- A trained and published Custom Vision model
- Project ID and published iteration name from [Custom Vision portal](https://www.customvision.ai/)

### Personalizer (api-version: v1.1-preview.3)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **Rank Actions** | POST | `/personalizer/v1.1-preview.3/rank` | Get personalized ranking of actions based on context features. |
| **Send Reward** | POST | `/personalizer/v1.1-preview.3/events/{eventId}/reward` | Send a reward score (0-1) to reinforce learning from user interactions. |

**Workflow:**
1. Call **Rank** with context features and candidate actions
2. Present the top-ranked action to the user
3. Call **Reward** with the event ID and a reward value (0 = bad, 1 = good)

### AI Search (api-version: 2024-07-01)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **Search Index** | POST | `/indexes/{indexName}/docs/search` | Full-text, semantic, and vector search with filters, facets, scoring profiles, and AI-powered ranking. |

**Search Capabilities:**
- **Full-text search** - Simple or Lucene query syntax
- **Semantic search** - Set `queryType` to `semantic` with a `semanticConfiguration` for AI reranking, extractive captions, and answers
- **Filters** - OData filter expressions
- **Facets** - Aggregations for navigation
- **Vector search** - Similarity search with embedding vectors
- **Hybrid search** - Combined text + vector queries

> **Note:** AI Search uses the same API key as your Foundry resource when deployed as a connected service, or you can point to a standalone search service by using the resource name of your search service.

### OCR / Read API (api-version: v3.2)

| Operation | Method | Path | Description |
|-----------|--------|------|--------------|
| **Read Text (OCR)** | POST | `/vision/v3.2/read/analyze` | Extract printed and handwritten text from images. |
| **Get OCR Result** | GET | `/vision/v3.2/read/analyzeResults/{operationId}` | Get text extraction results. |

**Capabilities:**
- Supports 164+ languages
- Handles printed and handwritten text
- Preserves document structure (lines, words, bounding boxes)
- Auto-detects language if not specified

### Batch Transcription (api-version: v3.2)

| Operation | Method | Path | Description |
|-----------|--------|------|--------------|
| **Create Batch Transcription** | POST | `/speechtotext/v3.2/transcriptions` | Start batch transcription for audio files. |
| **Get Batch Transcription** | GET | `/speechtotext/v3.2/transcriptions/{transcriptionId}` | Get transcription job status. |
| **Get Transcription Files** | GET | `/speechtotext/v3.2/transcriptions/{transcriptionId}/files` | Get transcription output files. |

**Features:**
- Process multiple audio files in a single job
- Speaker diarization (identify who spoke when)
- Word-level timestamps
- Profanity filtering options
- Support for Azure Blob Storage input

### MCP Protocol (Model Context Protocol)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| **MCP Request** | POST | `/mcp` | JSON-RPC 2.0 endpoint for Copilot Studio integration. |

**Supported MCP Tools:**
- `chat_completion` - Send prompts to AI models
- `get_embeddings` - Generate text embeddings
- `create_assistant` - Create AI assistants
- `run_assistant` - Run assistant conversations
- `analyze_content_safety` - Check text for harmful content
- `analyze_image` - Analyze images for tags, objects, captions
- `evaluate_response` - Evaluate AI responses for quality/safety
- `transcribe_audio` - Convert audio to text
- `translate_text` - Translate text between languages
- `analyze_language` - Analyze text for sentiment, entities, PII
- `analyze_document` - Extract content from documents

## Setup Instructions

### 1. Create the Microsoft Foundry Resource

1. Navigate to the [Azure Portal](https://portal.azure.com)
2. Create a new **Microsoft Foundry** resource
3. Note the **Endpoint** URL (e.g., `https://your-resource.services.ai.azure.com/`)
4. Navigate to **Keys and Endpoint** and copy one of the API keys

### 2. Deploy a Model

#### Option A: Deploy Foundry Model (Serverless) via CLI

Deploy a model for the Model Inference API (`/models/chat/completions`):

```bash
# List available models
az cognitiveservices account list-models -n "your-resource" -g "your-rg" -o table

# Deploy a model (e.g., Phi-4)
az cognitiveservices account deployment create \
    -n "your-resource" \
    -g "your-rg" \
    --deployment-name "Phi-4" \
    --model-name "Phi-4" \
    --model-version 7 \
    --model-format Microsoft \
    --sku-capacity 1 \
    --sku-name GlobalStandard
```

#### Option B: Deploy Azure OpenAI Model via Portal

Deploy a model for the Azure OpenAI API (`/openai/deployments/{deployment}/chat/completions`):

1. Go to [Azure Portal](https://portal.azure.com) → your Cognitive Services resource
2. Navigate to **Model deployments** → **Deploy model**
3. Select a model (e.g., gpt-4o) and create a deployment
4. Note the deployment name (used in the path parameter)

### 3. Import the Connector

1. Go to [Power Automate](https://make.powerautomate.com) or [Power Apps](https://make.powerapps.com)
2. Navigate to **Custom connectors**
3. Select **Import an OpenAPI file** and upload `apiDefinition.swagger.json`
4. Update the **Host** field to match your Azure AI Services endpoint (e.g., `your-resource.services.ai.azure.com`)
5. Save the connector

### 4. Create a Connection

1. Test the connector or create a new connection
2. Enter your API key when prompted
3. Verify connectivity using the **Get Model Info** action

## Configuration

### Host Configuration

The connector's `host` value in the swagger must be updated to match your Microsoft Foundry endpoint. Replace `placeholder.services.ai.azure.com` with your actual endpoint hostname.

### API Versions

- **Model Inference**: `2024-05-01-preview`
- **AI Agents/Assistants**: `2025-04-01-preview`

API versions are set as internal parameters and do not need to be configured by end users.

### Application Insights (Optional)

To enable telemetry, add your Application Insights connection string to the `APP_INSIGHTS_CONNECTION_STRING` constant in `script.csx`:

```csharp
private const string APP_INSIGHTS_CONNECTION_STRING = "InstrumentationKey=YOUR-KEY;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/;...";
```

## Example Scenarios

### Chat Completion

Send a conversation to an AI model and receive a completion.

#### Using Foundry Models (Serverless)

Use **Chat Completion** operation with the `model` parameter:

```json
{
  "messages": [
    { "role": "system", "content": "You are a helpful assistant." },
    { "role": "user", "content": "What is the capital of France?" }
  ],
  "model": "Phi-4",
  "temperature": 0.7,
  "max_tokens": 800
}
```

#### Using Azure OpenAI Deployments

Use **Chat Completion (Azure OpenAI)** operation with the deployment name in the path:

- Set **Deployment Name** = `gpt-4o` (your deployment name)

```json
{
  "messages": [
    { "role": "system", "content": "You are a helpful assistant." },
    { "role": "user", "content": "What is the capital of France?" }
  ],
  "temperature": 0.7,
  "max_tokens": 800
}
```

### AI Agents Workflow

Create an AI assistant and run conversations:

1. Use **Create Assistant** with a model and instructions
2. Use **Create Thread** to start a conversation
3. Use **Create Message** to add user messages
4. Use **Create Run** to execute the assistant
5. Use **Get Run** to check status
6. Use **List Messages** to retrieve the response

Or use **Create Thread and Run** to do it all in one call.

### Semantic Search with Embeddings

Generate embeddings for text to enable semantic search:

1. Use **Get Text Embeddings** to generate vectors for your documents
2. Store vectors in a database (e.g., Azure AI Search, Dataverse)
3. At query time, embed the search query and compare vectors

### Copilot Studio Integration (MCP)

Use the MCP endpoint for Copilot Studio:

1. Configure the connector with your endpoint
2. Use the `/mcp` endpoint as an MCP server
3. Available tools: `chat_completion`, `get_embeddings`, `create_assistant`, `run_assistant`, `analyze_content_safety`, `analyze_image`, `evaluate_response`

### Content Safety Moderation

Screen user-generated content for harmful material:

1. Use **Analyze Text** with the text content to check
2. Review the `categoriesAnalysis` for each harm category
3. Block or flag content with severity >= 4 (Medium or High)

```json
{
  "text": "User input to check for harmful content"
}
```

### Image Analysis with Vision

Extract information from images:

1. Use **Analyze Image (Vision)** with the image URL
2. Set `features` to specify what to extract (caption, tags, objects, read)
3. Parse the results for captions, detected objects, or OCR text

### AI Response Evaluation

Evaluate AI responses for quality metrics:

1. Use **Submit Evaluation** with the response and evaluation type
2. For `groundedness`, include the source context
3. For `relevance`, include the original query
4. Check the score (1-5 for quality, 0-6 for safety)

```json
{
  "evaluationType": "groundedness",
  "response": "The AI's response to evaluate",
  "context": "The source material the response should be grounded in",
  "query": "The original user question"
}
```

### Audio Transcription

Transcribe audio files to text:

1. Deploy a Whisper model in your Microsoft Foundry resource
2. Use **Transcribe Audio** with the audio file
3. Optionally specify the language for better accuracy

```json
{
  "file": "<audio file content>",
  "language": "en",
  "response_format": "json"
}
```

### Text Translation

Translate text between languages:

1. Use **Translate Text** with the text and target language
2. Optionally specify the source language (auto-detected if omitted)
3. Supports batch translation with multiple texts

```json
{
  "to": "es",
  "texts": [
    { "Text": "Hello, how are you?" },
    { "Text": "Welcome to our service." }
  ]
}
```

### Language Analysis

Analyze text for sentiment, entities, or key phrases:

1. Use **Analyze Text (Language)** with the text and analysis type
2. Choose from: SentimentAnalysis, EntityRecognition, KeyPhraseExtraction, PiiEntityRecognition
3. Parse the results for document-level or sentence-level analysis

```json
{
  "kind": "SentimentAnalysis",
  "analysisInput": {
    "documents": [
      {
        "id": "1",
        "language": "en",
        "text": "I absolutely love this product! It exceeded all my expectations."
      }
    ]
  }
}
```

### Document Processing

Extract content from documents:

1. Use **Analyze Document** with the document URL and model
2. Retrieve the operation ID from the response
3. Poll **Get Document Analysis Result** until processing completes

```json
{
  "urlSource": "https://example.com/document.pdf"
}
```

## API Reference

- [Microsoft Foundry API](https://learn.microsoft.com/en-us/rest/api/aifoundry/)
- [Foundry Models Inference](https://learn.microsoft.com/en-us/azure/ai-foundry/concepts/deployments-overview)
- [Foundry Agent Service](https://learn.microsoft.com/en-us/azure/ai-foundry/what-is-foundry)
- [Azure Content Safety API](https://learn.microsoft.com/en-us/rest/api/contentsafety/)
- [Azure Computer Vision API](https://learn.microsoft.com/en-us/rest/api/computervision/image-analysis/analyze)
- [Azure Speech Services](https://learn.microsoft.com/en-us/azure/ai-services/openai/reference#audio-transcription)
- [Azure Translator API](https://learn.microsoft.com/en-us/azure/ai-services/translator/reference/v3-0-reference)
- [Azure Language Services](https://learn.microsoft.com/en-us/azure/ai-services/language-service/overview)
- [Azure Document Intelligence](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/)
- [MCP Specification](https://modelcontextprotocol.org/)

## AI Evaluation - Technical Notes

The AI Evaluation functionality in this connector is based on reverse-engineering the `azure-ai-evaluation` Python SDK. The SDK internally uses these Azure ML workspace endpoints:

- `/submitannotation` - Submit content for evaluation
- `/submitaoaievaluation` - Submit Azure OpenAI evaluation
- `/operations/{id}` - Get operation results
- `/simulation/*` - Adversarial simulation endpoints

The connector implements a simplified version that:
1. **Safety evaluations** (hate, violence, selfharm, sexual) use the Content Safety API
2. **Quality evaluations** (groundedness, relevance, coherence, fluency, similarity) use LLM-based assessment via the Chat Completion API

This approach provides comparable results without requiring an Azure ML workspace.

### What's Implemented

| Category | Evaluators | Implementation |
|----------|-----------|----------------|
| **Quality (AI-assisted)** | Groundedness, Relevance, Coherence, Fluency, Similarity | ✅ Chat Completion with evaluation prompts, returns score 1-5 |
| **Safety** | Hate, Violence, SelfHarm, Sexual | ✅ Content Safety API, returns severity 0-6 |
| **NLP Metrics** | F1, BLEU, ROUGE, GLEU, METEOR | ✅ Local computation in script.csx, returns score 0-1 |

### NLP Metrics Details

| Metric | Description | Score Range |
|--------|-------------|-------------|
| **F1** | Harmonic mean of precision and recall - measures token overlap | 0-1 |
| **BLEU** | Bilingual Evaluation Understudy - n-gram precision with brevity penalty | 0-1 |
| **ROUGE** | ROUGE-L - longest common subsequence based | 0-1 |
| **GLEU** | Google-BLEU - n-gram overlap across all n-gram sizes | 0-1 |
| **METEOR** | Metric for Evaluation of Translation - unigram precision/recall with stemming | 0-1 |

**Usage**: Provide `response` and `groundTruth` fields to compare the AI response against a reference answer.

### What's NOT Implemented

| Category | Evaluators | Reason |
|----------|-----------|--------|
| **Advanced Safety** | IndirectAttack, ProtectedMaterial | Requires Azure AI Project with specific capabilities |
| **Composite Evaluators** | QAEvaluator, ContentSafetyEvaluator | Can be achieved by calling multiple individual evaluators |
| **Simulators** | Simulator, AdversarialSimulator | Requires async callback architecture and Azure AI Project |
| **Batch Evaluation** | `evaluate()` API for datasets | Connector evaluates single responses; use Power Automate loops for batch processing |
| **Retrieval Evaluator** | RetrievalEvaluator | Evaluates RAG retrieval quality - can be approximated with Relevance evaluator |

### Workarounds

**For batch evaluation**: Use Power Automate's "Apply to each" loop to evaluate multiple responses, storing results in Dataverse or SharePoint.

**For composite evaluation**: Call Submit Evaluation multiple times with different evaluation types and aggregate results.
