/**
 * Kanban Board — interactive task board with drag-to-move cards.
 * Tools: show_kanban (model+app), create_task (model+app), move_card (app-only)
 * Mock data: 12 cards, 3 columns, 4 team members at Zava Corp.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { disclaimerText } from "../shared/disclaimer.js";
import { tryElicitate } from "../shared/elicitation.js";
import { kanbanWidgetHtml } from "./widgets/kanban-widget.js";

interface Card {
  id: string;
  title: string;
  description: string;
  column: string;
  assignee: string;
  priority: "low" | "medium" | "high";
}

const COLUMNS = ["To Do", "In Progress", "Done"];
const TEAM = ["Alex Rivera", "Jordan Chen", "Sam Patel", "Taylor Kim"];

// In-memory card store (resets on server restart)
let cards: Card[] = [
  { id: "ZAVA-001", title: "Update landing page copy", description: "Revise hero section for Q3 campaign", column: "To Do", assignee: "Alex Rivera", priority: "high" },
  { id: "ZAVA-002", title: "Fix checkout timeout", description: "Users report 504 errors on payment submit", column: "To Do", assignee: "Jordan Chen", priority: "high" },
  { id: "ZAVA-003", title: "Add dark mode toggle", description: "Settings page UI for theme preference", column: "To Do", assignee: "Sam Patel", priority: "low" },
  { id: "ZAVA-004", title: "Write API docs for /orders", description: "OpenAPI spec for new order endpoints", column: "To Do", assignee: "Taylor Kim", priority: "medium" },
  { id: "ZAVA-005", title: "Migrate user table to v2 schema", description: "Add new fields for GDPR compliance", column: "In Progress", assignee: "Jordan Chen", priority: "high" },
  { id: "ZAVA-006", title: "Design onboarding flow", description: "Figma mockups for new user walkthrough", column: "In Progress", assignee: "Alex Rivera", priority: "medium" },
  { id: "ZAVA-007", title: "Load test payment service", description: "Simulate 10k concurrent transactions", column: "In Progress", assignee: "Sam Patel", priority: "medium" },
  { id: "ZAVA-008", title: "Set up staging environment", description: "Docker Compose config for QA team", column: "In Progress", assignee: "Taylor Kim", priority: "low" },
  { id: "ZAVA-009", title: "Implement SSO login", description: "SAML 2.0 integration with Okta", column: "Done", assignee: "Jordan Chen", priority: "high" },
  { id: "ZAVA-010", title: "Audit npm dependencies", description: "Run npm audit fix and update lockfile", column: "Done", assignee: "Sam Patel", priority: "low" },
  { id: "ZAVA-011", title: "Create sales report template", description: "Monthly revenue breakdown by region", column: "Done", assignee: "Alex Rivera", priority: "medium" },
  { id: "ZAVA-012", title: "Fix mobile nav overflow", description: "Hamburger menu clips on small screens", column: "Done", assignee: "Taylor Kim", priority: "medium" },
];

function getBoardData() {
  const board: Record<string, Card[]> = {};
  for (const col of COLUMNS) {
    board[col] = cards.filter((c) => c.column === col);
  }
  return { columns: COLUMNS, board, totalCards: cards.length, team: TEAM };
}

export function registerKanban(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/kanban.html";

  // --- Resource: widget HTML ---
  server.resource("Kanban Board UI", RESOURCE_URI, async () => ({
    contents: [
      {
        uri: RESOURCE_URI,
        mimeType: "text/html;profile=mcp-app" as const,
        text: kanbanWidgetHtml(),
      },
    ],
  }));

  // --- Tool: show_kanban (model + app) ---
  server.registerTool(
    "show_kanban",
    {
      description: "Display an interactive Kanban task board for Zava Corp. Shows cards across To Do, In Progress, and Done columns. Drag cards between columns to update status.",
      inputSchema: {
        project: z.string().optional().describe("Project name to display (default: Zava Corp Sprint)"),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      // Try elicitation for project name
      const elicited = await tryElicitate<{ project: string }>(server, "Configure the Kanban board.", {
        type: "object",
        properties: {
          project: {
            type: "string",
            title: "Project Name",
            description: "Name for this board",
            default: "Zava Corp Sprint",
          },
        },
        required: [],
      });

      const project = elicited?.project || args.project || "Zava Corp Sprint";
      const data = getBoardData();

      return {
        content: [{ type: "text" as const, text: disclaimerText(`Kanban board "${project}": ${data.totalCards} cards across ${COLUMNS.length} columns.`) }],
        structuredContent: { project, ...data },
      };
    }
  );

  // --- Tool: create_task (model + app) ---
  server.registerTool(
    "create_task",
    {
      description: "Create a new task card on the Kanban board.",
      inputSchema: {
        title: z.string().optional().describe("Task title"),
        description: z.string().optional().describe("Task description"),
        column: z.enum(["To Do", "In Progress", "Done"]).optional().describe("Which column to place the card in"),
        assignee: z.string().optional().describe("Team member to assign"),
        priority: z.enum(["low", "medium", "high"]).optional().describe("Task priority"),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      const elicited = await tryElicitate<{
        title: string; description: string; column: string; assignee: string; priority: string;
      }>(server, "Enter the new task details.", {
        type: "object",
        properties: {
          title: { type: "string", title: "Title", description: "Task title" },
          description: { type: "string", title: "Description", description: "Brief task description" },
          column: { type: "string", title: "Column", enum: COLUMNS, default: "To Do" },
          assignee: { type: "string", title: "Assignee", enum: TEAM },
          priority: { type: "string", title: "Priority", enum: ["low", "medium", "high"], default: "medium" },
        },
        required: ["title"],
      });

      const title = elicited?.title || args.title || "Untitled Task";
      const description = elicited?.description || args.description || "";
      const column = elicited?.column || args.column || "To Do";
      const assignee = elicited?.assignee || args.assignee || TEAM[0];
      const priority = (elicited?.priority || args.priority || "medium") as Card["priority"];

      const newCard: Card = {
        id: `ZAVA-${String(cards.length + 1).padStart(3, "0")}`,
        title, description, column, assignee, priority,
      };
      cards.push(newCard);

      const data = getBoardData();
      return {
        content: [{ type: "text" as const, text: disclaimerText(`Created card ${newCard.id}: "${title}" in ${column}.`) }],
        structuredContent: { created: newCard, ...data },
      };
    }
  );

  // --- Tool: move_card (app-only) ---
  server.registerTool(
    "move_card",
    {
      description: "Move a card to a different column on the Kanban board.",
      inputSchema: {
        cardId: z.string().describe("Card ID (e.g., ZAVA-001)"),
        targetColumn: z.enum(["To Do", "In Progress", "Done"]).describe("Target column"),
      },
      _meta: {
        ui: {
          resourceUri: RESOURCE_URI,
          visibility: ["app"],
        },
      },
    },
    async (args) => {
      const card = cards.find((c) => c.id === args.cardId);
      if (!card) {
        return {
          content: [{ type: "text" as const, text: `Card ${args.cardId} not found.` }],
          isError: true,
        };
      }
      card.column = args.targetColumn;
      const data = getBoardData();
      return {
        content: [{ type: "text" as const, text: disclaimerText(`Moved ${card.id} to ${args.targetColumn}.`) }],
        structuredContent: { moved: { id: card.id, to: args.targetColumn }, ...data },
      };
    }
  );
}
