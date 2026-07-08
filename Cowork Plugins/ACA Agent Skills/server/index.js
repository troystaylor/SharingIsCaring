import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StreamableHTTPServerTransport } from "@modelcontextprotocol/sdk/server/streamableHttp.js";
import express from "express";
import crypto from "crypto";
import path from "path";
import { fileURLToPath } from "url";
import { z } from "zod";
import { registerGithubTools } from "./tools/github.js";
import { registerSandboxTools } from "./tools/sandbox.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

const app = express();
app.use(express.json());

// Serve MCP App widgets with correct MIME type
app.use("/ui", (req, res, next) => {
    if (req.path.endsWith(".html")) {
        res.setHeader("Content-Type", "text/html;profile=mcp-app");
    }
    next();
}, express.static(path.join(__dirname, "public")));

function createServer() {
    const server = new McpServer({ name: "agent-skills", version: "1.0.0" });
    registerGithubTools(server, z);
    registerSandboxTools(server, z);
    return server;
}

// Stateless mode: each request gets its own server+transport pair.
// This is fine because all tools are stateless (GitHub API + sandbox creation).
app.post("/mcp", async (req, res) => {
    console.log("POST /mcp method:", req.body?.method);
    const transport = new StreamableHTTPServerTransport({
        sessionIdGenerator: undefined, // Stateless — no session tracking
    });
    const server = createServer();
    await server.connect(transport);
    await transport.handleRequest(req, res, req.body);
});

app.get("/health", (req, res) => res.json({ status: "ok" }));

const port = process.env.PORT || 8080;
app.listen(port, () => console.log(`Agent Skills MCP server listening on :${port}`));
