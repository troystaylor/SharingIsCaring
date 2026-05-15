import { BrowserContext, Page, Browser } from 'playwright-core';
import type { RemoteBrowserSession } from './playwright-workspaces-client';
export interface ActionRecord {
    timestamp: string;
    action: string;
    input: Record<string, unknown>;
    success: boolean;
    durationMs: number;
    url: string;
    error?: string;
}
export interface Session {
    id: string;
    browser: Browser;
    context: BrowserContext;
    page: Page;
    remoteSession: RemoteBrowserSession;
    url: string;
    createdAt: Date;
    expiresAt: Date;
    actionCount: number;
    recording: ActionRecord[];
    recordingEnabled: boolean;
}
export declare class SessionStore {
    private sessions;
    private cleanupInterval;
    private defaultRecording;
    constructor();
    create(browser: Browser, context: BrowserContext, page: Page, remoteSession: RemoteBrowserSession, url: string, ttlMinutes?: number): Session;
    get(id: string): Session | undefined;
    close(id: string): Promise<void>;
    closeAll(): Promise<void>;
    getActiveCount(): number;
    recordAction(id: string, action: Omit<ActionRecord, 'timestamp'>): void;
    private cleanup;
}
//# sourceMappingURL=session-store.d.ts.map