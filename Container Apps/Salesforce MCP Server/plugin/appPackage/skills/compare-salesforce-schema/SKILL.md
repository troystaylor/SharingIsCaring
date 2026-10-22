---
name: compare-salesforce-schema
description: |
  Compares Salesforce object definitions over time or across API versions. Use
  when the user asks "what changed on this object", "did anyone add a field",
  "what would move us to a newer API version expose", "why is this field missing
  in API v52", "which API versions does the org support", or wants to track
  schema changes for an integration.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: comparison
---

# Compare Salesforce Schema

## What This Skill Does

Finds what changed in an object's fields, either between two API versions or
since a snapshot taken earlier.

## When to Activate

- The user asks what is different between two API versions
- The user asks whether an object changed since they last looked
- A field the user expects is missing, and an API version may be the reason
- The user is planning to move an integration to a newer API version

## Workflow

### Between two API versions

1. Call `list_api_versions` to see what the org supports, the server default, and
   the latest.
2. Call `compare_api_versions` with `sobject`, `fromApiVersion`, and optionally
   `toApiVersion` (the server default when empty).
3. Report fields that were `added`, `removed`, and `changed`, and what each
   change means for a query or an integration.

### Since an earlier snapshot

1. **Take a snapshot** with `get_schema_snapshot` for the object. Give the user
   the result and tell them to keep it. **The server does not store snapshots.**
2. **Later, compare** with `compare_schema`. Pass the snapshot's `fields` as
   `baselineFields` and its `fingerprint` as `baselineFingerprint`.
3. If `identical` is true, say nothing changed. Otherwise report `added`,
   `removed`, and `changed`.

## Reading a Result

Each field has a short signature, for example `string(80)|required` or
`picklist(255)|values=5:ab12cd34`. A change in the type, size, required flag,
writable flags, lookup target, controlling field, or the set of active picklist
values shows up as `changed` with a `before` and `after`. Translate these into
plain words, such as "Name now allows 120 characters instead of 80".

## What to Watch For

- **A snapshot reflects what the signed-in person can see.** Someone with different
  field-level security may get a different set of fields, so compare like with like.
- **Labels and help text are not compared.** A relabelled field is not a schema
  change here, by design.
- **A snapshot belongs to one API version.** Compare it against the same version
  it was taken under, or the differences may come from the version, not the org.
- **Lists are capped** at 200 fields each. The `counts` are complete; if there is
  a `note`, say the lists are partial.
- **Results can come from a short cache.** If the user just changed the schema
  and the comparison shows nothing, say it may need a few minutes.
- A version the org does not support is refused with the supported range. Offer
  the supported versions from `list_api_versions`.
