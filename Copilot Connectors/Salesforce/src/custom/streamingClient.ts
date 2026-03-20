// ── Streaming API Client (#5) ──
// CometD-based streaming for orgs without Pub/Sub API access.
// Falls back to generic long-polling approach.

import { getAccessToken, getInstanceUrl } from "../auth/salesforceAuth";
import { getConfig } from "../config/connectorConfig";

export interface StreamingEvent {
  channel: string;
  data: {
    event: {
      createdDate: string;
      replayId: number;
      type: "created" | "updated" | "deleted" | "undeleted";
    };
    sobject: Record<string, unknown>;
  };
}

export type StreamingEventHandler = (event: StreamingEvent) => Promise<void>;

interface StreamingSubscription {
  topic: string;
  replayId: number;
  cancel: () => void;
}

// ── CometD long-polling implementation ──

export class StreamingClient {
  private baseUrl: string = "";
  private clientId: string = "";
  private running = false;
  private subscriptions = new Map<string, StreamingEventHandler>();

  async connect(): Promise<void> {
    const instanceUrl = await getInstanceUrl();
    const apiVersion = getConfig().salesforce.apiVersion;
    this.baseUrl = `${instanceUrl}/cometd/${apiVersion.replace("v", "")}`;

    // Handshake
    const handshakeResponse = await this.cometdRequest([
      {
        channel: "/meta/handshake",
        version: "1.0",
        minimumVersion: "1.0",
        supportedConnectionTypes: ["long-polling"],
      },
    ]);

    if (!handshakeResponse[0]?.successful) {
      throw new Error(`CometD handshake failed: ${JSON.stringify(handshakeResponse[0])}`);
    }

    this.clientId = handshakeResponse[0].clientId as string;
    this.running = true;
    console.log(`[Streaming] Connected, clientId: ${this.clientId}`);
  }

  async subscribe(
    topic: string,
    handler: StreamingEventHandler,
    replayId = -1
  ): Promise<StreamingSubscription> {
    if (!this.clientId) {
      throw new Error("Not connected — call connect() first");
    }

    await this.cometdRequest([
      {
        channel: "/meta/subscribe",
        clientId: this.clientId,
        subscription: topic,
        ext: {
          replay: { [topic]: replayId },
        },
      },
    ]);

    this.subscriptions.set(topic, handler);
    console.log(`[Streaming] Subscribed to: ${topic}`);

    // Start long-polling loop
    this.poll().catch((err) => {
      console.error(`[Streaming] Poll error: ${err}`);
    });

    return {
      topic,
      replayId,
      cancel: () => {
        this.subscriptions.delete(topic);
        this.unsubscribe(topic).catch(() => {});
      },
    };
  }

  private async poll(): Promise<void> {
    while (this.running && this.subscriptions.size > 0) {
      try {
        const responses = await this.cometdRequest([
          {
            channel: "/meta/connect",
            clientId: this.clientId,
            connectionType: "long-polling",
          },
        ]);

        for (const resp of responses) {
          const channel = resp.channel as string | undefined;
          if (channel && !channel.startsWith("/meta/")) {
            const handler = this.subscriptions.get(channel);
            if (handler) {
              await handler(resp as unknown as StreamingEvent);
            }
          }
        }
      } catch (err) {
        console.error(`[Streaming] Poll error: ${err}`);
        await new Promise((r) => setTimeout(r, 5000));
      }
    }
  }

  private async unsubscribe(topic: string): Promise<void> {
    await this.cometdRequest([
      {
        channel: "/meta/unsubscribe",
        clientId: this.clientId,
        subscription: topic,
      },
    ]);
    console.log(`[Streaming] Unsubscribed from: ${topic}`);
  }

  async disconnect(): Promise<void> {
    this.running = false;
    if (this.clientId) {
      await this.cometdRequest([
        {
          channel: "/meta/disconnect",
          clientId: this.clientId,
        },
      ]);
      console.log("[Streaming] Disconnected");
    }
    this.subscriptions.clear();
  }

  private async cometdRequest(messages: unknown[]): Promise<Array<Record<string, string | unknown>>> {
    const token = await getAccessToken();

    const response = await fetch(this.baseUrl, {
      method: "POST",
      headers: {
        Authorization: `Bearer ${token}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify(messages),
    });

    if (!response.ok) {
      throw new Error(`CometD request failed (${response.status})`);
    }

    return (await response.json()) as Array<Record<string, string | unknown>>;
  }
}

// ── PushTopic / CDC topic helpers ──

export function getPushTopicChannel(topicName: string): string {
  return `/topic/${topicName}`;
}

export function getCdcChannel(objectName: string): string {
  return `/data/${objectName}ChangeEvent`;
}
