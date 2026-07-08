/**
 * ACA Sandboxes execution tools.
 *
 * Creates ephemeral sandboxes to execute skill steps (shell commands, file operations).
 * Uses the ACA Sandboxes data plane API (management.{region}.azuredevcompute.io).
 *
 * Required env vars:
 *   AZURE_SUBSCRIPTION_ID
 *   AZURE_RESOURCE_GROUP
 *   SANDBOX_GROUP_NAME
 *   SANDBOX_REGION (e.g., westus2)
 *
 * Auth: Managed Identity or DefaultAzureCredential → token for https://dynamicsessions.io/.default
 */

const SANDBOX_REGION = process.env.SANDBOX_REGION || "westus2";
const SUBSCRIPTION_ID = process.env.AZURE_SUBSCRIPTION_ID;
const RESOURCE_GROUP = process.env.AZURE_RESOURCE_GROUP;
const SANDBOX_GROUP = process.env.SANDBOX_GROUP_NAME;

// In-memory tracking of active sandboxes
const activeSandboxes = new Map();

async function getDataPlaneToken() {
    // Container Apps managed identity uses IDENTITY_ENDPOINT + IDENTITY_HEADER
    const endpoint = process.env.IDENTITY_ENDPOINT;
    const header = process.env.IDENTITY_HEADER;

    if (endpoint && header) {
        const url = `${endpoint}?resource=https://dynamicsessions.io&api-version=2019-08-01`;
        const res = await fetch(url, { headers: { "X-IDENTITY-HEADER": header } });
        if (!res.ok) throw new Error(`MI token failed: ${res.status} ${await res.text()}`);
        const data = await res.json();
        return data.access_token;
    }

    // Fallback: AZURE_SANDBOX_TOKEN env var for local dev
    if (process.env.AZURE_SANDBOX_TOKEN) return process.env.AZURE_SANDBOX_TOKEN;
    throw new Error("No managed identity available and AZURE_SANDBOX_TOKEN not set");
}

function getDataPlaneEndpoint() {
    return `https://management.${SANDBOX_REGION}.azuredevcompute.io`;
}

function sandboxBasePath() {
    return `${getDataPlaneEndpoint()}/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}/sandboxGroups/${SANDBOX_GROUP}`;
}

// Pre-built disk image IDs (created in agent-skills-sandboxes group)
const DISK_IMAGES = {
    ubuntu: { name: "ubuntu", isPublic: true },
    "dotnet-sdk": { id: "cec2e767-85f1-4833-b879-4011898f5528" }, // mcr.microsoft.com/dotnet/sdk:10.0
};

async function createSandbox({ image, tier }) {
    const token = await getDataPlaneToken();
    const url = `${sandboxBasePath()}/sandboxes?includeDebug=true`;

    // Resolve image to disk image reference
    const diskImage = DISK_IMAGES[image] || DISK_IMAGES["dotnet-sdk"]; // Default to dotnet-sdk for skill execution

    const body = {
        sourcesRef: { diskImage },
        resources: tierToResources(tier || "M"),
        lifecycle: {
            autoSuspendPolicy: { enabled: true, interval: 600, mode: "Memory" },
        },
    };

    const res = await fetch(url, {
        method: "PUT",
        headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });

    if (!res.ok) throw new Error(`Create sandbox failed: ${res.status} ${await res.text()}`);
    const data = await res.json();
    const sandboxId = data.id || data.name;
    activeSandboxes.set(sandboxId, { created: Date.now(), image, commands: [] });
    return sandboxId;
}

async function executeCommand(sandboxId, command) {
    const token = await getDataPlaneToken();
    const url = `${sandboxBasePath()}/sandboxes/${sandboxId}/executeShellCommand`;

    const res = await fetch(url, {
        method: "POST",
        headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
        body: JSON.stringify({ command }),
    });

    if (!res.ok) throw new Error(`Execute failed: ${res.status} ${await res.text()}`);
    const data = await res.json();

    // Track command history
    const sandbox = activeSandboxes.get(sandboxId);
    if (sandbox) sandbox.commands.push({ command, exitCode: data.exitCode, timestamp: Date.now() });

    return {
        stdout: data.stdout || "",
        stderr: data.stderr || "",
        exitCode: data.exitCode ?? -1,
    };
}

