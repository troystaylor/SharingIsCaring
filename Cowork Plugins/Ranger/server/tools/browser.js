// Browser automation tools — Playwright via ACA Sandboxes
import { createSandbox, executeCommand, readFile, deleteSandbox } from "./sandbox-client.js";

export function registerBrowserTools(server, z) {
    server.tool("create_browser_session", "Boot a new Playwright sandbox with Chromium. Returns a session_id for subsequent browser operations.", {
        cpu: z.string().optional().describe("CPU: 2000m or 4000m. Default: 2000m"),
        memory: z.string().optional().describe("Memory: 4Gi or 8Gi. Default: 4Gi"),
        viewport: z.string().optional().describe("Viewport as WxH. Default: 1280x720"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ cpu, memory, viewport }) => {
        const sessionId = await createSandbox("browser", cpu || "2000m", memory || "4Gi");
        const config = JSON.stringify({ viewport: viewport || "1280x720", locale: "en-US" });
        await executeCommand(sessionId, `mkdir -p /workspace && echo '${Buffer.from(config).toString("base64")}' | base64 -d > /workspace/.session-config.json`);
        return { content: [{ type: "text", text: JSON.stringify({ session_id: sessionId, state: "Running", viewport: viewport || "1280x720" }) }] };
    });

    server.tool("navigate", "Navigate browser to a URL. Returns page title, final URL, HTTP status.", {
        session_id: z.string().describe("Session ID"),
        url: z.string().describe("URL to navigate to"),
        wait_until: z.string().optional().describe("domcontentloaded, networkidle, load, commit"),
        timeout: z.number().optional().describe("Timeout in seconds. Default 30"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ session_id, url, wait_until, timeout }) => {
        const script = buildNavigateScript(url, wait_until || "domcontentloaded", timeout || 30);
        const result = await runPlaywright(session_id, script);
        return { content: [{ type: "text", text: JSON.stringify(parseOutput(result)) }] };
    });

    server.tool("screenshot", "Capture screenshot of current page or element. Returns base64 PNG.", {
        session_id: z.string().describe("Session ID"),
        selector: z.string().optional().describe("CSS selector for element. Omit for full viewport."),
        full_page: z.boolean().optional().describe("Capture entire scrollable page"),
        format: z.string().optional().describe("png or jpeg"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ session_id, selector, full_page, format }) => {
        const fmt = format || "png";
        const filePath = `/workspace/screenshot.${fmt}`;
        const script = buildScreenshotScript(selector || "", full_page || false, fmt, filePath);
        await runPlaywright(session_id, script);
        const content = await readFile(session_id, filePath);
        return { content: [{ type: "text", text: JSON.stringify({ file_path: filePath, mime_type: `image/${fmt}`, size_bytes: Buffer.from(content, "base64").length, content }) }] };
    });

    server.tool("click", "Click an element by selector.", {
        session_id: z.string().describe("Session ID"),
        selector: z.string().describe("CSS/XPath/Playwright locator"),
        wait_after: z.string().optional().describe("navigation, networkidle, or CSS selector to wait for"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ session_id, selector, wait_after }) => {
        const script = buildClickScript(selector, wait_after || "");
        const result = await runPlaywright(session_id, script);
        return { content: [{ type: "text", text: JSON.stringify(parseOutput(result)) }] };
    });

    server.tool("fill", "Fill form fields by selector/value pairs.", {
        session_id: z.string().describe("Session ID"),
        fields: z.array(z.object({ selector: z.string(), value: z.string() })).describe("Fields to fill"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ session_id, fields }) => {
        const script = buildFillScript(fields);
        const result = await runPlaywright(session_id, script);
        return { content: [{ type: "text", text: JSON.stringify(parseOutput(result)) }] };
    });

    server.tool("extract", "Extract text/HTML/attributes from elements.", {
        session_id: z.string().describe("Session ID"),
        selector: z.string().describe("CSS selector"),
        extract: z.string().optional().describe("textContent, innerText, innerHTML, outerHTML, attribute"),
        attribute: z.string().optional().describe("Attribute name (when extract=attribute)"),
        all: z.boolean().optional().describe("All matches or just first"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ session_id, selector, extract, attribute, all }) => {
        const script = buildExtractScript(selector, extract || "textContent", attribute || "", all || false);
        const result = await runPlaywright(session_id, script);
        return { content: [{ type: "text", text: JSON.stringify(parseOutput(result)) }] };
    });

    server.tool("generate_pdf", "Export current page as PDF.", {
        session_id: z.string().describe("Session ID"),
        format: z.string().optional().describe("Letter, A4, Legal, etc."),
        landscape: z.boolean().optional(),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ session_id, format, landscape }) => {
        const filePath = "/workspace/page.pdf";
        const script = buildPdfScript(format || "Letter", landscape || false, filePath);
        await runPlaywright(session_id, script);
        const content = await readFile(session_id, filePath);
        return { content: [{ type: "text", text: JSON.stringify({ file_path: filePath, size_bytes: Buffer.from(content, "base64").length, content }) }] };
    });

    server.tool("run_playwright_script", "Execute custom Playwright script. Has access to 'page' and 'context'.", {
        session_id: z.string().describe("Session ID"),
        script: z.string().describe("Playwright JavaScript to execute"),
        timeout: z.number().optional().describe("Timeout in seconds"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ session_id, script: userScript, timeout }) => {
        const wrapped = buildCustomScript(userScript);
        const result = await runPlaywright(session_id, wrapped);
        return { content: [{ type: "text", text: JSON.stringify(parseOutput(result)) }] };
    });

    server.tool("get_console_log", "Retrieve browser console messages from session.", {
        session_id: z.string().describe("Session ID"),
    }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, { destructiveHint: false, readOnlyHint: false, openWorldHint: true }, async ({ session_id }) => {
        try {
            const content = await readFile(session_id, "/workspace/.console-log.json");
            const messages = JSON.parse(Buffer.from(content, "base64").toString("utf8"));
            return { content: [{ type: "text", text: JSON.stringify({ session_id, messages }) }] };
        } catch {
            return { content: [{ type: "text", text: JSON.stringify({ session_id, messages: [] }) }] };
        }
    });
}

