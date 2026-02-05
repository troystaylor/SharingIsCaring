#!/usr/bin/env node
import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
  Tool,
} from '@modelcontextprotocol/sdk/types.js';
import { z } from 'zod';
import * as dotenv from 'dotenv';
import { CopilotAgent, createCopilotAgent } from '../agent/copilot-agent.js';
import { AuthService, createAuthService } from '../agent/auth-service.js';

// Load environment variables
dotenv.config();

// Server configuration
const SERVER_NAME = 'power-agent-desktop';
const SERVER_VERSION = '1.0.0';

// Tool schemas
const ChatSchema = z.object({
  message: z.string().describe('The message to send to the agent'),
  conversationId: z.string().optional().describe('Optional conversation ID to continue'),
});

const RenderCardSchema = z.object({
  cardPayload: z.object({}).passthrough().describe('The adaptive card JSON payload'),
});

const RenderWidgetSchema = z.object({
  html: z.string().describe('HTML content to render in sandbox'),
  width: z.number().optional().describe('Widget width in pixels'),
  height: z.number().optional().describe('Widget height in pixels'),
});

const ProductGridSchema = z.object({
  products: z.array(z.object({
    name: z.string(),
    price: z.number(),
    image: z.string().optional(),
    description: z.string().optional(),
  })).describe('Array of products to display'),
});

// Tool definitions
const TOOLS: Tool[] = [
  {
    name: 'chat_with_agent',
    description: 'Send a message to the Copilot Studio agent and receive a response',
    inputSchema: {
      type: 'object',
      properties: {
        message: { type: 'string', description: 'The message to send to the agent' },
        conversationId: { type: 'string', description: 'Optional conversation ID to continue' },
      },
      required: ['message'],
    },
  },
  {
    name: 'get_agent_capabilities',
    description: 'Query what the Copilot Studio agent can do',
    inputSchema: {
      type: 'object',
      properties: {},
    },
  },
  {
    name: 'start_conversation',
    description: 'Begin a new conversation session with the agent',
    inputSchema: {
      type: 'object',
      properties: {},
    },
  },
  {
    name: 'sign_in',
    description: 'Initiate authentication flow for Copilot Studio',
    inputSchema: {
      type: 'object',
      properties: {},
    },
  },
  {
    name: 'clear_credentials',
    description: 'Sign out and clear cached authentication tokens',
    inputSchema: {
      type: 'object',
      properties: {},
    },
  },
  {
    name: 'get_agent_details',
    description: 'Fetch agent name and metadata from Copilot Studio',
    inputSchema: {
      type: 'object',
      properties: {},
    },
  },
  {
    name: 'render_adaptive_card',
    description: 'Render a Microsoft Adaptive Card in the UI',
    inputSchema: {
      type: 'object',
      properties: {
        cardPayload: { type: 'object', description: 'The adaptive card JSON payload' },
      },
      required: ['cardPayload'],
    },
  },
  {
    name: 'render_product_grid',
    description: 'Display products in a visual card grid layout',
    inputSchema: {
      type: 'object',
      properties: {
        products: {
          type: 'array',
          items: {
            type: 'object',
            properties: {
              name: { type: 'string' },
              price: { type: 'number' },
              image: { type: 'string' },
              description: { type: 'string' },
            },
            required: ['name', 'price'],
          },
        },
      },
      required: ['products'],
    },
  },
  {
    name: 'render_widget',
    description: 'Render custom HTML in a sandboxed widget',
    inputSchema: {
      type: 'object',
      properties: {
        html: { type: 'string', description: 'HTML content to render' },
        width: { type: 'number', description: 'Widget width' },
        height: { type: 'number', description: 'Widget height' },
      },
      required: ['html'],
    },
  },
  {
    name: 'render_mcp_app',
    description: 'Render an interactive MCP App UI in the chat. The app runs in a sandboxed iframe and can call back to MCP tools.',
    inputSchema: {
      type: 'object',
      properties: {
        name: { type: 'string', description: 'Display name for the app' },
        html: { type: 'string', description: 'HTML content for the app UI' },
        data: { type: 'object', description: 'Initial data to pass to the app' },
        height: { type: 'number', description: 'App height in pixels (default: 400)' },
      },
      required: ['name', 'html'],
    },
  },
];

// State
let authService: AuthService | null = null;
let copilotAgent: CopilotAgent | null = null;

// Initialize services
function initializeServices() {
  const clientId = process.env.AZURE_CLIENT_ID || process.env.appClientId;
  const tenantId = process.env.AZURE_TENANT_ID || process.env.tenantId;

  if (clientId && tenantId) {
    authService = createAuthService(clientId, tenantId);
    
    copilotAgent = createCopilotAgent({
      directConnectUrl: process.env.directConnectUrl,
      environmentId: process.env.environmentId,
      schemaName: process.env.schemaName,
      authService: authService,
    });
  }
}

// Create MCP server
const server = new Server(
  {
    name: SERVER_NAME,
    version: SERVER_VERSION,
  },
  {
    capabilities: {
      tools: {},
    },
  }
);

// Handle list tools request
server.setRequestHandler(ListToolsRequestSchema, async () => {
  return { tools: TOOLS };
});

