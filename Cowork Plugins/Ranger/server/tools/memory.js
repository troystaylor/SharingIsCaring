// Memory tools — in-memory store (Cosmos blocked by Azure Policy network restrictions)
// TODO: Switch to Cosmos with private endpoint when VNet is configured

const memoryStore = new Map(); // key → { userId, scope, key, value, updatedAt }

function getStore() {
    return memoryStore;
}

export function registerMemoryTools(server, z) {
    server.tool("save_memory", "Save information to persistent memory. Private by default.", {
        key: z.string().describe("Memory key (e.g., 'user_preference', 'project_notes')"),
        value: z.string().describe("Value to store"),
        scope: z.string().optional().describe("'private' (default, per-user) or 'shared' (org-wide)"),
        user_id: z.string().optional().describe("User ID (from Cowork context). Required for private scope."),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ key, value, scope, user_id }) => {
        const store = getStore();
        const s = scope || "private";
        const uid = user_id || "anonymous";
        const partitionKey = s === "shared" ? "org" : uid;
        const id = `${partitionKey}:${key}`;

        store.set(id, { userId: partitionKey, scope: s, key, value, updatedAt: new Date().toISOString() });
        return { content: [{ type: "text", text: JSON.stringify({ success: true, key, scope: s }) }] };
    });

    server.tool("recall_memory", "Retrieve a stored memory by key.", {
        key: z.string().describe("Memory key to retrieve"),
        scope: z.string().optional().describe("'private' or 'shared'. Default: private"),
        user_id: z.string().optional().describe("User ID for private scope"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ key, scope, user_id }) => {
        const store = getStore();
        const s = scope || "private";
        const partitionKey = s === "shared" ? "org" : (user_id || "anonymous");
        const id = `${partitionKey}:${key}`;

        const record = store.get(id);
        if (!record) return { content: [{ type: "text", text: JSON.stringify({ found: false, key }) }] };
        return { content: [{ type: "text", text: JSON.stringify({ found: true, key, value: record.value, updatedAt: record.updatedAt }) }] };
    });

    server.tool("list_memories", "List all stored memories.", {
        scope: z.string().optional().describe("'private' or 'shared'. Default: private"),
        user_id: z.string().optional().describe("User ID for private scope"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ scope, user_id }) => {
        const store = getStore();
        const s = scope || "private";
        const partitionKey = s === "shared" ? "org" : (user_id || "anonymous");

        const memories = [];
        for (const [, v] of store) {
            if (v.userId === partitionKey) memories.push({ key: v.key, value: v.value, updatedAt: v.updatedAt });
        }
        return { content: [{ type: "text", text: JSON.stringify({ scope: s, count: memories.length, memories }) }] };
    });

    server.tool("delete_memory", "Delete a stored memory.", {
        key: z.string().describe("Memory key to delete"),
        scope: z.string().optional().describe("'private' or 'shared'"),
        user_id: z.string().optional().describe("User ID for private scope"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ key, scope, user_id }) => {
        const store = getStore();
        const s = scope || "private";
        const partitionKey = s === "shared" ? "org" : (user_id || "anonymous");
        const id = `${partitionKey}:${key}`;

        if (store.has(id)) {
            store.delete(id);
            return { content: [{ type: "text", text: JSON.stringify({ success: true, key, deleted: true }) }] };
        }
        return { content: [{ type: "text", text: JSON.stringify({ success: false, key, error: "Not found" }) }] };
    });
}
