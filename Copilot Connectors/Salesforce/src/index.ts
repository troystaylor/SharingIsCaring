// ── Azure Functions Entry Point ──
// Exposes HTTP and Timer triggers for the Salesforce Copilot Connector.

import {
  app,
  HttpRequest,
  HttpResponseInit,
  InvocationContext,
  Timer,
} from "@azure/functions";

import { runFullCrawl, runExtendedCrawl } from "./crawlers/fullCrawl";
import {
  runIncrementalCrawl,
  startCdcSync,
} from "./crawlers/incrementalCrawl";
import {
  ensureConnection,
  getConnection,
  getSchemaStatus,
  deleteConnection,
  deleteItem,
} from "./references/connectionManager";
import { loadConfig, getConfig } from "./config/connectorConfig";
import { getAccessToken } from "./auth/salesforceAuth";

// ── Background job tracker ──
interface CrawlJob {
  id: string;
  type: string;
  status: "running" | "completed" | "failed";
  startedAt: string;
  completedAt?: string;
  results?: unknown;
  error?: string;
}
const crawlJobs = new Map<string, CrawlJob>();

// ── HTTP Trigger: Manual full crawl ──

async function httpTriggerFullCrawl(
  request: HttpRequest,
  context: InvocationContext
): Promise<HttpResponseInit> {
  context.log("[HTTP] Full crawl triggered");

  const jobId = `full-${Date.now()}`;
  const job: CrawlJob = { id: jobId, type: "fullCrawl", status: "running", startedAt: new Date().toISOString() };
  crawlJobs.set(jobId, job);

  try {
    loadConfig();
  } catch (error) {
    job.status = "failed";
    job.error = error instanceof Error ? error.message : String(error);
    return { status: 500, jsonBody: { message: "Config load failed", error: job.error } };
  }

  // Fire and forget — run crawl in background
  runFullCrawl()
    .then((results) => {
      job.status = "completed";
      job.completedAt = new Date().toISOString();
      job.results = results;
      context.log(`[HTTP] Full crawl completed: ${JSON.stringify(results)}`);
    })
    .catch((error) => {
      job.status = "failed";
      job.completedAt = new Date().toISOString();
      job.error = error instanceof Error ? error.message : String(error);
      context.error(`[HTTP] Full crawl failed: ${error}`);
    });

  return {
    status: 202,
    jsonBody: { message: "Full crawl started", jobId },
  };
}

app.http("fullCrawl", {
  methods: ["POST"],
  authLevel: "function",
  handler: httpTriggerFullCrawl,
});

// ── HTTP Trigger: Manual incremental crawl ──

async function httpTriggerIncrementalCrawl(
  request: HttpRequest,
  context: InvocationContext
): Promise<HttpResponseInit> {
  const sinceDate =
    request.query.get("since") ||
    new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString();

  context.log(`[HTTP] Incremental crawl triggered since ${sinceDate}`);

  const jobId = `incr-${Date.now()}`;
  const job: CrawlJob = { id: jobId, type: "incrementalCrawl", status: "running", startedAt: new Date().toISOString() };
  crawlJobs.set(jobId, job);

  try {
    loadConfig();
  } catch (error) {
    job.status = "failed";
    job.error = error instanceof Error ? error.message : String(error);
    return { status: 500, jsonBody: { message: "Config load failed", error: job.error } };
  }

  runIncrementalCrawl(sinceDate)
    .then((results) => {
      job.status = "completed";
      job.completedAt = new Date().toISOString();
      job.results = results;
      context.log(`[HTTP] Incremental crawl completed: ${JSON.stringify(results)}`);
    })
    .catch((error) => {
      job.status = "failed";
      job.completedAt = new Date().toISOString();
      job.error = error instanceof Error ? error.message : String(error);
      context.error(`[HTTP] Incremental crawl failed: ${error}`);
    });

  return {
    status: 202,
    jsonBody: { message: "Incremental crawl started", jobId, sinceDate },
  };
}

app.http("incrementalCrawl", {
  methods: ["POST"],
  authLevel: "function",
  handler: httpTriggerIncrementalCrawl,
});

// ── HTTP Trigger: Connection setup ──

async function httpTriggerSetup(
  request: HttpRequest,
  context: InvocationContext
): Promise<HttpResponseInit> {
  context.log("[HTTP] Connection setup triggered");

  try {
    loadConfig();
    await ensureConnection();
    return {
      status: 200,
      jsonBody: { message: "Connection and schema ready" },
    };
  } catch (error) {
    context.error(`[HTTP] Setup failed: ${error}`);
    return {
      status: 500,
      jsonBody: {
        message: "Setup failed",
        error: error instanceof Error ? error.message : String(error),
      },
    };
  }
}

app.http("setup", {
  methods: ["POST"],
  authLevel: "function",
  handler: httpTriggerSetup,
});

// ── Timer Trigger: Scheduled incremental crawl (every 15 minutes) ──

