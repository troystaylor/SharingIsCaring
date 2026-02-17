import { describe, it, expect, beforeEach, vi } from 'vitest';

beforeEach(() => {
    vi.resetModules();
});

describe('getRole', () => {
    it('returns admin for admin-prefixed keys', async () => {
        vi.stubEnv('RBAC_ENABLED', 'true');
        const { getRole } = await import('../middleware/rbac');
        expect(getRole('admin_abc123')).toBe('admin');
    });

    it('returns viewer for viewer-prefixed keys', async () => {
        vi.stubEnv('RBAC_ENABLED', 'true');
        const { getRole } = await import('../middleware/rbac');
        expect(getRole('viewer_xyz')).toBe('viewer');
    });

    it('defaults to user for unknown key prefixes', async () => {
        vi.stubEnv('RBAC_ENABLED', 'true');
        const { getRole } = await import('../middleware/rbac');
        expect(getRole('some_random_key')).toBe('user');
    });

    it('returns admin when RBAC is disabled', async () => {
        vi.stubEnv('RBAC_ENABLED', 'false');
        const { getRole } = await import('../middleware/rbac');
        expect(getRole('viewer_key')).toBe('admin');
    });
});

describe('isToolAllowed', () => {
    it('allows all tools for admin', async () => {
        vi.stubEnv('RBAC_ENABLED', 'true');
        const { isToolAllowed } = await import('../middleware/rbac');
        expect(isToolAllowed('admin', 'browser_evaluate')).toBe(true);
        expect(isToolAllowed('admin', 'browser_screenshot')).toBe(true);
    });

    it('blocks browser_evaluate for user role', async () => {
        vi.stubEnv('RBAC_ENABLED', 'true');
        const { isToolAllowed } = await import('../middleware/rbac');
        expect(isToolAllowed('user', 'browser_evaluate')).toBe(false);
    });

    it('allows only read-only tools for viewer', async () => {
        vi.stubEnv('RBAC_ENABLED', 'true');
        const { isToolAllowed } = await import('../middleware/rbac');
        expect(isToolAllowed('viewer', 'browser_screenshot')).toBe(true);
        expect(isToolAllowed('viewer', 'browser_get_text')).toBe(true);
        expect(isToolAllowed('viewer', 'browser_click')).toBe(false);
        expect(isToolAllowed('viewer', 'browser_navigate')).toBe(false);
    });
});
