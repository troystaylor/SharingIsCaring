# Scope Documentation

Central documentation hub for the Scope platform — an AI coding agent benchmarking system.

## Architecture & Design

| Document | Description |
|----------|-------------|
| [System Overview](architecture/overview.md) | High-level architecture, component interactions, data flow |
| [System Architecture](architecture/system-architecture.md) | Runtime topology of deployed components, local development services, and in-progress migrations |
| [Application Design](architecture/app-design.md) | Data models, API design, judge pipeline, queue patterns, criteria system |
| [Database Schema & Indexes](architecture/db.md) | MongoDB collections, document shapes, and indexes |
| [Data Organization: Projects](architecture/data-organization-projects.md) | Proposal for scoping user-facing data to projects |
| [Queue Scheduling & Back Pressure](architecture/queue-scheduler.md) | Priority scheduling, pause/resume, and queue dispatch |
| [Retry Utilities](architecture/retry.md) | Shared retry primitives for transient failures |
| [Observability](architecture/observability.md) | Application Insights telemetry and OpenTelemetry export |
| [Real-Time Data Flow](research/realtime-data-flow.md) | How the Portal uses SSE, change streams, and polling |

### Evaluation building blocks

| Document | Description |
|----------|-------------|
| [Criteria Provider](architecture/criteria-provider.md) | How the Judge loads evaluation criteria from different sources |
| [Gates](design/gates.md) | Multi-phase evaluation pipeline (Select, Build, Test, Run, Deploy) |
| [Codebases](architecture/codebases.md) | Immutable, numbered starting project snapshots |
| [Resources](architecture/resources.md) | Lifecycle definitions that provide external dependencies to a run |
| [Skills](architecture/skills.md) | Reusable agent instruction packages included in benchmarks |
| [Post-Processing Pipeline](architecture/post-processing.md) | Derived artifacts from completed runs, such as ATIF trajectories |
| [Prompt Evaluations](architecture/prompt-evaluations.md) | Static prompt quality, user-controlled surface red teaming, datasets, commands, and maintenance |

### Workers, gateways, and tokens

| Document | Description |
|----------|-------------|
| [Worker Requirements](architecture/worker-requirements.md) | Requirements checklist for coding agent workers |
| [Worker Compliance](architecture/worker-compliance.md) | Per-worker compliance matrix against requirements |
| [AI Gateway](architecture/ai-gateway.md) | Shared Rust TLS-intercepting proxy between workers and AI providers |
| [Gateway WebSocket Support](architecture/gateway-websocket.md) | Recording WebSocket traffic through the gateway |
| [MCP Gateway](architecture/mcp-gateway.md) | MCPJungle sidecar that aggregates MCP servers per worker |
| [Kubedock](architecture/kubedock.md) | Docker command access for workers on Kubernetes |
| [Token Manager](architecture/token-manager.md) | Capability-based token management, validation, round-robin distribution |
| [Authentication & RBAC](architecture/auth-rbac.md) | Explicit-login IdP authentication, Redis user-access caching, Portal handshake; deferred RBAC roadmap |
| [CLI Distribution](architecture/cli-distribution.md) | How the CLI is bundled, released, installed, and updated |

### Database and operations

| Document | Description |
|----------|-------------|
| [Database Migrations](architecture/db-migrations.md) | Lightweight MongoDB migration framework, writing and running migrations |
| [Database Collection Scaling](architecture/db-collection-scaling.md) | Per-collection autoscale throughput, ASO reconcile policy, operator runbook |

## Infrastructure and deployment

The Azure and Kubernetes deployment guides are published on the documentation site and
sourced from [`website/src/content/docs/self-hosting/`](../website/src/content/docs/self-hosting/).

| Document | Description |
|----------|-------------|
| [Self-hosting overview](../website/src/content/docs/self-hosting/overview.md) | Reference architecture, installed workloads, and identity boundaries |
| [Provision Azure infrastructure](../website/src/content/docs/self-hosting/provision-aks.mdx) | AKS cluster, data services, and workload identities |
| [Deploy with Helm](../website/src/content/docs/self-hosting/deploy-helm.md) | Install the core services and coding-agent workers |
| [Configuration](../website/src/content/docs/self-hosting/configuration.md) | Deployment configuration reference |
| [Scaling with KEDA](../website/src/content/docs/self-hosting/scaling.md) | Queue-driven worker and node-pool scaling |
| [Operations](../website/src/content/docs/self-hosting/operations.md) | Day-two operations |
| [Shared Dev Infrastructure](shared-dev-infra.md) | Use a shared dev Cosmos DB account (`infra/`, `pnpm shared-infra:*`) while keeping local MongoDB as the default |
| [Local setup on Windows](local-setup-windows.md) | Helper script, local deviations from upstream, and machine-level changes for running the stack on Windows |

## Responsible AI

| Document | Description |
|----------|-------------|
| [Responsible AI FAQ](responsible-ai-faq.md) | Intended uses, limitations, AI capabilities, risks and mitigations for the OneRAI transparency documentation |

## Operations

| Document | Description |
|----------|-------------|
| [Cosmos DB Backup & Restore](ops/cosmos-backup-restore.md) | `pnpm db:dump` / `pnpm db:restore` rollback tooling, presets, verification, Cosmos caveats |

## Research

| Document | Description |
|----------|-------------|
| [Delta Storage](research/delta-storage.md) | Approaches for space-efficient storage of coding agent iteration snapshots |
| [Real-Time Data Flow](research/realtime-data-flow.md) | SSE, change streams, and polling in the Portal |

## Design

| Document | Description |
|----------|-------------|
| [Gates](design/gates.md) | Multi-phase evaluation pipeline design |
| [Palette](design/palette.md) | Portal color tokens and the role each one fills |

## Tips & Tricks

| Document | Description |
|----------|-------------|
| [Developer Tips & Tricks](tips/README.md) | Short, practical tips for working productively in this repo (one file per tip) |

## Agent experience playbook

[`ax-playbook/`](ax-playbook/) contains the agent experience (AX) practitioner skill, its
reference material, and a sample readout.

---

## Where else is documentation?

- **[Root README](../README.md)**: overview, quick start, repository layout, and troubleshooting.
- **[CONTRIBUTING.md](../CONTRIBUTING.md)**: prerequisites, local development, testing, and the pull request process.
- **[ENV_VARIABLES.md](../ENV_VARIABLES.md)**: environment variable reference, including provider credentials and the Judge, feedback, and report models.
- **[`config/`](../config/)**: portable example scenarios, personas, criteria, and traits as YAML. MongoDB is the runtime source of truth.
- **Component READMEs**: [CLI](../apps/cli/README.md), [LLM evaluation package](../packages/llm-eval/README.md), [static prompt evaluations](../evaluations/static-prompts/README.md), and the [documentation site](../website/README.md).
- **[AGENTS.md](../AGENTS.md)** and **[CLAUDE.md](../CLAUDE.md)**: development conventions and patterns for AI assistants.
- **[Documentation site](https://microsoft.github.io/scope/)**: user guides, concepts, and the REST API reference.
