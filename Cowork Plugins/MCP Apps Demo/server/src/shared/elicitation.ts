/**
 * Elicitation helper — sends elicitation/create mid-tool-call and handles
 * accept/decline/cancel responses. Falls back gracefully when the host
 * doesn't support elicitation.
 */

import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

export interface ElicitationSchema {
  type: "object";
  properties: Record<string, {
    type: string;
    title?: string;
    description?: string;
    enum?: string[];
    format?: string;
    default?: string | number | boolean;
  }>;
  required?: string[];
}

export interface ElicitationResult<T = Record<string, unknown>> {
  action: "accept" | "decline" | "cancel";
  content?: T;
}

/**
 * Attempt to elicit structured input from the user mid-tool-call.
 *
 * Returns the user's response if accepted, or null if:
 * - The host doesn't support elicitation (capability not advertised)
 * - The user declined or cancelled
 * - An error occurred during elicitation
 *
 * When null is returned, the tool handler should fall back to using
 * whatever arguments were passed directly in the tool call.
 */
export async function tryElicitate<T = Record<string, unknown>>(
  server: McpServer,
  message: string,
  schema: ElicitationSchema
): Promise<T | null> {
  try {
    const result = await (server as any).server.createElicitation({
      message,
      requestedSchema: schema,
    }) as ElicitationResult<T>;

    if (result.action === "accept" && result.content) {
      return result.content;
    }
    return null;
  } catch {
    // Host doesn't support elicitation or error occurred — fall back
    return null;
  }
}
