# Database Collection Scaling

How Cosmos DB MongoDB collections are sized (autoscale throughput) and
managed in integration and production. This is **infrastructure-as-code**
owned by the app: the manifests live next to the app deployments and are
reconciled by FluxCD via
[Azure Service Operator (ASO)](https://azure.github.io/azure-service-operator/).

## Ownership at a glance

The database has three concerns and **three independent owners**. This doc
covers only the first row.

| Concern              | Owner                                       | Where it lives                                          |
|----------------------|---------------------------------------------|---------------------------------------------------------|
| Collection scaling (autoscale `maxThroughput`, reconcile policy, creation/deletion of collection resources) | **ASO via FluxCD**, defined in this doc | ASO collection manifests + per-env overlays |
| Indexes (creation, removal, backfills) | **`packages/db-migrations`** — see [`db-migrations.md`](./db-migrations.md) | `packages/db-migrations/src/migrations/` |
| Document shape, query patterns, business semantics | **Application code** — see [`db.md`](./db.md) | `packages/*/src/` |

These owners are designed not to step on each other:

- ASO manifests intentionally omit `spec.resource.indexes` and
  `spec.resource.shardKey`. Empirically validated that ASO does not strip
  existing indexes when reconciling under `manage`
  ([#644](https://github.com/growth-ecosystems/scope-core/pull/644)).
- The migration job creates indexes idempotently — re-running is a no-op
  if the index already exists.
- Application code never calls `createIndex`, `dropIndex`, or
  `db.command({ offerThroughput: ... })` at runtime.

If you're trying to **change throughput**, you're in the right doc. If you
need to **add or change an index**, stop here and read
[`db-migrations.md`](./db-migrations.md).

## Why per-collection throughput

Cosmos DB for MongoDB does not allow adding **shared, database-level**
throughput to an existing database that already has collections. Our
database (`scope-mt`) was created implicitly by the app's first write
without shared throughput, so each collection now provisions its own
throughput independently.

We use **autoscale** (not manual) on every collection: Cosmos charges hourly
based on the highest RU/s consumed in that hour, and auto-derives the floor
as `max / 10`. This gives us elastic capacity for bursty workloads at the
same idle cost as manual provisioning at the floor.

## Where it's defined

Each collection has an ASO `MongodbDatabaseCollection` manifest (one YAML per
collection), with per-environment overlays patching `maxThroughput` (e.g.
`requests` is 1000 at base, 3000 in integration, 6000 in prod).

Each manifest is an ASO `MongodbDatabaseCollection` resource. ASO watches
these in-cluster, talks to the Azure ARM API, and reconciles the live
collection's throughput against `spec.options.autoscaleSettings.maxThroughput`.

## Sizing matrix

All values are `maxThroughput` (RU/s). The autoscale floor is always `max / 10`.

| Collection                    | base | integration | prod |
|-------------------------------|-----:|------------:|-----:|
| `accounts`                    | 1000 |        1000 | 1000 |
| `agents`                      | 1000 |        1000 | 1000 |
| `criteria`                    | 1000 |        1000 | 1000 |
| `extensions`                  | 1000 |        1000 | 1000 |
| `feature-flags`               | 1000 |        1000 | 1000 |
| `insights`                    | 1000 |        1000 | 1000 |
| `mcp-secrets`                 | 1000 |        1000 | 1000 |
| `mcp-servers`                 | 1000 |        1000 | 1000 |
| `_migrations`                 | 1000 |        1000 | 1000 |
| `models`                      | 1000 |        1000 | 1000 |
| `profile-versions`            | 1000 |        1000 | 1000 |
| `profiles`                    | 1000 |        1000 | 1000 |
| `prompt-feature-extractions`  | 1000 |        1000 | 1000 |
| `prompt-features`             | 1000 |        1000 | 1000 |
| `report-templates`            | 1000 |        1000 | 1000 |
| `reports`                     | 1000 |        1000 | 1000 |
| **`requests`**                | **1000** | **3000** | **6000** |
| `skill-revisions`             | 1000 |        1000 | 1000 |
| `skills`                      | 1000 |        1000 | 1000 |
| `task-prompts`                | 1000 |        1000 | 1000 |
| `tokens`                      | 1000 |        1000 | 1000 |

`requests` is the highest-traffic collection (one document per agent run,
plus per-iteration writes). It needs more headroom in higher-traffic
environments. Per-env overrides live in the overlay `kustomization.yaml`
under `patches:`.

## Reconcile policy

Every manifest is annotated with:

```yaml
metadata:
  annotations:
    serviceoperator.azure.com/reconcile-policy: manage
```

`manage` means ASO actively reconciles the live Cosmos collection against
the manifest spec. To temporarily pause reconciliation on a single
collection (e.g. during an incident), flip its annotation to `skip` and
commit. Flux will apply the change, ASO will stop writing to Azure for that
resource, and any out-of-band changes (e.g. a manual `az` command or Portal
edit) will not be reverted.

## What ASO does **not** manage

Recapping the [ownership table](#ownership-at-a-glance):

- **Indexes** are owned by `packages/db-migrations` (see migration
  [`002-create-indexes.ts`](../../packages/db-migrations/src/migrations/002-create-indexes.ts)
  and follow-ups). ASO leaves them alone because our manifests omit
  `spec.resource.indexes`.
- **Shard keys** are not used (Cosmos for MongoDB, single shard per
  collection). See [Future: sharding](#future-sharding) below.
- **Document content / schema** is owned by application code. ASO has no
  visibility into individual documents.

## Future: sharding

We do not currently shard any collection. If we ever need to, the shard key
belongs in **the same ASO manifest that owns scaling** — alongside
`autoscaleSettings` — under `spec.resource.shardKey`:

```yaml
# ASO collection manifest: <collection>
spec:
  resource:
    id: requests
    shardKey:
      submissionId: Hash       # field, with Hash or Range distribution
    indexes:
      - key: { keys: [_id] }
      - key: { keys: [submissionId] }   # required: shard key must be indexed
  options:
    autoscaleSettings:
      maxThroughput: 6000
```

Why ASO and not `db-migrations`:

- **Shard keys are immutable per collection in Cosmos** — they're set at
  create time. Changing a shard key requires creating a new collection and
  copying data. That's an infra/lifecycle decision, same shape as
  throughput, so same owner.
- **The MongoDB driver has no `shardCollection` support against Cosmos for
  MongoDB.** Sharding has to go through the ARM API (or
  `az cosmosdb mongodb collection update --shard ...`), which is exactly
  what ASO does.

Tradeoff to be aware of when adopting sharding: we'd lose the current
clean split where ASO owns *only* throughput. The `indexes` field would
have to appear in the ASO manifest for at least the shard-key index,
partially duplicating what migration 002 declares for that field.

## Operator runbook

### Bump throughput on a collection

1. Edit the collection's ASO manifest (changes both envs) **or** its per-env
   overlay (changes one env).
2. Commit and open a PR against `main`.
3. After merge, FluxCD reconciles the manifest, ASO calls Azure ARM, and
   Cosmos updates the live throughput.
4. Verify with:
   ```bash
   az cosmosdb mongodb collection throughput show \
     --account-name "db-scope-v2-<env>" \
     --resource-group "rg-scope-v2-<env>" \
     --database-name scope-mt --name "<collection>" \
     --query "resource.autoscaleSettings.maxThroughput" -o tsv
   ```

### Add a new collection

1. Create a new ASO collection manifest using an existing one as template.
2. Add the filename to the collection kustomization.
3. If the K8s metadata name and the Azure collection name differ (e.g.
   leading underscore, hyphen-vs-underscore), set `spec.azureName` to the
   exact Azure name. See `migrations.yaml` (K8s `migrations`,
   Azure `_migrations`) for an example.
4. Commit. FluxCD reconciles, ASO creates the collection if it does not
   exist.

### Pause reconciliation on one collection

```bash
# In the manifest:
metadata:
  annotations:
    serviceoperator.azure.com/reconcile-policy: skip
```

Commit the change. ASO will keep observing but stop writing. Flip back to
`manage` when ready.

### Verify all collections live

```bash
kubectl --context aks-scope-v2-<env> -n scoped \
  get mongodbdatabasecollection
# All rows should show READY: True with no errors.
```

To inspect what ASO observes for a single collection:

```bash
kubectl --context aks-scope-v2-<env> -n scoped \
  get mongodbdatabasecollection <collection> -o yaml
# .status.resource shows throughput, indexes, shard key as observed in Azure.
```

## History

- [#642](https://github.com/growth-ecosystems/scope-core/pull/642) — Stage 1:
  introduced the manifests with `reconcile-policy: skip` (observe-only).
- [#643](https://github.com/growth-ecosystems/scope-core/pull/643) — fixed
  `_migrations` `azureName` so the manifest could find the live collection.
- [#644](https://github.com/growth-ecosystems/scope-core/pull/644) — Stage 2:
  flipped `reconcile-policy` to `manage` everywhere. Includes the
  index-preservation safety test.
