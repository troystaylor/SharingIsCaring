---
name: explain-metric-or-kpi
description: Explain how a Tableau Next metric or KPI is defined and calculated - formula, source data, targets - and list the measures and dimensions behind a semantic model. Use when asked what a metric means, how a number is computed, or what a model contains.
---

# Explain Metric Or KPI

## When to use this skill

- The user wants the definition, formula or lineage of a number, or an overview of a semantic model.
- Cue phrases: "how is win rate calculated", "what KPIs are in the Customer Health model", "what does this metric measure".

## How to do it

1. Find the model: `list_semantic_models`, or `search_assets` filtered to `semanticModel`.
2. List what exists: `list_semantic_model_metrics`, then `list_semantic_model_calculated_measures` and `list_semantic_model_calculated_dimensions` for derived fields.
3. For a named metric, call `get_semantic_model_metric` and explain the formula, data source and any configured targets or thresholds in plain language.
4. To show structure, use `list_semantic_model_data_objects`, `list_semantic_model_relationships`, and per object `list_semantic_model_measures` and `list_semantic_model_dimensions`. For how a specific logical view is built, call `get_semantic_model_logical_view`.
5. Summarize: what the metric means, how it is computed, which dimensions can slice it, and caveats.

## What to watch for

- Quote definitions as returned; do not infer a formula the tool did not provide.
- If a model or metric is unavailable, it is a permissions limit for this user.
- To actually compute values, use the Ask Analytics Question skill.
