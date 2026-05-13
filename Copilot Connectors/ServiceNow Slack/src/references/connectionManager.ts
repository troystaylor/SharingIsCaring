// ── Graph External Connection Manager ──
// Creates/manages the external connection and registers schema.

import { Client } from "@microsoft/microsoft-graph-client";
import { TokenCredentialAuthenticationProvider } from "@microsoft/microsoft-graph-client/authProviders/azureTokenCredentials";
import { ClientSecretCredential } from "@azure/identity";
import { ExternalItem, ExternalConnection } from "../models/types";
import { getSchema } from "./schema";
import { getConfig } from "../config/connectorConfig";

let graphClient: Client | null = null;

function getGraphClient(): Client {
  if (graphClient) return graphClient;

  const tenantId = process.env.AZURE_TENANT_ID!;
  const clientId = process.env.AZURE_CLIENT_ID!;
  const clientSecret = process.env.AZURE_CLIENT_SECRET!;

  const credential = new ClientSecretCredential(
    tenantId,
    clientId,
    clientSecret
  );

  const authProvider = new TokenCredentialAuthenticationProvider(credential, {
    scopes: ["https://graph.microsoft.com/.default"],
  });

  graphClient = Client.initWithMiddleware({ authProvider });
  return graphClient;
}

// ── Adaptive Card result layout for Copilot/Search ──

function getResultLayout(): object {
  return {
    type: "AdaptiveCard",
    version: "1.3",
    body: [
      {
        type: "ColumnSet",
        columns: [
          {
            type: "Column",
            width: "auto",
            items: [
              {
                type: "Image",
                url: "https://a.slack-edge.com/80588/marketing/img/meta/slack_hash_256.png",
                size: "Small",
                altText: "Slack",
              },
            ],
          },
          {
            type: "Column",
            width: "stretch",
            items: [
              {
                type: "TextBlock",
                text: "[${title}](${url})",
                weight: "Bolder",
                size: "Medium",
                wrap: true,
              },
              {
                type: "ColumnSet",
                spacing: "Small",
                columns: [
                  {
                    type: "Column",
                    width: "auto",
                    items: [
                      {
                        type: "TextBlock",
                        text: "#${channelName}",
                        color: "Accent",
                        weight: "Bolder",
                        size: "Small",
                      },
                    ],
                  },
                  {
                    type: "Column",
                    width: "auto",
                    items: [
                      {
                        type: "TextBlock",
                        text: "${authorName}",
                        size: "Small",
                        isSubtle: true,
                      },
                    ],
                  },
                  {
                    type: "Column",
                    width: "auto",
                    items: [
                      {
                        type: "TextBlock",
                        text: "${messageTimestamp}",
                        isSubtle: true,
                        size: "Small",
                      },
                    ],
                  },
                ],
              },
            ],
          },
        ],
      },
      {
        type: "TextBlock",
        text: "${description}",
        wrap: true,
        maxLines: 3,
        spacing: "Small",
        isSubtle: true,
      },
    ],
    $schema: "http://adaptivecards.io/schemas/adaptive-card.json",
  };
}

// ── Connection lifecycle ──

export async function createConnection(): Promise<void> {
  const config = getConfig();
  const client = getGraphClient();

  const connection: ExternalConnection = {
    id: config.connector.connectorId,
    name: config.connector.connectorName,
    description: config.connector.connectorDescription,
    searchSettings: {
      searchResultTemplates: [
        {
          id: "slackResult",
          priority: 1,
          layout: getResultLayout(),
        },
      ],
    },
  };

  console.log(`[Graph] Creating external connection: ${config.connector.connectorId}`);

  try {
    await client.api("/external/connections").post(connection);
    console.log(`[Graph] Connection created: ${config.connector.connectorId}`);
  } catch (error: unknown) {
    const graphError = error as { statusCode?: number; message?: string };
    if (
      graphError.statusCode === 409 ||
      (graphError.message && graphError.message.includes("already exists"))
    ) {
      console.log(`[Graph] Connection already exists: ${config.connector.connectorId}`);
      return;
    }
    throw error;
  }
}

