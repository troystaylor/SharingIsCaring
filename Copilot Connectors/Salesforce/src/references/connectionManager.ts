// ── Graph External Connection Manager ──
// Creates/manages the external connection and registers schema.

import { Client } from "@microsoft/microsoft-graph-client";
import { TokenCredentialAuthenticationProvider } from "@microsoft/microsoft-graph-client/authProviders/azureTokenCredentials";
import { ClientSecretCredential } from "@azure/identity";
import { ExternalItem, ExternalConnection } from "../models/graphTypes";
import { getSchema } from "../references/schema";
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

// ── Connection lifecycle ──

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
                url: "https://upload.wikimedia.org/wikipedia/commons/thumb/f/f9/Salesforce.com_logo.svg/1200px-Salesforce.com_logo.svg.png",
                size: "Small",
                altText: "Salesforce",
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
                        text: "${objectType}",
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
                        text: "Modified ${lastModifiedDateTime}",
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
          id: "salesforceResult",
          priority: 1,
          layout: getResultLayout(),
        },
      ],
    },
  };

  console.log(`[Graph] Creating external connection: ${config.connector.connectorId}`);

  try {
    await client
      .api("/external/connections")
      .post(connection);
    console.log(`[Graph] Connection created: ${config.connector.connectorId}`);
  } catch (error: unknown) {
    const graphError = error as { statusCode?: number; code?: string; message?: string };
    // Connection may already exist if deletion was async or a concurrent request created it
    if (graphError.statusCode === 409 ||
        (graphError.message && graphError.message.includes("already exists"))) {
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
    if (error instanceof Error && "statusCode" in error && (error as { statusCode: number }).statusCode === 404) {
      return null;
    }
    throw error;
  }
}

export async function deleteConnection(): Promise<void> {
  const config = getConfig().connector;
  const client = getGraphClient();

  console.log(`[Graph] Deleting external connection: ${config.connectorId}`);
  await client
    .api(`/external/connections/${config.connectorId}`)
    .delete();
  console.log(`[Graph] Connection deleted: ${config.connectorId}`);
}

export async function updateConnection(updates: Partial<ExternalConnection>): Promise<void> {
  const config = getConfig().connector;
  const client = getGraphClient();

  console.log(`[Graph] Updating external connection: ${config.connectorId}`);
  await client
    .api(`/external/connections/${config.connectorId}`)
    .patch(updates);
  console.log(`[Graph] Connection updated: ${config.connectorId}`);
}

// ── Schema registration ──

export async function registerSchema(): Promise<void> {
  const config = getConfig().connector;
  const client = getGraphClient();
  const schema = getSchema();

  console.log(`[Graph] Registering schema for connection: ${config.connectorId}`);

  try {
    await client
      .api(`/external/connections/${config.connectorId}/schema`)
      .header("Content-Type", "application/json")
      .patch(schema);
  } catch (error: unknown) {
    const graphError = error as { statusCode?: number; code?: string; body?: string; message?: string };
    console.error(`[Graph] Schema registration error: ${JSON.stringify({
      statusCode: graphError.statusCode,
      code: graphError.code,
      message: graphError.message,
      body: graphError.body,
    })}`);
    throw error;
  }

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
    // Graph SDK sometimes fails to parse the 200 response body — treat as success
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
        const err = result.reason as { statusCode?: number; code?: string; body?: string; message?: string };
        console.error(`[Graph] Failed to upsert item: ${JSON.stringify({
          statusCode: err.statusCode,
          code: err.code,
          message: err.message,
          body: typeof err.body === 'string' ? err.body.substring(0, 500) : undefined,
        })}`);
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
    // Update description and search settings on existing connection
    try {
      const config = getConfig();
      await updateConnection({
        id: config.connector.connectorId,
        name: config.connector.connectorName,
        description: config.connector.connectorDescription,
        searchSettings: {
          searchResultTemplates: [
            {
              id: "salesforceResult",
              priority: 1,
              layout: getResultLayout(),
            },
          ],
        },
      });
    } catch (error) {
      console.warn(`[Graph] Failed to update connection settings: ${error}`);
    }
  } else {
    console.log(`[Graph] Connection in state: ${existing.state}`);
  }
}