// Handle tool calls
server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const { name, arguments: args } = request.params;

  try {
    switch (name) {
      case 'chat_with_agent': {
        const { message, conversationId } = ChatSchema.parse(args);
        
        if (!copilotAgent) {
          return {
            content: [{ type: 'text', text: 'Agent not initialized. Please configure environment variables.' }],
            isError: true,
          };
        }

        const result = await copilotAgent.sendMessage(message, conversationId);
        const responses = copilotAgent.getTextResponses(result.activities);
        const cards = copilotAgent.getAdaptiveCards(result.activities);

        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify({
                conversationId: result.conversationId,
                agentName: result.agentName,
                responses: responses,
                hasCards: cards.length > 0,
                cardCount: cards.length,
              }, null, 2),
            },
          ],
        };
      }

      case 'start_conversation': {
        if (!copilotAgent) {
          return {
            content: [{ type: 'text', text: 'Agent not initialized.' }],
            isError: true,
          };
        }

        const state = await copilotAgent.startConversation();
        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify({
                conversationId: state.conversationId,
                agentName: state.agentName,
                status: 'Conversation started',
              }, null, 2),
            },
          ],
        };
      }

      case 'get_agent_capabilities': {
        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify({
                capabilities: [
                  'Natural language conversation',
                  'Adaptive card rendering',
                  'Voice input/output',
                  'Wake word activation',
                  'Multi-turn conversations',
                  'Suggested actions',
                ],
                agentName: copilotAgent?.getAgentName() || 'Unknown',
              }, null, 2),
            },
          ],
        };
      }

      case 'sign_in': {
        if (!authService) {
          return {
            content: [{ type: 'text', text: 'Auth service not configured. Set AZURE_CLIENT_ID and AZURE_TENANT_ID.' }],
            isError: true,
          };
        }

        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify({
                message: 'Use device code flow to authenticate',
                instructions: 'Authentication must be initiated from the Electron app UI',
              }, null, 2),
            },
          ],
        };
      }

      case 'clear_credentials': {
        if (authService) {
          await authService.signOut();
          return {
            content: [{ type: 'text', text: 'Credentials cleared successfully.' }],
          };
        }
        return {
          content: [{ type: 'text', text: 'Auth service not available.' }],
          isError: true,
        };
      }

      case 'get_agent_details': {
        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify({
                agentName: copilotAgent?.getAgentName() || 'Not connected',
                isConnected: copilotAgent?.isConnected() || false,
                conversationState: copilotAgent?.getConversationState(),
              }, null, 2),
            },
          ],
        };
      }

      case 'render_adaptive_card': {
        const { cardPayload } = RenderCardSchema.parse(args);
        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify({
                type: 'adaptive_card',
                payload: cardPayload,
                rendered: true,
              }, null, 2),
            },
          ],
        };
      }

      case 'render_product_grid': {
        const { products } = ProductGridSchema.parse(args);
        
        // Generate adaptive card for product grid
        const card = {
          type: 'AdaptiveCard',
          version: '1.5',
          body: [
            {
              type: 'Container',
              items: products.map(product => ({
                type: 'ColumnSet',
                columns: [
                  product.image ? {
                    type: 'Column',
                    width: 'auto',
                    items: [{
                      type: 'Image',
                      url: product.image,
                      size: 'Small',
                    }],
                  } : null,
                  {
                    type: 'Column',
                    width: 'stretch',
                    items: [
                      { type: 'TextBlock', text: product.name, weight: 'Bolder' },
                      { type: 'TextBlock', text: `$${product.price.toFixed(2)}`, color: 'Accent' },
                      product.description ? { type: 'TextBlock', text: product.description, wrap: true } : null,
                    ].filter(Boolean),
                  },
                ].filter(Boolean),
              })),
            },
          ],
        };

        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify({
                type: 'product_grid',
                productCount: products.length,
                card: card,
              }, null, 2),
            },
          ],
        };
      }

      case 'render_widget': {
        const { html, width, height } = RenderWidgetSchema.parse(args);
        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify({
                type: 'widget',
                html: html,
                dimensions: { width: width || 400, height: height || 300 },
              }, null, 2),
            },
          ],
        };
      }

      case 'render_mcp_app': {
        const name = (args as { name: string }).name;
        const html = (args as { html: string }).html;
        const data = (args as { data?: object }).data || {};
        const height = (args as { height?: number }).height || 400;
        
        // Return UI resource in MCP Apps format
        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify({
                type: 'mcp_app',
                uiResource: {
                  uri: `ui://${SERVER_NAME}/${name.toLowerCase().replace(/\s+/g, '-')}`,
                  name: name,
                  html: html,
                  data: data,
                  height: height,
                },
                message: `Rendering MCP App: ${name}`,
              }, null, 2),
            },
          ],
        };
      }

      default:
        return {
          content: [{ type: 'text', text: `Unknown tool: ${name}` }],
          isError: true,
        };
    }
  } catch (error) {
    const errorMessage = error instanceof Error ? error.message : String(error);
    return {
      content: [{ type: 'text', text: `Error: ${errorMessage}` }],
      isError: true,
    };
  }
});

// Main entry point
async function main() {
  console.error(`Starting ${SERVER_NAME} v${SERVER_VERSION}`);
  
  // Initialize services
  initializeServices();

  // Create transport and start server
  const transport = new StdioServerTransport();
  await server.connect(transport);
  
  console.error('MCP server running on stdio');
}

main().catch((error) => {
  console.error('Fatal error:', error);
  process.exit(1);
});
