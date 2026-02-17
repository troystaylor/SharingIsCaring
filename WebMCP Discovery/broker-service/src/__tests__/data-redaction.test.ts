import { describe, it, expect } from 'vitest';
import { redactText, redactObject } from '../middleware/data-redaction';

describe('redactText', () => {
    it('masks SSN patterns', () => {
        const result = redactText('SSN is 123-45-6789');
        expect(result).toContain('***-**-****');
        expect(result).not.toContain('123-45-6789');
    });

    it('masks email addresses', () => {
        const result = redactText('Contact user@example.com for info');
        expect(result).toContain('***@***.***');
        expect(result).not.toContain('user@example.com');
    });

    it('masks bearer tokens', () => {
        const result = redactText('Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.test.sig');
        expect(result).toContain('[REDACTED]');
        expect(result).not.toContain('eyJhbGciOiJIUzI1NiJ9');
    });

    it('returns empty/null input unchanged', () => {
        expect(redactText('')).toBe('');
    });
});

describe('redactObject', () => {
    it('redacts fields by name', () => {
        const obj = { username: 'alice', password: 'secret123', role: 'admin' };
        const result = redactObject(obj) as Record<string, unknown>;
        expect(result.password).toBe('[REDACTED]');
        expect(result.username).toBe('alice');
        expect(result.role).toBe('admin');
    });

    it('redacts sensitive patterns in string values', () => {
        const obj = { info: 'My SSN is 123-45-6789' };
        const result = redactObject(obj) as Record<string, unknown>;
        expect(result.info).toContain('***-**-****');
    });

    it('handles nested objects', () => {
        const obj = { user: { name: 'Alice', token: 'abc123' } };
        const result = redactObject(obj) as Record<string, Record<string, unknown>>;
        expect(result.user.token).toBe('[REDACTED]');
        expect(result.user.name).toBe('Alice');
    });

    it('handles arrays', () => {
        const arr = [{ password: 'x' }, { name: 'Bob' }];
        const result = redactObject(arr) as Record<string, unknown>[];
        expect(result[0].password).toBe('[REDACTED]');
        expect(result[1].name).toBe('Bob');
    });

    it('passes through null and primitives', () => {
        expect(redactObject(null)).toBeNull();
        expect(redactObject(42)).toBe(42);
        expect(redactObject(true)).toBe(true);
    });
});
