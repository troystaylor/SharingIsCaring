---
name: c3-build-agent-tools
description: Design and test custom tools and prompts for a C3 application's App MCP server, and plan how they appear to agents. Use for "turn this method into a tool", "make a tool that checks inventory", "why can't the agent call my tool", or "write an MCP prompt".
---

# Build agent tools for C3

Uses both connectors. Apply `c3-access` first. This skill produces designs, code and checklists. Creating and enabling tools happens in C3 AI Studio by a user with the right role.

## Background

- A tool is a `GenaiCore.Tool`. Types are **Python Tool** (a custom Python function), **C3 Action Tool** (wraps an existing type method) and, with a Postgres vector store, **Semantic Search Tool**.
- A tool is visible to agents only when it is stored (not a draft) and **Enabled for MCP**. Only an App Admin can enable it.
- The **LLM description** and parameter descriptions are passed to the agent. They decide how well the agent calls the tool.
- Prompts are templates with arguments, managed under **MCP > Prompts**.

## Steps

1. **Define the job.** Get one sentence: what the tool answers or does, who calls it, and whether it reads or writes.
2. **Choose the type.** Use a C3 Action Tool when a type method already does the work. Use a Python Tool when you need to combine calls or shape the result. Look up the method with `getC3TypeInformation`.
3. **Draft the contract.**
   - Name: a short verb phrase, for example `checkStockLevels`.
   - LLM description: what it does, when to use it, when not to, and what it returns.
   - Parameters: name, type, required or optional, and one example value each. Keep them few.
   - Output: compact and structured. Return fields the agent needs, not whole records.
4. **Show the code.** For a Python Tool, write the function. For a C3 Action Tool, name the type and method for `createFromMethodName`. Present it for the user to create in **Agents > Gallery > Tools**.
5. **Test.** Point the user to **Test Tool** in the Tool Workbench, then, once enabled, invoke the tool from the C3 App MCP connector and check the result.
6. **Check the checklist.** Stored, enabled for MCP, in the user's toolkit if toolkit filtering is on, and visible in the live tool list.

## Rules

- Write tools are changes. Say which ones write, and apply `c3-write-safety` when you test them.
- Don't expose tools that run arbitrary code, return secrets, or bypass the user's roles.
- Use **MCP > Monitor** to debug a failing call. Ask an admin to look at the arguments, result and stack trace, and don't ask them to paste personal data into chat.
