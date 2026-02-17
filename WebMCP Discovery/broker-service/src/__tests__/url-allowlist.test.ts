import { describe, it, expect, beforeEach, vi } from 'vitest';

// Reset env before each test
beforeEach(() => {
    vi.resetModules();
});

describe('isUrlAllowed', () => {
    it('blocks localhost (SSRF protection)', async () => {
        const { isUrlAllowed } = await import('../middleware/url-allowlist');
        const result = isUrlAllowed('http://localhost:8080/admin');
        expect(result.allowed).toBe(false);
        expect(result.reason).toContain('internal address');
    });

    it('blocks 127.0.0.1', async () => {
        const { isUrlAllowed } = await import('../middleware/url-allowlist');
        const result = isUrlAllowed('http://127.0.0.1/secret');
        expect(result.allowed).toBe(false);
    });

    it('blocks Azure IMDS endpoint', async () => {
        const { isUrlAllowed } = await import('../middleware/url-allowlist');
        const result = isUrlAllowed('http://169.254.169.254/metadata/identity');
        expect(result.allowed).toBe(false);
    });

    it('blocks non-http protocols', async () => {
        const { isUrlAllowed } = await import('../middleware/url-allowlist');
        const result = isUrlAllowed('file:///etc/passwd');
        expect(result.allowed).toBe(false);
        expect(result.reason).toContain('Protocol');
    });

    it('allows public HTTPS URLs when no allowlist set', async () => {
        const { isUrlAllowed } = await import('../middleware/url-allowlist');
        const result = isUrlAllowed('https://example.com/page');
        expect(result.allowed).toBe(true);
    });

    it('rejects invalid URLs', async () => {
        const { isUrlAllowed } = await import('../middleware/url-allowlist');
        const result = isUrlAllowed('not-a-url');
        expect(result.allowed).toBe(false);
        expect(result.reason).toBe('Invalid URL');
    });
});
