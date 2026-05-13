"use strict";
// ── Graph External Connection Manager ──
// Creates/manages the external connection and registers schema.
Object.defineProperty(exports, "__esModule", { value: true });
exports.createConnection = createConnection;
exports.getConnection = getConnection;
exports.deleteConnection = deleteConnection;
exports.registerSchema = registerSchema;
exports.getSchemaStatus = getSchemaStatus;
exports.waitForSchemaReady = waitForSchemaReady;
exports.upsertItem = upsertItem;
exports.deleteItem = deleteItem;
exports.upsertItemsBatch = upsertItemsBatch;
exports.ensureConnection = ensureConnection;
const microsoft_graph_client_1 = require("@microsoft/microsoft-graph-client");
const azureTokenCredentials_1 = require("@microsoft/microsoft-graph-client/authProviders/azureTokenCredentials");
const identity_1 = require("@azure/identity");
const schema_1 = require("./schema");
const connectorConfig_1 = require("../config/connectorConfig");
let graphClient = null;
function getGraphClient() {
    if (graphClient)
        return graphClient;
    const tenantId = process.env.AZURE_TENANT_ID;
    const clientId = process.env.AZURE_CLIENT_ID;
    const clientSecret = process.env.AZURE_CLIENT_SECRET;
    const credential = new identity_1.ClientSecretCredential(tenantId, clientId, clientSecret);
    const authProvider = new azureTokenCredentials_1.TokenCredentialAuthenticationProvider(credential, {
        scopes: ["https://graph.microsoft.com/.default"],
    });
    graphClient = microsoft_graph_client_1.Client.initWithMiddleware({ authProvider });
    return graphClient;
}
// ── Adaptive Card result layout for Copilot/Search ──
function getResultLayout() {
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
async function createConnection() {
    const config = (0, connectorConfig_1.getConfig)();
    const client = getGraphClient();
    const connection = {
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
    }
    catch (error) {
        const graphError = error;
        if (graphError.statusCode === 409 ||
            (graphError.message && graphError.message.includes("already exists"))) {
            console.log(`[Graph] Connection already exists: ${config.connector.connectorId}`);
            return;
        }
        throw error;
    }
}
async function getConnection() {
    const config = (0, connectorConfig_1.getConfig)().connector;
    const client = getGraphClient();
    try {
        const connection = await client
            .api(`/external/connections/${config.connectorId}`)
            .get();
        return connection;
    }
    catch (error) {
        if (error instanceof Error &&
            "statusCode" in error &&
            error.statusCode === 404) {
            return null;
        }
        throw error;
    }
}
async function deleteConnection() {
    const config = (0, connectorConfig_1.getConfig)().connector;
    const client = getGraphClient();
    console.log(`[Graph] Deleting external connection: ${config.connectorId}`);
    await client.api(`/external/connections/${config.connectorId}`).delete();
    console.log(`[Graph] Connection deleted: ${config.connectorId}`);
}
// ── Schema registration ──
async function registerSchema() {
    const config = (0, connectorConfig_1.getConfig)().connector;
    const client = getGraphClient();
    const schema = (0, schema_1.getSchema)();
    console.log(`[Graph] Registering schema for connection: ${config.connectorId}`);
    await client
        .api(`/external/connections/${config.connectorId}/schema`)
        .header("Content-Type", "application/json")
        .patch(schema);
    console.log("[Graph] Schema registration initiated (may take up to 10 minutes)");
}
async function getSchemaStatus() {
    const config = (0, connectorConfig_1.getConfig)().connector;
    const client = getGraphClient();
    const schema = await client
        .api(`/external/connections/${config.connectorId}/schema`)
        .get();
    return schema?.status || "unknown";
}
async function waitForSchemaReady(pollIntervalMs = 30000, maxWaitMs = 900000) {
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
async function upsertItem(item) {
    const config = (0, connectorConfig_1.getConfig)().connector;
    const client = getGraphClient();
    try {
        await client
            .api(`/external/connections/${config.connectorId}/items/${item.id}`)
            .header("Content-Type", "application/json")
            .put(item);
    }
    catch (err) {
        const e = err;
        if (e.statusCode === 200 && e.code === "SyntaxError") {
            return; // PUT succeeded, response parsing failed — ignore
        }
        throw err;
    }
}
async function deleteItem(itemId) {
    const config = (0, connectorConfig_1.getConfig)().connector;
    const client = getGraphClient();
    await client
        .api(`/external/connections/${config.connectorId}/items/${itemId}`)
        .delete();
}
async function upsertItemsBatch(items, batchSize = 4, delayMs = 250) {
    let succeeded = 0;
    let failed = 0;
    for (let i = 0; i < items.length; i += batchSize) {
        const batch = items.slice(i, i + batchSize);
        const results = await Promise.allSettled(batch.map((item) => upsertItem(item)));
        for (const result of results) {
            if (result.status === "fulfilled") {
                succeeded++;
            }
            else {
                failed++;
                const err = result.reason;
                console.error(`[Graph] Failed to upsert item: ${JSON.stringify({
                    statusCode: err.statusCode,
                    message: err.message,
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
async function ensureConnection() {
    const existing = await getConnection();
    if (!existing) {
        await createConnection();
        await registerSchema();
        await waitForSchemaReady();
    }
    else if (existing.state === "draft") {
        await registerSchema();
        await waitForSchemaReady();
    }
    else if (existing.state === "ready") {
        console.log("[Graph] Connection already exists and is ready");
    }
    else {
        console.log(`[Graph] Connection in state: ${existing.state}`);
    }
}
//# sourceMappingURL=connectionManager.js.map