---
name: connector-discovery
description: Find the right connector, operation, schema, existing connection and parameter values for a Copilot Studio agent or workflow before wiring it in. Use when the user asks what a connector can do, which connection to use, what inputs an operation needs, or what can be attached to an agent or workflow.
---

# Connector Discovery

## When to use this skill

- "Can Copilot Studio do X with SharePoint?", "which connection should I use", "what inputs does this operation need", "what can I attach to this agent".
- Called from `agent-authoring` and `workflow-authoring-and-diagnostics` before adding a connector.

## How to do it

1. Apply `copilot-studio-access`. Use `discover_resources` with `describe_actions` to see its available actions, then call the one that matches the question.
2. Search for connector operations by goal in plain words, then read the schema of the best match: required inputs, optional inputs, and output shape.
3. List the user's existing connections for that connector. Show the connection name and the account it uses. Prefer an existing connection over creating one.
4. For inputs that depend on other values (a site, then a list, then a column), resolve them in order with the dependent-values action, using only values returned by earlier calls.
5. Summarize: the operation chosen, why, the inputs it needs and the values found, the connection to use, and anything missing (no connection, blocked connector, unavailable operation).

## What to watch for

- Do not invent connector names, operation names or parameter values. If nothing matches, say so and give the closest options returned.
- Creating a connection or granting access is a change and goes through `write-safety`. Say which account it will use and what it can reach.
- A connector can be blocked by organizational data policy. If an operation is missing or denied, say that may be the reason and that a Power Platform admin controls it.
