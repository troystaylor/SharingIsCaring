import { Request, Response, NextFunction } from 'express';

/**
 * RBAC Middleware
 * Provides role-based access control using API key prefixes.
 * 
 * API Key format: {role}_{key} (e.g., admin_abc123, viewer_xyz789)
 * 
 * Roles:
 *   admin  - Full access: all tools, all domains, recording, config
 *   user   - Standard access: all tools, allowed domains
 *   viewer - Read-only: screenshots, getText, getPage, no navigation to new sites
 * 
 * Set RBAC_ENABLED=true to enable (default: false)
 * Set API_KEYS as JSON: {"admin_key1":"admin","user_key2":"user","viewer_key3":"viewer"}
 */

export type Role = 'admin' | 'user' | 'viewer';

interface RolePermissions {
    canNavigate: boolean;
    canExecuteTools: boolean;
    canCreateSessions: boolean;
    canManageRecording: boolean;
    canAccessConfig: boolean;
    allowedToolPatterns: string[];
    blockedToolPatterns: string[];
}

const ROLE_PERMISSIONS: Record<Role, RolePermissions> = {
    admin: {
        canNavigate: true,
        canExecuteTools: true,
        canCreateSessions: true,
        canManageRecording: true,
        canAccessConfig: true,
        allowedToolPatterns: ['*'],
        blockedToolPatterns: []
    },
    user: {
        canNavigate: true,
        canExecuteTools: true,
        canCreateSessions: true,
        canManageRecording: false,
        canAccessConfig: false,
        allowedToolPatterns: ['*'],
        blockedToolPatterns: ['browser_evaluate', 'browser_start_tracing', 'browser_stop_tracing']
    },
    viewer: {
        canNavigate: false,
        canExecuteTools: true,
        canCreateSessions: true,
        canManageRecording: false,
        canAccessConfig: false,
        allowedToolPatterns: [
            'browser_screenshot', 'browser_get_text', 'browser_get_page_content',
            'browser_get_attribute', 'browser_extract_table', 'browser_get_all_links',
            'browser_get_computed_style', 'browser_get_metrics', 'browser_get_timing',
            'browser_list_tabs', 'browser_list_frames', 'browser_get_console_logs',
            'browser_get_media_state', 'browser_get_bounding_box', 'browser_count_elements',
            'browser_is_visible', 'browser_is_enabled', 'browser_get_cookies',
            'browser_get_local_storage', 'browser_get_session_storage',
            'discover_tools', 'get_session', 'list_session_tools'
        ],
        blockedToolPatterns: []
    }
};

const rbacEnabled = process.env.RBAC_ENABLED === 'true';
let apiKeyRoles: Record<string, Role> = {};

try {
    apiKeyRoles = JSON.parse(process.env.API_KEYS || '{}');
} catch {
    // If not valid JSON, use simple API_KEY env for backward compatibility
}

export function getRole(apiKey: string): Role {
    if (!rbacEnabled) return 'admin';
    
    // Check explicit key-to-role mapping
    if (apiKeyRoles[apiKey]) return apiKeyRoles[apiKey];

    // Check key prefix
    if (apiKey.startsWith('admin_')) return 'admin';
    if (apiKey.startsWith('viewer_')) return 'viewer';
    
    return 'user'; // Default role
}

export function isToolAllowed(role: Role, toolName: string): boolean {
    const perms = ROLE_PERMISSIONS[role];
    
    // Check blocked first
    if (perms.blockedToolPatterns.includes(toolName)) return false;
    
    // Check allowed
    if (perms.allowedToolPatterns.includes('*')) return true;
    return perms.allowedToolPatterns.includes(toolName);
}

export function rbacMiddleware(req: Request, res: Response, next: NextFunction): void {
    if (!rbacEnabled || req.path === '/health') {
        next();
        return;
    }

    const apiKey = req.headers['x-api-key'] as string || '';
    const role = getRole(apiKey);
    const perms = ROLE_PERMISSIONS[role];

    // Attach role to request
    (req as any).role = role;
    (req as any).permissions = perms;

    // Check navigation permission
    if (!perms.canNavigate) {
        const isNavigation = req.body?.url && 
            (req.path.includes('/navigate') || req.path.includes('/sessions') && req.method === 'POST');
        if (isNavigation) {
            res.status(403).json({ error: 'Insufficient permissions: navigation not allowed for viewer role' });
            return;
        }
    }

    // Check session creation
    if (!perms.canCreateSessions && req.path.endsWith('/sessions') && req.method === 'POST') {
        res.status(403).json({ error: 'Insufficient permissions: cannot create sessions' });
        return;
    }

    // Check tool execution
    const toolMatch = req.path.match(/\/tools\/([^/]+)\/call/);
    if (toolMatch) {
        const toolName = toolMatch[1];
        if (!isToolAllowed(role, toolName)) {
            res.status(403).json({ error: `Insufficient permissions: tool '${toolName}' not allowed for ${role} role` });
            return;
        }
    }

    // Check recording/config access
    if (req.path.includes('/recording') && !perms.canManageRecording) {
        res.status(403).json({ error: 'Insufficient permissions: recording not allowed' });
        return;
    }

    next();
}
