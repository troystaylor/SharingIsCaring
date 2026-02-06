/**
 * Agent Client - Wraps CopilotStudioClient for conversation lifecycle management
 * Gets tokens from AuthService, reads connection settings from env vars
 */

import { CopilotStudioClient, ConnectionSettings } from "@microsoft/agents-copilotstudio-client";
import { AuthService } from "./auth-service.js";

export interface Activity {
  type?: string;
  text?: string;
  from?: {
    id?: string;
    name?: string;
  };
  attachments?: Attachment[];
  suggestedActions?: {
    actions?: Array<{ title?: string; value?: string }>;
  };
}

export interface Attachment {
  contentType?: string;
  content?: unknown;
}

export interface ConversationInfo {
  conversationId: string;
  agentName: string;
  greeting?: string;
}

export interface AgentResponse {
  text: string[];
  cards: Attachment[];
  suggestedActions: string[];
  raw: Activity[];
}

export interface AgentClientConfig {
  directConnectUrl?: string;
  environmentId?: string;
  schemaName?: string;
}

export class AgentClient {
  private client: CopilotStudioClient | null = null;
  private authService: AuthService;
  private config: AgentClientConfig;
  private agentName = "Copilot";
  private conversationId: string | null = null;

  constructor(authService: AuthService, config: AgentClientConfig) {
    this.authService = authService;
    this.config = config;
  }

  /**
   * Initialize or reinitialize the Copilot Studio client with a fresh token
   */
  private async ensureClient(): Promise<CopilotStudioClient> {
    const token = await this.authService.getAccessToken();

    const settings = new ConnectionSettings({
      appClientId: "",
      tenantId: "",
      authority: "",
      environmentId: this.config.environmentId || "",
      agentIdentifier: this.config.schemaName || "",
      directConnectUrl: this.config.directConnectUrl,
    });

    this.client = new CopilotStudioClient(settings, token);
    return this.client;
  }

  /**
   * Start a new conversation session
   */
  async startConversation(): Promise<ConversationInfo> {
    const client = await this.ensureClient();
    const activities = await client.startConversationAsync();

    this.conversationId = `conv_${Date.now()}`;
    this.extractAgentName(activities as Activity[]);

    const response = this.processActivities(activities as Activity[]);

    return {
      conversationId: this.conversationId,
      agentName: this.agentName,
      greeting: response.text.join("\n\n") || undefined,
    };
  }

  /**
   * Send a message and get the agent's response
   */
  async sendMessage(message: string): Promise<AgentResponse> {
    if (!this.client) {
      await this.startConversation();
    }

    const activity = {
      type: "message",
      text: message,
      from: { id: "user", name: "User" },
    };

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const activities = await this.client!.sendActivity(activity as any);
    this.extractAgentName(activities as Activity[]);

    return this.processActivities(activities as Activity[]);
  }

  /**
   * End the current conversation
   */
  async endConversation(): Promise<void> {
    this.client = null;
    this.conversationId = null;
  }

  /**
   * Check if there is an active conversation
   */
  hasActiveConversation(): boolean {
    return this.client !== null && this.conversationId !== null;
  }

  /**
   * Get the current conversation ID
   */
  getConversationId(): string | null {
    return this.conversationId;
  }

  /**
   * Get the agent's display name
   */
  getAgentName(): string {
    return this.agentName;
  }

  /**
   * Extract a readable agent name from activity metadata
   */
  private extractAgentName(activities: Activity[]): void {
    for (const activity of activities) {
      if (activity.from?.name && activity.from.name !== "User") {
        const name = activity.from.name;
        if (name.includes("_")) {
          const cleaned = name.split("_").pop() || name;
          this.agentName = cleaned
            .replace(/([A-Z])/g, " $1")
            .replace(/^./, (s: string) => s.toUpperCase())
            .trim();
        } else {
          this.agentName = name;
        }
        break;
      }
    }
  }

  /**
   * Process raw activities into structured response
   */
  private processActivities(activities: Activity[]): AgentResponse {
    const text: string[] = [];
    const cards: Attachment[] = [];
    const suggestedActions: string[] = [];

    for (const activity of activities) {
      if (activity.type === "message" && activity.text && activity.from?.id !== "user") {
        text.push(activity.text);
      }

      if (activity.attachments) {
        for (const attachment of activity.attachments) {
          if (attachment.contentType === "application/vnd.microsoft.card.adaptive") {
            cards.push(attachment);
          }
        }
      }

      if (activity.suggestedActions?.actions) {
        for (const action of activity.suggestedActions.actions) {
          if (action.title) {
            suggestedActions.push(action.title);
          }
        }
      }
    }

    return { text, cards, suggestedActions, raw: activities };
  }
}
