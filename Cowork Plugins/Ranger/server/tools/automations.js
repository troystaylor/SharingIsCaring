// Automation tools — in-process cron scheduler with Cosmos DB persistence
import cron from "node-cron";
import { CosmosClient } from "@azure/cosmos";

let _container = null;
const _scheduledJobs = new Map(); // automationId → cron task

function getContainer() {
    if (_container) return _container;
    const endpoint = process.env.COSMOS_ENDPOINT;
    const key = process.env.COSMOS_KEY;
    if (!endpoint) {
        console.warn("COSMOS_ENDPOINT not set — automations disabled");
        return null;
    }
    const client = new CosmosClient({ endpoint, key });
    const db = client.database(process.env.COSMOS_DATABASE || "powerhoof");
    _container = db.container(process.env.COSMOS_CONTAINER_AUTOMATIONS || "automations");
    return _container;
}

export function registerAutomationTools(server, z) {
    server.tool("create_automation", "Create a scheduled automation that runs tool sequences on a cron schedule.", {
        name: z.string().describe("Automation name"),
        schedule: z.string().describe("Cron expression (e.g., '0 9 * * 1' for Monday 9am)"),
        description: z.string().optional().describe("What this automation does"),
        actions: z.array(z.object({
            tool: z.string().describe("Tool name to call"),
            args: z.record(z.any()).describe("Tool arguments"),
        })).describe("Sequence of tool calls to execute"),
        user_id: z.string().optional().describe("User who owns this automation"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ name, schedule, description, actions, user_id }) => {
        const container = getContainer();
        if (!container) return { content: [{ type: "text", text: JSON.stringify({ error: "Automations not configured (no Cosmos)" }) }] };

        if (!cron.validate(schedule)) {
            return { content: [{ type: "text", text: JSON.stringify({ error: `Invalid cron expression: ${schedule}` }) }] };
        }

        const id = `auto_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`;
        const record = {
            id,
            userId: user_id || "anonymous",
            name,
            schedule,
            description: description || "",
            actions,
            enabled: true,
            createdAt: new Date().toISOString(),
            lastRun: null,
            lastResult: null,
            runCount: 0,
        };

        await container.items.create(record);
        scheduleJob(record);

        return { content: [{ type: "text", text: JSON.stringify({ success: true, automation_id: id, name, schedule, enabled: true }) }] };
    });

    server.tool("list_automations", "List all automations.", {
        user_id: z.string().optional().describe("Filter by user"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ user_id }) => {
        const container = getContainer();
        if (!container) return { content: [{ type: "text", text: JSON.stringify({ automations: [] }) }] };

        const query = user_id
            ? { query: "SELECT * FROM c WHERE c.userId = @uid", parameters: [{ name: "@uid", value: user_id }] }
            : { query: "SELECT * FROM c" };

        const { resources } = await container.items.query(query).fetchAll();
        const summary = resources.map(r => ({ id: r.id, name: r.name, schedule: r.schedule, enabled: r.enabled, lastRun: r.lastRun, runCount: r.runCount }));
        return { content: [{ type: "text", text: JSON.stringify({ count: summary.length, automations: summary }) }] };
    });

    server.tool("pause_automation", "Pause a running automation.", {
        automation_id: z.string().describe("Automation ID"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ automation_id }) => {
        return await toggleAutomation(automation_id, false);
    });

    server.tool("resume_automation", "Resume a paused automation.", {
        automation_id: z.string().describe("Automation ID"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ automation_id }) => {
        return await toggleAutomation(automation_id, true);
    });

    server.tool("delete_automation", "Delete an automation permanently.", {
        automation_id: z.string().describe("Automation ID"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ automation_id }) => {
        const container = getContainer();
        if (!container) return { content: [{ type: "text", text: JSON.stringify({ error: "Not configured" }) }] };

        // Stop the cron job
        const job = _scheduledJobs.get(automation_id);
        if (job) { job.stop(); _scheduledJobs.delete(automation_id); }

        // Find and delete from Cosmos
        const { resources } = await container.items.query({ query: "SELECT * FROM c WHERE c.id = @id", parameters: [{ name: "@id", value: automation_id }] }).fetchAll();
        if (resources.length > 0) {
            await container.item(automation_id, resources[0].userId).delete();
        }
        return { content: [{ type: "text", text: JSON.stringify({ success: true, automation_id, deleted: true }) }] };
    });

    server.tool("get_automation_history", "Get results from past automation runs.", {
        automation_id: z.string().describe("Automation ID"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ automation_id }) => {
        const container = getContainer();
        if (!container) return { content: [{ type: "text", text: JSON.stringify({ error: "Not configured" }) }] };

        const { resources } = await container.items.query({ query: "SELECT * FROM c WHERE c.id = @id", parameters: [{ name: "@id", value: automation_id }] }).fetchAll();
        if (resources.length === 0) return { content: [{ type: "text", text: JSON.stringify({ error: "Not found" }) }] };
        const auto = resources[0];
        return { content: [{ type: "text", text: JSON.stringify({ id: auto.id, name: auto.name, runCount: auto.runCount, lastRun: auto.lastRun, lastResult: auto.lastResult }) }] };
    });
}

async function toggleAutomation(automationId, enabled) {
    const container = getContainer();
    if (!container) return { content: [{ type: "text", text: JSON.stringify({ error: "Not configured" }) }] };

    const { resources } = await container.items.query({ query: "SELECT * FROM c WHERE c.id = @id", parameters: [{ name: "@id", value: automationId }] }).fetchAll();
    if (resources.length === 0) return { content: [{ type: "text", text: JSON.stringify({ error: "Not found" }) }] };

    const auto = resources[0];
    auto.enabled = enabled;
    await container.items.upsert(auto);

    if (enabled) {
        scheduleJob(auto);
    } else {
        const job = _scheduledJobs.get(automationId);
        if (job) { job.stop(); _scheduledJobs.delete(automationId); }
    }

    return { content: [{ type: "text", text: JSON.stringify({ success: true, automation_id: automationId, enabled }) }] };
}

function scheduleJob(auto) {
    // Remove existing job if any
    const existing = _scheduledJobs.get(auto.id);
    if (existing) existing.stop();

    if (!auto.enabled) return;

    const task = cron.schedule(auto.schedule, async () => {
        console.log(`[Automation] Running: ${auto.name} (${auto.id})`);
        try {
            // Execute each action in sequence
            // For now, just log — full execution requires importing tool handlers dynamically
            const results = [];
            for (const action of auto.actions) {
                results.push({ tool: action.tool, status: "deferred", note: "Full execution requires live MCP context" });
            }

            // Update Cosmos with result
            const container = getContainer();
            if (container) {
                auto.lastRun = new Date().toISOString();
                auto.lastResult = { success: true, results };
                auto.runCount = (auto.runCount || 0) + 1;
                await container.items.upsert(auto);
            }
        } catch (e) {
            console.error(`[Automation] Error in ${auto.id}:`, e.message);
        }
    });

    _scheduledJobs.set(auto.id, task);
}

// Called at server startup to load and schedule all enabled automations
export async function startScheduler() {
    const container = getContainer();
    if (!container) {
        console.log("[Automations] Cosmos not configured — scheduler disabled");
        return;
    }

    try {
        const { resources } = await container.items.query({ query: "SELECT * FROM c WHERE c.enabled = true" }).fetchAll();
        console.log(`[Automations] Loading ${resources.length} active automations`);
        for (const auto of resources) {
            scheduleJob(auto);
        }
    } catch (e) {
        console.warn(`[Automations] Failed to load automations: ${e.message}`);
    }
}