export async function getConnection(): Promise<ExternalConnection | null> {
  const config = getConfig().connector;
  const client = getGraphClient();

  try {
    const connection = await client
      .api(`/external/connections/${config.connectorId}`)
      .get();
    return connection as ExternalConnection;
  } catch (error: unknown) {
    if (
      error instanceof Error &&
      "statusCode" in error &&
      (error as { statusCode: number }).statusCode === 404
    ) {
      return null;
    }
    throw error;
  }
}

export async function deleteConnection(): Promise<void> {
  const config = getConfig().connector;
  const client = getGraphClient();

  console.log(`[Graph] Deleting external connection: ${config.connectorId}`);
  await client.api(`/external/connections/${config.connectorId}`).delete();
  console.log(`[Graph] Connection deleted: ${config.connectorId}`);
}

// ── Schema registration ──

export async function registerSchema(): Promise<void> {
  const config = getConfig().connector;
  const client = getGraphClient();
  const schema = getSchema();

  console.log(`[Graph] Registering schema for connection: ${config.connectorId}`);

  await client
    .api(`/external/connections/${config.connectorId}/schema`)
    .header("Content-Type", "application/json")
    .patch(schema);

  console.log("[Graph] Schema registration initiated (may take up to 10 minutes)");
}

export async function getSchemaStatus(): Promise<string> {
  const config = getConfig().connector;
  const client = getGraphClient();

  const schema = await client
    .api(`/external/connections/${config.connectorId}/schema`)
    .get();

  return schema?.status || "unknown";
}

export async function waitForSchemaReady(
  pollIntervalMs = 30000,
  maxWaitMs = 900000
): Promise<void> {
  const start = Date.now();

  while (Date.now() - start < maxWaitMs) {
    const connection = await getConnection();
    if (connection?.state === "ready") {
      console.log("[Graph] Connection is ready — schema registered");
      return;
    }

    console.log(`[Graph] Connection state: ${connection?.state || "unknown"}, waiting...`);
    await new Promise((r) => setTimeout(r, pollIntervalMs));
  }

  throw new Error("Schema did not become ready within timeout");
}

// ── Item operations ──

export async function upsertItem(item: ExternalItem): Promise<void> {
  const config = getConfig().connector;
  const client = getGraphClient();

  try {
    await client
      .api(`/external/connections/${config.connectorId}/items/${item.id}`)
      .header("Content-Type", "application/json")
      .put(item);
  } catch (err: unknown) {
    const e = err as { statusCode?: number; code?: string };
    if (e.statusCode === 200 && e.code === "SyntaxError") {
      return; // PUT succeeded, response parsing failed — ignore
    }
    throw err;
  }
}

export async function deleteItem(itemId: string): Promise<void> {
  const config = getConfig().connector;
  const client = getGraphClient();

  await client
    .api(`/external/connections/${config.connectorId}/items/${itemId}`)
    .delete();
}

export async function upsertItemsBatch(
  items: ExternalItem[],
  batchSize = 4,
  delayMs = 250
): Promise<{ succeeded: number; failed: number }> {
  let succeeded = 0;
  let failed = 0;

  for (let i = 0; i < items.length; i += batchSize) {
    const batch = items.slice(i, i + batchSize);
    const results = await Promise.allSettled(
      batch.map((item) => upsertItem(item))
    );

    for (const result of results) {
      if (result.status === "fulfilled") {
        succeeded++;
      } else {
        failed++;
        const err = result.reason as { statusCode?: number; message?: string };
        console.error(
          `[Graph] Failed to upsert item: ${JSON.stringify({
            statusCode: err.statusCode,
            message: err.message,
          })}`
        );
      }
    }

    if (i + batchSize < items.length) {
      await new Promise((r) => setTimeout(r, delayMs));
    }
  }

  console.log(`[Graph] Batch upsert complete: ${succeeded} succeeded, ${failed} failed`);
  return { succeeded, failed };
}

// ── Ensure connection + schema ──

export async function ensureConnection(): Promise<void> {
  const existing = await getConnection();

  if (!existing) {
    await createConnection();
    await registerSchema();
    await waitForSchemaReady();
  } else if (existing.state === "draft") {
    await registerSchema();
    await waitForSchemaReady();
  } else if (existing.state === "ready") {
    console.log("[Graph] Connection already exists and is ready");
  } else {
    console.log(`[Graph] Connection in state: ${existing.state}`);
  }
}
