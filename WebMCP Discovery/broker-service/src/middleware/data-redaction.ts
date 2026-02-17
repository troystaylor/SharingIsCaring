/**
 * Data Redaction Service
 * Masks sensitive data in screenshots, logs, and tool results.
 * 
 * Set REDACTION_PATTERNS as comma-separated regex patterns
 * Set REDACTION_FIELDS as comma-separated field names to mask
 */

const customPatterns = (process.env.REDACTION_PATTERNS || '')
    .split(',')
    .filter(Boolean)
    .map(p => new RegExp(p, 'gi'));

const redactFields = new Set(
    (process.env.REDACTION_FIELDS || 'password,ssn,credit_card,api_key,secret,token,authorization')
        .split(',')
        .map(f => f.trim().toLowerCase())
);

// Built-in patterns for common sensitive data
const BUILTIN_PATTERNS: Array<{ name: string; regex: RegExp; replacement: string }> = [
    {
        name: 'credit_card',
        regex: /\b(?:\d{4}[-\s]?){3}\d{4}\b/g,
        replacement: '****-****-****-$$LAST4$$'
    },
    {
        name: 'ssn',
        regex: /\b\d{3}-\d{2}-\d{4}\b/g,
        replacement: '***-**-****'
    },
    {
        name: 'email',
        regex: /\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b/g,
        replacement: '***@***.***'
    },
    {
        name: 'phone',
        regex: /\b(?:\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b/g,
        replacement: '(***) ***-****'
    },
    {
        name: 'bearer_token',
        regex: /Bearer\s+[A-Za-z0-9\-._~+\/]+=*/gi,
        replacement: 'Bearer [REDACTED]'
    },
    {
        name: 'api_key_value',
        regex: /(?:api[_-]?key|apikey|secret|token|password|authorization)\s*[:=]\s*["']?[^\s"',}{]+/gi,
        replacement: '[REDACTED_CREDENTIAL]'
    }
];

export function redactText(text: string): string {
    if (!text) return text;

    let result = text;

    // Apply built-in patterns
    for (const pattern of BUILTIN_PATTERNS) {
        result = result.replace(pattern.regex, pattern.replacement);
    }

    // Apply custom patterns
    for (const regex of customPatterns) {
        result = result.replace(regex, '[REDACTED]');
    }

    return result;
}

export function redactObject(obj: unknown): unknown {
    if (obj === null || obj === undefined) return obj;
    if (typeof obj === 'string') return redactText(obj);
    if (typeof obj !== 'object') return obj;

    if (Array.isArray(obj)) {
        return obj.map(item => redactObject(item));
    }

    const result: Record<string, unknown> = {};
    for (const [key, value] of Object.entries(obj as Record<string, unknown>)) {
        if (redactFields.has(key.toLowerCase())) {
            result[key] = '[REDACTED]';
        } else if (typeof value === 'string') {
            result[key] = redactText(value);
        } else if (typeof value === 'object') {
            result[key] = redactObject(value);
        } else {
            result[key] = value;
        }
    }
    return result;
}

/**
 * Redact sensitive regions from a screenshot buffer.
 * Injects CSS to hide sensitive fields before capture.
 * Returns the CSS to inject before taking screenshots.
 */
export function getRedactionCSS(): string {
    return `
        input[type="password"],
        input[type="tel"],
        input[name*="ssn"],
        input[name*="credit"],
        input[name*="card"],
        input[name*="cvv"],
        input[name*="social"],
        [data-sensitive],
        [data-redact] {
            filter: blur(8px) !important;
        }
    `;
}
