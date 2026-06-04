# ACS Manifests

This folder holds [Agent Control Specification (ACS)](https://github.com/microsoft/agent-governance-toolkit/tree/main/policy-engine) manifests. ACS is the lifecycle-aware policy layer of the Agent Governance Toolkit. A single YAML manifest declares what to validate at each of the 8 intervention points across the agent loop:

```
agent_startup -> input -> pre_model_call -> post_model_call ->
pre_tool_call -> post_tool_call -> output -> agent_shutdown
```

ACS verdicts go beyond `allow`/`deny`: a policy can return `warn`, `escalate`, or `transform` (e.g., redact a tool result before returning it to the agent).

## Connector operations

These map to three new operations in the connector:

| Operation | Endpoint | Purpose |
|-----------|----------|---------|
| `LoadAcsManifest` | `POST /api/acs/manifest/load` | Register a manifest by id |
| `EvaluateIntervention` | `POST /api/acs/evaluate` | Submit a snapshot at any intervention point, get a verdict |
| `TransformPayload` | `POST /api/acs/transform` | Same as evaluate, but surfaces the transformed body when verdict is `transform` |

## Default state — scaffold only

Out of the box the three ACS endpoints return **HTTP 501** with a setup pointer. The manifest registry, request models, MCP tool definitions, and Swagger contracts are all wired — only the call into the native ACS runtime is left for you to enable.

## Enabling live ACS evaluation

The `AgentControlSpecification` .NET SDK is currently published as **local NuGet artifacts** (not on nuget.org). To wire it:

### 1. Obtain the SDK nupkg

Build it from the upstream repo or grab it from a release artifact:

```bash
git clone https://github.com/microsoft/agent-governance-toolkit.git
cd agent-governance-toolkit/policy-engine/sdk/dotnet
dotnet pack -c Release -o ../../../../artifacts
```

Copy the resulting `AgentControlSpecification.*.nupkg` (and any companion packages such as `AgentControlSpecification.AI`) into a sibling `local-packages/` folder next to this README.

### 2. Configure a local NuGet source

Add a `NuGet.config` in `container-app/`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="local-packages" value="./local-packages" />
  </packageSources>
</configuration>
```

### 3. Reference the SDK in `AgentGovernance.Api.csproj`

```xml
<ItemGroup>
  <PackageReference Include="AgentControlSpecification" Version="0.3.*" />
</ItemGroup>
```

### 4. Provide the OPA binary (Rego policies only)

If your manifests use `type: rego` policies, the SDK shells out to OPA via `ACS_OPA_PATH`. Add to the Dockerfile runtime stage:

```dockerfile
RUN apt-get update && apt-get install -y curl && \
    curl -L -o /usr/local/bin/opa https://openpolicyagent.org/downloads/latest/opa_linux_amd64_static && \
    chmod +x /usr/local/bin/opa
ENV ACS_OPA_PATH=/usr/local/bin/opa
```

Manifests that only use `type: test` or `type: cedar` policies do not need OPA.

### 5. Wire `AcsRegistry.SdkAvailable` and `EvaluateAsync`

In `Program.cs`, flip `AcsRegistry.SdkAvailable` to `true` and add an `EvaluateAsync` method that calls into the SDK. The expected shape (verify against the installed SDK version):

```csharp
using AgentControlSpecification;

public static async Task<InterventionPointResult> EvaluateAsync(AcsEvaluateRequest req)
{
    if (!TryGet(req.ManifestId, out var path))
        throw new InvalidOperationException($"Manifest '{req.ManifestId}' not registered.");

    var control = AgentControl.FromPath(path);
    var interventionPoint = Enum.Parse<InterventionPoint>(req.InterventionPoint, ignoreCase: true);
    var mode = string.Equals(req.Mode, "evaluate_only", StringComparison.OrdinalIgnoreCase)
        ? EnforcementMode.EvaluateOnly
        : EnforcementMode.Enforce;

    return await control.EvaluateInterventionPointAsync(
        new InterventionPointRequest(interventionPoint, req.Snapshot, mode, req.ToolName));
}
```

Then replace the two `Results.Json(..., statusCode: 501)` blocks in the ACS endpoints with calls into `AcsRegistry.EvaluateAsync(req)` and project the result into a clean JSON shape.

## References

- ACS specification: <https://github.com/microsoft/agent-governance-toolkit/blob/main/policy-engine/spec/SPECIFICATION.md>
- .NET SDK README: <https://github.com/microsoft/agent-governance-toolkit/tree/main/policy-engine/sdk/dotnet>
- Rego examples: <https://github.com/microsoft/agent-governance-toolkit/tree/main/policy-engine/examples/lifecycle_rego>
