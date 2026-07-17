// Shared ACA Sandbox data plane client with managed identity auth

const SUBSCRIPTION_ID = process.env.AZURE_SUBSCRIPTION_ID;
const RESOURCE_GROUP = process.env.AZURE_RESOURCE_GROUP || "rg-aca-code-interpreter";
const SANDBOX_GROUP = process.env.SANDBOX_GROUP_NAME || "code-interpreter";
const REGION = process.env.SANDBOX_REGION || "westus2";
const DATA_PLANE_BASE = `https://management.${REGION}.azuredevcompute.io`;

// Disk images
const DISK_IMAGES = {
    browser: { id: process.env.BROWSER_DISK_IMAGE || "399fdfc6-05f9-4060-aba1-baa3d6286d12", isPublic: false },
    code: { id: process.env.CODE_DISK_IMAGE || "e50631d1-311f-4fd7-95ed-777f77030935", isPublic: false },
    unified: { id: process.env.UNIFIED_DISK_IMAGE, isPublic: false }, // Will be set once built
};

// Token cache
let _cachedToken = null;
let _tokenExpiry = 0;

export async function getDataPlaneToken() {
    if (_cachedToken && Date.now() < _tokenExpiry - 60000) {
        return _cachedToken;
    }

    // 1. Container Apps managed identity
    const endpoint = process.env.IDENTITY_ENDPOINT;
    const header = process.env.IDENTITY_HEADER;

    if (endpoint && header) {
        const url = `${endpoint}?resource=https://dynamicsessions.io&api-version=2019-08-01`;
        const res = await fetch(url, { headers: { "X-IDENTITY-HEADER": header } });
        if (!res.ok) throw new Error(`MSI token error: ${res.status} ${await res.text()}`);
        const data = await res.json();
        _cachedToken = data.access_token;
        _tokenExpiry = Date.now() + (parseInt(data.expires_in) * 1000);
        return _cachedToken;
    }

    // 2. Fallback: env var token (local dev)
    if (process.env.AZURE_SANDBOX_TOKEN) {
        _cachedToken = process.env.AZURE_SANDBOX_TOKEN;
        _tokenExpiry = Date.now() + 3600000;
        return _cachedToken;
    }

    throw new Error("No managed identity available and AZURE_SANDBOX_TOKEN not set");
}

function sandboxGroupPath() {
    return `/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}/sandboxGroups/${SANDBOX_GROUP}`;
}

function sandboxPath(sandboxId) {
    return `${sandboxGroupPath()}/sandboxes/${sandboxId}`;
}

export async function createSandbox(imageType = "browser", cpu = "2000m", memory = "4Gi") {
    const image = DISK_IMAGES[imageType] || DISK_IMAGES.browser;
    const body = {
        sourcesRef: { diskImage: image.id ? { id: image.id, isPublic: false } : { name: "ubuntu", isPublic: true } },
        vmmType: "CloudHypervisor",
        resources: { cpu, memory, disk: cpu === "1000m" ? "20Gi" : "30Gi" },
        lifecycle: {
            autoSuspendPolicy: { enabled: true, interval: 600, mode: "Memory" },
            autoDeletePolicy: { enabled: false, deleteIntervalInSeconds: 3600 },
        },
    };
    const result = await sendRequest("PUT", `${sandboxGroupPath()}/sandboxes?includeDebug=true`, body);
    return result.id || result.name;
}

export async function executeCommand(sandboxId, command) {
    return sendRequest("POST", `${sandboxPath(sandboxId)}/executeShellCommand`, { command });
}

export async function deleteSandbox(sandboxId) {
    return sendRequest("DELETE", `${sandboxPath(sandboxId)}`);
}

export async function getSandboxStatus(sandboxId) {
    return sendRequest("GET", `${sandboxPath(sandboxId)}`);
}

export async function readFile(sandboxId, filePath) {
    // Files endpoint returns 401 — use executeCommand to cat the file as base64 instead
    const result = await executeCommand(sandboxId, `base64 -w 0 "${filePath}"`);
    const b64 = (result.stdout || "").trim();
    if (!b64) throw new Error(`File read error: empty output for ${filePath}`);
    return b64;
}

export async function listFiles(sandboxId, dirPath = "/workspace") {
    const result = await executeCommand(sandboxId, `ls -la ${dirPath}`);
    return { files: (result.stdout || "").split("\n").filter(l => l.trim()) };
}

async function sendRequest(method, path, body) {
    const token = await getDataPlaneToken();
    const url = `${DATA_PLANE_BASE}${path}`;
    const opts = {
        method,
        headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    };
    if (body) opts.body = JSON.stringify(body);
    const res = await fetch(url, opts);
    const text = await res.text();
    if (!res.ok) throw new Error(`ACA API error (${res.status}): ${text.substring(0, 200)}`);
    return text ? JSON.parse(text) : {};
}
