---
name: memory
description: |
  Save, recall, and list persistent key-value memories. Use when the user asks to
  "remember this", "save a memory", "store a value", "recall", "what did I save",
  "list memories", or any request involving persistent storage of information.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: create-and-update
---

# Memory

## When to Activate

- "Remember [value]", "save [key] = [value]", "store this for later"
- "Recall [key]", "what did I save", "get my memory"
- "List all memories", "what have I stored"

## Workflow

1. **Save**: `save_memory(key, value, scope)` — scope is "private" (default) or "shared"
2. **Recall**: `recall_memory(key, scope)` — returns the stored value
3. **List**: `list_memories(scope)` — returns all keys with previews

## Parameters

- `key`: A short identifier (e.g., "meeting-notes", "api-key-name", "test")
- `value`: Any text content to store
- `scope`: "private" (only you) or "shared" (visible to your org)

## Output Format

Confirm the operation with the key and a brief preview of the value stored/retrieved.
