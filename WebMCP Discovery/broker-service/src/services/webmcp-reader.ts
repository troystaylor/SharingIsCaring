import { Page } from 'playwright';

export interface WebMCPTool {
    name: string;
    description: string;
    inputSchema: Record<string, unknown>;
    outputSchema?: Record<string, unknown>;
    category?: string;
    requiresAuth?: boolean;
}

export interface WebMCPDiscoveryResult {
    hasWebMCP: boolean;
    tools: WebMCPTool[];
    serverInfo?: {
        name: string;
        version: string;
    };
}

// Playwright fallback tools when WebMCP is not available
// Based on https://playwright.dev/ capabilities
export const PLAYWRIGHT_FALLBACK_TOOLS: WebMCPTool[] = [
    // === NAVIGATION ===
    {
        name: 'browser_navigate',
        description: 'Navigate to a URL',
        inputSchema: {
            type: 'object',
            properties: {
                url: { type: 'string', description: 'URL to navigate to' },
                wait_until: { type: 'string', enum: ['load', 'domcontentloaded', 'networkidle'], description: 'When to consider navigation complete (default: networkidle)' },
                timeout: { type: 'number', description: 'Timeout in ms (default: 30000)' }
            },
            required: ['url']
        },
        category: 'navigation'
    },
    {
        name: 'browser_go_back',
        description: 'Navigate back in browser history',
        inputSchema: { type: 'object', properties: {} },
        category: 'navigation'
    },
    {
        name: 'browser_go_forward',
        description: 'Navigate forward in browser history',
        inputSchema: { type: 'object', properties: {} },
        category: 'navigation'
    },
    {
        name: 'browser_reload',
        description: 'Reload the current page',
        inputSchema: { type: 'object', properties: {} },
        category: 'navigation'
    },

    // === INTERACTION - Basic ===
    {
        name: 'browser_click',
        description: 'Click an element on the page',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of element to click' },
                button: { type: 'string', enum: ['left', 'right', 'middle'], description: 'Mouse button (default: left)' },
                click_count: { type: 'number', description: 'Number of clicks (default: 1)' },
                timeout: { type: 'number', description: 'Timeout in ms' }
            },
            required: ['selector']
        },
        category: 'interaction'
    },
    {
        name: 'browser_dblclick',
        description: 'Double-click an element',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of element' }
            },
            required: ['selector']
        },
        category: 'interaction'
    },
    {
        name: 'browser_hover',
        description: 'Hover over an element',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of element' }
            },
            required: ['selector']
        },
        category: 'interaction'
    },
    {
        name: 'browser_type',
        description: 'Type text into an input field',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of input element' },
                text: { type: 'string', description: 'Text to type' },
                submit: { type: 'boolean', description: 'Press Enter after typing' }
            },
            required: ['selector', 'text']
        },
        category: 'interaction'
    },
    {
        name: 'browser_press_key',
        description: 'Press a keyboard key (Enter, Tab, Escape, ArrowDown, etc.)',
        inputSchema: {
            type: 'object',
            properties: {
                key: { type: 'string', description: 'Key to press (e.g., Enter, Tab, Escape, ArrowDown, Control+A)' },
                selector: { type: 'string', description: 'Optional element to focus first' }
            },
            required: ['key']
        },
        category: 'interaction'
    },
    {
        name: 'browser_select',
        description: 'Select an option from a dropdown',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of select element' },
                value: { type: 'string', description: 'Value to select' }
            },
            required: ['selector', 'value']
        },
        category: 'interaction'
    },
    {
        name: 'browser_check',
        description: 'Check a checkbox or radio button',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of checkbox/radio' }
            },
            required: ['selector']
        },
        category: 'interaction'
    },
    {
        name: 'browser_uncheck',
        description: 'Uncheck a checkbox',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of checkbox' }
            },
            required: ['selector']
        },
        category: 'interaction'
    },

    // === INTERACTION - Locators (Playwright recommended) ===
    {
        name: 'browser_click_text',
        description: 'Click an element by its visible text content',
        inputSchema: {
            type: 'object',
            properties: {
                text: { type: 'string', description: 'Text to find and click' },
                exact: { type: 'boolean', description: 'Require exact match (default: false)' }
            },
            required: ['text']
        },
        category: 'interaction'
    },
    {
        name: 'browser_click_role',
        description: 'Click an element by its ARIA role (button, link, textbox, etc.)',
        inputSchema: {
            type: 'object',
            properties: {
                role: { type: 'string', description: 'ARIA role (button, link, textbox, checkbox, menuitem, etc.)' },
                name: { type: 'string', description: 'Accessible name of the element' },
                exact: { type: 'boolean', description: 'Require exact name match' }
            },
            required: ['role']
        },
        category: 'interaction'
    },
    {
        name: 'browser_click_label',
        description: 'Click a form field by its label text',
        inputSchema: {
            type: 'object',
            properties: {
                label: { type: 'string', description: 'Label text' },
                exact: { type: 'boolean', description: 'Require exact match' }
            },
            required: ['label']
        },
        category: 'interaction'
    },
    {
        name: 'browser_fill_label',
        description: 'Fill a form field by its label text',
        inputSchema: {
            type: 'object',
            properties: {
                label: { type: 'string', description: 'Label text' },
                text: { type: 'string', description: 'Text to fill' }
            },
            required: ['label', 'text']
        },
        category: 'interaction'
    },
    {
        name: 'browser_fill_placeholder',
        description: 'Fill an input by its placeholder text',
        inputSchema: {
            type: 'object',
            properties: {
                placeholder: { type: 'string', description: 'Placeholder text' },
                text: { type: 'string', description: 'Text to fill' }
            },
            required: ['placeholder', 'text']
        },
        category: 'interaction'
    },

    // === FORM HANDLING ===
    {
        name: 'browser_fill_form',
        description: 'Fill multiple form fields at once',
        inputSchema: {
            type: 'object',
            properties: {
                fields: { type: 'object', description: 'Object mapping selectors to values: { "#email": "user@example.com", "#password": "secret" }' },
                submit_selector: { type: 'string', description: 'Optional selector of submit button to click after filling' }
            },
            required: ['fields']
        },
        category: 'forms'
    },
    {
        name: 'browser_upload_file',
        description: 'Upload a file to a file input',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of file input' },
                content_base64: { type: 'string', description: 'Base64-encoded file content' },
                filename: { type: 'string', description: 'Filename for the upload' }
            },
            required: ['selector']
        },
        category: 'forms'
    },

    // === CAPTURE & EXTRACTION ===
    {
        name: 'browser_screenshot',
        description: 'Take a screenshot of the current page',
        inputSchema: {
            type: 'object',
            properties: {
                full_page: { type: 'boolean', description: 'Capture full scrollable page' },
                format: { type: 'string', enum: ['png', 'jpeg'], description: 'Image format (default: png)' },
                quality: { type: 'number', description: 'JPEG quality 0-100 (default: 80)' }
            }
        },
        category: 'capture'
    },
    {
        name: 'browser_screenshot_element',
        description: 'Take a screenshot of a specific element',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of element to capture' }
            },
            required: ['selector']
        },
        category: 'capture'
    },
    {
        name: 'browser_pdf',
        description: 'Generate a PDF of the current page',
        inputSchema: {
            type: 'object',
            properties: {
                format: { type: 'string', enum: ['A4', 'Letter'], description: 'Paper format (default: A4)' }
            }
        },
        category: 'capture'
    },
    {
        name: 'browser_get_text',
        description: 'Get text content from an element or entire page',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector (if omitted, gets all page text)' }
            }
        },
        category: 'extraction'
    },
    {
        name: 'browser_get_inner_html',
        description: 'Get the inner HTML of an element',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of element' }
            },
            required: ['selector']
        },
        category: 'extraction'
    },
    {
        name: 'browser_get_attribute',
        description: 'Get an attribute value from an element',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of element' },
                attribute: { type: 'string', description: 'Attribute name' }
            },
            required: ['selector', 'attribute']
        },
        category: 'extraction'
    },
    {
        name: 'browser_get_all_text',
        description: 'Get text from all matching elements',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector matching multiple elements' }
            },
            required: ['selector']
        },
        category: 'extraction'
    },
    {
        name: 'browser_get_page_content',
        description: 'Get full page HTML, URL, and title',
        inputSchema: { type: 'object', properties: {} },
        category: 'extraction'
    },

    // === TABLE EXTRACTION ===
    {
        name: 'browser_extract_table',
        description: 'Extract data from an HTML table as structured JSON',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of table element' }
            },
            required: ['selector']
        },
        category: 'extraction'
    },
    {
        name: 'browser_extract_links',
        description: 'Extract all links from the page or a container',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of container (optional, defaults to entire page)' }
            }
        },
        category: 'extraction'
    },

    // === WAITING ===
    {
        name: 'browser_wait_for_selector',
        description: 'Wait for an element to appear on the page',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector to wait for' },
                state: { type: 'string', enum: ['attached', 'visible', 'hidden'], description: 'State to wait for (default: visible)' },
                timeout: { type: 'number', description: 'Max wait time in ms (default: 30000)' }
            },
            required: ['selector']
        },
        category: 'utility'
    },
    {
        name: 'browser_wait_for_text',
        description: 'Wait for specific text to appear on the page',
        inputSchema: {
            type: 'object',
            properties: {
                text: { type: 'string', description: 'Text to wait for' },
                timeout: { type: 'number', description: 'Max wait time in ms' }
            },
            required: ['text']
        },
        category: 'utility'
    },
    {
        name: 'browser_wait_for_url',
        description: 'Wait for the URL to match a pattern',
        inputSchema: {
            type: 'object',
            properties: {
                url: { type: 'string', description: 'URL or URL pattern to wait for' },
                timeout: { type: 'number', description: 'Max wait time in ms' }
            },
            required: ['url']
        },
        category: 'utility'
    },
    {
        name: 'browser_wait_for_load',
        description: 'Wait for a specific load state',
        inputSchema: {
            type: 'object',
            properties: {
                state: { type: 'string', enum: ['load', 'domcontentloaded', 'networkidle'], description: 'Load state to wait for' },
                timeout: { type: 'number', description: 'Max wait time in ms' }
            }
        },
        category: 'utility'
    },
    {
        name: 'browser_wait_for_response',
        description: 'Wait for a network response matching a URL pattern',
        inputSchema: {
            type: 'object',
            properties: {
                url_pattern: { type: 'string', description: 'URL pattern to match (partial match)' },
                timeout: { type: 'number', description: 'Max wait time in ms' }
            },
            required: ['url_pattern']
        },
        category: 'utility'
    },

    // === SCROLLING ===
    {
        name: 'browser_scroll',
        description: 'Scroll the page or an element',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector (optional, scrolls page if omitted)' },
                direction: { type: 'string', enum: ['up', 'down', 'left', 'right'], description: 'Scroll direction' },
                amount: { type: 'number', description: 'Pixels to scroll (default: 300)' }
            },
            required: ['direction']
        },
        category: 'interaction'
    },
    {
        name: 'browser_scroll_to_element',
        description: 'Scroll an element into view',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of element to scroll to' }
            },
            required: ['selector']
        },
        category: 'interaction'
    },
    {
        name: 'browser_scroll_to_bottom',
        description: 'Scroll to the bottom of the page',
        inputSchema: { type: 'object', properties: {} },
        category: 'interaction'
    },
    {
        name: 'browser_scroll_to_top',
        description: 'Scroll to the top of the page',
        inputSchema: { type: 'object', properties: {} },
        category: 'interaction'
    },

    // === JAVASCRIPT EXECUTION ===
    {
        name: 'browser_evaluate',
        description: 'Execute JavaScript in the page context and return the result',
        inputSchema: {
            type: 'object',
            properties: {
                script: { type: 'string', description: 'JavaScript code to execute' }
            },
            required: ['script']
        },
        category: 'advanced'
    },
    {
        name: 'browser_evaluate_on_element',
        description: 'Execute JavaScript on a specific element',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of element' },
                script: { type: 'string', description: 'JavaScript code (element available as "el")' }
            },
            required: ['selector', 'script']
        },
        category: 'advanced'
    },

    // === DIALOGS ===
    {
        name: 'browser_handle_dialog',
        description: 'Set up handler for the next JavaScript dialog (alert, confirm, prompt)',
        inputSchema: {
            type: 'object',
            properties: {
                accept: { type: 'boolean', description: 'Accept or dismiss the dialog' },
                prompt_text: { type: 'string', description: 'Text to enter for prompt dialogs' }
            },
            required: ['accept']
        },
        category: 'advanced'
    },

    // === DOWNLOADS ===
    {
        name: 'browser_download',
        description: 'Click a download link and get the file content',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of download link/button' }
            },
            required: ['selector']
        },
        category: 'advanced'
    },

    // === COOKIES ===
    {
        name: 'browser_get_cookies',
        description: 'Get all cookies or cookies for a specific URL',
        inputSchema: {
            type: 'object',
            properties: {
                url: { type: 'string', description: 'Filter cookies by URL (optional)' }
            }
        },
        category: 'advanced'
    },
    {
        name: 'browser_set_cookies',
        description: 'Set cookies in the browser',
        inputSchema: {
            type: 'object',
            properties: {
                cookies: { type: 'array', description: 'Array of cookie objects with name, value, domain, path', items: { type: 'object' } }
            },
            required: ['cookies']
        },
        category: 'advanced'
    },
    {
        name: 'browser_clear_cookies',
        description: 'Clear all cookies',
        inputSchema: { type: 'object', properties: {} },
        category: 'advanced'
    },

    // === ACCESSIBILITY ===
    {
        name: 'browser_get_accessibility_tree',
        description: 'Get the accessibility tree of the page (useful for understanding page structure)',
        inputSchema: {
            type: 'object',
            properties: {
                interesting_only: { type: 'boolean', description: 'Only return interesting nodes (default: true)' }
            }
        },
        category: 'advanced'
    },

    // === ELEMENT STATE ===
    {
        name: 'browser_is_visible',
        description: 'Check if an element is visible',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of element' }
            },
            required: ['selector']
        },
        category: 'utility'
    },
    {
        name: 'browser_is_enabled',
        description: 'Check if an element is enabled (not disabled)',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of element' }
            },
            required: ['selector']
        },
        category: 'utility'
    },
    {
        name: 'browser_is_checked',
        description: 'Check if a checkbox/radio is checked',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of element' }
            },
            required: ['selector']
        },
        category: 'utility'
    },
    {
        name: 'browser_count_elements',
        description: 'Count elements matching a selector',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector' }
            },
            required: ['selector']
        },
        category: 'utility'
    },
    {
        name: 'browser_get_bounding_box',
        description: 'Get the position and size of an element',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of element' }
            },
            required: ['selector']
        },
        category: 'utility'
    },

    // === NETWORK ===
    {
        name: 'browser_intercept_requests',
        description: 'Set up request interception with optional URL pattern filter. Returns intercepted requests.',
        inputSchema: {
            type: 'object',
            properties: {
                url_pattern: { type: 'string', description: 'URL pattern to intercept (glob or regex)' },
                action: { type: 'string', enum: ['log', 'block', 'modify'], description: 'What to do with matched requests (default: log)' },
                modify_headers: { type: 'object', description: 'Headers to add/modify on requests' }
            }
        },
        category: 'network'
    },
    {
        name: 'browser_mock_response',
        description: 'Mock API responses for a URL pattern',
        inputSchema: {
            type: 'object',
            properties: {
                url_pattern: { type: 'string', description: 'URL pattern to mock' },
                status: { type: 'number', description: 'HTTP status code (default: 200)' },
                body: { type: 'string', description: 'Response body (JSON string or text)' },
                content_type: { type: 'string', description: 'Content-Type header (default: application/json)' },
                headers: { type: 'object', description: 'Additional response headers' }
            },
            required: ['url_pattern', 'body']
        },
        category: 'network'
    },
    {
        name: 'browser_get_network_log',
        description: 'Get all network requests made since session started or last clear',
        inputSchema: {
            type: 'object',
            properties: {
                url_filter: { type: 'string', description: 'Filter by URL pattern (optional)' },
                include_response_body: { type: 'boolean', description: 'Include response bodies (may be large)' }
            }
        },
        category: 'network'
    },
    {
        name: 'browser_clear_network_log',
        description: 'Clear the network request log',
        inputSchema: { type: 'object', properties: {} },
        category: 'network'
    },
    {
        name: 'browser_wait_for_request',
        description: 'Wait for a specific network request',
        inputSchema: {
            type: 'object',
            properties: {
                url_pattern: { type: 'string', description: 'URL pattern to wait for' },
                method: { type: 'string', description: 'HTTP method filter (GET, POST, etc.)' },
                timeout: { type: 'number', description: 'Timeout in ms' }
            },
            required: ['url_pattern']
        },
        category: 'network'
    },

    // === DEVICE EMULATION ===
    {
        name: 'browser_set_viewport',
        description: 'Set the browser viewport size',
        inputSchema: {
            type: 'object',
            properties: {
                width: { type: 'number', description: 'Viewport width in pixels' },
                height: { type: 'number', description: 'Viewport height in pixels' },
                device_scale_factor: { type: 'number', description: 'Device pixel ratio (default: 1)' },
                is_mobile: { type: 'boolean', description: 'Enable mobile mode' },
                has_touch: { type: 'boolean', description: 'Enable touch events' }
            },
            required: ['width', 'height']
        },
        category: 'emulation'
    },
    {
        name: 'browser_emulate_device',
        description: 'Emulate a specific device (iPhone, Pixel, iPad, etc.)',
        inputSchema: {
            type: 'object',
            properties: {
                device: { type: 'string', description: 'Device name: iPhone 14, iPhone 14 Pro Max, Pixel 7, iPad Pro 11, Galaxy S23, etc.' }
            },
            required: ['device']
        },
        category: 'emulation'
    },
    {
        name: 'browser_set_geolocation',
        description: 'Set the geolocation for the browser',
        inputSchema: {
            type: 'object',
            properties: {
                latitude: { type: 'number', description: 'Latitude (-90 to 90)' },
                longitude: { type: 'number', description: 'Longitude (-180 to 180)' },
                accuracy: { type: 'number', description: 'Accuracy in meters (default: 0)' }
            },
            required: ['latitude', 'longitude']
        },
        category: 'emulation'
    },
    {
        name: 'browser_set_timezone',
        description: 'Set the timezone for the browser',
        inputSchema: {
            type: 'object',
            properties: {
                timezone: { type: 'string', description: 'Timezone ID (e.g., America/New_York, Europe/London, Asia/Tokyo)' }
            },
            required: ['timezone']
        },
        category: 'emulation'
    },
    {
        name: 'browser_set_locale',
        description: 'Set the locale for the browser',
        inputSchema: {
            type: 'object',
            properties: {
                locale: { type: 'string', description: 'Locale (e.g., en-US, fr-FR, de-DE, ja-JP)' }
            },
            required: ['locale']
        },
        category: 'emulation'
    },
    {
        name: 'browser_set_offline',
        description: 'Simulate offline mode',
        inputSchema: {
            type: 'object',
            properties: {
                offline: { type: 'boolean', description: 'Enable offline mode' }
            },
            required: ['offline']
        },
        category: 'emulation'
    },
    {
        name: 'browser_throttle_network',
        description: 'Throttle network speed (simulate slow connections)',
        inputSchema: {
            type: 'object',
            properties: {
                preset: { type: 'string', enum: ['slow-3g', 'fast-3g', '4g', 'wifi', 'offline'], description: 'Network speed preset' },
                download_kbps: { type: 'number', description: 'Custom download speed in Kbps' },
                upload_kbps: { type: 'number', description: 'Custom upload speed in Kbps' },
                latency_ms: { type: 'number', description: 'Custom latency in ms' }
            }
        },
        category: 'emulation'
    },

    // === STORAGE ===
    {
        name: 'browser_get_local_storage',
        description: 'Get all localStorage items or a specific key',
        inputSchema: {
            type: 'object',
            properties: {
                key: { type: 'string', description: 'Specific key to get (optional, returns all if omitted)' }
            }
        },
        category: 'storage'
    },
    {
        name: 'browser_set_local_storage',
        description: 'Set localStorage items',
        inputSchema: {
            type: 'object',
            properties: {
                items: { type: 'object', description: 'Key-value pairs to set' }
            },
            required: ['items']
        },
        category: 'storage'
    },
    {
        name: 'browser_get_session_storage',
        description: 'Get all sessionStorage items or a specific key',
        inputSchema: {
            type: 'object',
            properties: {
                key: { type: 'string', description: 'Specific key to get (optional)' }
            }
        },
        category: 'storage'
    },
    {
        name: 'browser_set_session_storage',
        description: 'Set sessionStorage items',
        inputSchema: {
            type: 'object',
            properties: {
                items: { type: 'object', description: 'Key-value pairs to set' }
            },
            required: ['items']
        },
        category: 'storage'
    },
    {
        name: 'browser_clear_storage',
        description: 'Clear localStorage, sessionStorage, or both',
        inputSchema: {
            type: 'object',
            properties: {
                type: { type: 'string', enum: ['local', 'session', 'both'], description: 'Which storage to clear (default: both)' }
            }
        },
        category: 'storage'
    },
    {
        name: 'browser_get_indexed_db',
        description: 'Get data from IndexedDB',
        inputSchema: {
            type: 'object',
            properties: {
                database: { type: 'string', description: 'Database name' },
                store: { type: 'string', description: 'Object store name' },
                key: { type: 'string', description: 'Specific key (optional, returns all if omitted)' }
            },
            required: ['database', 'store']
        },
        category: 'storage'
    },

    // === MULTI-TAB ===
    {
        name: 'browser_new_tab',
        description: 'Open a new browser tab',
        inputSchema: {
            type: 'object',
            properties: {
                url: { type: 'string', description: 'URL to open in new tab (optional)' }
            }
        },
        category: 'tabs'
    },
    {
        name: 'browser_list_tabs',
        description: 'List all open tabs in the session',
        inputSchema: { type: 'object', properties: {} },
        category: 'tabs'
    },
    {
        name: 'browser_switch_tab',
        description: 'Switch to a different tab',
        inputSchema: {
            type: 'object',
            properties: {
                index: { type: 'number', description: 'Tab index (0-based)' },
                url_pattern: { type: 'string', description: 'Switch to tab matching URL pattern' }
            }
        },
        category: 'tabs'
    },
    {
        name: 'browser_close_tab',
        description: 'Close a tab',
        inputSchema: {
            type: 'object',
            properties: {
                index: { type: 'number', description: 'Tab index to close (default: current tab)' }
            }
        },
        category: 'tabs'
    },
    {
        name: 'browser_wait_for_popup',
        description: 'Wait for a popup window to open (OAuth flows, etc.)',
        inputSchema: {
            type: 'object',
            properties: {
                timeout: { type: 'number', description: 'Timeout in ms' }
            }
        },
        category: 'tabs'
    },

    // === CONSOLE & ERRORS ===
    {
        name: 'browser_get_console_logs',
        description: 'Get browser console logs',
        inputSchema: {
            type: 'object',
            properties: {
                level: { type: 'string', enum: ['log', 'info', 'warn', 'error', 'all'], description: 'Filter by log level (default: all)' },
                clear: { type: 'boolean', description: 'Clear logs after retrieving' }
            }
        },
        category: 'debugging'
    },
    {
        name: 'browser_get_js_errors',
        description: 'Get JavaScript errors that occurred on the page',
        inputSchema: {
            type: 'object',
            properties: {
                clear: { type: 'boolean', description: 'Clear errors after retrieving' }
            }
        },
        category: 'debugging'
    },
    {
        name: 'browser_enable_console_capture',
        description: 'Enable/disable console log capture',
        inputSchema: {
            type: 'object',
            properties: {
                enabled: { type: 'boolean', description: 'Enable console capture' }
            },
            required: ['enabled']
        },
        category: 'debugging'
    },

    // === MEDIA ===
    {
        name: 'browser_play_video',
        description: 'Play a video element',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of video element' }
            },
            required: ['selector']
        },
        category: 'media'
    },
    {
        name: 'browser_pause_video',
        description: 'Pause a video element',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of video element' }
            },
            required: ['selector']
        },
        category: 'media'
    },
    {
        name: 'browser_get_media_state',
        description: 'Get the state of a video/audio element',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of media element' }
            },
            required: ['selector']
        },
        category: 'media'
    },
    {
        name: 'browser_set_media_time',
        description: 'Seek to a specific time in a video/audio',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of media element' },
                time: { type: 'number', description: 'Time in seconds' }
            },
            required: ['selector', 'time']
        },
        category: 'media'
    },
    {
        name: 'browser_set_media_volume',
        description: 'Set volume of a video/audio element',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of media element' },
                volume: { type: 'number', description: 'Volume 0-1' }
            },
            required: ['selector', 'volume']
        },
        category: 'media'
    },

    // === DRAG & DROP ===
    {
        name: 'browser_drag_and_drop',
        description: 'Drag an element and drop it on another element',
        inputSchema: {
            type: 'object',
            properties: {
                source_selector: { type: 'string', description: 'CSS selector of element to drag' },
                target_selector: { type: 'string', description: 'CSS selector of drop target' }
            },
            required: ['source_selector', 'target_selector']
        },
        category: 'interaction'
    },
    {
        name: 'browser_drag_to_position',
        description: 'Drag an element to specific coordinates',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of element to drag' },
                x: { type: 'number', description: 'Target X coordinate' },
                y: { type: 'number', description: 'Target Y coordinate' }
            },
            required: ['selector', 'x', 'y']
        },
        category: 'interaction'
    },

    // === RICH TEXT / CONTENTEDITABLE ===
    {
        name: 'browser_fill_contenteditable',
        description: 'Fill content into a contenteditable element or rich text editor',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of contenteditable element' },
                content: { type: 'string', description: 'Content to insert (can include HTML)' },
                html: { type: 'boolean', description: 'Insert as HTML (default: false, inserts as text)' }
            },
            required: ['selector', 'content']
        },
        category: 'forms'
    },
    {
        name: 'browser_get_editor_content',
        description: 'Get content from a rich text editor or contenteditable element',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of editor element' },
                format: { type: 'string', enum: ['text', 'html'], description: 'Return format (default: text)' }
            },
            required: ['selector']
        },
        category: 'forms'
    },

    // === SHADOW DOM ===
    {
        name: 'browser_pierce_shadow',
        description: 'Query an element inside shadow DOM using >> piercing selector',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'Piercing selector (e.g., "my-component >> .inner-element")' }
            },
            required: ['selector']
        },
        category: 'advanced'
    },
    {
        name: 'browser_get_shadow_root',
        description: 'Check if an element has a shadow root',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of host element' }
            },
            required: ['selector']
        },
        category: 'advanced'
    },

    // === PERFORMANCE ===
    {
        name: 'browser_get_metrics',
        description: 'Get page performance metrics',
        inputSchema: { type: 'object', properties: {} },
        category: 'performance'
    },
    {
        name: 'browser_get_timing',
        description: 'Get navigation and resource timing data',
        inputSchema: { type: 'object', properties: {} },
        category: 'performance'
    },
    {
        name: 'browser_start_tracing',
        description: 'Start performance tracing',
        inputSchema: {
            type: 'object',
            properties: {
                screenshots: { type: 'boolean', description: 'Capture screenshots during trace' },
                categories: { type: 'array', items: { type: 'string' }, description: 'Trace categories to include' }
            }
        },
        category: 'performance'
    },
    {
        name: 'browser_stop_tracing',
        description: 'Stop performance tracing and get trace data',
        inputSchema: { type: 'object', properties: {} },
        category: 'performance'
    },
    {
        name: 'browser_get_coverage',
        description: 'Get JavaScript and CSS code coverage',
        inputSchema: {
            type: 'object',
            properties: {
                type: { type: 'string', enum: ['js', 'css', 'both'], description: 'Coverage type (default: both)' }
            }
        },
        category: 'performance'
    },

    // === VISUAL ===
    {
        name: 'browser_compare_screenshots',
        description: 'Compare current page to a baseline screenshot',
        inputSchema: {
            type: 'object',
            properties: {
                baseline_base64: { type: 'string', description: 'Base64-encoded baseline image' },
                threshold: { type: 'number', description: 'Difference threshold 0-1 (default: 0.1)' },
                selector: { type: 'string', description: 'Compare specific element only (optional)' }
            },
            required: ['baseline_base64']
        },
        category: 'visual'
    },
    {
        name: 'browser_highlight_element',
        description: 'Visually highlight an element on the page (useful for debugging)',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of element to highlight' },
                color: { type: 'string', description: 'Highlight color (default: red)' },
                duration_ms: { type: 'number', description: 'How long to show highlight (default: 2000)' }
            },
            required: ['selector']
        },
        category: 'visual'
    },
    {
        name: 'browser_get_computed_style',
        description: 'Get computed CSS styles for an element',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of element' },
                properties: { type: 'array', items: { type: 'string' }, description: 'Specific properties to get (optional, returns all if omitted)' }
            },
            required: ['selector']
        },
        category: 'visual'
    },

    // === PERMISSIONS ===
    {
        name: 'browser_grant_permission',
        description: 'Grant a browser permission (geolocation, notifications, camera, microphone, etc.)',
        inputSchema: {
            type: 'object',
            properties: {
                permission: { type: 'string', description: 'Permission name: geolocation, notifications, camera, microphone, clipboard-read, clipboard-write' },
                origin: { type: 'string', description: 'Origin to grant permission for (optional, defaults to current page)' }
            },
            required: ['permission']
        },
        category: 'permissions'
    },
    {
        name: 'browser_deny_permission',
        description: 'Deny a browser permission',
        inputSchema: {
            type: 'object',
            properties: {
                permission: { type: 'string', description: 'Permission name to deny' },
                origin: { type: 'string', description: 'Origin to deny permission for' }
            },
            required: ['permission']
        },
        category: 'permissions'
    },
    {
        name: 'browser_reset_permissions',
        description: 'Reset all permissions to default state',
        inputSchema: { type: 'object', properties: {} },
        category: 'permissions'
    },

    // === CLIPBOARD ===
    {
        name: 'browser_copy_to_clipboard',
        description: 'Copy text to the clipboard',
        inputSchema: {
            type: 'object',
            properties: {
                text: { type: 'string', description: 'Text to copy to clipboard' }
            },
            required: ['text']
        },
        category: 'utility'
    },
    {
        name: 'browser_read_clipboard',
        description: 'Read text from the clipboard',
        inputSchema: { type: 'object', properties: {} },
        category: 'utility'
    },

    // === FRAMES ===
    {
        name: 'browser_list_frames',
        description: 'List all frames in the page',
        inputSchema: { type: 'object', properties: {} },
        category: 'frames'
    },
    {
        name: 'browser_frame_click',
        description: 'Click an element inside an iframe',
        inputSchema: {
            type: 'object',
            properties: {
                frame_selector: { type: 'string', description: 'CSS selector of iframe' },
                element_selector: { type: 'string', description: 'CSS selector of element within frame' }
            },
            required: ['frame_selector', 'element_selector']
        },
        category: 'frames'
    },
    {
        name: 'browser_frame_fill',
        description: 'Fill an input inside an iframe',
        inputSchema: {
            type: 'object',
            properties: {
                frame_selector: { type: 'string', description: 'CSS selector of iframe' },
                element_selector: { type: 'string', description: 'CSS selector of input within frame' },
                text: { type: 'string', description: 'Text to fill' }
            },
            required: ['frame_selector', 'element_selector', 'text']
        },
        category: 'frames'
    },
    {
        name: 'browser_frame_get_text',
        description: 'Get text from an element inside an iframe',
        inputSchema: {
            type: 'object',
            properties: {
                frame_selector: { type: 'string', description: 'CSS selector of iframe' },
                element_selector: { type: 'string', description: 'CSS selector of element within frame' }
            },
            required: ['frame_selector', 'element_selector']
        },
        category: 'frames'
    },

    // =============================
    // HIGH-LEVEL ACTION TOOLS
    // =============================
    {
        name: 'browser_login',
        description: 'Fill in a login form with username and password, then submit. Auto-detects common login form selectors.',
        inputSchema: {
            type: 'object',
            properties: {
                username: { type: 'string', description: 'Username or email' },
                password: { type: 'string', description: 'Password' },
                username_selector: { type: 'string', description: 'CSS selector for username field (optional, auto-detected)' },
                password_selector: { type: 'string', description: 'CSS selector for password field (optional, auto-detected)' },
                submit_selector: { type: 'string', description: 'CSS selector for submit button (optional, auto-detected)' }
            },
            required: ['username', 'password']
        },
        category: 'high-level'
    },
    {
        name: 'browser_fill_form',
        description: 'Fill multiple form fields at once. Supports text inputs, selects, checkboxes, and radio buttons.',
        inputSchema: {
            type: 'object',
            properties: {
                fields: { type: 'object', description: 'Key-value pairs of CSS selector → value to fill' },
                submit_selector: { type: 'string', description: 'CSS selector of submit button to click after filling (optional)' }
            },
            required: ['fields']
        },
        category: 'high-level'
    },
    {
        name: 'browser_search',
        description: 'Find the search box on a page and perform a search. Auto-detects common search input selectors.',
        inputSchema: {
            type: 'object',
            properties: {
                query: { type: 'string', description: 'Search query text' },
                search_selector: { type: 'string', description: 'CSS selector for search input (optional, auto-detected)' }
            },
            required: ['query']
        },
        category: 'high-level'
    },
    {
        name: 'browser_checkout',
        description: 'Click checkout/buy button and optionally fill checkout fields.',
        inputSchema: {
            type: 'object',
            properties: {
                checkout_selector: { type: 'string', description: 'CSS selector for checkout button (optional, auto-detected)' },
                fields: { type: 'object', description: 'Key-value pairs of checkout form fields to fill' }
            }
        },
        category: 'high-level'
    },

    // =============================
    // SMART SELECTORS
    // =============================
    {
        name: 'browser_click_text',
        description: 'Click an element by its visible text content. More resilient than CSS selectors.',
        inputSchema: {
            type: 'object',
            properties: {
                text: { type: 'string', description: 'Visible text to click' },
                element_type: { type: 'string', enum: ['any', 'button', 'link'], description: 'Element type filter (default: any)' }
            },
            required: ['text']
        },
        category: 'smart-selectors'
    },
    {
        name: 'browser_click_role',
        description: 'Click an element by its ARIA role and accessible name.',
        inputSchema: {
            type: 'object',
            properties: {
                role: { type: 'string', description: 'ARIA role (button, link, checkbox, tab, menuitem, etc.)' },
                name: { type: 'string', description: 'Accessible name of the element' }
            },
            required: ['role', 'name']
        },
        category: 'smart-selectors'
    },
    {
        name: 'browser_click_label',
        description: 'Click an element associated with a form label.',
        inputSchema: {
            type: 'object',
            properties: {
                label: { type: 'string', description: 'Label text' }
            },
            required: ['label']
        },
        category: 'smart-selectors'
    },
    {
        name: 'browser_fill_label',
        description: 'Fill an input by its associated label text.',
        inputSchema: {
            type: 'object',
            properties: {
                label: { type: 'string', description: 'Label text' },
                text: { type: 'string', description: 'Text to fill' }
            },
            required: ['label', 'text']
        },
        category: 'smart-selectors'
    },
    {
        name: 'browser_click_placeholder',
        description: 'Click an input by its placeholder text.',
        inputSchema: {
            type: 'object',
            properties: {
                placeholder: { type: 'string', description: 'Placeholder text' }
            },
            required: ['placeholder']
        },
        category: 'smart-selectors'
    },
    {
        name: 'browser_get_by_test_id',
        description: 'Interact with an element by its data-testid attribute.',
        inputSchema: {
            type: 'object',
            properties: {
                test_id: { type: 'string', description: 'The data-testid value' },
                action: { type: 'string', enum: ['click', 'fill', 'getText'], description: 'Action to perform (default: getText)' },
                text: { type: 'string', description: 'Text to fill (when action=fill)' }
            },
            required: ['test_id']
        },
        category: 'smart-selectors'
    },

    // =============================
    // ERROR RECOVERY
    // =============================
    {
        name: 'browser_safe_click',
        description: 'Click with auto-retry, scroll-into-view, and wait. More resilient than browser_click.',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of element to click' },
                retries: { type: 'number', description: 'Max retry attempts (default: 3)' }
            },
            required: ['selector']
        },
        category: 'error-recovery'
    },
    {
        name: 'browser_safe_fill',
        description: 'Fill with auto-retry, scroll-into-view, and wait. More resilient than browser_type.',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector of input' },
                text: { type: 'string', description: 'Text to fill' },
                retries: { type: 'number', description: 'Max retry attempts (default: 3)' }
            },
            required: ['selector', 'text']
        },
        category: 'error-recovery'
    },
    {
        name: 'browser_wait_and_click',
        description: 'Wait for element to appear then click it.',
        inputSchema: {
            type: 'object',
            properties: {
                selector: { type: 'string', description: 'CSS selector to wait for and click' },
                timeout: { type: 'number', description: 'Max wait time in ms (default: 30000)' }
            },
            required: ['selector']
        },
        category: 'error-recovery'
    },

    // =============================
    // RECORDING MODE
    // =============================
    {
        name: 'browser_start_recording',
        description: 'Start recording user interactions (clicks and inputs) on the page.',
        inputSchema: { type: 'object', properties: {} },
        category: 'recording'
    },
    {
        name: 'browser_stop_recording',
        description: 'Stop recording and return all captured actions.',
        inputSchema: { type: 'object', properties: {} },
        category: 'recording'
    },
    {
        name: 'browser_get_recording',
        description: 'Get the current recording without stopping it.',
        inputSchema: { type: 'object', properties: {} },
        category: 'recording'
    },
    {
        name: 'browser_replay_recording',
        description: 'Replay a recorded sequence of actions.',
        inputSchema: {
            type: 'object',
            properties: {
                actions: { type: 'array', description: 'Array of recorded actions to replay', items: { type: 'object' } }
            },
            required: ['actions']
        },
        category: 'recording'
    },

    // =============================
    // NETWORK EGRESS CONTROL
    // =============================
    {
        name: 'browser_block_domains',
        description: 'Block network requests to specific domains (e.g., ad trackers, analytics).',
        inputSchema: {
            type: 'object',
            properties: {
                domains: { type: 'array', items: { type: 'string' }, description: 'List of domains to block' }
            },
            required: ['domains']
        },
        category: 'network'
    }
];

