---
name: understand-salesforce-schema
description: |
  Explains how a Salesforce object is built. Use when the user asks "what fields
  does Opportunity have", "what are the picklist values for Status", "what does
  the Region field mean", "how is this formula calculated", "why was my save
  rejected", "what validation rules are on Case", "what record types exist",
  "what's on the page layout", or wants to learn an object before querying or
  changing it.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
---

# Understand a Salesforce Object

## What This Skill Does

Describes the shape and meaning of a Salesforce object: its fields, record types,
layouts, picklists, and the rules that govern its data.

## When to Activate

- The user asks what fields, types, or lookups an object has
- The user asks what values a picklist allows
- The user asks what a field means or how a formula field is calculated
- The user wants to know why a record could not be saved
- Before querying or changing an object you do not know well

## Choosing the Tool

| The user wants | Call |
|---|---|
| Which objects exist | `list_objects` with `nameContains` |
| Fields, types, lookups | `describe_object`, with `fieldNameContains` for a big object |
| What a field is for, its help text | `list_custom_fields` or `get_field_metadata` |
| How a formula field is calculated | `get_field_metadata` and read `definition.formula` |
| The object's purpose | `get_object_metadata` (the description an admin wrote) |
| Record types | `get_record_types` |
| Which fields a user sees when editing | `get_page_layout`, with `recordTypeId` when the object has several |
| Allowed picklist values | `get_picklist_values`, with `recordTypeId` to narrow them |
| Why a save is rejected | `list_validation_rules`, with `includeFormulas: true` for the logic |
| Recently viewed records | `get_object_info` |

## Workflow

1. Start with `describe_object` unless the question is about one specific thing.
2. For picklists on an object that has record types, call `get_record_types`
   first. The allowed values can differ by record type.
3. For a dependent picklist, `get_picklist_values` lists `validWhen`: the values
   of the controlling field that make each value valid. Explain the dependency in
   plain words.
4. When asked why a save failed, read the validation rule's `errorMessage` and
   formula and translate the formula into a plain-language condition.

## What to Watch For

- **Descriptions and help text exist only if an admin wrote them.** Do not make
  one up. Say there is none and describe the field from its name and type.
- **Setup lookups can come back as a note instead of data.** The setup metadata
  tools need extra Salesforce permission. Report the note's reason and fall back
  to `describe_object`; do not retry the same call.
- **`includeFormulas` costs one API call per rule.** Use it when the logic is
  what the user asked about, not for a general listing.
- **Results may come from a short-lived cache.** `schema.source` and
  `schema.retrievedAt` say how current they are. If the user just changed the
  schema in Salesforce and does not see it, say the result may be a few minutes
  old.
- **Descriptions differ by API version.** If a field the user expects is missing,
  hand off to `compare-salesforce-schema`.