async function timerTriggerIncrementalCrawl(
  timer: Timer,
  context: InvocationContext
): Promise<void> {
  context.log("[Timer] Scheduled incremental crawl starting");

  const sinceDate = new Date(Date.now() - 20 * 60 * 1000).toISOString(); // 20 min overlap

  try {
    loadConfig();
    const results = await runIncrementalCrawl(sinceDate);
    context.log(`[Timer] Incremental crawl complete: ${JSON.stringify(results)}`);
  } catch (error) {
    context.error(`[Timer] Incremental crawl failed: ${error}`);
  }
}

app.timer("scheduledIncrementalCrawl", {
  schedule: "0 */15 * * * *",
  handler: timerTriggerIncrementalCrawl,
});

// ── HTTP Trigger: Start CDC real-time sync ──

let activeCdcSync: { cancel: () => void } | null = null;

async function httpTriggerStartCdc(
  request: HttpRequest,
  context: InvocationContext
): Promise<HttpResponseInit> {
  context.log("[HTTP] CDC sync start triggered");

  if (activeCdcSync) {
    return {
      status: 409,
      jsonBody: { message: "CDC sync already running" },
    };
  }

  try {
    loadConfig();
    activeCdcSync = await startCdcSync();
    return {
      status: 200,
      jsonBody: { message: "CDC real-time sync started" },
    };
  } catch (error) {
    context.error(`[HTTP] CDC start failed: ${error}`);
    return {
      status: 500,
      jsonBody: {
        message: "CDC sync start failed",
        error: error instanceof Error ? error.message : String(error),
      },
    };
  }
}

app.http("startCdc", {
  methods: ["POST"],
  authLevel: "function",
  handler: httpTriggerStartCdc,
});

// ── HTTP Trigger: Stop CDC real-time sync ──

async function httpTriggerStopCdc(
  request: HttpRequest,
  context: InvocationContext
): Promise<HttpResponseInit> {
  if (!activeCdcSync) {
    return {
      status: 404,
      jsonBody: { message: "No active CDC sync to stop" },
    };
  }

  activeCdcSync.cancel();
  activeCdcSync = null;
  context.log("[HTTP] CDC sync stopped");

  return {
    status: 200,
    jsonBody: { message: "CDC real-time sync stopped" },
  };
}

app.http("stopCdc", {
  methods: ["POST"],
  authLevel: "function",
  handler: httpTriggerStopCdc,
});

// ── HTTP Trigger: Connection status ──

async function httpTriggerStatus(
  request: HttpRequest,
  context: InvocationContext
): Promise<HttpResponseInit> {
  context.log("[HTTP] Status check triggered");

  try {
    loadConfig();
    const config = getConfig();
    const connection = await getConnection();

    if (!connection) {
      return {
        status: 200,
        jsonBody: {
          connectorId: config.connector.connectorId,
          connectionExists: false,
        },
      };
    }

    const schemaStatus = await getSchemaStatus();

    return {
      status: 200,
      jsonBody: {
        connectorId: config.connector.connectorId,
        connectionExists: true,
        connectionName: connection.name,
        connectionDescription: connection.description,
        connectionState: connection.state || "unknown",
        schemaStatus,
        cdcSyncRunning: activeCdcSync !== null,
        runtime: {
          environment: process.env.WEBSITE_SITE_NAME ? "azure" : "local",
          functionApp: process.env.WEBSITE_SITE_NAME || "localhost",
          region: process.env.REGION_NAME || "local",
        },
      },
    };
  } catch (error) {
    context.error(`[HTTP] Status check failed: ${error}`);
    return {
      status: 500,
      jsonBody: {
        message: "Status check failed",
        error: error instanceof Error ? error.message : String(error),
      },
    };
  }
}

app.http("status", {
  methods: ["GET"],
  authLevel: "function",
  handler: httpTriggerStatus,
});

// ── HTTP Trigger: Health check ──

async function httpTriggerHealth(
  request: HttpRequest,
  context: InvocationContext
): Promise<HttpResponseInit> {
  context.log("[HTTP] Health check triggered");

  const checks: Record<string, { ok: boolean; error?: string }> = {};

  try {
    loadConfig();
  } catch (error) {
    return {
      status: 503,
      jsonBody: {
        healthy: false,
        checks: { config: { ok: false, error: error instanceof Error ? error.message : String(error) } },
      },
    };
  }

  // Check Salesforce token
  try {
    await getAccessToken();
    checks.salesforce = { ok: true };
  } catch (error) {
    checks.salesforce = { ok: false, error: error instanceof Error ? error.message : String(error) };
  }

  // Check Graph connection
  try {
    const conn = await getConnection();
    checks.graph = conn ? { ok: true } : { ok: false, error: "Connection not found" };
  } catch (error) {
    checks.graph = { ok: false, error: error instanceof Error ? error.message : String(error) };
  }

  const healthy = Object.values(checks).every((c) => c.ok);

  const runtime = {
    environment: process.env.WEBSITE_SITE_NAME ? "azure" : "local",
    functionApp: process.env.WEBSITE_SITE_NAME || "localhost",
    region: process.env.REGION_NAME || "local",
    nodeVersion: process.version,
    timestamp: new Date().toISOString(),
  };

  return {
    status: healthy ? 200 : 503,
    jsonBody: { healthy, runtime, checks },
  };
}

