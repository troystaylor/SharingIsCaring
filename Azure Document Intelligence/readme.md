# Azure Document Intelligence

## Overview

The Azure Document Intelligence connector provides access to the [Azure Document Intelligence REST API](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/) (v4.0 GA, 2024-11-30). Extract text, key-value pairs, tables, and structured fields from documents using prebuilt and custom AI models.

This is a **Power Mission Control** (PMC) connector: 20 operations are available as standard Power Automate actions, and 11 high-value operations are exposed via MCP for AI agent use in Copilot Studio.

## Prerequisites

- An [Azure subscription](https://azure.microsoft.com/free/)
- An [Azure Document Intelligence resource](https://portal.azure.com/#create/Microsoft.CognitiveServicesFormRecognizer) (or Azure AI Services multi-service resource)
- An API key or Entra ID app registration with Cognitive Services User role

## Authentication

### Option 1: API Key
1. Go to your Document Intelligence resource in the Azure portal.
2. Navigate to **Keys and Endpoint**.
3. Copy the **Resource Name** (e.g., `my-doc-intel` from `https://my-doc-intel.cognitiveservices.azure.com`).
4. Copy either **Key 1** or **Key 2**.

### Option 2: Microsoft Entra ID (OAuth)
1. Register an application in Microsoft Entra ID.
2. Grant it the **Cognitive Services User** role on your Document Intelligence resource.
3. Create a client secret.
4. Provide the Client ID, Client Secret, and Resource Name when creating the connection.

## Supported Prebuilt Models

| Model ID | Use Case |
|----------|----------|
| `prebuilt-read` | Extract printed and handwritten text |
| `prebuilt-layout` | Extract text, tables, and document structure |
| `prebuilt-invoice` | Extract invoice fields |
| `prebuilt-receipt` | Extract receipt fields |
| `prebuilt-idDocument` | Extract ID card and passport fields |
| `prebuilt-tax.us.w2` | Extract W-2 tax form fields |
| `prebuilt-tax.us` | Unified US tax form extraction |
| `prebuilt-bankStatement` | Extract bank statement details |
| `prebuilt-healthInsuranceCard.us` | Extract insurance card fields |
| `prebuilt-contract` | Extract contract details |
| `prebuilt-creditCard` | Extract credit card fields |
| `prebuilt-check.us` | Extract check fields |
| `prebuilt-payStub.us` | Extract pay stub fields |
| `prebuilt-marriageCertificate.us` | Extract marriage certificate fields |
| `prebuilt-mortgage.us.*` | Various mortgage form models |

## Supported Operations

### Document Models (10 operations)
- Analyze Document (async — returns Operation-Location header)
- Get Analyze Result
- Analyze Document Output (produces searchable PDF)
- List Models
- Get Model
- Delete Model
- Build Custom Model
- Compose Model
- Authorize Model Copy
- Copy Model

### Document Classifiers (6 operations)
- Build Classifier
- List Classifiers
- Get Classifier
- Delete Classifier
- Classify Document
- Get Classify Result

### Operations (2 operations)
- List Operations
- Get Operation

### Service (1 operation)
- Get Service Info

## MCP Capabilities (Copilot Studio)

When used as an MCP connector in Copilot Studio, the agent has access to 11 curated tools:

| Domain | Tools |
|--------|-------|
| Analysis | Analyze document (all prebuilt/custom models), Get analyze result |
| Models | List models, Get model details |
| Classifiers | List classifiers, Get classifier, Classify document, Get classify result |
| Operations | List operations, Get operation status |
| Service | Get service info |

## Async Operations

Document analysis and classification are async operations:
1. The POST request returns HTTP 202 with an `Operation-Location` header.
2. Poll the Operation-Location URL (or use Get Analyze Result) until status is `succeeded`.
3. The MCP script handles polling automatically for Copilot Studio agents.
4. In Power Automate flows, use the separate POST + GET operations with a "Do Until" loop.

## Known Limitations

- Free tier (F0) is limited to 500 pages/month.
- Custom neural model builds have per-resource quotas (check via Get Service Info).
- Maximum document size varies by model (typically 500 MB for prebuilt models).
- Some add-on features (high-resolution OCR, formula extraction) incur additional costs.
