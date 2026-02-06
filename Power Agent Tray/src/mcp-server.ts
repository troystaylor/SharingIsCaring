/**
 * MCP Server - Exposes Copilot Studio agent as MCP tools
 * Transport-agnostic: works with both Stdio and Streamable HTTP transports
 */

import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import {
  CallToolRequestSchema,
  ListResourcesRequestSchema,
  ReadResourceRequestSchema,
  ListToolsRequestSchema,
  Tool,
} from "@modelcontextprotocol/sdk/types.js";
import * as fs from "fs";
import * as path from "path";
import { AuthService } from "./auth-service.js";
import { AgentClient } from "./agent-client.js";

const SERVER_NAME = "power-agent-tray";
const SERVER_VERSION = "1.0.0";

// MCP Apps mime type for UI resources
const RESOURCE_MIME_TYPE = "text/html;profile=mcp-app";

// UI resource URI for the chat app
const CHAT_UI_RESOURCE_URI = "ui://chat-with-agent/chat-app.html";

// Tool definitions
const TOOLS: Tool[] = [
  {
    name: "chat_with_agent",
    description:
      "Send a message to the Copilot Studio agent and receive a response. Automatically starts a conversation if none is active.",
    inputSchema: {
      type: "object",
      properties: {
        message: {
          type: "string",
          description: "The message to send to the agent",
        },
      },
      required: ["message"],
    },
  },
  {
    name: "start_conversation",
    description:
      "Begin a new conversation session with the Copilot Studio agent. Returns the agent name and any greeting message.",
    inputSchema: {
      type: "object",
      properties: {},
    },
  },
  {
    name: "end_conversation",
    description: "End the current conversation session with the agent.",
    inputSchema: {
      type: "object",
      properties: {},
    },
  },
  {
    name: "get_auth_status",
    description:
      "Check whether the user is authenticated and return the current username if available.",
    inputSchema: {
      type: "object",
      properties: {},
    },
  },
  {
    name: "login",
    description:
      "Trigger the browser-based PKCE login flow. Opens the system browser for Microsoft Entra ID authentication.",
    inputSchema: {
      type: "object",
      properties: {},
    },
  },
  {
    name: "logout",
    description:
      "Sign out and clear all cached authentication tokens.",
    inputSchema: {
      type: "object",
      properties: {},
    },
  },
];

export interface McpServerDeps {
  authService: AuthService;
  agentClient: AgentClient;
  onLoginRequested: () => Promise<void>;
}

/**
 * Create and configure the MCP Server instance (transport-agnostic)
 */