async function deleteSandbox(sandboxId) {
    const token = await getDataPlaneToken();
    const url = `${sandboxBasePath()}/sandboxes/${sandboxId}`;

    await fetch(url, { method: "DELETE", headers: { Authorization: `Bearer ${token}` } });
    activeSandboxes.delete(sandboxId);
}

async function listFiles(sandboxId, path) {
    const token = await getDataPlaneToken();
    const url = `${sandboxBasePath()}/sandboxes/${sandboxId}/files/list?path=${encodeURIComponent(path || "/")}`;
    const res = await fetch(url, { headers: { Authorization: `Bearer ${token}` } });
    if (!res.ok) throw new Error(`List files failed: ${res.status} ${await res.text()}`);
    return res.json();
}

async function readFile(sandboxId, path) {
    const token = await getDataPlaneToken();
    const url = `${sandboxBasePath()}/sandboxes/${sandboxId}/files?path=${encodeURIComponent(path)}`;
    const res = await fetch(url, { headers: { Authorization: `Bearer ${token}` } });
    if (!res.ok) throw new Error(`Read file failed: ${res.status} ${await res.text()}`);
    return res.text();
}

function tierToResources(tier) {
    const tiers = {
        XS: { cpu: "250m", memory: "512Mi" },
        S: { cpu: "500m", memory: "1024Mi" },
        M: { cpu: "1000m", memory: "2048Mi" },
        L: { cpu: "2000m", memory: "4096Mi" },
    };
    return tiers[tier] || tiers.M;
}

// --- Tool registration ---

