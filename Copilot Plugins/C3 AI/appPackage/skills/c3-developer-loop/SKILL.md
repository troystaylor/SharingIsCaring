---
name: c3-developer-loop
description: Help a C3 developer write, test and fix application code - look up Types, generate Types and Transforms, check package issues, run tests, and save seed data. Use for "why won't my type compile", "write a test for...", "run the tests", "what does this type do", or "how do I do X in C3".
---

# C3 developer loop

Uses both connectors. Apply `c3-access` first.

| Need | Connector and tool |
| --- | --- |
| How something works in C3, Type or Transform scaffolding, coding standards | C3 Central MCP: documentation search and generation tools from its live tool list |
| What a type in this application looks like | C3 App MCP: `getC3TypeInformation`, `getEntityTypes`, `entityFieldRetriever` |
| Are my types, implementations and seed data valid | C3 App MCP: `readPkgIssues` |
| Does my code work | C3 App MCP: `runTest` |
| Save default configuration | C3 App MCP: `upsertSeedData` |

## Loop

1. **Understand.** Look up the type with `getC3TypeInformation`. For general platform questions, search C3 documentation through Central MCP first and cite what it returned.
2. **Write.** Give the user the Type, implementation or test as code for them to put in their package. These connectors don't edit the user's source files. Follow the conventions Central MCP returns.
3. **Check.** Run `readPkgIssues` and report each issue with its type and message. Fix and re-run until it is clean.
4. **Test.** Run `runTest` with the test name. It returns YAML. Report passes and failures, and for each failure show the assertion and the actual value.
5. **Repeat** until green, then summarize what changed.

## Rules

- `runTest` runs against the live application. Don't run tests that insert or delete data in a production environment. Ask which environment this is.
- `readPkgIssues` and `runTest` see the code that is **provisioned** in the application, not the user's unsaved local edits. Remind the user to sync their package first when results look stale. In the C3 AI VS Code extension, a file shows as unsynced until it is saved and sent to the server, and as validated once the server has checked it.
- These connectors don't edit local files or sync packages. Writing and syncing code happens in the user's IDE, ideally with the C3 AI VS Code extension.
- `upsertSeedData` writes to the database and is a change under `c3-write-safety`. Show the seed data and get confirmation first.
- Don't invent C3 syntax. If Central MCP can't confirm it, say you're unsure and show how to verify with `getC3TypeInformation` or a test.
