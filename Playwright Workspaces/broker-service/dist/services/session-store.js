"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.SessionStore = void 0;
const uuid_1 = require("uuid");
class SessionStore {
    sessions = new Map();
    cleanupInterval;
    defaultRecording = process.env.SESSION_RECORDING === 'true';
    constructor() {
        this.cleanupInterval = setInterval(() => this.cleanup(), 60_000);
    }
    create(browser, context, page, remoteSession, url, ttlMinutes = 15) {
        const id = (0, uuid_1.v4)();
        const now = new Date();
        const session = {
            id,
            browser,
            context,
            page,
            remoteSession,
            url,
            createdAt: now,
            expiresAt: new Date(now.getTime() + ttlMinutes * 60_000),
            actionCount: 0,
            recording: [],
            recordingEnabled: this.defaultRecording,
        };
        this.sessions.set(id, session);
        return session;
    }
    get(id) {
        const session = this.sessions.get(id);
        if (session && session.expiresAt < new Date()) {
            this.close(id);
            return undefined;
        }
        return session;
    }
    async close(id) {
        const session = this.sessions.get(id);
        if (session) {
            try {
                await session.browser.close();
            }
            catch {
                // Remote browser may already be gone
            }
            this.sessions.delete(id);
        }
    }
    async closeAll() {
        for (const [id] of this.sessions) {
            await this.close(id);
        }
    }
    getActiveCount() {
        return this.sessions.size;
    }
    recordAction(id, action) {
        const session = this.sessions.get(id);
        if (session?.recordingEnabled) {
            session.recording.push({
                ...action,
                timestamp: new Date().toISOString(),
            });
        }
        if (session) {
            session.actionCount++;
        }
    }
    async cleanup() {
        const now = new Date();
        for (const [id, session] of this.sessions) {
            if (session.expiresAt < now) {
                await this.close(id);
            }
        }
    }
}
exports.SessionStore = SessionStore;
//# sourceMappingURL=session-store.js.map