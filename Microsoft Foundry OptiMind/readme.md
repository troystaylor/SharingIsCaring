# Microsoft Foundry OptiMind

## Overview

The Microsoft Foundry OptiMind connector integrates Microsoft's OptiMind model into Power Platform. OptiMind is a specialized 20-billion parameter language model that translates business optimization problems described in natural language into mathematical formulations (MILP) and executable GurobiPy Python code.

Supports scheduling, routing, supply chain planning, resource allocation, network design, and other optimization categories.

## Prerequisites

1. An Azure subscription with access to Microsoft Foundry
2. Deploy the **OptiMind-SFT** model from the [Foundry Model Catalog](https://ai.azure.com/catalog/models/microsoft-optimind-sft)
3. Note the **Resource Name** (e.g., `my-foundry-resource` from `https://my-foundry-resource.services.ai.azure.com`) and **API Key** from the deployment

## Connection Setup

| Parameter | Description | Example |
|---|---|---|
| **Resource Name** | The Azure AI Services resource name | `my-foundry-resource` |
| **API Key** | API key from the Foundry portal | (from Keys & Endpoint) |

## Operations

### Formulate Optimization Problem

Translate a natural language business problem into a mathematical formulation and GurobiPy code.

| Parameter | Required | Description |
|---|---|---|
| Problem Description | Yes | The optimization problem in natural language |
| Additional Context | No | Extra data, parameters, or constraints |
| Temperature | No | Sampling temperature (default: 0.9, recommended) |
| Max Tokens | No | Maximum tokens to generate (default: 4096) |

**Example input:**

> A factory produces two products A and B. Product A requires 2 hours of machine time and 1 hour of labor. Product B requires 1 hour of machine time and 3 hours of labor. The factory has 100 hours of machine time and 90 hours of labor available per week. Product A generates $40 profit and Product B generates $60 profit. Maximize weekly profit.

**Returns:** Mathematical formulation with decision variables, constraints, objective function, and executable GurobiPy Python code.

### Parse Optimization Formulation

Parse a raw formulation response into separate components. This operation runs entirely in the connector — no API call is made.

| Parameter | Required | Description |
|---|---|---|
| Formulation Text | Yes | The raw formulation text from the Formulate operation |

**Returns:**

| Field | Description |
|---|---|
| Reasoning | Step-by-step thinking from the model |
| Mathematical Model | The MILP/LP formulation (variables, constraints, objective) |
| Python Code | The extracted GurobiPy code block |
| Has Code | Boolean — whether a code block was found |

### Refine Optimization Formulation

Refine a previous formulation based on feedback. Pass the original output and describe what should change — added constraints, different objective, modified parameters. The connector injects a refinement prompt that preserves existing constraints while applying changes.

| Parameter | Required | Description |
|---|---|---|
| Original Formulation | Yes | The previous formulation output to refine |
| Feedback | Yes | What to change (e.g., "add a constraint that no driver works more than 8 hours") |
| Temperature | No | Sampling temperature (default: 0.9) |
| Max Tokens | No | Maximum tokens to generate (default: 4096) |

**Returns:** Updated formulation with the requested changes applied while preserving the rest.

### Explain Optimization Formulation

Get a plain-English explanation of an optimization formulation, tailored to a target audience.

| Parameter | Required | Description |
|---|---|---|
| Formulation Text | Yes | The optimization formulation to explain |
| Audience | No | Target audience: `business stakeholder` (default), `technical manager`, or `data scientist` |
| Max Tokens | No | Maximum tokens to generate (default: 2048) |

**Returns:** Non-technical summary of what the model optimizes, what decisions it makes, and what constraints it enforces.

### Chat Completion

General-purpose chat with the OptiMind model. Use for follow-up questions, discussing optimization concepts, or any prompt that doesn't fit the structured operations above.

| Parameter | Required | Description |
|---|---|---|
| Messages | Yes | Array of message objects with `role` (system/user/assistant) and `content` |
| Temperature | No | Sampling temperature (default: 0.9) |
| Top P | No | Nucleus sampling probability mass (default: 1.0) |
| Max Tokens | No | Maximum tokens to generate (default: 4096) |

## Suggested Workflow

1. **Formulate** — describe the problem in plain language
2. **Parse** — extract reasoning, math, and code into separate fields
3. **Explain** — generate a summary for stakeholders who don't read math
4. **Refine** — iterate on the formulation with feedback (repeat as needed)
5. **Execute** — run the extracted Python code in a GurobiPy environment

## MCP Protocol Support

This connector includes an MCP (Model Context Protocol) endpoint for Copilot Studio integration with five tools:

| Tool | Description |
|---|---|
| `formulate_optimization` | Translate optimization problems to MILP formulations and GurobiPy code |
| `parse_formulation` | Extract reasoning, math, and code from a formulation response |
| `refine_optimization` | Refine a previous formulation based on feedback |
| `explain_optimization` | Generate a plain-English explanation for a target audience |
| `chat_completion` | General chat for discussing optimization concepts |

## About OptiMind

- **Developer:** Microsoft Research, Machine Learning and Optimization (MLO) Group
- **Architecture:** Mixture-of-Experts (MoE) transformer (20B parameters, 3.6B activated)
- **Context Length:** 128,000 tokens
- **Training Data:** Cleaned subsets of OR-Instruct and OptMATH-Train
- **License:** MIT
- **Paper:** [OptiMind: Teaching LLMs to Think Like Optimization Experts](https://arxiv.org/abs/2509.22979)
- **Status:** Experimental (released for research purposes)

## Known Limitations

- The model can produce incorrect formulations or invalid code
- Specialized to optimization benchmarks; general text tasks are not guaranteed
- Generated code should be reviewed by a human before execution
- Requires GurobiPy with a valid Gurobi license to execute generated code
- Parse operation uses regex extraction — it may not capture formulations that deviate from the standard OptiMind output format

## Authors

- Troy Taylor, troy@troystaylor.com, https://github.com/troystaylor
