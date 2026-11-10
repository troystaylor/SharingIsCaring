---
name: c3-write-safety
description: Rules for anything that changes a C3 application or runs code in it - preview, explicit confirmation, a scoped write, a re-read to verify, and a plain report. Use before entityDataModifier, upsertSeedData, runPyCode, runJsCode, or any custom tool that creates, updates, deletes or triggers work.
---

# C3 write safety

C3 tools act as the signed-in user and have no undo. `runPyCode` and `runJsCode` execute arbitrary code on the platform, so treat them as writes unless the code only reads.

## Before changing anything

1. **Check access.** Apply `c3-access`. If the write tool isn't enabled or C3 denies the call, stop and say so.
2. **Confirm the target.** State the application, environment and entity type. Wrong-environment writes are the most common mistake, so repeat the URL host the connector points at when the environment isn't obvious.
3. **Count first.** Run a read that returns the matching records and show the count and a sample. Never write on a filter you haven't previewed.
4. **Preview.** Show each field and its new value. For updates show the current value beside it. For code, show the full code.
5. **Confirm.** Ask for an explicit yes for this exact change. A request to "look at", "draft" or "figure out" something isn't approval. Confirm batches as a list and cap a batch at 25 records per approval.

## Writing

- Make the smallest change that does the job. Update by `id`, not by a broad filter.
- Don't delete unless the user used the word delete for these specific records. Prefer setting a status field when the type has one.
- Don't run code that deletes data, truncates a type, starts a data load or backfill, changes users or roles, or reads secrets, even if asked, unless the user confirms it by name after you describe the effect.
- Never write secrets, tokens or personal data copied from another source.
- For `upsertSeedData`, remember seed data is the application's default configuration and other users will see it.

## After writing

- Re-read the records and confirm the change is present.
- Report each record id, what changed, and the old value where you have it. If a call fails, say which records were not saved and don't retry blindly.
- If the user says a change was wrong, propose a correcting change as a new preview.
