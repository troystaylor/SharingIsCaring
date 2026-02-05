import { CopilotStudioClient, ConnectionSettings, CopilotStudioWebChat } from '@microsoft/agents-copilotstudio-client';
import { AuthService } from './auth-service.js';

// Define activity types since they're not exported from the SDK
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

export interface ConversationState {
  conversationId: string;
  watermark?: string;
  agentName?: string;
}

export interface SendMessageResult {
  activities: Activity[];
  conversationId: string;
  agentName?: string;
}

export interface AgentConfig {
  directConnectUrl?: string;
  environmentId?: string;
  schemaName?: string;
  authService: AuthService;
}

export class CopilotAgent {
  private client: CopilotStudioClient | null = null;
  private directLine: ReturnType<typeof CopilotStudioWebChat.createConnection> | null = null;
  private config: AgentConfig;
  private conversationState: ConversationState | null = null;
  private agentName: string = 'Copilot';
  private currentToken: string = '';
  private useDirectLine: boolean = false;

  constructor(config: AgentConfig, useDirectLine: boolean = false) {
    this.config = config;
    this.useDirectLine = useDirectLine;
  }

  /**
   * Initialize the Copilot Studio client
   */
  async initialize(): Promise<void> {
    const token = await this.config.authService.getAccessToken();
    
    if (!token) {
      throw new Error('No access token available. Please authenticate first.');
    }
    
    this.currentToken = token;

    // Create connection settings using ConnectionSettings class (matching Microsoft sample)
    const settings = new ConnectionSettings({
      // App registration settings for auth (already handled externally)
      appClientId: '',
      tenantId: '',
      authority: '',
      // Agent connection settings
      environmentId: this.config.environmentId || '',
      agentIdentifier: this.config.schemaName || '',
      // Direct connection URL if provided
      directConnectUrl: this.config.directConnectUrl,
      // Enable debug logging
    });

    this.client = new CopilotStudioClient(settings, token);

    // Optionally create DirectLine connection for enhanced features (reconnection logic)
    if (this.useDirectLine) {
      this.directLine = CopilotStudioWebChat.createConnection(this.client);
    }
  }

  /**
   * Start a new conversation
   */
  async startConversation(): Promise<ConversationState> {
    if (!this.client) {
      await this.initialize();
    }

    const activities = await this.client!.startConversationAsync();
    
    // Generate a conversation ID (the SDK may handle this internally)
    this.conversationState = {
      conversationId: `conv_${Date.now()}`,
    };

    // Process initial activities for agent name
    if (activities && activities.length > 0) {
      this.extractAgentName(activities as Activity[]);
    }

    return {
      ...this.conversationState,
      agentName: this.agentName,
    };
  }

  /**
   * Send a message to the agent
   */
  async sendMessage(text: string, _conversationId?: string): Promise<SendMessageResult> {
    if (!this.client) {
      await this.initialize();
    }

    // Auto-start conversation if none exists
    if (!this.conversationState) {
      await this.startConversation();
    }

    // Create a message activity
    const activity = {
      type: 'message',
      text: text,
      from: { id: 'user', name: 'User' },
    };

    // Send the message and get response activities
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const activities = await this.client!.sendActivity(activity as any);

    // Extract agent name from activities
    if (activities && activities.length > 0) {
      this.extractAgentName(activities as Activity[]);
    }

    return {
      activities: (activities || []) as Activity[],
      conversationId: this.conversationState!.conversationId,
      agentName: this.agentName,
    };
  }

  /**
   * Extract agent name from activities
   */
  private extractAgentName(activities: Activity[]): void {
    for (const activity of activities) {
      // Check for agent name in activity.from
      if (activity.from?.name && activity.from.name !== 'User') {
        // Convert schema name format to readable format
        // e.g., "tst_computerPartsStoreAssistant" -> "Computer Parts Store Assistant"
        const name = activity.from.name;
        if (name.includes('_')) {
          // Remove prefix and convert camelCase to title case
          const cleaned = name.split('_').pop() || name;
          this.agentName = cleaned
            .replace(/([A-Z])/g, ' $1')
            .replace(/^./, (str: string) => str.toUpperCase())
            .trim();
        } else {
          this.agentName = name;
        }
        break;
      }
    }
  }

  /**
   * Get activities with optional filtering
   */
  getTextResponses(activities: Activity[]): string[] {
    return activities
      .filter(a => a.type === 'message' && a.text && a.from?.id !== 'user')
      .map(a => a.text!);
  }

  /**
   * Get adaptive cards from activities
   */
  getAdaptiveCards(activities: Activity[]): Attachment[] {
    const cards: Attachment[] = [];
    
    for (const activity of activities) {
      if (activity.attachments) {
        for (const attachment of activity.attachments) {
          if (attachment.contentType === 'application/vnd.microsoft.card.adaptive') {
            cards.push(attachment);
          }
        }
      }
    }
    
    return cards;
  }

  /**
   * Get suggested actions from activities
   */
  getSuggestedActions(activities: Activity[]): string[] {
    const actions: string[] = [];
    
    for (const activity of activities) {
      if (activity.suggestedActions?.actions) {
        for (const action of activity.suggestedActions.actions) {
          if (action.title) {
            actions.push(action.title);
          }
        }
      }
    }
    
    return actions;
  }

  /**
   * End the current conversation
   */
  async endConversation(): Promise<void> {
    this.conversationState = null;
    this.client = null;
  }

  /**
   * Get current conversation state
   */
  getConversationState(): ConversationState | null {
    return this.conversationState;
  }

  /**
   * Get the agent's display name
   */
  getAgentName(): string {
    return this.agentName;
  }

  /**
   * Check if connected to agent
   */
  isConnected(): boolean {
    return this.client !== null && this.conversationState !== null;
  }
}

// Factory function
export function createCopilotAgent(config: AgentConfig): CopilotAgent {
  return new CopilotAgent(config);
}
