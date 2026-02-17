import { BrowserContext, Page, Browser } from 'playwright';
import { v4 as uuidv4 } from 'uuid';

export interface ActionRecord {
    timestamp: string;
    toolName: string;
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
    url: string;
    createdAt: Date;
    expiresAt: Date;
    callCount: number;
    hasWebMCP: boolean;
    recording: ActionRecord[];
    recordingEnabled: boolean;
}

export class SessionStore {
    private sessions = new Map<string, Session>();
    private cleanupInterval: NodeJS.Timeout;
    private defaultRecording = process.env.SESSION_RECORDING === 'true';

    constructor() {
        // Clean up expired sessions every minute
        this.cleanupInterval = setInterval(() => this.cleanup(), 60000);
    }

    create(
        browser: Browser,
        context: BrowserContext,
        page: Page,
        url: string,
        ttlMinutes: number = 15
    ): Session {
        const id = uuidv4();
        const now = new Date();
        const session: Session = {
            id,
            browser,
            context,
            page,
            url,
            createdAt: now,
            expiresAt: new Date(now.getTime() + ttlMinutes * 60000),
            callCount: 0,
            hasWebMCP: false,
            recording: [],
            recordingEnabled: this.defaultRecording
        };
        this.sessions.set(id, session);
        return session;
    }

    get(id: string): Session | undefined {
        const session = this.sessions.get(id);
        if (session && session.expiresAt < new Date()) {
            this.close(id);
            return undefined;
        }
        return session;
    }

    async close(id: string): Promise<void> {
        const session = this.sessions.get(id);
        if (session) {
            await session.browser.close();
            this.sessions.delete(id);
        }
    }

    async closeAll(): Promise<void> {
        for (const [id] of this.sessions) {
            await this.close(id);
        }
    }

    getActiveCount(): number {
        return this.sessions.size;
    }

    recordAction(id: string, action: Omit<ActionRecord, 'timestamp'>): void {
        const session = this.sessions.get(id);
        if (session?.recordingEnabled) {
            session.recording.push({
                ...action,
                timestamp: new Date().toISOString()
            });
        }
    }

    getRecording(id: string): ActionRecord[] {
        return this.sessions.get(id)?.recording || [];
    }

    setRecording(id: string, enabled: boolean): void {
        const session = this.sessions.get(id);
        if (session) session.recordingEnabled = enabled;
    }

    private async cleanup(): Promise<void> {
        const now = new Date();
        for (const [id, session] of this.sessions) {
            if (session.expiresAt < now) {
                await this.close(id);
            }
        }
    }
}
