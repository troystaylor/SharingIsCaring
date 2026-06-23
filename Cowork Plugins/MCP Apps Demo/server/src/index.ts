/**
 * MCP Apps Demo Server — Express app with Streamable HTTP transport.
 * Single endpoint at /mcp serving all demo tools and widget resources.
 */

import express from "express";
import cors from "cors";
import { StreamableHTTPServerTransport } from "@modelcontextprotocol/sdk/server/streamableHttp.js";
import { createServer } from "./server.js";

const app = express();
app.use(cors());
app.use(express.json());

app.get("/health", (_req, res) => {
  res.json({ status: "ok", server: "mcp-apps-demo" });
});

// Streamable HTTP transport — each request gets its own transport instance
// but shares the same McpServer (tools/resources are registered once).
const mcpServer = createServer();

app.post("/mcp", async (req, res) => {
  const transport = new StreamableHTTPServerTransport({
    sessionIdGenerator: undefined, // stateless for demo
  });

  res.on("close", () => {
    transport.close();
  });

  await mcpServer.connect(transport);
  await transport.handleRequest(req, res, req.body);
});

// Handle GET and DELETE for SSE fallback (required by spec)
app.get("/mcp", async (req, res) => {
  res.writeHead(405).end(JSON.stringify({
    jsonrpc: "2.0",
    error: { code: -32000, message: "Method not allowed. Use POST." },
    id: null,
  }));
});

app.delete("/mcp", async (req, res) => {
  res.writeHead(405).end(JSON.stringify({
    jsonrpc: "2.0",
    error: { code: -32000, message: "Method not allowed." },
    id: null,
  }));
});

const port = parseInt(process.env.PORT || "8080", 10);
app.listen(port, () => {
  console.log(`MCP Apps Demo Server listening on port ${port}`);
  console.log(`  Health: http://localhost:${port}/health`);
  console.log(`  MCP:    http://localhost:${port}/mcp`);
});