export function registerSandboxTools(server, z) {
    server.tool(
        "execute_in_sandbox",
        "Execute shell commands in an isolated ACA Sandbox. Creates a fresh sandbox (or reuses an existing one), runs the commands sequentially, and returns stdout/stderr for each. Use to execute skill steps that require a terminal. Each sandbox auto-deletes after 10 minutes of idle.",
        {
            commands: z.array(z.string()).describe("Shell commands to execute sequentially (e.g., ['dotnet new console', 'dotnet add package Microsoft.Extensions.AI'])"),
            image: z.string().optional().describe("Disk image for the sandbox. Default: 'ubuntu'. Options: 'ubuntu', 'dotnet-sdk', 'node', 'python'."),
            sandbox_id: z.string().optional().describe("Optional: reuse an existing sandbox by ID. Omit to create a new one."),
        },
        async ({ commands, image, sandbox_id }) => {
            try {
                const id = sandbox_id || (await createSandbox({ image, tier: "M" }));
                const results = [];

                for (const cmd of commands) {
                    const result = await executeCommand(id, cmd);
                    results.push({ command: cmd, ...result });
                    if (result.exitCode !== 0) break;
                }

                return { content: [{ type: "text", text: JSON.stringify({ sandbox_id: id, results, reusable: true }) }] };
            } catch (e) {
                console.error("execute_in_sandbox error:", e.message);
                return { content: [{ type: "text", text: JSON.stringify({ error: e.message }) }], isError: true };
            }
        }
    );

    server.tool(
        "get_sandbox_status",
        "Check the status of an active sandbox and see its command history.",
        {
            sandbox_id: z.string().describe("The sandbox ID returned by execute_in_sandbox."),
        },
        async ({ sandbox_id }) => {
            const sandbox = activeSandboxes.get(sandbox_id);
            if (!sandbox) {
                return { content: [{ type: "text", text: JSON.stringify({ error: "Sandbox not found or expired" }) }], isError: true };
            }
            return {
                content: [{
                    type: "text",
                    text: JSON.stringify({
                        sandbox_id,
                        created: new Date(sandbox.created).toISOString(),
                        age_seconds: Math.round((Date.now() - sandbox.created) / 1000),
                        commands_run: sandbox.commands.length,
                        last_command: sandbox.commands.at(-1) || null,
                    }),
                }],
            };
        }
    );

    server.tool(
        "delete_sandbox",
        "Delete a sandbox immediately. Use when done with execution to free resources.",
        {
            sandbox_id: z.string().describe("The sandbox ID to delete."),
        },
        async ({ sandbox_id }) => {
            await deleteSandbox(sandbox_id);
            return { content: [{ type: "text", text: JSON.stringify({ deleted: sandbox_id }) }] };
        }
    );

    server.tool(
        "read_sandbox_file",
        "Read the contents of a file from a sandbox. Use after executing commands to inspect generated files, code, configs, or output.",
        {
            sandbox_id: z.string().describe("The sandbox ID."),
            path: z.string().describe("Absolute path to the file (e.g., /root/MyProject/Program.cs)"),
        },
        async ({ sandbox_id, path }) => {
            try {
                const content = await readFile(sandbox_id, path);
                return { content: [{ type: "text", text: content }] };
            } catch (e) {
                console.error("read_sandbox_file error:", e.message);
                return { content: [{ type: "text", text: JSON.stringify({ error: e.message }) }], isError: true };
            }
        }
    );

    server.tool(
        "list_sandbox_files",
        "List files and directories in a sandbox path. Use to explore what was created by skill execution.",
        {
            sandbox_id: z.string().describe("The sandbox ID."),
            path: z.string().optional().describe("Directory path to list. Default: /root"),
        },
        async ({ sandbox_id, path }) => {
            try {
                const files = await listFiles(sandbox_id, path || "/root");
                return { content: [{ type: "text", text: JSON.stringify(files) }] };
            } catch (e) {
                console.error("list_sandbox_files error:", e.message);
                return { content: [{ type: "text", text: JSON.stringify({ error: e.message }) }], isError: true };
            }
        }
    );

    server.tool(
        "apply_skill",
        "One-shot tool: fetches a skill from GitHub, extracts executable commands from its code blocks, and runs them in a fresh sandbox. Returns the combined skill guidance + execution results. Use when the user says 'apply that skill' or 'run the steps from [skill]'.",
        {
            owner: z.string().describe("Repository owner (e.g., dotnet)"),
            repo: z.string().describe("Repository name (e.g., skills)"),
            skill_path: z.string().describe("Path to skill directory (e.g., plugins/dotnet-ai/skills/technology-selection)"),
            image: z.string().optional().describe("Sandbox disk image. Default: ubuntu."),
            github_token: z.string().optional().describe("Optional GitHub PAT for higher rate limits."),
        },
        async ({ owner, repo, skill_path, image, github_token }) => {
            try {
                // 1. Fetch skill from GitHub
                const githubUrl = `https://api.github.com/repos/${owner}/${repo}/contents/${skill_path}/SKILL.md?ref=main`;
                const headers = { "User-Agent": "AgentSkills-MCP/1.0", Accept: "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28" };
                if (github_token) headers.Authorization = `token ${github_token}`;
                const ghRes = await fetch(githubUrl, { headers });
                if (!ghRes.ok) throw new Error(`GitHub ${ghRes.status}: ${await ghRes.text()}`);
                const ghData = await ghRes.json();
                const skillContent = Buffer.from(ghData.content, "base64").toString("utf-8");

                // 2. Extract executable commands from bash/shell code blocks
                const codeBlockRegex = /```(?:bash|shell|sh|console)\n([\s\S]*?)```/g;
                const commands = [];
                let match;
                while ((match = codeBlockRegex.exec(skillContent)) !== null) {
                    const block = match[1].trim().split("\n").filter(l => l && !l.startsWith("#") && !l.startsWith("//"));
                    commands.push(...block);
                }

                if (commands.length === 0) {
                    return { content: [{ type: "text", text: JSON.stringify({ skill: `${owner}/${repo}/${skill_path}`, content: skillContent, commands_found: 0, note: "No executable code blocks found. This skill is guidance-only." }) }] };
                }

                // 3. Execute in sandbox
                const sandboxId = await createSandbox({ image, tier: "M" });
                const results = [];
                for (const cmd of commands.slice(0, 20)) { // Cap at 20 commands
                    const result = await executeCommand(sandboxId, cmd);
                    results.push({ command: cmd, ...result });
                    if (result.exitCode !== 0) break;
                }

                return { content: [{ type: "text", text: JSON.stringify({ skill: `${owner}/${repo}/${skill_path}`, sandbox_id: sandboxId, commands_executed: results.length, total_commands_found: commands.length, results, reusable: true }) }] };
            } catch (e) {
                console.error("apply_skill error:", e.message);
                return { content: [{ type: "text", text: JSON.stringify({ error: e.message }) }], isError: true };
            }
        }
    );
}
