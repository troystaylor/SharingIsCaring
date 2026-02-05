/**
 * MCP Apps UI Components for Power Agent Desktop
 * Provides UI rendering capabilities for MCP tools
 */

export interface CardAction {
  type: 'submit' | 'openUrl' | 'showCard';
  title: string;
  data?: Record<string, unknown>;
  url?: string;
}

export interface ProductItem {
  name: string;
  price: number;
  image?: string;
  description?: string;
  id?: string;
}

export interface WidgetConfig {
  html: string;
  width?: number;
  height?: number;
  sandbox?: boolean;
}

/**
 * Format currency with proper locale
 */
export function formatCurrency(amount: number, currency = 'USD'): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: currency,
  }).format(amount);
}

/**
 * Create an adaptive card for a single product
 */
export function createProductCard(product: ProductItem): Record<string, unknown> {
  return {
    type: 'AdaptiveCard',
    version: '1.5',
    body: [
      product.image ? {
        type: 'Image',
        url: product.image,
        size: 'Large',
        horizontalAlignment: 'Center',
      } : null,
      {
        type: 'TextBlock',
        text: product.name,
        weight: 'Bolder',
        size: 'Medium',
        wrap: true,
      },
      {
        type: 'TextBlock',
        text: formatCurrency(product.price),
        color: 'Accent',
        size: 'Medium',
      },
      product.description ? {
        type: 'TextBlock',
        text: product.description,
        wrap: true,
        maxLines: 3,
      } : null,
    ].filter(Boolean),
    actions: [
      {
        type: 'Action.Submit',
        title: 'Add to Cart',
        data: {
          action: 'addToCart',
          productId: product.id || product.name,
        },
      },
      {
        type: 'Action.Submit',
        title: 'View Details',
        data: {
          action: 'viewDetails',
          productId: product.id || product.name,
        },
      },
    ],
  };
}

/**
 * Create a product grid adaptive card
 */
export function createProductGridCard(products: ProductItem[]): Record<string, unknown> {
  const columns = products.map(product => ({
    type: 'Column',
    width: 'stretch',
    items: [
      product.image ? {
        type: 'Image',
        url: product.image,
        size: 'Medium',
      } : null,
      {
        type: 'TextBlock',
        text: product.name,
        weight: 'Bolder',
        wrap: true,
      },
      {
        type: 'TextBlock',
        text: formatCurrency(product.price),
        color: 'Accent',
      },
    ].filter(Boolean),
    selectAction: {
      type: 'Action.Submit',
      data: {
        action: 'selectProduct',
        productId: product.id || product.name,
      },
    },
  }));

  // Split into rows of 3 columns max
  const rows: Array<typeof columns> = [];
  for (let i = 0; i < columns.length; i += 3) {
    rows.push(columns.slice(i, i + 3));
  }

  return {
    type: 'AdaptiveCard',
    version: '1.5',
    body: rows.map(row => ({
      type: 'ColumnSet',
      columns: row,
      spacing: 'Medium',
    })),
  };
}

/**
 * Create a confirmation dialog card
 */
export function createConfirmationCard(
  title: string,
  message: string,
  confirmText = 'Confirm',
  cancelText = 'Cancel'
): Record<string, unknown> {
  return {
    type: 'AdaptiveCard',
    version: '1.5',
    body: [
      {
        type: 'TextBlock',
        text: title,
        weight: 'Bolder',
        size: 'Large',
      },
      {
        type: 'TextBlock',
        text: message,
        wrap: true,
      },
    ],
    actions: [
      {
        type: 'Action.Submit',
        title: confirmText,
        style: 'positive',
        data: { confirmed: true },
      },
      {
        type: 'Action.Submit',
        title: cancelText,
        style: 'destructive',
        data: { confirmed: false },
      },
    ],
  };
}

/**
 * Create a form input card
 */
export function createInputCard(
  title: string,
  fields: Array<{
    id: string;
    label: string;
    type?: 'text' | 'number' | 'date' | 'choice';
    placeholder?: string;
    choices?: string[];
    required?: boolean;
  }>
): Record<string, unknown> {
  const body: Array<Record<string, unknown>> = [
    {
      type: 'TextBlock',
      text: title,
      weight: 'Bolder',
      size: 'Medium',
    },
  ];

  for (const field of fields) {
    body.push({
      type: 'TextBlock',
      text: field.label,
      spacing: 'Medium',
    });

    if (field.type === 'choice' && field.choices) {
      body.push({
        type: 'Input.ChoiceSet',
        id: field.id,
        placeholder: field.placeholder,
        choices: field.choices.map(c => ({ title: c, value: c })),
        isRequired: field.required,
      });
    } else if (field.type === 'date') {
      body.push({
        type: 'Input.Date',
        id: field.id,
        isRequired: field.required,
      });
    } else if (field.type === 'number') {
      body.push({
        type: 'Input.Number',
        id: field.id,
        placeholder: field.placeholder,
        isRequired: field.required,
      });
    } else {
      body.push({
        type: 'Input.Text',
        id: field.id,
        placeholder: field.placeholder,
        isRequired: field.required,
      });
    }
  }

  return {
    type: 'AdaptiveCard',
    version: '1.5',
    body: body,
    actions: [
      {
        type: 'Action.Submit',
        title: 'Submit',
      },
    ],
  };
}

/**
 * Create sandboxed widget HTML wrapper
 */
export function createWidgetHtml(config: WidgetConfig): string {
  const { html, width = 400, height = 300 } = config;
  
  // Wrap in iframe for sandboxing
  const sandboxAttrs = config.sandbox !== false 
    ? 'sandbox="allow-scripts allow-same-origin"' 
    : '';
  
  return `
    <div class="widget-container" style="width: ${width}px; height: ${height}px;">
      <iframe 
        srcdoc="${escapeHtml(html)}"
        ${sandboxAttrs}
        style="width: 100%; height: 100%; border: none;"
      ></iframe>
    </div>
  `;
}

/**
 * Escape HTML for safe embedding
 */
function escapeHtml(text: string): string {
  const map: Record<string, string> = {
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#039;',
  };
  return text.replace(/[&<>"']/g, m => map[m]);
}

/**
 * Create a status/progress card
 */
export function createStatusCard(
  title: string,
  status: 'success' | 'warning' | 'error' | 'info',
  message: string
): Record<string, unknown> {
  const colors: Record<string, string> = {
    success: 'Good',
    warning: 'Warning',
    error: 'Attention',
    info: 'Accent',
  };

  const icons: Record<string, string> = {
    success: '✅',
    warning: '⚠️',
    error: '❌',
    info: 'ℹ️',
  };

  return {
    type: 'AdaptiveCard',
    version: '1.5',
    body: [
      {
        type: 'ColumnSet',
        columns: [
          {
            type: 'Column',
            width: 'auto',
            items: [
              {
                type: 'TextBlock',
                text: icons[status],
                size: 'Large',
              },
            ],
          },
          {
            type: 'Column',
            width: 'stretch',
            items: [
              {
                type: 'TextBlock',
                text: title,
                weight: 'Bolder',
                color: colors[status],
              },
              {
                type: 'TextBlock',
                text: message,
                wrap: true,
              },
            ],
          },
        ],
      },
    ],
  };
}