export async function discoverWebMCPTools(page: Page): Promise<WebMCPDiscoveryResult> {
    try {
        // Check for WebMCP via navigator.modelContext
        const webmcpData = await page.evaluate(() => {
            const mc = (navigator as any).modelContext;
            if (!mc) return null;

            // WebMCP spec: tools are registered via modelContext.registerTool()
            // and can be listed via modelContext.tools
            const tools = mc.tools || [];
            const serverInfo = mc.serverInfo || null;

            return {
                tools: tools.map((t: any) => ({
                    name: t.name,
                    description: t.description || '',
                    inputSchema: t.inputSchema || { type: 'object', properties: {} },
                    outputSchema: t.outputSchema,
                    category: t.category,
                    requiresAuth: t.requiresAuth
                })),
                serverInfo
            };
        });

        if (webmcpData && webmcpData.tools.length > 0) {
            return {
                hasWebMCP: true,
                tools: webmcpData.tools,
                serverInfo: webmcpData.serverInfo
            };
        }

        // No WebMCP found - return fallback tools
        return {
            hasWebMCP: false,
            tools: PLAYWRIGHT_FALLBACK_TOOLS
        };
    } catch (error) {
        // Error reading WebMCP - return fallback
        return {
            hasWebMCP: false,
            tools: PLAYWRIGHT_FALLBACK_TOOLS
        };
    }
}

export async function callWebMCPTool(
    page: Page,
    toolName: string,
    input: Record<string, unknown>
): Promise<unknown> {
    return await page.evaluate(
        ({ toolName, input }) => {
            const mc = (navigator as any).modelContext;
            if (!mc) throw new Error('WebMCP not available on this page');

            const tool = mc.tools?.find((t: any) => t.name === toolName);
            if (!tool) throw new Error(`Tool '${toolName}' not found`);

            // WebMCP spec: tools are called via their handler function
            return tool.handler(input);
        },
        { toolName, input }
    );
}