// --- Script builders ---

async function runPlaywright(sessionId, script) {
    const b64 = Buffer.from(script).toString("base64");
    const cmd = `echo '${b64}' | base64 -d > /tmp/_pw.js && NODE_PATH=/usr/lib/node_modules node /tmp/_pw.js`;
    return executeCommand(sessionId, cmd);
}

function parseOutput(result) {
    const stdout = (result.stdout || "").trim();
    const exitCode = result.exitCode ?? -1;
    if (exitCode !== 0) return { error: result.stderr || "Script failed", exitCode };
    try { return JSON.parse(stdout); } catch { return { output: stdout }; }
}

function preamble() {
    return `const { chromium } = require('playwright');
const fs = require('fs');
(async () => {
  const config = fs.existsSync('/workspace/.session-config.json') ? JSON.parse(fs.readFileSync('/workspace/.session-config.json','utf8')) : {};
  const [w,h] = (config.viewport||'1280x720').split('x').map(Number);
  const browser = await chromium.launch({ headless: true });
  const ctxOpts = { viewport: {width:w, height:h}, locale: config.locale||'en-US', ignoreHTTPSErrors: true };
  if (fs.existsSync('/workspace/.browser-state.json')) ctxOpts.storageState = '/workspace/.browser-state.json';
  const context = await browser.newContext(ctxOpts);
  const page = await context.newPage();
  const _console = [];
  page.on('console', msg => _console.push({type:msg.type(),text:msg.text()}));
`;
}

function postamble(saveUrl = true) {
    return `
  ${saveUrl ? "fs.writeFileSync('/workspace/.last-url.txt', page.url());" : ""}
  await context.storageState({ path: '/workspace/.browser-state.json' });
  fs.writeFileSync('/workspace/.console-log.json', JSON.stringify(_console));
  await browser.close();
})().catch(e => { console.error(e.message); process.exit(1); });`;
}

