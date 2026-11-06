# Coding Worker Requirements

> **Status:** Current as of March 2026.

This document defines the requirements that every coding agent worker must satisfy. Requirements are derived from the `WorkerProcessor` interface, the `CodingAgentQueueProcessor` orchestration layer, and the patterns established by the four existing workers (Copilot CLI, Claude Code, VS Code Web, VS Code Electron).

## Quick Reference

| # | Requirement | Required | Interface |
|---|-------------|:--------:|-----------|
| 1 | [Implement `WorkerProcessor`](#1-implement-workerprocessor) | ✅ | `WorkerProcessor` |
| 2 | [Consume messages via `processMessage`](#2-consume-messages-via-processmessage) | ✅ | `processMessage()` |
| 3 | [Return `WorkerResult`](#3-return-workerresult) | ✅ | `WorkerResult` |
| 4 | [Publish structured logs](#4-publish-structured-logs) | ✅ | `WorkerLogFn` |
| 5 | [Read credentials via Token Manager](#5-read-credentials-via-token-manager) | ✅ | `TokenManagerClient` |
| 6 | [Capture HAR files](#6-capture-har-files) | Recommended | `DevProxyClient` |
| 7 | [Capture video recordings](#7-capture-video-recordings) | Conditional | `WorkerResult.videoFilePaths` |
| 8 | [Implement lifecycle hooks](#8-implement-lifecycle-hooks) | Recommended | `setup()` / `teardown()` |
| 9 | [Report agent & component versions](#9-report-agent--component-versions) | ✅ | `getAgentVersion()` / `getComponentVersions()` |
| 10 | [Support model selection](#10-support-model-selection) | ✅ | `WorkerProcessorOptions.model` |
| 11 | [Support MCP servers](#11-support-mcp-servers) | Recommended | `WorkerProcessorOptions.mcpServerConfigs` |
| 12 | [Support Skills](#12-support-skills) | Recommended | `WorkerProcessorOptions.skillConfigs` |
| 13 | [Have integration tests](#13-have-integration-tests) | Recommended | — |
| 14 | [Support multi-turn conversations](#14-support-multi-turn-conversations) | ✅ | `setup()` + `processMessage()` × N + `teardown()` |
| 15 | [Auto-approve agent permissions](#15-auto-approve-agent-permissions) | ✅ | Worker-specific |
| 16 | [Sandbox workspace filesystem access](#16-sandbox-workspace-filesystem-access) | Recommended | ACP `readTextFile()` / `writeTextFile()` |
| 17 | [Persist auth state across iterations](#17-persist-auth-state-across-iterations) | Conditional | `processMessage()` side-effect |
| 18 | [Register the worker and version](#18-register-the-worker-and-version) | ✅ | Agent registry manifests |

## Detailed Requirements

### 1. Implement `WorkerProcessor`

Every worker must export a class that implements the `WorkerProcessor` interface from `shared`:

```typescript
import { WorkerProcessor } from "shared";

class MyAgentProcessor implements WorkerProcessor {
  readonly workerName = "coder-my-agent";
  readonly skillAgentType = "copilot" as const; // optional
  // ...
}
```

The `workerName` must be unique and match the registered agent `_id`. Queue
routing is independent: the scheduler uses the selected active version's
explicit `queueName` and never derives a queue from `workerName`.

`skillAgentType` may be `"copilot"` or `"claude-code"` to install skills in an
additional agent-specific directory. Omit it to use only the universal
`.agents/skills` path; never infer skill layout from the worker ID.

**Source:** [`packages/shared/src/types/types.ts`](../../packages/shared/src/types/types.ts) — `WorkerProcessor` interface.

---

### 2. Consume messages via `processMessage`

The core contract. The queue processor calls `processMessage()` with the task prompt, a log function, and options. The worker must invoke its coding agent and return a `WorkerResult`.

```typescript
async processMessage(
  message: string,         // The task prompt
  log: WorkerLogFn,        // Structured logging callback
  options?: WorkerProcessorOptions
): Promise<WorkerResult>
```

For multi-turn runs, `processMessage()` is called multiple times on the same worker instance (once per iteration), with judge feedback appended to the message. The worker must support repeated invocations without re-initialization (see [lifecycle hooks](#8-implement-lifecycle-hooks)).

**Source:** [`packages/shared/src/queue/queue-processor.ts`](../../packages/shared/src/queue/queue-processor.ts) — `handleRequest()` orchestration.

---

### 3. Return `WorkerResult`

Every `processMessage()` call must return a `WorkerResult`:

```typescript
interface WorkerResult {
  response: string;           // The coding agent's text response (required)
  harFilePath?: string;       // Path to HAR file on disk (optional)
  videoFilePaths?: string[];  // Paths to video recordings on disk (optional)
  tokenUsage?: TokenUsage;    // LLM token usage counters (optional)
}
```

The queue processor handles uploading HAR and video files to Azure Blob Storage — the worker only needs to provide local file paths.

**Source:** [`packages/shared/src/types/types.ts`](../../packages/shared/src/types/types.ts) — `WorkerResult` interface.

---

### 4. Publish structured logs

All worker operations must emit logs through the provided `WorkerLogFn` callback. Logs are streamed in real time to the Portal and CLI via Redis Pub/Sub → SSE.

```typescript
type WorkerLogFn = (
  level: "info" | "warn" | "error" | "debug",
  message: string,
  data?: Record<string, unknown>
) => Promise<void>;
```

Workers should log:
- Startup and configuration (agent version, model, MCP server count, skill count)
- Token acquisition (redacted preview)
- Key state transitions (e.g., auth flow steps, agent start/stop)
- Errors and warnings with context

**Source:** [`packages/shared/src/types/types.ts`](../../packages/shared/src/types/types.ts) — `WorkerLogFn` type.

---

### 5. Read credentials via Token Manager

Workers must **not** hardcode or require credentials via environment variables at deployment time. Instead, they acquire credentials at runtime through the `TokenManagerClient`:

```typescript
import { TokenManagerClient } from "shared";

const tokenClient = new TokenManagerClient();

// Simple acquisition (returns token string)
const token = await tokenClient.acquireToken("copilot-sdk");

// Full acquisition with metadata (returns { value, tokenType, ... })
const response = await tokenClient.acquireTokenFull("claude-code-cli", "anthropic-oauth");
```

Each worker uses the appropriate **capability** for its agent:

| Worker | Capability | Token Type |
|--------|-----------|------------|
| `coder-acp-copilot` | `copilot-sdk` | GitHub PAT / OAuth |
| `coder-acp-claude-code` | `claude-code-cli` / `anthropic-oauth` | Anthropic API key / OAuth |

The Token Manager provides round-robin distribution, automatic validation, and secure storage via Azure Key Vault.

**Source:** [`docs/architecture/token-manager.md`](token-manager.md) — Token Manager architecture.

---

### 6. Capture HAR files

Workers should capture HTTP traffic using a **proxy-based** approach. HAR (HTTP Archive) files enable analysis of tool calls, API usage patterns, and token consumption.

Two proxy backends are available, selected via the `createProxyClient()` factory:

| Backend | Set via | Description |
|---------|---------|-------------|
| **AI Gateway** (default) | `PROXY_BACKEND=gateway` | Shared Rust TLS-intercepting proxy with plugin architecture. Single centralized service replaces per-worker sidecars. |
| **DevProxy** (legacy) | `PROXY_BACKEND=devproxy` | .NET DevProxy sidecar. One per worker. Used by CLI-based workers. |

```typescript
import { createProxyClient } from "shared";

const proxyClient = createProxyClient(log);
if (proxyClient) {
  await proxyClient.waitForReady();
  await proxyClient.downloadCertificate("/tmp/proxy-ca.crt");
  await proxyClient.startRecording();

  // ... run the agent ...

  const { harFilePath, tokenUsage } = await proxyClient.stopAndCollectHar(log);
  return { response, ...(harFilePath && { harFilePath }), ...(tokenUsage && { tokenUsage }) };
}
```

The queue processor automatically sanitizes HAR files (strips credentials) before uploading to blob storage.

HAR files are parsed to extract `ToolCall[]` data (tool name, arguments, timestamps) for analytics. The `stopAndCollectHar()` method also extracts `TokenUsage` from HAR entries, enabling token usage reporting without agent-specific instrumentation.

> **Anthropic prompt-cache tokens**: For Claude/Anthropic models the API response `usage.input_tokens` is only the *non-cached* remainder of the prompt — the bulk is reported separately in `cache_creation_input_tokens` and `cache_read_input_tokens`. The extractor sums all three into the prompt count, so prompt tokens reflect the full prompt size (cached + uncached). OpenAI/GitHub Models `prompt_tokens` already includes cached tokens and is used as-is.

For native CLI binaries that don't honor `NODE_EXTRA_CA_CERTS` (e.g., the Copilot CLI binary), workers should create a combined CA bundle using `proxyClient.createCombinedCaBundle()` and inject it via `SSL_CERT_FILE`.

**When required:** All CLI-based workers (Copilot, Claude Code) and desktop workers (VS Code Electron) should support HAR capture. Browser-based workers (VS Code Web) may use alternative approaches.

**Source:** [`packages/shared/src/har/`](../../packages/shared/src/har/) — HAR parsing and sanitization.  
**Source:** [`docs/architecture/ai-gateway.md`](ai-gateway.md) — AI Gateway design document.

---

### 7. Capture video recordings

Workers that drive a **non-headless UI** (browser, desktop app) must capture video recordings of the agent session. This enables visual debugging and audit trails.

```typescript
// In processMessage():
return {
  response: agentResponse,
  videoFilePaths: ["/tmp/videos/session.webm"],
};

// In setup() — for auth flow recordings:
return {
  videoFilePaths: ["/tmp/videos/setup-totp-login.webm"],
};
```

The queue processor uploads videos to blob storage at two levels:
- **Setup videos** — captured during `setup()` (e.g., TOTP login flow), stored under `{requestId}/setup/`
- **Session videos** — captured during `processMessage()`, stored under `{requestId}/`


**Source:** VS Code Web worker design (`docs/architecture/vscode-web-worker.md`, not included in this repository) — VS Code Web worker design.

---

### 8. Implement lifecycle hooks

Workers should implement `setup()` and `teardown()` for resource management:

```typescript
interface WorkerProcessor {
  setup?(log: WorkerLogFn, options?: WorkerProcessorOptions): Promise<SetupResult | void>;
  teardown?(log: WorkerLogFn): Promise<void>;
}
```

- **`setup()`** — Called once before the first `processMessage()`. Use to start long-lived processes (VS Code server, browser), authenticate, prepare the workspace. May return `SetupResult` with `videoFilePaths` from the setup phase.
- **`teardown()`** — Called once after the last `processMessage()`, **even on error**. Use to stop processes, close browsers, clean up temp files.

For multi-turn runs, the lifecycle is: `setup()` → `processMessage()` × N → `teardown()`. The worker instance is reused across iterations — `setup()` and `teardown()` are called exactly once.

**Source:** [`packages/shared/src/queue/queue-processor.ts`](../../packages/shared/src/queue/queue-processor.ts) — lifecycle orchestration.

---

### 9. Report agent & component versions

Workers should report version information for traceability:

```typescript
getAgentVersion(): string {
  // Return a version prefix, e.g. "copilot-0.0.415"
  return `copilot-${process.env.COPILOT_CLI_VERSION || "unknown"}`;
}

getComponentVersions(): Record<string, string> {
  // Return component env vars from versions.env
  return {
    COPILOT_CLI_VERSION: process.env.COPILOT_CLI_VERSION || "unknown",
  };
}
```

The queue processor uses `getAgentVersion()` to build the `workerVersion` field stamped on each run: `{agentVersion}-{buildTime}-{gitCommit}`.

**Naming convention:** Agent version is `{agent}-{semver}` (e.g., `copilot-0.0.415`, `claude-agent-acp-0.1.2-sdk-1.0.0`).

**Source:** [`packages/shared/src/types/types.ts`](../../packages/shared/src/types/types.ts) — `getAgentVersion()`, `getComponentVersions()`.

---

### 10. Support model selection

Workers must respect the `model` field from `WorkerProcessorOptions` and pass it to their coding agent:

```typescript
async processMessage(message: string, log: WorkerLogFn, options?: WorkerProcessorOptions) {
  const model = options?.model;  // e.g. "gpt-4.1", "claude-sonnet-4"
  // Pass to agent configuration
}
```

The available models are registered in `CodingAgentDocument.supportedModels` and validated at submission time by the API.

---

### 11. Support MCP servers

Workers should pass resolved MCP server configurations to the coding agent when present:

```typescript
const mcpConfigs = options?.mcpServerConfigs ?? [];
// Pass to agent as MCP server config (format varies by agent)
```

MCP servers are resolved by the queue processor before `processMessage()` is called. The worker receives fully resolved `McpServerConfig[]` objects with name, type, URL, headers, and arguments.

**Source:** [`packages/shared/src/types/mcp.ts`](../../packages/shared/src/types/mcp.ts) — MCP types.

---

### 12. Support Skills

Skill archives are extracted to the workspace filesystem by the queue processor before `processMessage()` is called. Workers don't need to handle skill extraction — agents discover skills natively from well-known directories:

```
/workspace/.agents/skills/<skillName>/SKILL.md   # Universal
/workspace/.copilot/skills/<skillName>/SKILL.md  # Copilot-specific
/workspace/.claude/skills/<skillName>/SKILL.md   # Claude-specific
```

Workers should log the skill count for traceability:

```typescript
await log("info", "Starting processor", {
  skillCount: skillConfigs.length,
  skills: skillConfigs.map(s => s.name),
});
```

**Source:** [`docs/architecture/skills.md`](skills.md) — Skills architecture.

---

### 13. Have integration tests

Workers should have integration tests that exercise the full flow: setup → processMessage → teardown with real (or simulated) agent interactions.

**Recommended patterns:**
- **Docker-based tests** — Build the worker image, run in a container with bind mounts for artifacts
- **Credential injection** — Use `.env` files with test account credentials
- **Artifact capture** — Bind mount directories for videos, snapshots, HAR files
- **Multi-prompt flow** — Test at least two sequential prompts to verify auth reuse and session continuity

- Two-prompt sequential test: fresh auth + session reuse
- ARIA snapshots at every state transition for AI-assisted debugging
- Video recordings for visual audit

**Source:** VS Code Web worker design (`docs/architecture/vscode-web-worker.md`, not included in this repository) — Integration test architecture.

---

### 14. Support multi-turn conversations

The queue processor calls `processMessage()` multiple times when criteria are present (multi-turn mode). Workers must:

1. Maintain state across calls (via `setup()` / `teardown()` lifecycle)
2. Accept feedback-augmented prompts on subsequent calls
3. Return fresh `WorkerResult` for each iteration (including per-turn HAR and video if available)

The multi-turn loop is: `setup()` → (`processMessage()` → judge → feedback) × N → `teardown()`.

**Source:** [`packages/shared/src/queue/queue-processor.ts`](../../packages/shared/src/queue/queue-processor.ts) — `processMultiTurn()`.

---

### 15. Auto-approve agent permissions

All workers must ensure that their coding agent can execute tool calls and file operations without interactive confirmation prompts. Since workers run in isolated containers with no interactive user, the agent must operate in a fully autonomous ("yolo") mode.

The mechanism varies by worker type:

- **ACP-based workers** — implement `requestPermission()` to auto-approve all permission requests, **and** set the ACP session mode to `autopilot` after creating the session. The `--yolo` CLI flag alone does **not** change the ACP session mode: an ACP session starts in `agent` mode, where execute/bash tool calls (e.g. `npm run build`) are denied non-interactively. Autopilot mode enables allow-all and runs without prompts. The Copilot CLI advertises modes by their canonical ACP URL ids (e.g. `https://agentclientprotocol.com/protocol/session-modes#autopilot`), so match on the full id:

```typescript
async requestPermission(
  params: acp.RequestPermissionRequest
): Promise<acp.RequestPermissionResponse> {
  const firstOption = params.options[0];
  if (firstOption) {
    return { outcome: { outcome: "selected", optionId: firstOption.optionId } };
  }
  return { outcome: { outcome: "cancelled" } };
}

// After session/new — switch to autopilot so commands run headlessly.
const AUTOPILOT_MODE_ID =
  "https://agentclientprotocol.com/protocol/session-modes#autopilot";
const autopilot = sessionResult.modes?.availableModes
  ?.find((m) => m.id === AUTOPILOT_MODE_ID || m.id.endsWith("#autopilot"));
if (autopilot) {
  await connection.setSessionMode({ sessionId, modeId: autopilot.id });
}
```

- **Browser-based workers** (VS Code Web) — the agent operates through Playwright-driven UI automation; no explicit approval mechanism is needed since the automation controls the interaction directly.

- **Desktop IDE workers** (VS Code Electron) — must configure the IDE or agent extension to auto-approve tool calls without user confirmation (e.g., via VS Code settings or extension-specific yolo flags).

**When required:** Mandatory for all workers. The specific mechanism depends on the agent interface.

**Source:** [`apps/workers/coder-acp-copilot/src/acp-client.ts`](../../apps/workers/coder-acp-copilot/src/acp-client.ts) — `ACPClientHandler.requestPermission()`.

---

### 16. Sandbox workspace filesystem access

ACP workers that implement the `readTextFile` / `writeTextFile` filesystem callbacks must enforce path traversal protection. All file paths must be resolved and validated to remain within the workspace directory:

```typescript
private resolvePath(filePath: string): string {
  const fullPath = isAbsolute(filePath)
    ? resolve(filePath)
    : resolve(this.workspacePath, filePath);
  if (!fullPath.startsWith(this.workspacePath + "/") && fullPath !== this.workspacePath) {
    throw new Error(`Path traversal blocked: "${filePath}" resolves outside workspace`);
  }
  return fullPath;
}
```

**When required:** Recommended for ACP workers that implement filesystem callbacks. Claude Code's `ACPClientHandler` implements this; Copilot CLI's handler returns empty content for reads (agent manages its own filesystem).

**Source:** [`apps/workers/coder-acp-claude-code/src/acp-client.ts`](../../apps/workers/coder-acp-claude-code/src/acp-client.ts) — `ACPClientHandler.resolvePath()`.

---

### 17. Persist auth state across iterations

Workers that use browser-based authentication (cookie state) must persist updated auth state after each `processMessage()` call. This ensures that token refreshes during a session are carried forward to subsequent iterations in multi-turn runs:

```typescript
// After receiving the response, persist cookies for next iteration
await saveStorageState(AUTH_STATE_PATH);
this.storageStateJson = readFileSync(AUTH_STATE_PATH, "utf-8");
```

This is critical for long multi-turn runs where OAuth tokens may expire between iterations. The saved state includes refreshed cookies and session tokens.

**When required:** Mandatory for browser-based workers with cookie authentication (VS Code Web). Not applicable for workers using stateless API tokens (Copilot CLI, Claude Code) or workers that mint a token once in `setup()` (VS Code Electron).

---

### 18. Register the worker and version

Every deployable worker must register an agent manifest and at least one version
manifest. These records are the only source for current availability, display
name, capabilities, versions, and scheduling queues.

Agent payload (`POST /api/v1/agents`):

```yaml
_id: coder-my-agent
name: My Agent
description: My coding agent worker
modelProvider: my-provider
available: true
capabilities:
  supportsReasoningEffort: true
  supportsMcpServers: true
  supportsSkills: true
  supportsExtensions: false
```

All four capability properties are optional booleans. Omitted and false both
mean unsupported. `available` must be exactly `true` for new submissions.

Version payload (`POST /api/v1/agents/{_id}/versions`):

```yaml
agentVersion: my-agent-1.2.3
workerVersion: my-agent-1.2.3-build-abc123
components:
  MY_AGENT_VERSION: 1.2.3
gitCommit: abc123
buildTime: "2026-03-01T12:00:00Z"
imageTag: 1.2.3
queueName: my-explicit-worker-queue
```

Every shown version field is required and must be non-empty.
`components` is a string-to-string map. Registration activates the version.
When another version of the same agent registers that queue, the new registration
atomically retires the previous same-agent owner; the last successful registration
wins. A different agent cannot claim the queue and receives HTTP 409. Versions can
also be retired or reactivated through the version status endpoint; reactivation
uses the same same-agent takeover rule. Queue names are opaque deployment-owned
values, and each queue has at most one non-deleted active exact target. Dynamically
named workers are supported, and no platform service prepends `queue-` or otherwise
derives queue names.

At runtime, `SCOPE_AGENT_VERSION` or `WorkerProcessor.getAgentVersion()` must
provide the exact registered `agentVersion`. The queue processor fails during
startup if neither provides a non-empty identity. This prevents a worker from
executing a stale or misrouted message. The OSS local
Compose manifests set `SCOPE_AGENT_VERSION` to their development manifest value;
production deployments normally derive it from the installed agent components.

For Jobs and Docker Compose, use the reusable helper:

```bash
scripts/register-agent.sh \
  http://api:80 \
  apps/workers/coder-my-agent/agent.yaml \
  apps/workers/coder-my-agent/agent-version.yaml
```

The helper is idempotent: both API endpoints upsert by agent ID and version ID.
Docker Compose commands may pass `--available true|false` after the agent
manifest to override its availability in the submitted JSON without modifying
the manifest on disk.
It waits for `/health`, retries network failures and HTTP
408/425/429/5xx responses with bounded exponential backoff, and exits non-zero
immediately for permanent errors such as invalid manifests, unknown agents, or
other 4xx responses. Retry bounds can be configured with the positive-integer
`SCOPE_REGISTRATION_MAX_ATTEMPTS`,
`SCOPE_REGISTRATION_BASE_DELAY_SECONDS`, and
`SCOPE_REGISTRATION_MAX_DELAY_SECONDS` variables. Curl calls are bounded by
`SCOPE_REGISTRATION_CONNECT_TIMEOUT_SECONDS` (default 5) and
`SCOPE_REGISTRATION_REQUEST_TIMEOUT_SECONDS` (default 30). Only transient DNS,
connection, timeout, and transfer failures are retried; permanent curl
configuration and certificate failures exit immediately. Registration Jobs
must not hide helper failures with `|| true`.

The OSS Compose `register-agents` service uses this contract for Copilot and
Claude. It waits for the API's Docker health check (`GET /health` must return
200), not merely for the API container to start. The probe uses Node's built-in
HTTP client, runs every 5 seconds with a 3-second timeout, and allows a 120-second
startup grace period followed by 12 consecutive failures before marking the API
unhealthy. A successful probe releases registration immediately, without waiting
out the grace period.

This ordering also applies to `pnpm docker:dev:portal`: concurrent `tsx` startup
and `tsc --watch` compilation under the API CPU limit can outlast the registration
helper's readiness retry budget. The helper retains its own bounded retries for
transient failures after the API is healthy. Scheduler/workers still require
registration to exit successfully; failures are not ignored. If startup remains
blocked, inspect the API logs and Docker health status before increasing retries.

Cross-repository overlays can mount additional manifests and invoke the same
helper before running their workers.


---

## Worker Bootstrapping

Every worker's entry point follows the same pattern:

```typescript
import { CodingAgentQueueProcessor, QueueProcessorConfig } from "shared";

const processor = new MyAgentProcessor();

const config: QueueProcessorConfig = {
  mongoUri: process.env.MONGODB_URI!,
  mongoDatabase: process.env.MONGODB_DATABASE || "scope-mt",
  mongoCollection: process.env.MONGODB_COLLECTION || "requests",
  storageAccountName: process.env.AZURE_STORAGE_ACCOUNT_NAME!,
  storageConnectionString: process.env.AZURE_STORAGE_CONNECTION_STRING,
  queueName: process.env.QUEUE_NAME!,
  batchSize: parseInt(process.env.BATCH_SIZE || "1"),
  pollIntervalMs: parseInt(process.env.POLL_INTERVAL_MS || "5000"),
  redisHost: process.env.REDIS_HOST!,
  redisPort: parseInt(process.env.REDIS_PORT || "6380"),
  redisPassword: process.env.REDIS_PASSWORD!,
  apiBaseUrl: process.env.SCOPE_MT_API_URL,
};

const queueProcessor = new CodingAgentQueueProcessor(config, processor);
queueProcessor.start();
```

The `CodingAgentQueueProcessor` handles all queue polling, message visibility, MongoDB persistence, blob storage uploads, HAR sanitization, video uploads, project-scoped MCP/skill/extension resolution, multi-turn orchestration, and report triggering. The worker only implements the `WorkerProcessor` interface.

Before resolving runtime resources, the queue processor atomically claims the exact
queued run (`requestId` + `runId`) and records its worker instance. MCP servers,
skills, secrets, and extensions are then resolved using the request's `projectId`.
If setup fails, the base error path can terminalize only that owned processing run,
so a resolver error cannot leave a dequeued run queued or overwrite a concurrent
retry/cancellation. Prompt bodies and codebase revisions remain point reads by
their immutable IDs and do not require project query parameters.

## Existing Workers

For a detailed breakdown of which requirements each worker meets, see the [Worker Compliance Matrix](worker-compliance.md).

## Key Files

| File | Purpose |
|------|---------|
| [`packages/shared/src/types/types.ts`](../../packages/shared/src/types/types.ts) | `WorkerProcessor`, `WorkerResult`, `SetupResult`, `WorkerProcessorOptions` |
| [`packages/shared/src/queue/queue-processor.ts`](../../packages/shared/src/queue/queue-processor.ts) | `CodingAgentQueueProcessor` — lifecycle orchestration |
| [`packages/shared/src/queue/base-queue-processor.ts`](../../packages/shared/src/queue/base-queue-processor.ts) | `BaseQueueProcessor` — queue polling, message handling |
| [`packages/shared/src/har/har-parser.ts`](../../packages/shared/src/har/har-parser.ts) | HAR sanitization and tool call extraction |
| [`packages/shared/src/storage/blob-storage.ts`](../../packages/shared/src/storage/blob-storage.ts) | Azure Blob Storage upload client |
| [`docs/architecture/token-manager.md`](token-manager.md) | Token Manager architecture |
| [`docs/architecture/skills.md`](skills.md) | Skills delivery architecture |
| `docs/architecture/vscode-web-worker.md` (not included in this repository) | VS Code Web worker reference implementation |
