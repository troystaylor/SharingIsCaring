/**
 * Playwright Workspaces Client
 *
 * Provisions remote Chromium browsers on Azure Playwright Workspaces via the
 * data-plane REST API. Returns a CDP WebSocket URL that playwright-core can
 * connect to with chromium.connectOverCDP().
 *
 * No local browser installation is needed — all browsers run in Azure.
 */

export interface PlaywrightWorkspacesConfig {
    serviceUrl: string;       // wss://<region>.api.playwright.microsoft.com/playwrightworkspaces/<id>/browsers
    accessToken: string;
}

export interface RemoteBrowserSession {
    sessionUrl: string;       // wss:// WebSocket URL for CDP connection
    region: string;
    workspaceId: string;
}

const URL_PATTERN = /^wss:\/\/(\w+)\.api\.playwright\.microsoft\.com\/playwrightworkspaces\/([^/]+)\/browsers$/;

function parseServiceUrl(url: string): { region: string; workspaceId: string } {
    const match = URL_PATTERN.exec(url);
    if (!match) {
        throw new Error(
            `Invalid PLAYWRIGHT_SERVICE_URL format: ${url}\n` +
            `Expected: wss://<region>.api.playwright.microsoft.com/playwrightworkspaces/<workspaceId>/browsers`
        );
    }
    return { region: match[1], workspaceId: match[2] };
}

/**
 * Provision a remote Chromium browser on Playwright Workspaces.
 * Cold start is 30-90s on first call; subsequent calls are faster.
 */
export async function provisionRemoteBrowser(
    config: PlaywrightWorkspacesConfig,
    os: string = 'linux'
): Promise<RemoteBrowserSession> {
    const { region, workspaceId } = parseServiceUrl(config.serviceUrl);

    const apiUrl =
        `https://${region}.api.playwright.microsoft.com` +
        `/playwrightworkspaces/${workspaceId}/browsers` +
        `?os=${os}&browser=chromium&playwrightVersion=cdp&shouldRedirect=false` +
        `&accessKey=${encodeURIComponent(config.accessToken)}`;

    // Generous timeout — cold starts can take up to 90s
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 120_000);

    try {
        const response = await fetch(apiUrl, {
            method: 'GET',
            headers: { 'Accept': 'application/json' },
            signal: controller.signal,
        });

        if (!response.ok) {
            const body = await response.text();
            throw new Error(`Playwright Workspaces provisioning failed (${response.status}): ${body}`);
        }

        const data = await response.json() as { sessionUrl?: string; wsEndpoint?: string };
        const sessionUrl = data.sessionUrl || data.wsEndpoint;

        if (!sessionUrl) {
            throw new Error('No sessionUrl in Playwright Workspaces response');
        }

        return { sessionUrl, region, workspaceId };
    } finally {
        clearTimeout(timeout);
    }
}

/**
 * Build config from environment variables.
 */
export function getConfigFromEnv(): PlaywrightWorkspacesConfig {
    const serviceUrl = process.env.PLAYWRIGHT_SERVICE_URL;
    const accessToken = process.env.PLAYWRIGHT_SERVICE_ACCESS_TOKEN;

    if (!serviceUrl) {
        throw new Error('PLAYWRIGHT_SERVICE_URL environment variable is required');
    }
    if (!accessToken) {
        throw new Error('PLAYWRIGHT_SERVICE_ACCESS_TOKEN environment variable is required');
    }

    return { serviceUrl, accessToken };
}
