// Code execution tools — Python/bash/.NET via ACA Sandboxes
import { createSandbox, executeCommand, readFile, listFiles as listSandboxFiles } from "./sandbox-client.js";

const LANGUAGE_COMMANDS = {
    python: "python3 -c",
    javascript: "node -e",
    typescript: "npx tsx -e",
    bash: "bash -c",
    powershell: "pwsh -c",
    dotnet: "dotnet-script eval",
};

// Auto-import script — runs once per new Python session to pre-import common libraries
const PYTHON_WARMUP = `
import pandas as pd
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from PIL import Image
import requests
from bs4 import BeautifulSoup
import json, os, sys, re, math, datetime, csv, io
import yaml
import jinja2
from pathlib import Path
`.trim();

// Track which sessions have been warmed up
const warmedSessions = new Set();

export function registerCodeTools(server, z) {
    server.tool("execute_code", "Execute code in an isolated sandbox. Python (default), bash, JavaScript, TypeScript, .NET.", {
        code: z.string().describe("Code to execute"),
        language: z.string().optional().describe("python, bash, javascript, typescript, dotnet. Default: python"),
        session_id: z.string().optional().describe("Existing session. Omit to auto-create."),
        timeout: z.number().optional().describe("Timeout in seconds. Default 30"),
    }, { title: "Execute Code", readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ code, language, session_id, timeout }) => {
        const lang = language || "python";
        let sid = session_id;
        if (!sid) sid = await createSandbox("code", "1000m", "2Gi");

        // Warm up Python session with auto-imports on first use
        if (lang === "python" && !warmedSessions.has(sid)) {
            const warmupCmd = buildExecCommand("python", PYTHON_WARMUP);
            await executeCommand(sid, warmupCmd);
            warmedSessions.add(sid);
        }

        const command = buildExecCommand(lang, code);
        const result = await executeCommand(sid, command);

        let stdout = result.stdout || "";
        let stderr = result.stderr || "";
        if (stdout.length > 8192) stdout = stdout.substring(0, 8192) + "\n[truncated]";
        if (stderr.length > 8192) stderr = stderr.substring(0, 8192) + "\n[truncated]";

        // Detect image output (matplotlib saves to /workspace/)
        let imageFiles = [];
        if (lang === "python") {
            const lsResult = await executeCommand(sid, `ls /workspace/*.png /workspace/*.jpg /workspace/*.jpeg /workspace/*.svg 2>/dev/null`);
            const files = (lsResult.stdout || "").trim().split("\n").filter(f => f.trim());
            imageFiles = files;
        }

        // Build response with optional structuredContent for inline widget
        const textResult = { session_id: sid, stdout, stderr, exit_code: result.exitCode ?? -1, language: lang };
        if (imageFiles.length > 0) {
            textResult.image_files = imageFiles;
            textResult.file_path = imageFiles[0]; // Primary image for follow-up actions
        }

        const response = { content: [{ type: "text", text: JSON.stringify(textResult) }] };

        // If images found, add as MCP image content type (inline rendering)
        if (imageFiles.length > 0) {
            try {
                const b64 = await readFile(sid, imageFiles[0]);
                const ext = imageFiles[0].split(".").pop().toLowerCase();
                const mime = ext === "svg" ? "image/svg+xml" : `image/${ext === "jpg" ? "jpeg" : ext}`;
                // Standard MCP image content — Cowork should render this inline
                response.content.push({ type: "image", data: b64, mimeType: mime });
                // Also keep structuredContent for widget-capable hosts
                response.structuredContent = {
                    type: "chart",
                    image: b64,
                    mimeType: mime,
                    fileName: imageFiles[0].split("/").pop(),
                    sessionId: sid,
                    filePath: imageFiles[0],
                };
            } catch (e) {
                console.error("Image read for inline display failed:", e.message);
            }
        }

        return response;
    });

    server.tool("upload_file", "Upload a file into the sandbox workspace.", {
        session_id: z.string().describe("Session ID"),
        file_name: z.string().describe("File name"),
        content: z.string().describe("Base64-encoded file content"),
        path: z.string().optional().describe("Target directory. Default: /workspace"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ session_id, file_name, content, path: dir }) => {
        const targetDir = dir || "/workspace";
        const fullPath = `${targetDir}/${file_name}`;
        const cmd = `mkdir -p ${targetDir} && echo '${content}' | base64 -d > ${fullPath}`;
        await executeCommand(session_id, cmd);
        return { content: [{ type: "text", text: JSON.stringify({ success: true, file_path: fullPath }) }] };
    });

    server.tool("download_artifact", "Download a file from the sandbox workspace.", {
        session_id: z.string().describe("Session ID"),
        file_path: z.string().describe("Full path in sandbox"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ session_id, file_path }) => {
        const content = await readFile(session_id, file_path);
        const fileName = file_path.split("/").pop();
        return { content: [{ type: "text", text: JSON.stringify({ file_name: fileName, content, size_bytes: Buffer.from(content, "base64").length }) }] };
    });

    server.tool("list_files", "List files in sandbox workspace directory.", {
        session_id: z.string().describe("Session ID"),
        path: z.string().optional().describe("Directory path. Default: /workspace"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ session_id, path: dir }) => {
        const result = await listSandboxFiles(session_id, dir || "/workspace");
        return { content: [{ type: "text", text: JSON.stringify(result) }] };
    });
}

function buildExecCommand(language, code) {
    const b64 = Buffer.from(code).toString("base64");
    const ext = { python: ".py", javascript: ".js", typescript: ".ts", bash: ".sh", powershell: ".ps1", dotnet: ".csx" }[language] || ".py";
    const runner = { python: "python3", javascript: "node", typescript: "npx tsx", bash: "bash", powershell: "pwsh", dotnet: "dotnet-script" }[language] || "python3";
    return `echo '${b64}' | base64 -d > /tmp/_code${ext} && ${runner} /tmp/_code${ext}`;
}