export function createMcpServer(deps: McpServerDeps): Server {
  const { authService, agentClient, onLoginRequested } = deps;

  const server = new Server(
    { name: SERVER_NAME, version: SERVER_VERSION },
    { capabilities: { tools: {}, resources: {} } }
  );

  // List available tools
  server.setRequestHandler(ListToolsRequestSchema, async () => {
    return { tools: TOOLS };
  });

  // List UI resources
  server.setRequestHandler(ListResourcesRequestSchema, async () => {
    return {
      resources: [
        {
          uri: CHAT_UI_RESOURCE_URI,
          name: "Agent Chat UI",
          mimeType: RESOURCE_MIME_TYPE,
        },
      ],
    };
  });

  // Read UI resource \u2014 serve the bundled HTML
  server.setRequestHandler(ReadResourceRequestSchema, async (request) => {
    const { uri } = request.params;

    if (uri === CHAT_UI_RESOURCE_URI) {
      const htmlPath = path.join(__dirname, "ui", "chat-app.html");
      const html = fs.readFileSync(htmlPath, "utf-8");
      return {
        contents: [
          {
            uri: CHAT_UI_RESOURCE_URI,
            mimeType: RESOURCE_MIME_TYPE,
            text: html,
          },
        ],
      };
    }

    return {
      contents: [{ uri, mimeType: "text/plain", text: `Unknown resource: ${uri}` }],
    };
  });

  // Handle tool calls
  server.setRequestHandler(CallToolRequestSchema, async (request) => {
    const { name, arguments: args } = request.params;

    try {
      switch (name) {
        // \u2500\u2500 chat_with_agent \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
        case "chat_with_agent": {
          const message = (args as { message: string }).message;
          if (!message) {
            return {
              content: [{ type: "text", text: "Error: message is required" }],
              isError: true,
            };
          }

          const isAuthed = await authService.isAuthenticated();
          if (!isAuthed) {
            return {
              content: [
                {
                  type: "text",
                  text: "Not authenticated. Please use the 'login' tool first.",
                },
              ],
              isError: true,
            };
          }

          const response = await agentClient.sendMessage(message);
          const replyText =
            response.text.join("\n\n") || "(No text response from agent)";

          return {
            content: [
              {
                type: "text",
                text: JSON.stringify(
                  {
                    agentName: agentClient.getAgentName(),
                    conversationId: agentClient.getConversationId(),
                    response: replyText,
                    hasCards: response.cards.length > 0,
                    suggestedActions: response.suggestedActions,
                  },
                  null,
                  2
                ),
              },
            ],
            _meta: {
              ui: {
                resourceUri: CHAT_UI_RESOURCE_URI,
              },
            },
          };
        }

        // \u2500\u2500 start_conversation \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
        case "start_conversation": {
          const isAuthed = await authService.isAuthenticated();
          if (!isAuthed) {
            return {
              content: [
                {
                  type: "text",
                  text: "Not authenticated. Please use the 'login' tool first.",
                },
              ],
              isError: true,
            };
          }

          const info = await agentClient.startConversation();
          return {
            content: [
              {
                type: "text",
                text: JSON.stringify(
                  {
                    status: "Conversation started",
                    conversationId: info.conversationId,
                    agentName: info.agentName,
                    greeting: info.greeting || null,
                  },
                  null,
                  2
                ),
              },
            ],
          };
        }

        // \u2500\u2500 end_conversation \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
        case "end_conversation": {
          if (!agentClient.hasActiveConversation()) {
            return {
              content: [{ type: "text", text: "No active conversation to end." }],
            };
          }

          await agentClient.endConversation();
          return {
            content: [{ type: "text", text: "Conversation ended." }],
          };
        }

        // \u2500\u2500 get_auth_status \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
        case "get_auth_status": {
          const authenticated = await authService.isAuthenticated();
          const user = authenticated
            ? await authService.getCurrentUser()
            : null;

          return {
            content: [
              {
                type: "text",
                text: JSON.stringify(
                  {
                    authenticated,
                    user: user || null,
                    hasActiveConversation: agentClient.hasActiveConversation(),
                  },
                  null,
                  2
                ),
              },
            ],
          };
        }

        // \u2500\u2500 login \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
        case "login": {
          const alreadyAuthed = await authService.isAuthenticated();
          if (alreadyAuthed) {
            const user = await authService.getCurrentUser();
            return {
              content: [
                {
                  type: "text",
                  text: `Already authenticated as ${user || "unknown user"}.`,
                },
              ],
            };
          }

          await onLoginRequested();

          // Check if login succeeded
          const nowAuthed = await authService.isAuthenticated();
          if (nowAuthed) {
            const user = await authService.getCurrentUser();
            return {
              content: [
                {
                  type: "text",
                  text: `Successfully authenticated as ${user || "unknown user"}.`,
                },
              ],
            };
          }

          return {
            content: [
              {
                type: "text",
                text: "Login flow was initiated. Check the system browser to complete authentication.",
              },
            ],
          };
        }

        // \u2500\u2500 logout \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
        case "logout": {
          await agentClient.endConversation();
          await authService.logout();
          return {
            content: [
              { type: "text", text: "Logged out and cleared all tokens." },
            ],
          };
        }

        default:
          return {
            content: [{ type: "text", text: `Unknown tool: ${name}` }],
            isError: true,
          };
      }
    } catch (error) {
      const msg = error instanceof Error ? error.message : String(error);
      console.error(`[MCP] Tool '${name}' error:`, msg);
      return {
        content: [{ type: "text", text: `Error: ${msg}` }],
        isError: true,
      };
    }
  });

  return server;
}