function reloadLastUrl() {
    return `  if (fs.existsSync('/workspace/.last-url.txt')) { await page.goto(fs.readFileSync('/workspace/.last-url.txt','utf8').trim(), {waitUntil:'domcontentloaded'}); }\n`;
}

function buildNavigateScript(url, waitUntil, timeout) {
    return `${preamble()}  const response = await page.goto(${JSON.stringify(url)}, { waitUntil: ${JSON.stringify(waitUntil)}, timeout: ${timeout * 1000} });
  const result = { url: page.url(), title: await page.title(), status: response ? response.status() : 0 };
  console.log(JSON.stringify(result));
${postamble()}`;
}

function buildScreenshotScript(selector, fullPage, format, filePath) {
    const opts = `{ path: ${JSON.stringify(filePath)}, type: ${JSON.stringify(format)}${fullPage ? ", fullPage: true" : ""} }`;
    const capture = selector
        ? `  await page.locator(${JSON.stringify(selector)}).first().screenshot(${opts});`
        : `  await page.screenshot(${opts});`;
    return `${preamble()}${reloadLastUrl()}${capture}
  console.log(JSON.stringify({success:true}));
${postamble(false)}`;
}

function buildClickScript(selector, waitAfter) {
    let click = `  await page.locator(${JSON.stringify(selector)}).click();`;
    if (waitAfter === "navigation") click = `  await Promise.all([page.waitForNavigation(), page.locator(${JSON.stringify(selector)}).click()]);`;
    else if (waitAfter === "networkidle") click += `\n  await page.waitForLoadState('networkidle');`;
    else if (waitAfter) click += `\n  await page.waitForSelector(${JSON.stringify(waitAfter)});`;
    return `${preamble()}${reloadLastUrl()}${click}
  console.log(JSON.stringify({ url: page.url(), title: await page.title() }));
${postamble()}`;
}

function buildFillScript(fields) {
    const fills = fields.map(f => `  await page.locator(${JSON.stringify(f.selector)}).fill(${JSON.stringify(f.value)});`).join("\n");
    return `${preamble()}${reloadLastUrl()}${fills}
  console.log(JSON.stringify({ url: page.url(), title: await page.title(), fieldsFilled: ${fields.length} }));
${postamble()}`;
}

function buildExtractScript(selector, extract, attribute, all) {
    let extractCode;
    if (all) {
        const evalLine = extract === "attribute" ? `await el.getAttribute(${JSON.stringify(attribute)}) || ''` : extract === "outerHTML" ? `await el.evaluate(e => e.outerHTML)` : `await el.${extract}()`;
        extractCode = `  const elements = await page.locator(${JSON.stringify(selector)}).all();
  const results = [];
  for (const el of elements) { results.push(${evalLine}); }
  console.log(JSON.stringify({ count: results.length, results }));`;
    } else {
        const evalLine = extract === "attribute" ? `await el.getAttribute(${JSON.stringify(attribute)}) || ''` : extract === "outerHTML" ? `await el.evaluate(e => e.outerHTML)` : `await el.${extract}()`;
        extractCode = `  const el = page.locator(${JSON.stringify(selector)}).first();
  const value = ${evalLine};
  console.log(JSON.stringify({ count: 1, results: [value] }));`;
    }
    return `${preamble()}${reloadLastUrl()}${extractCode}
${postamble(false)}`;
}

function buildPdfScript(format, landscape, filePath) {
    return `${preamble()}${reloadLastUrl()}  await page.pdf({ path: ${JSON.stringify(filePath)}, format: ${JSON.stringify(format)}, landscape: ${landscape}, printBackground: true });
  console.log(JSON.stringify({success:true}));
${postamble(false)}`;
}

function buildCustomScript(userScript) {
    return `${preamble()}${reloadLastUrl()}  // --- User Script ---
${userScript}
  // --- End ---
${postamble()}`;
}
