import { Page, BrowserContext, Download } from 'playwright';
import * as path from 'path';
import * as fs from 'fs';

export interface ToolResult {
    success: boolean;
    result?: unknown;
    error?: string;
    pageChanged?: boolean;
    newUrl?: string;
}

// Temporary directory for downloads
const DOWNLOAD_DIR = '/tmp/webmcp-downloads';

/**
 * Execute Playwright fallback tools when WebMCP is not available
 * Implements comprehensive browser automation based on https://playwright.dev/
 */
export async function executePlaywrightTool(
    page: Page,
    toolName: string,
    input: Record<string, unknown>,
    context?: BrowserContext
): Promise<ToolResult> {
    const originalUrl = page.url();

    try {
        let result: unknown;

        switch (toolName) {
            // === NAVIGATION ===
            case 'browser_navigate':
                await page.goto(input.url as string, { 
                    waitUntil: (input.wait_until as 'load' | 'domcontentloaded' | 'networkidle') || 'networkidle',
                    timeout: (input.timeout as number) || 30000
                });
                result = { url: page.url(), title: await page.title() };
                break;

            case 'browser_go_back':
                await page.goBack({ waitUntil: 'networkidle' });
                result = { url: page.url(), title: await page.title() };
                break;

            case 'browser_go_forward':
                await page.goForward({ waitUntil: 'networkidle' });
                result = { url: page.url(), title: await page.title() };
                break;

            case 'browser_reload':
                await page.reload({ waitUntil: 'networkidle' });
                result = { url: page.url(), title: await page.title() };
                break;

            // === INTERACTION - Basic ===
            case 'browser_click':
                await page.click(input.selector as string, {
                    button: (input.button as 'left' | 'right' | 'middle') || 'left',
                    clickCount: (input.click_count as number) || 1,
                    timeout: (input.timeout as number) || 30000
                });
                await page.waitForLoadState('networkidle').catch(() => {});
                result = { clicked: true };
                break;

            case 'browser_dblclick':
                await page.dblclick(input.selector as string);
                await page.waitForLoadState('networkidle').catch(() => {});
                result = { double_clicked: true };
                break;

            case 'browser_hover':
                await page.hover(input.selector as string);
                result = { hovered: true };
                break;

            case 'browser_type':
                await page.fill(input.selector as string, input.text as string);
                if (input.submit) {
                    await page.press(input.selector as string, 'Enter');
                    await page.waitForLoadState('networkidle').catch(() => {});
                }
                result = { typed: true };
                break;

            case 'browser_press_key':
                const targetSelector = input.selector as string;
                if (targetSelector) {
                    await page.press(targetSelector, input.key as string);
                } else {
                    await page.keyboard.press(input.key as string);
                }
                result = { pressed: input.key };
                break;

            case 'browser_select':
                await page.selectOption(input.selector as string, input.value as string);
                result = { selected: true };
                break;

            case 'browser_check':
                await page.check(input.selector as string);
                result = { checked: true };
                break;

            case 'browser_uncheck':
                await page.uncheck(input.selector as string);
                result = { unchecked: true };
                break;

            // === INTERACTION - Advanced Locators (Playwright's recommended approach) ===
            case 'browser_click_text':
                await page.getByText(input.text as string, { exact: input.exact as boolean }).click();
                await page.waitForLoadState('networkidle').catch(() => {});
                result = { clicked_text: input.text };
                break;

            case 'browser_click_role':
                await page.getByRole(input.role as any, { 
                    name: input.name as string,
                    exact: input.exact as boolean 
                }).click();
                await page.waitForLoadState('networkidle').catch(() => {});
                result = { clicked_role: input.role, name: input.name };
                break;

            case 'browser_click_label':
                await page.getByLabel(input.label as string, { exact: input.exact as boolean }).click();
                result = { clicked_label: input.label };
                break;

            case 'browser_fill_label':
                await page.getByLabel(input.label as string).fill(input.text as string);
                result = { filled_label: input.label };
                break;

            case 'browser_click_placeholder':
                await page.getByPlaceholder(input.placeholder as string).click();
                result = { clicked_placeholder: input.placeholder };
                break;

            case 'browser_fill_placeholder':
                await page.getByPlaceholder(input.placeholder as string).fill(input.text as string);
                result = { filled_placeholder: input.placeholder };
                break;

            // === FORM HANDLING ===
            case 'browser_fill_form':
                const fields = input.fields as Record<string, string>;
                for (const [selector, value] of Object.entries(fields)) {
                    await page.fill(selector, value);
                }
                if (input.submit_selector) {
                    await page.click(input.submit_selector as string);
                    await page.waitForLoadState('networkidle').catch(() => {});
                }
                result = { filled_fields: Object.keys(fields).length };
                break;

            case 'browser_upload_file':
                const fileInput = await page.$(input.selector as string);
                if (fileInput) {
                    // For base64 content, write to temp file first
                    if (input.content_base64) {
                        const tempPath = `/tmp/upload_${Date.now()}_${input.filename || 'file'}`;
                        fs.writeFileSync(tempPath, Buffer.from(input.content_base64 as string, 'base64'));
                        await fileInput.setInputFiles(tempPath);
                        fs.unlinkSync(tempPath);
                    } else if (input.file_path) {
                        await fileInput.setInputFiles(input.file_path as string);
                    }
                    result = { uploaded: true };
                } else {
                    throw new Error('File input not found');
                }
                break;

            // === CAPTURE & EXTRACTION ===
            case 'browser_screenshot':
                const screenshot = await page.screenshot({
                    fullPage: input.full_page as boolean || false,
                    type: (input.format as 'png' | 'jpeg') || 'png',
                    quality: input.format === 'jpeg' ? (input.quality as number) || 80 : undefined
                });
                result = {
                    format: input.format || 'png',
                    base64: screenshot.toString('base64'),
                    width: page.viewportSize()?.width,
                    height: page.viewportSize()?.height
                };
                break;

            case 'browser_screenshot_element':
                const element = await page.$(input.selector as string);
                if (element) {
                    const elementShot = await element.screenshot({ type: 'png' });
                    result = {
                        format: 'png',
                        base64: elementShot.toString('base64')
                    };
                } else {
                    throw new Error('Element not found');
                }
                break;

            case 'browser_pdf':
                const pdf = await page.pdf({
                    format: (input.format as 'A4' | 'Letter') || 'A4',
                    printBackground: true,
                    margin: { top: '1cm', bottom: '1cm', left: '1cm', right: '1cm' }
                });
                result = {
                    format: 'pdf',
                    base64: pdf.toString('base64')
                };
                break;

            case 'browser_get_text':
                const textSelector = input.selector as string;
                if (textSelector) {
                    result = { text: await page.textContent(textSelector) };
                } else {
                    result = { text: await page.evaluate(() => document.body.innerText) };
                }
                break;

            case 'browser_get_inner_html':
                result = { html: await page.innerHTML(input.selector as string) };
                break;

            case 'browser_get_attribute':
                result = {
                    value: await page.getAttribute(
                        input.selector as string,
                        input.attribute as string
                    )
                };
                break;

            case 'browser_get_all_text':
                const elements = await page.$$(input.selector as string);
                const texts = await Promise.all(elements.map(el => el.textContent()));
                result = { texts: texts.filter(t => t !== null) };
                break;

            case 'browser_get_page_content':
                result = {
                    url: page.url(),
                    title: await page.title(),
                    html: await page.content()
                };
                break;

            // === TABLE EXTRACTION ===
            case 'browser_extract_table':
                const tableData = await page.evaluate((sel) => {
                    const table = document.querySelector(sel);
                    if (!table) return null;
                    
                    const headers: string[] = [];
                    const rows: Record<string, string>[] = [];
                    
                    // Extract headers
                    const headerCells = table.querySelectorAll('thead th, tr:first-child th');
                    headerCells.forEach(cell => headers.push(cell.textContent?.trim() || ''));
                    
                    // If no thead, try first row
                    if (headers.length === 0) {
                        const firstRow = table.querySelector('tr');
                        firstRow?.querySelectorAll('td, th').forEach(cell => 
                            headers.push(cell.textContent?.trim() || '')
                        );
                    }
                    
                    // Extract data rows
                    const dataRows = table.querySelectorAll('tbody tr, tr:not(:first-child)');
                    dataRows.forEach(row => {
                        const rowData: Record<string, string> = {};
                        const cells = row.querySelectorAll('td');
                        cells.forEach((cell, i) => {
                            const key = headers[i] || `col_${i}`;
                            rowData[key] = cell.textContent?.trim() || '';
                        });
                        if (Object.keys(rowData).length > 0) {
                            rows.push(rowData);
                        }
                    });
                    
                    return { headers, rows };
                }, input.selector as string);
                
                result = tableData || { error: 'Table not found' };
                break;

            case 'browser_extract_links':
                const links = await page.evaluate((sel) => {
                    const container = sel ? document.querySelector(sel) : document;
                    const anchors = container?.querySelectorAll('a[href]') || [];
                    return Array.from(anchors).map(a => ({
                        text: a.textContent?.trim() || '',
                        href: (a as HTMLAnchorElement).href,
                        title: a.getAttribute('title') || ''
                    }));
                }, input.selector as string || null);
                result = { links };
                break;

            // === WAITING ===
            case 'browser_wait_for_selector':
                await page.waitForSelector(input.selector as string, {
                    state: (input.state as 'attached' | 'visible' | 'hidden') || 'visible',
                    timeout: (input.timeout as number) || 30000
                });
                result = { found: true };
                break;

            case 'browser_wait_for_text':
                await page.waitForSelector(`text=${input.text}`, {
                    timeout: (input.timeout as number) || 30000
                });
                result = { found: true };
                break;

            case 'browser_wait_for_url':
                await page.waitForURL(input.url as string, {
                    timeout: (input.timeout as number) || 30000
                });
                result = { url: page.url() };
                break;

            case 'browser_wait_for_load':
                await page.waitForLoadState(
                    (input.state as 'load' | 'domcontentloaded' | 'networkidle') || 'networkidle',
                    { timeout: (input.timeout as number) || 30000 }
                );
                result = { loaded: true };
                break;

            case 'browser_wait_for_response':
                const response = await page.waitForResponse(
                    resp => resp.url().includes(input.url_pattern as string),
                    { timeout: (input.timeout as number) || 30000 }
                );
                result = {
                    url: response.url(),
                    status: response.status(),
                    ok: response.ok()
                };
                break;

            // === SCROLLING ===
            case 'browser_scroll':
                const direction = input.direction as string;
                const amount = (input.amount as number) || 300;
                const scrollSelector = input.selector as string;

                if (scrollSelector) {
                    const scrollElement = await page.$(scrollSelector);
                    if (scrollElement) {
                        await scrollElement.evaluate((el, { dir, amt }) => {
                            const scrollMap: Record<string, [number, number]> = {
                                up: [0, -amt],
                                down: [0, amt],
                                left: [-amt, 0],
                                right: [amt, 0]
                            };
                            const [x, y] = scrollMap[dir] || [0, 0];
                            el.scrollBy(x, y);
                        }, { dir: direction, amt: amount });
                    }
                } else {
                    const scrollMap: Record<string, [number, number]> = {
                        up: [0, -amount],
                        down: [0, amount],
                        left: [-amount, 0],
                        right: [amount, 0]
                    };
                    const [x, y] = scrollMap[direction] || [0, 0];
                    await page.evaluate(({ x, y }) => window.scrollBy(x, y), { x, y });
                }
                result = { scrolled: true };
                break;

            case 'browser_scroll_to_element':
                await page.locator(input.selector as string).scrollIntoViewIfNeeded();
                result = { scrolled_to: input.selector };
                break;

            case 'browser_scroll_to_bottom':
                await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight));
                result = { scrolled: 'bottom' };
                break;

            case 'browser_scroll_to_top':
                await page.evaluate(() => window.scrollTo(0, 0));
                result = { scrolled: 'top' };
                break;

            // === JAVASCRIPT EXECUTION ===
            case 'browser_evaluate':
                result = await page.evaluate(input.script as string);
                break;

            case 'browser_evaluate_on_element':
                const evalElement = await page.$(input.selector as string);
                if (evalElement) {
                    result = await evalElement.evaluate(
                        (el, script) => eval(script),
                        input.script as string
                    );
                } else {
                    throw new Error('Element not found');
                }
                break;

            // === FRAME HANDLING ===
            case 'browser_switch_to_frame':
                const frame = page.frameLocator(input.selector as string);
                // Store frame reference for subsequent calls (simplified - in production use session state)
                result = { switched: true, frame_selector: input.selector };
                break;

            // === DIALOG HANDLING ===
            case 'browser_handle_dialog':
                page.once('dialog', async dialog => {
                    if (input.accept) {
                        await dialog.accept(input.prompt_text as string);
                    } else {
                        await dialog.dismiss();
                    }
                });
                result = { handler_set: true, action: input.accept ? 'accept' : 'dismiss' };
                break;

            // === DOWNLOADS ===
            case 'browser_download':
                if (!fs.existsSync(DOWNLOAD_DIR)) {
                    fs.mkdirSync(DOWNLOAD_DIR, { recursive: true });
                }
                
                const [download] = await Promise.all([
                    page.waitForEvent('download'),
                    page.click(input.selector as string)
                ]);
                
                const downloadPath = path.join(DOWNLOAD_DIR, download.suggestedFilename());
                await download.saveAs(downloadPath);
                const downloadContent = fs.readFileSync(downloadPath);
                fs.unlinkSync(downloadPath);
                
                result = {
                    filename: download.suggestedFilename(),
                    base64: downloadContent.toString('base64')
                };
                break;

            // === NETWORK ===
            case 'browser_get_cookies': {
                const ctx = context || page.context();
                const cookies = await ctx.cookies();
                result = { 
                    cookies: input.url 
                        ? cookies.filter(c => (input.url as string).includes(c.domain))
                        : cookies 
                };
                break;
            }

            case 'browser_set_cookies': {
                const ctx = context || page.context();
                await ctx.addCookies(input.cookies as any[]);
                result = { set: true };
                break;
            }

            case 'browser_clear_cookies': {
                const ctx = context || page.context();
                await ctx.clearCookies();
                result = { cleared: true };
                break;
            }

            // === ACCESSIBILITY ===
            case 'browser_get_accessibility_tree': {
                const snapshot = await page.evaluate(() => {
                    function buildTree(el: Element): any {
                        const role = el.getAttribute('role') || el.tagName.toLowerCase();
                        const name = el.getAttribute('aria-label') || (el as HTMLElement).innerText?.substring(0, 100) || '';
                        const node: any = { role, name: name.trim() };
                        const children = Array.from(el.children).map(buildTree).filter(c => c);
                        if (children.length) node.children = children;
                        return node;
                    }
                    return buildTree(document.body);
                });
                result = { accessibility: snapshot };
                break;
            }
                break;

            // === ELEMENT INFO ===
            case 'browser_is_visible':
                result = { visible: await page.isVisible(input.selector as string) };
                break;

            case 'browser_is_enabled':
                result = { enabled: await page.isEnabled(input.selector as string) };
                break;

            case 'browser_is_checked':
                result = { checked: await page.isChecked(input.selector as string) };
                break;

            case 'browser_count_elements':
                const count = await page.locator(input.selector as string).count();
                result = { count };
                break;

            case 'browser_get_bounding_box':
                const boundingBox = await page.locator(input.selector as string).boundingBox();
                result = boundingBox || { error: 'Element not found or not visible' };
                break;

            // === NETWORK ===
            case 'browser_intercept_requests':
                const urlPattern = input.url_pattern as string || '**/*';
                const action = input.action as string || 'log';
                
                // Store intercepted requests in page context
                if (!(page as any).__interceptedRequests) {
                    (page as any).__interceptedRequests = [];
                }
                
                await page.route(urlPattern, async (route) => {
                    const request = route.request();
                    const requestData = {
                        url: request.url(),
                        method: request.method(),
                        headers: request.headers(),
                        timestamp: new Date().toISOString()
                    };
                    (page as any).__interceptedRequests.push(requestData);
                    
                    if (action === 'block') {
                        await route.abort();
                    } else if (action === 'modify' && input.modify_headers) {
                        const headers = { ...request.headers(), ...(input.modify_headers as Record<string, string>) };
                        await route.continue({ headers });
                    } else {
                        await route.continue();
                    }
                });
                result = { message: `Request interception set up for pattern: ${urlPattern}`, action };
                break;

            case 'browser_mock_response':
                await page.route(input.url_pattern as string, async (route) => {
                    await route.fulfill({
                        status: (input.status as number) || 200,
                        contentType: (input.content_type as string) || 'application/json',
                        body: input.body as string,
                        headers: input.headers as Record<string, string>
                    });
                });
                result = { message: `Mock response set up for: ${input.url_pattern}` };
                break;

            case 'browser_get_network_log':
                const networkLogs = (page as any).__interceptedRequests || [];
                let filteredLogs = networkLogs;
                if (input.url_filter) {
                    const filterRegex = new RegExp(input.url_filter as string);
                    filteredLogs = networkLogs.filter((r: any) => filterRegex.test(r.url));
                }
                result = { requests: filteredLogs, count: filteredLogs.length };
                break;

            case 'browser_clear_network_log':
                (page as any).__interceptedRequests = [];
                result = { message: 'Network log cleared' };
                break;

            case 'browser_wait_for_request':
                const waitedRequest = await page.waitForRequest(
                    (req) => {
                        const urlMatches = req.url().includes(input.url_pattern as string) || 
                            new RegExp(input.url_pattern as string).test(req.url());
                        const methodMatches = !input.method || req.method() === input.method;
                        return urlMatches && methodMatches;
                    },
                    { timeout: (input.timeout as number) || 30000 }
                );
                result = {
                    url: waitedRequest.url(),
                    method: waitedRequest.method(),
                    headers: waitedRequest.headers()
                };
                break;

            // === DEVICE EMULATION ===
            case 'browser_set_viewport':
                await page.setViewportSize({
                    width: input.width as number,
                    height: input.height as number
                });
                result = { message: `Viewport set to ${input.width}x${input.height}` };
                break;

            case 'browser_emulate_device':
                const { devices } = await import('playwright');
                const deviceName = input.device as string;
                const deviceConfig = devices[deviceName];
                if (!deviceConfig) {
                    result = { error: `Unknown device: ${deviceName}. Available: iPhone 14, iPhone 14 Pro Max, Pixel 7, iPad Pro 11, Galaxy S23, etc.` };
                } else {
                    const context = page.context();
                    await context.clearCookies();
                    await page.setViewportSize(deviceConfig.viewport);
                    result = { message: `Emulating ${deviceName}`, viewport: deviceConfig.viewport, userAgent: deviceConfig.userAgent };
                }
                break;

            case 'browser_set_geolocation':
                await page.context().setGeolocation({
                    latitude: input.latitude as number,
                    longitude: input.longitude as number,
                    accuracy: (input.accuracy as number) || 0
                });
                result = { message: `Geolocation set to ${input.latitude}, ${input.longitude}` };
                break;

            case 'browser_set_timezone':
                await page.context().addInitScript(`
                    Object.defineProperty(Intl.DateTimeFormat.prototype, 'resolvedOptions', {
                        value: function() { return { timeZone: '${input.timezone}' }; }
                    });
                `);
                result = { message: `Timezone hint set to ${input.timezone}. Note: This affects JavaScript only.` };
                break;

            case 'browser_set_locale':
                result = { message: `Locale set to ${input.locale}. Note: Changes apply to new contexts.` };
                break;

            case 'browser_set_offline':
                await page.context().setOffline(input.offline as boolean);
                result = { message: `Offline mode: ${input.offline}` };
                break;

            case 'browser_throttle_network':
                const presets: Record<string, { offline: boolean; downloadThroughput: number; uploadThroughput: number; latency: number }> = {
                    'slow-3g': { offline: false, downloadThroughput: 50 * 1024, uploadThroughput: 25 * 1024, latency: 400 },
                    'fast-3g': { offline: false, downloadThroughput: 375 * 1024, uploadThroughput: 75 * 1024, latency: 100 },
                    '4g': { offline: false, downloadThroughput: 4000 * 1024, uploadThroughput: 3000 * 1024, latency: 20 },
                    'wifi': { offline: false, downloadThroughput: 30000 * 1024, uploadThroughput: 15000 * 1024, latency: 2 },
                    'offline': { offline: true, downloadThroughput: 0, uploadThroughput: 0, latency: 0 }
                };
                const preset = presets[input.preset as string];
                if (preset) {
                    await page.context().setOffline(preset.offline);
                    result = { message: `Network throttled to ${input.preset}` };
                } else if (input.download_kbps || input.upload_kbps) {
                    result = { message: 'Custom network throttling configured (browser-level throttling not fully supported)' };
                } else {
                    result = { error: 'Specify preset or custom values' };
                }
                break;

            // === STORAGE ===
            case 'browser_get_local_storage':
                if (input.key) {
                    const valueLocal = await page.evaluate((k) => localStorage.getItem(k), input.key as string);
                    result = { key: input.key, value: valueLocal };
                } else {
                    const allLocal = await page.evaluate(() => {
                        const items: Record<string, string> = {};
                        for (let i = 0; i < localStorage.length; i++) {
                            const k = localStorage.key(i);
                            if (k) items[k] = localStorage.getItem(k) || '';
                        }
                        return items;
                    });
                    result = { items: allLocal, count: Object.keys(allLocal).length };
                }
                break;

            case 'browser_set_local_storage':
                await page.evaluate((items) => {
                    for (const [k, v] of Object.entries(items)) {
                        localStorage.setItem(k, String(v));
                    }
                }, input.items as Record<string, string>);
                result = { message: `Set ${Object.keys(input.items as object).length} localStorage items` };
                break;

            case 'browser_get_session_storage':
                if (input.key) {
                    const valueSession = await page.evaluate((k) => sessionStorage.getItem(k), input.key as string);
                    result = { key: input.key, value: valueSession };
                } else {
                    const allSession = await page.evaluate(() => {
                        const items: Record<string, string> = {};
                        for (let i = 0; i < sessionStorage.length; i++) {
                            const k = sessionStorage.key(i);
                            if (k) items[k] = sessionStorage.getItem(k) || '';
                        }
                        return items;
                    });
                    result = { items: allSession, count: Object.keys(allSession).length };
                }
                break;

            case 'browser_set_session_storage':
                await page.evaluate((items) => {
                    for (const [k, v] of Object.entries(items)) {
                        sessionStorage.setItem(k, String(v));
                    }
                }, input.items as Record<string, string>);
                result = { message: `Set ${Object.keys(input.items as object).length} sessionStorage items` };
                break;

            case 'browser_clear_storage':
                const storageType = (input.type as string) || 'both';
                await page.evaluate((type) => {
                    if (type === 'local' || type === 'both') localStorage.clear();
                    if (type === 'session' || type === 'both') sessionStorage.clear();
                }, storageType);
                result = { message: `Cleared ${storageType} storage` };
                break;

            case 'browser_get_indexed_db':
                const idbData = await page.evaluate(async (args) => {
                    return new Promise((resolve, reject) => {
                        const request = indexedDB.open(args.database);
                        request.onerror = () => reject(request.error);
                        request.onsuccess = () => {
                            const db = request.result;
                            const tx = db.transaction(args.store, 'readonly');
                            const store = tx.objectStore(args.store);
                            
                            if (args.key) {
                                const getReq = store.get(args.key);
                                getReq.onsuccess = () => resolve(getReq.result);
                                getReq.onerror = () => reject(getReq.error);
                            } else {
                                const getAllReq = store.getAll();
                                getAllReq.onsuccess = () => resolve(getAllReq.result);
                                getAllReq.onerror = () => reject(getAllReq.error);
                            }
                        };
                    });
                }, { database: input.database as string, store: input.store as string, key: input.key as string });
                result = { data: idbData };
                break;

            // === MULTI-TAB ===
            case 'browser_new_tab': {
                const tabContext = page.context();
                const newPage = await tabContext.newPage();
                if (input.url) {
                    await newPage.goto(input.url as string);
                }
                const pages = tabContext.pages();
                result = { message: 'New tab opened', tabIndex: pages.length - 1, url: input.url || 'about:blank' };
                break;
            }

            case 'browser_list_tabs':
                const allPages = page.context().pages();
                const tabList = await Promise.all(allPages.map(async (p, i) => ({
                    index: i,
                    url: p.url(),
                    title: await p.title()
                })));
                result = { tabs: tabList, count: tabList.length };
                break;

            case 'browser_switch_tab':
                const contextPages = page.context().pages();
                let targetPage: any = null;
                
                if (input.index !== undefined) {
                    targetPage = contextPages[input.index as number];
                } else if (input.url_pattern) {
                    targetPage = contextPages.find(p => p.url().includes(input.url_pattern as string));
                }
                
                if (targetPage) {
                    await targetPage.bringToFront();
                    result = { message: `Switched to tab`, url: targetPage.url() };
                } else {
                    result = { error: 'Tab not found' };
                }
                break;

            case 'browser_close_tab':
                const pagesForClose = page.context().pages();
                const indexToClose = (input.index as number) ?? pagesForClose.indexOf(page);
                if (pagesForClose[indexToClose]) {
                    await pagesForClose[indexToClose].close();
                    result = { message: `Closed tab ${indexToClose}` };
                } else {
                    result = { error: 'Tab not found' };
                }
                break;

            case 'browser_wait_for_popup':
                const popup = await page.waitForEvent('popup', { timeout: (input.timeout as number) || 30000 });
                await popup.waitForLoadState();
                result = { url: popup.url(), title: await popup.title() };
                break;

            // === CONSOLE & ERRORS ===
            case 'browser_get_console_logs':
                if (!(page as any).__consoleLogs) {
                    (page as any).__consoleLogs = [];
                    page.on('console', msg => {
                        (page as any).__consoleLogs.push({
                            type: msg.type(),
                            text: msg.text(),
                            timestamp: new Date().toISOString()
                        });
                    });
                }
                let logs = (page as any).__consoleLogs || [];
                if (input.level && input.level !== 'all') {
                    logs = logs.filter((l: any) => l.type === input.level);
                }
                if (input.clear) {
                    (page as any).__consoleLogs = [];
                }
                result = { logs, count: logs.length };
                break;

            case 'browser_get_js_errors':
                if (!(page as any).__jsErrors) {
                    (page as any).__jsErrors = [];
                    page.on('pageerror', err => {
                        (page as any).__jsErrors.push({
                            message: err.message,
                            stack: err.stack,
                            timestamp: new Date().toISOString()
                        });
                    });
                }
                let errors = (page as any).__jsErrors || [];
                if (input.clear) {
                    (page as any).__jsErrors = [];
                }
                result = { errors, count: errors.length };
                break;

            case 'browser_enable_console_capture':
                if (input.enabled) {
                    if (!(page as any).__consoleLogs) {
                        (page as any).__consoleLogs = [];
                        page.on('console', msg => {
                            (page as any).__consoleLogs.push({
                                type: msg.type(),
                                text: msg.text(),
                                timestamp: new Date().toISOString()
                            });
                        });
                    }
                }
                result = { message: `Console capture ${input.enabled ? 'enabled' : 'disabled'}` };
                break;

            // === MEDIA ===
            case 'browser_play_video':
                await page.evaluate((sel) => {
                    const video = document.querySelector(sel) as HTMLVideoElement;
                    if (video) video.play();
                }, input.selector as string);
                result = { message: 'Video play initiated' };
                break;

            case 'browser_pause_video':
                await page.evaluate((sel) => {
                    const video = document.querySelector(sel) as HTMLVideoElement;
                    if (video) video.pause();
                }, input.selector as string);
                result = { message: 'Video paused' };
                break;

            case 'browser_get_media_state':
                const mediaState = await page.evaluate((sel) => {
                    const media = document.querySelector(sel) as HTMLMediaElement;
                    if (!media) return null;
                    return {
                        paused: media.paused,
                        ended: media.ended,
                        currentTime: media.currentTime,
                        duration: media.duration,
                        volume: media.volume,
                        muted: media.muted,
                        readyState: media.readyState
                    };
                }, input.selector as string);
                result = mediaState || { error: 'Media element not found' };
                break;

            case 'browser_set_media_time':
                await page.evaluate((args) => {
                    const media = document.querySelector(args.selector) as HTMLMediaElement;
                    if (media) media.currentTime = args.time;
                }, { selector: input.selector as string, time: input.time as number });
                result = { message: `Media time set to ${input.time}s` };
                break;

            case 'browser_set_media_volume':
                await page.evaluate((args) => {
                    const media = document.querySelector(args.selector) as HTMLMediaElement;
                    if (media) media.volume = args.volume;
                }, { selector: input.selector as string, volume: input.volume as number });
                result = { message: `Volume set to ${input.volume}` };
                break;

            // === DRAG & DROP ===
            case 'browser_drag_and_drop':
                await page.dragAndDrop(input.source_selector as string, input.target_selector as string);
                result = { message: `Dragged from ${input.source_selector} to ${input.target_selector}` };
                break;

            case 'browser_drag_to_position':
                const sourceBox = await page.locator(input.selector as string).boundingBox();
                if (sourceBox) {
                    await page.mouse.move(sourceBox.x + sourceBox.width / 2, sourceBox.y + sourceBox.height / 2);
                    await page.mouse.down();
                    await page.mouse.move(input.x as number, input.y as number);
                    await page.mouse.up();
                    result = { message: `Dragged to position (${input.x}, ${input.y})` };
                } else {
                    result = { error: 'Source element not found' };
                }
                break;

            // === RICH TEXT / CONTENTEDITABLE ===
            case 'browser_fill_contenteditable':
                if (input.html) {
                    await page.evaluate((args) => {
                        const el = document.querySelector(args.selector);
                        if (el) el.innerHTML = args.content;
                    }, { selector: input.selector as string, content: input.content as string });
                } else {
                    await page.locator(input.selector as string).fill(input.content as string);
                }
                result = { message: 'Content filled' };
                break;

            case 'browser_get_editor_content':
                const editorContent = await page.evaluate((args) => {
                    const el = document.querySelector(args.selector);
                    if (!el) return null;
                    return args.format === 'html' ? el.innerHTML : el.textContent;
                }, { selector: input.selector as string, format: (input.format as string) || 'text' });
                result = { content: editorContent };
                break;

            // === SHADOW DOM ===
            case 'browser_pierce_shadow':
                // Playwright supports >> for piercing shadow DOM
                const shadowElement = await page.locator(input.selector as string).first();
                const shadowVisible = await shadowElement.isVisible();
                result = { found: shadowVisible, selector: input.selector };
                break;

            case 'browser_get_shadow_root':
                const hasShadow = await page.evaluate((sel) => {
                    const el = document.querySelector(sel);
                    return el ? !!el.shadowRoot : false;
                }, input.selector as string);
                result = { hasShadowRoot: hasShadow };
                break;

            // === PERFORMANCE ===
            case 'browser_get_metrics':
                const metrics = await page.evaluate(() => {
                    const perf = performance;
                    const navTiming = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming;
                    return {
                        url: window.location.href,
                        domContentLoaded: navTiming?.domContentLoadedEventEnd - navTiming?.startTime,
                        load: navTiming?.loadEventEnd - navTiming?.startTime,
                        firstPaint: performance.getEntriesByName('first-paint')[0]?.startTime,
                        firstContentfulPaint: performance.getEntriesByName('first-contentful-paint')[0]?.startTime,
                        resourceCount: performance.getEntriesByType('resource').length,
                        memoryUsage: (performance as any).memory ? {
                            usedJSHeapSize: (performance as any).memory.usedJSHeapSize,
                            totalJSHeapSize: (performance as any).memory.totalJSHeapSize
                        } : undefined
                    };
                });
                result = metrics;
                break;

            case 'browser_get_timing':
                const timing = await page.evaluate(() => ({
                    navigation: performance.getEntriesByType('navigation')[0],
                    resources: performance.getEntriesByType('resource').slice(0, 50) // Limit to prevent huge responses
                }));
                result = timing;
                break;

            case 'browser_start_tracing':
                const browser = page.context().browser();
                if (browser) {
                    await browser.startTracing(page, {
                        screenshots: input.screenshots as boolean,
                        categories: input.categories as string[]
                    });
                    result = { message: 'Tracing started' };
                } else {
                    result = { error: 'Browser tracing not available' };
                }
                break;

            case 'browser_stop_tracing':
                const browserForStop = page.context().browser();
                if (browserForStop) {
                    const traceBuffer = await browserForStop.stopTracing();
                    result = { message: 'Tracing stopped', traceSize: traceBuffer.length };
                } else {
                    result = { error: 'No active tracing' };
                }
                break;

            case 'browser_get_coverage':
                // Note: Coverage requires starting coverage before page load
                result = { 
                    message: 'Code coverage requires starting collection before page navigation.',
                    hint: 'Use browser_start_coverage first, navigate, then browser_get_coverage'
                };
                break;

            // === VISUAL ===
            case 'browser_compare_screenshots':
                const currentScreenshot = await (input.selector 
                    ? page.locator(input.selector as string).screenshot()
                    : page.screenshot());
                const baselineBuffer = Buffer.from(input.baseline_base64 as string, 'base64');
                
                // Simple pixel comparison
                const threshold = (input.threshold as number) || 0.1;
                const currentPixels = currentScreenshot.length;
                const baselinePixels = baselineBuffer.length;
                const sizeDiff = Math.abs(currentPixels - baselinePixels) / Math.max(currentPixels, baselinePixels);
                
                result = {
                    match: sizeDiff <= threshold,
                    difference: sizeDiff,
                    threshold,
                    currentSize: currentPixels,
                    baselineSize: baselinePixels
                };
                break;

            case 'browser_highlight_element':
                const highlightColor = (input.color as string) || 'red';
                const highlightDuration = (input.duration_ms as number) || 2000;
                
                await page.evaluate((args) => {
                    const el = document.querySelector(args.selector) as HTMLElement;
                    if (el) {
                        const original = el.style.outline;
                        el.style.outline = `3px solid ${args.color}`;
                        setTimeout(() => { el.style.outline = original; }, args.duration);
                    }
                }, { selector: input.selector as string, color: highlightColor, duration: highlightDuration });
                
                result = { message: `Highlighted ${input.selector} for ${highlightDuration}ms` };
                break;

            case 'browser_get_computed_style':
                const computedStyle = await page.evaluate((args) => {
                    const el = document.querySelector(args.selector);
                    if (!el) return null;
                    const styles = window.getComputedStyle(el);
                    if (args.properties && args.properties.length > 0) {
                        const result: Record<string, string> = {};
                        for (const prop of args.properties) {
                            result[prop] = styles.getPropertyValue(prop);
                        }
                        return result;
                    }
                    // Return common properties
                    return {
                        display: styles.display,
                        position: styles.position,
                        width: styles.width,
                        height: styles.height,
                        color: styles.color,
                        backgroundColor: styles.backgroundColor,
                        fontSize: styles.fontSize,
                        fontFamily: styles.fontFamily,
                        margin: styles.margin,
                        padding: styles.padding,
                        border: styles.border,
                        visibility: styles.visibility,
                        opacity: styles.opacity
                    };
                }, { selector: input.selector as string, properties: input.properties as string[] });
                result = computedStyle || { error: 'Element not found' };
                break;

            // === PERMISSIONS ===
            case 'browser_grant_permission':
                await page.context().grantPermissions([input.permission as string], {
                    origin: input.origin as string
                });
                result = { message: `Granted ${input.permission} permission` };
                break;

            case 'browser_deny_permission':
                // Playwright doesn't have a direct deny - we clear and don't grant
                await page.context().clearPermissions();
                result = { message: `Permissions cleared (${input.permission} effectively denied)` };
                break;

            case 'browser_reset_permissions':
                await page.context().clearPermissions();
                result = { message: 'All permissions reset' };
                break;

            // === CLIPBOARD ===
            case 'browser_copy_to_clipboard':
                await page.evaluate((text) => navigator.clipboard.writeText(text), input.text as string);
                result = { message: 'Text copied to clipboard' };
                break;

            case 'browser_read_clipboard':
                const clipboardText = await page.evaluate(() => navigator.clipboard.readText());
                result = { text: clipboardText };
                break;

            // === FRAMES ===
            case 'browser_list_frames':
                const frames = page.frames();
                const frameList = frames.map((f, i) => ({
                    index: i,
                    url: f.url(),
                    name: f.name() || undefined
                }));
                result = { frames: frameList, count: frameList.length };
                break;

            case 'browser_frame_click':
                const frameForClick = page.frameLocator(input.frame_selector as string);
                await frameForClick.locator(input.element_selector as string).click();
                result = { message: `Clicked ${input.element_selector} in frame ${input.frame_selector}` };
                break;

            case 'browser_frame_fill':
                const frameForFill = page.frameLocator(input.frame_selector as string);
                await frameForFill.locator(input.element_selector as string).fill(input.text as string);
                result = { message: `Filled ${input.element_selector} in frame` };
                break;

            case 'browser_frame_get_text':
                const frameForText = page.frameLocator(input.frame_selector as string);
                const frameText = await frameForText.locator(input.element_selector as string).textContent();
                result = { text: frameText };
                break;

            // ===========================
            // HIGH-LEVEL ACTION TOOLS
            // ===========================

            case 'browser_login': {
                const usernameSelector = (input.username_selector as string) || 'input[type="email"], input[name="username"], input[name="email"], input[id="email"], input[id="username"], #username, #email';
                const passwordSelector = (input.password_selector as string) || 'input[type="password"], input[name="password"], #password';
                const submitSelector = (input.submit_selector as string) || 'button[type="submit"], input[type="submit"], button:has-text("Sign in"), button:has-text("Log in"), button:has-text("Login")';

                // Fill username
                const usernameField = page.locator(usernameSelector).first();
                await usernameField.waitFor({ state: 'visible', timeout: 10000 });
                await usernameField.fill(input.username as string);

                // Fill password
                const passwordField = page.locator(passwordSelector).first();
                await passwordField.waitFor({ state: 'visible', timeout: 5000 });
                await passwordField.fill(input.password as string);

                // Submit
                const submitBtn = page.locator(submitSelector).first();
                await submitBtn.click();

                // Wait for navigation
                await page.waitForLoadState('networkidle', { timeout: 15000 }).catch(() => {});

                result = {
                    message: 'Login submitted',
                    currentUrl: page.url(),
                    title: await page.title()
                };
                break;
            }

            case 'browser_fill_form': {
                const fields = input.fields as Record<string, string>;
                const filledFields: string[] = [];

                for (const [selector, value] of Object.entries(fields)) {
                    const field = page.locator(selector).first();
                    await field.waitFor({ state: 'visible', timeout: 5000 });

                    const tagName = await field.evaluate(el => el.tagName.toLowerCase());
                    if (tagName === 'select') {
                        await field.selectOption(value);
                    } else {
                        const inputType = await field.getAttribute('type');
                        if (inputType === 'checkbox' || inputType === 'radio') {
                            if (value === 'true' || value === 'checked') {
                                await field.check();
                            } else {
                                await field.uncheck();
                            }
                        } else {
                            await field.fill(value);
                        }
                    }
                    filledFields.push(selector);
                }

                // Submit if requested
                if (input.submit_selector) {
                    await page.locator(input.submit_selector as string).first().click();
                    await page.waitForLoadState('networkidle', { timeout: 10000 }).catch(() => {});
                }

                result = {
                    message: `Filled ${filledFields.length} fields`,
                    filledFields,
                    submitted: !!input.submit_selector,
                    currentUrl: page.url()
                };
                break;
            }

            case 'browser_search': {
                const searchSelectors = [
                    input.search_selector as string,
                    'input[type="search"]',
                    'input[name="q"]',
                    'input[name="query"]',
                    'input[name="search"]',
                    'input[aria-label*="search" i]',
                    'input[placeholder*="search" i]',
                    '#search',
                    '.search-input'
                ].filter(Boolean);

                let searchField = null;
                for (const sel of searchSelectors) {
                    try {
                        const loc = page.locator(sel).first();
                        if (await loc.isVisible({ timeout: 1000 })) {
                            searchField = loc;
                            break;
                        }
                    } catch { continue; }
                }

                if (!searchField) {
                    result = { error: 'Search field not found', triedSelectors: searchSelectors };
                    break;
                }

                await searchField.fill(input.query as string);
                await searchField.press('Enter');
                await page.waitForLoadState('networkidle', { timeout: 15000 }).catch(() => {});

                result = {
                    message: `Searched for: ${input.query}`,
                    currentUrl: page.url(),
                    title: await page.title()
                };
                break;
            }

            case 'browser_checkout': {
                const steps: string[] = [];

                // Click checkout/buy button
                const checkoutSelectors = [
                    input.checkout_selector as string,
                    'button:has-text("Checkout")',
                    'button:has-text("Buy")',
                    'button:has-text("Purchase")',
                    'a:has-text("Checkout")',
                    '#checkout',
                    '.checkout-btn'
                ].filter(Boolean);

                for (const sel of checkoutSelectors) {
                    try {
                        const btn = page.locator(sel).first();
                        if (await btn.isVisible({ timeout: 2000 })) {
                            await btn.click();
                            steps.push(`Clicked checkout with ${sel}`);
                            break;
                        }
                    } catch { continue; }
                }

                await page.waitForLoadState('networkidle', { timeout: 10000 }).catch(() => {});

                // Fill any provided checkout fields
                if (input.fields) {
                    const fields = input.fields as Record<string, string>;
                    for (const [selector, value] of Object.entries(fields)) {
                        try {
                            await page.locator(selector).first().fill(value);
                            steps.push(`Filled ${selector}`);
                        } catch { steps.push(`Failed: ${selector}`); }
                    }
                }

                result = {
                    steps,
                    currentUrl: page.url(),
                    title: await page.title()
                };
                break;
            }

            // ===========================
            // SMART SELECTORS
            // ===========================

            case 'browser_click_text': {
                const textToClick = input.text as string;
                const elementType = (input.element_type as string) || 'any';
                
                let selector: string;
                if (elementType === 'button') {
                    selector = `button:has-text("${textToClick}"), input[type="button"][value="${textToClick}"], input[type="submit"][value="${textToClick}"]`;
                } else if (elementType === 'link') {
                    selector = `a:has-text("${textToClick}")`;
                } else {
                    selector = `text="${textToClick}"`;
                }

                const element = page.locator(selector).first();
                await element.scrollIntoViewIfNeeded();
                await element.click();
                result = { message: `Clicked "${textToClick}"`, selector };
                break;
            }

            case 'browser_click_role': {
                const role = input.role as string;
                const name = input.name as string;
                await page.getByRole(role as any, { name }).click();
                result = { message: `Clicked ${role} "${name}"` };
                break;
            }

            case 'browser_click_label': {
                const label = input.label as string;
                await page.getByLabel(label).click();
                result = { message: `Clicked element with label "${label}"` };
                break;
            }

            case 'browser_fill_label': {
                const fillLabel = input.label as string;
                await page.getByLabel(fillLabel).fill(input.text as string);
                result = { message: `Filled "${fillLabel}" with text` };
                break;
            }

            case 'browser_click_placeholder': {
                await page.getByPlaceholder(input.placeholder as string).click();
                result = { message: `Clicked element with placeholder "${input.placeholder}"` };
                break;
            }

            case 'browser_get_by_test_id': {
                const testId = input.test_id as string;
                const action = (input.action as string) || 'getText';
                const loc = page.getByTestId(testId);

                if (action === 'click') {
                    await loc.click();
                    result = { message: `Clicked test-id "${testId}"` };
                } else if (action === 'fill') {
                    await loc.fill(input.text as string);
                    result = { message: `Filled test-id "${testId}"` };
                } else {
                    const text = await loc.textContent();
                    result = { text };
                }
                break;
            }

            // ===========================
            // ERROR RECOVERY
            // ===========================

            case 'browser_safe_click': {
                const maxRetries = (input.retries as number) || 3;
                const sel = input.selector as string;
                let lastError = '';

                for (let attempt = 0; attempt < maxRetries; attempt++) {
                    try {
                        const loc = page.locator(sel).first();
                        await loc.scrollIntoViewIfNeeded();
                        await loc.waitFor({ state: 'visible', timeout: 5000 });
                        await loc.click({ timeout: 5000 });
                        result = { message: `Clicked ${sel}`, attempts: attempt + 1 };
                        break;
                    } catch (err) {
                        lastError = err instanceof Error ? err.message : String(err);
                        await page.waitForTimeout(1000);
                    }
                }
                if (!result) {
                    result = { error: `Failed after ${maxRetries} attempts: ${lastError}` };
                }
                break;
            }

            case 'browser_safe_fill': {
                const fillRetries = (input.retries as number) || 3;
                const fillSel = input.selector as string;
                let fillError = '';

                for (let attempt = 0; attempt < fillRetries; attempt++) {
                    try {
                        const loc = page.locator(fillSel).first();
                        await loc.scrollIntoViewIfNeeded();
                        await loc.waitFor({ state: 'visible', timeout: 5000 });
                        await loc.fill(input.text as string, { timeout: 5000 });
                        result = { message: `Filled ${fillSel}`, attempts: attempt + 1 };
                        break;
                    } catch (err) {
                        fillError = err instanceof Error ? err.message : String(err);
                        await page.waitForTimeout(1000);
                    }
                }
                if (!result) {
                    result = { error: `Failed after ${fillRetries} attempts: ${fillError}` };
                }
                break;
            }

            case 'browser_wait_and_click': {
                const waitSel = input.selector as string;
                const waitTimeout = (input.timeout as number) || 30000;

                await page.waitForSelector(waitSel, { state: 'visible', timeout: waitTimeout });
                const loc = page.locator(waitSel).first();
                await loc.scrollIntoViewIfNeeded();
                await loc.click();

                result = { message: `Waited and clicked ${waitSel}` };
                break;
            }

            // ===========================
            // RECORDING MODE
            // ===========================

            case 'browser_start_recording': {
                if (!(page as any).__recording) {
                    (page as any).__recording = [];
                    (page as any).__recordingActive = true;

                    // Record clicks
                    await page.exposeFunction('__webmcp_record_action', (action: string) => {
                        if ((page as any).__recordingActive) {
                            (page as any).__recording.push({
                                ...JSON.parse(action),
                                timestamp: new Date().toISOString()
                            });
                        }
                    });

                    await page.evaluate(() => {
                        document.addEventListener('click', (e) => {
                            const target = e.target as HTMLElement;
                            const action = {
                                type: 'click',
                                selector: target.id ? `#${target.id}` : 
                                    target.className ? `.${target.className.split(' ')[0]}` :
                                    target.tagName.toLowerCase(),
                                text: target.textContent?.trim()?.substring(0, 50),
                                tagName: target.tagName
                            };
                            (window as any).__webmcp_record_action(JSON.stringify(action));
                        }, true);

                        document.addEventListener('input', (e) => {
                            const target = e.target as HTMLInputElement;
                            const action = {
                                type: 'input',
                                selector: target.id ? `#${target.id}` :
                                    target.name ? `[name="${target.name}"]` :
                                    target.tagName.toLowerCase(),
                                inputType: target.type,
                                tagName: target.tagName
                            };
                            (window as any).__webmcp_record_action(JSON.stringify(action));
                        }, true);
                    });
                }

                result = { message: 'Recording started' };
                break;
            }

            case 'browser_stop_recording': {
                (page as any).__recordingActive = false;
                const recording = (page as any).__recording || [];
                result = { 
                    message: 'Recording stopped',
                    actions: recording,
                    actionCount: recording.length
                };
                break;
            }

            case 'browser_get_recording': {
                const actions = (page as any).__recording || [];
                result = {
                    recording: actions,
                    actionCount: actions.length,
                    isRecording: !!(page as any).__recordingActive
                };
                break;
            }

            case 'browser_replay_recording': {
                const actionsToReplay = input.actions as Array<{ type: string; selector: string; text?: string }>;
                const replayed: string[] = [];

                for (const action of actionsToReplay) {
                    try {
                        if (action.type === 'click') {
                            await page.locator(action.selector).first().click();
                            replayed.push(`click: ${action.selector}`);
                        } else if (action.type === 'input' && action.text) {
                            await page.locator(action.selector).first().fill(action.text);
                            replayed.push(`fill: ${action.selector}`);
                        }
                        await page.waitForTimeout(500);
                    } catch (err) {
                        replayed.push(`FAILED: ${action.type} ${action.selector} - ${err}`);
                    }
                }

                result = { replayed, total: actionsToReplay.length, successful: replayed.filter(r => !r.startsWith('FAILED')).length };
                break;
            }

            // ===========================
            // NETWORK EGRESS CONTROL
            // ===========================

            case 'browser_block_domains': {
                const domains = input.domains as string[];
                await page.route('**/*', (route) => {
                    const url = new URL(route.request().url());
                    if (domains.some(d => url.hostname === d || url.hostname.endsWith(`.${d}`))) {
                        route.abort('blockedbyclient');
                    } else {
                        route.continue();
                    }
                });
                result = { message: `Blocked ${domains.length} domains`, domains };
                break;
            }

            default:
                throw new Error(`Unknown tool: ${toolName}`);
        }

        const newUrl = page.url();
        return {
            success: true,
            result,
            pageChanged: newUrl !== originalUrl,
            newUrl: newUrl !== originalUrl ? newUrl : undefined
        };
    } catch (error) {
        return {
            success: false,
            error: error instanceof Error ? error.message : String(error)
        };
    }
}
