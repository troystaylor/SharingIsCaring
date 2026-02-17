# WebMCP Broker Service

The backend service for the WebMCP Discovery connector. Runs headless Chromium via Playwright to discover WebMCP tools on any web page and execute browser automation actions.

## Quick Start

```bash
# Install dependencies
npm install

# Copy and configure environment
cp .env.example .env

# Start in development mode
npm run dev
```

The service starts at `http://localhost:3000`. Verify with:

```bash
curl http://localhost:3000/health
```

## Scripts

| Command | Description |
|---------|-------------|
| `npm run dev` | Start with ts-node (development) |
| `npm run build` | Compile TypeScript to `dist/` |
| `npm start` | Run compiled output (production) |
| `npm test` | Run tests with Vitest |
| `npm run test:watch` | Run tests in watch mode |
| `npm run lint` | Check code with ESLint |
| `npm run lint:fix` | Auto-fix lint issues |

## Environment Variables

See [.env.example](.env.example) for a complete list with descriptions. Key variables:

| Variable | Default | Description |
|----------|---------|-------------|
| `PORT` | `3000` | Server port |
| `API_KEY` | — | API key for `X-API-Key` auth |
| `AUTH_MODE` | `apikey` | `apikey`, `managed-identity`, or `both` |
| `RBAC_ENABLED` | `false` | Enable role-based access control |
| `ALLOWED_DOMAINS` | — | Comma-separated domain allowlist |
| `AUDIT_LOG_LEVEL` | `basic` | `none`, `basic`, `detailed`, `full` |
| `SESSION_RECORDING` | `false` | Record tool executions per session |
| `MAX_BROWSERS` | `5` | Max concurrent browser instances |

## Docker

```bash
# Build
docker build -t webmcp-broker:latest .

# Run
docker run -p 3000:3000 -e API_KEY=your-key webmcp-broker:latest
```

## Architecture

```
src/
├── index.ts              # Express app, middleware chain
├── middleware/
│   ├── audit-logger.ts   # Request/response audit logging
│   ├── data-redaction.ts # Sensitive data masking
│   ├── managed-identity.ts # Azure AD / Entra ID auth
│   ├── rbac.ts           # Role-based access control
│   └── url-allowlist.ts  # SSRF protection
├── routes/
│   ├── discover.ts       # POST /api/discover
│   ├── execute.ts        # Tool execution + recording
│   └── sessions.ts       # Session CRUD + navigation
└── services/
    ├── browser-pool.ts    # Chromium instance management
    ├── playwright-executor.ts # 110+ Playwright tool handlers
    ├── session-store.ts   # Session state + action recording
    └── webmcp-reader.ts   # WebMCP discovery + tool definitions
```

## Deployment

See the [parent readme](../readme.md) for full deployment instructions using Azure Container Apps and Bicep.

## License

[MIT](../LICENSE)
