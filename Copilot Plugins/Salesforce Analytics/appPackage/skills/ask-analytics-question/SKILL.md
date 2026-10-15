---
name: ask-analytics-question
description: Answer a business question from governed Tableau Next data by finding the right semantic model and asking the Analytics Agent. Use when asked for numbers, trends or drivers such as win rate by region, revenue trend, or what drove churn.
---

# Ask Analytics Question

## When to use this skill

- The user asks an analytical question that a Tableau Next semantic model could answer.
- Cue phrases: "what is our win rate by region", "revenue trend last 6 months", "what drove the churn spike".

## How to do it

1. Call `list_semantic_models` (or `search_assets` with the topic and type `semanticModel`) and pick the model that fits the question. If several fit, name them and ask.
2. For unfamiliar models, call `get_semantic_model` and, if the question names a KPI, `list_semantic_model_metrics` so the answer uses the governed definition.
3. Call `analyze_data` with `utterance` set to the user's question, `target_entity_type` set to `sdm`, and `target_entity_name_or_id` set to the chosen model.
4. Present the narrative answer, the key figures, and which model and metric definitions were used. Offer a follow-up such as a breakdown by another dimension.

## What to watch for

- Answers can take a while; say you are running the analysis.
- If the tool reports an item is unavailable, the user lacks permission to that asset; say so rather than retrying.
- Do not recompute or alter the returned numbers; quote them and note the model.
- For dataset-level SAQL work, use the Query CRM Analytics Dataset skill instead.