app.http("health", {
  methods: ["GET"],
  authLevel: "function",
  handler: httpTriggerHealth,
});

// ── HTTP Trigger: Delete connection ──

async function httpTriggerDeleteConnection(
  request: HttpRequest,
  context: InvocationContext
): Promise<HttpResponseInit> {
  context.log("[HTTP] Delete connection triggered");

  try {
    loadConfig();
    await deleteConnection();
    return {
      status: 200,
      jsonBody: { message: "Connection deleted" },
    };
  } catch (error) {
    context.error(`[HTTP] Delete connection failed: ${error}`);
    return {
      status: 500,
      jsonBody: {
        message: "Delete connection failed",
        error: error instanceof Error ? error.message : String(error),
      },
    };
  }
}

app.http("deleteConnection", {
  methods: ["DELETE"],
  authLevel: "function",
  handler: httpTriggerDeleteConnection,
});

// ── HTTP Trigger: Delete single item ──

async function httpTriggerDeleteItem(
  request: HttpRequest,
  context: InvocationContext
): Promise<HttpResponseInit> {
  const itemId = request.query.get("itemId");

  if (!itemId) {
    return {
      status: 400,
      jsonBody: { message: "Missing required query parameter: itemId" },
    };
  }

  context.log(`[HTTP] Delete item triggered for ${itemId}`);

  try {
    loadConfig();
    await deleteItem(itemId);
    return {
      status: 200,
      jsonBody: { message: `Item '${itemId}' deleted` },
    };
  } catch (error) {
    context.error(`[HTTP] Delete item failed: ${error}`);
    return {
      status: 500,
      jsonBody: {
        message: "Delete item failed",
        error: error instanceof Error ? error.message : String(error),
      },
    };
  }
}

app.http("deleteItem", {
  methods: ["DELETE"],
  authLevel: "function",
  handler: httpTriggerDeleteItem,
});

// ── HTTP Trigger: Extended crawl (Reports, Chatter, Analytics) ──

async function httpTriggerExtendedCrawl(
  request: HttpRequest,
  context: InvocationContext
): Promise<HttpResponseInit> {
  const source = (request.query.get("source") || "all") as "reports" | "chatter" | "analytics" | "all";

  if (!["reports", "chatter", "analytics", "all"].includes(source)) {
    return {
      status: 400,
      jsonBody: { message: "Invalid source. Use: reports, chatter, analytics, or all" },
    };
  }

  context.log(`[HTTP] Extended crawl triggered for source: ${source}`);

  const jobId = `ext-${Date.now()}`;
  const job: CrawlJob = { id: jobId, type: "extendedCrawl", status: "running", startedAt: new Date().toISOString() };
  crawlJobs.set(jobId, job);

  try {
    loadConfig();
  } catch (error) {
    job.status = "failed";
    job.error = error instanceof Error ? error.message : String(error);
    return { status: 500, jsonBody: { message: "Config load failed", error: job.error } };
  }

  runExtendedCrawl(source)
    .then((results) => {
      job.status = "completed";
      job.completedAt = new Date().toISOString();
      job.results = results;
      context.log(`[HTTP] Extended crawl (${source}) completed: ${JSON.stringify(results)}`);
    })
    .catch((error) => {
      job.status = "failed";
      job.completedAt = new Date().toISOString();
      job.error = error instanceof Error ? error.message : String(error);
      context.error(`[HTTP] Extended crawl failed: ${error}`);
    });

  return {
    status: 202,
    jsonBody: { message: `Extended crawl (${source}) started`, jobId },
  };
}

app.http("extendedCrawl", {
  methods: ["POST"],
  authLevel: "function",
  handler: httpTriggerExtendedCrawl,
});

// ── HTTP Trigger: Crawl job status ──

async function httpTriggerCrawlStatus(
  request: HttpRequest,
  context: InvocationContext
): Promise<HttpResponseInit> {
  const jobId = request.query.get("jobId");

  if (jobId) {
    const job = crawlJobs.get(jobId);
    if (!job) {
      return { status: 404, jsonBody: { message: "Job not found" } };
    }
    return { status: 200, jsonBody: job };
  }

  // Return all jobs
  const jobs = Array.from(crawlJobs.values()).sort(
    (a, b) => new Date(b.startedAt).getTime() - new Date(a.startedAt).getTime()
  );
  return { status: 200, jsonBody: { jobs } };
}

app.http("crawlStatus", {
  methods: ["GET"],
  authLevel: "function",
  handler: httpTriggerCrawlStatus,
});
