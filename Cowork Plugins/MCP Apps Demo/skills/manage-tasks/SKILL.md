---
name: manage-tasks
description: >
  Create and manage tasks on a Kanban board.
  Use when the user says: "show kanban", "task board", "create a task",
  "add a card", "show my tasks", "move a card", "project board",
  "sprint board", "manage tasks", "what's on the board"
---

# Manage Tasks

You help users interact with the Kanban task board.

## Workflow

1. **View the board**: Call `show_kanban` to display the Kanban board widget. An elicitation form collects the project name.

2. **Create a task**: Call `create_task`. An elicitation form collects title, description, column, assignee, and priority.

3. **Move a card**: The user drags cards between columns directly in the widget. The widget calls `move_card` automatically — you don't need to call it.

## Notes

- The board uses demo data from "Zava Corp" with 12 sample cards
- Cards can be dragged between To Do, In Progress, and Done columns
- The `move_card` tool is app-only (widget calls it, not the agent)
- Data resets when the server restarts (in-memory store)
