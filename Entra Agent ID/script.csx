using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 1: CONNECTOR ENTRY POINT                                          ║
// ║                                                                            ║
// ║  Microsoft Entra Agent ID — dual-purpose Power Platform custom connector.   ║
// ║                                                                            ║
// ║   1. MCP server  — POST /mcp (InvokeMCP) speaks JSON-RPC 2.0 and exposes    ║
// ║      73 agent identity tools, 7 resources, and 5 prompts to Copilot         ║
// ║      Studio, Agent Framework, and any other MCP client.                     ║
// ║   2. REST actions — every other operationId is a plain Microsoft Graph      ║
// ║      call forwarded verbatim, for Power Automate and Logic Apps.            ║
// ║                                                                            ║
// ║  Graph v1.0 for the agent identity lifecycle; beta only for agent risk,     ║
// ║  inherited permissions, Conditional Access what-if, and the agent registry. ║
// ║                                                                            ║
// ║  Both share one connection and one set of delegated Graph permissions.      ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    // ── Server Configuration ─────────────────────────────────────────────

    /// <summary>
    /// Application Insights connection string (leave empty to disable telemetry).
    /// Format: InstrumentationKey=YOUR-KEY;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/;...
    /// </summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    /// <summary>The Agent ID lifecycle is generally available; risk, inherited permissions, and the registry are beta.</summary>
    private const string GraphV1 = "https://graph.microsoft.com/v1.0";
    private const string GraphBeta = "https://graph.microsoft.com/beta";

    /// <summary>Reserved agent registry collections that exist in every tenant.</summary>
    private const string GlobalCollectionId = "00000000-0000-0000-0000-000000000001";
    private const string QuarantinedCollectionId = "00000000-0000-0000-0000-000000000002";

    /// <summary>
    /// REST operations that Microsoft publishes only under /beta. The connector's basePath is
    /// /v1.0, so ExecuteAsync rewrites the version segment for these before forwarding.
    /// </summary>
    private static readonly HashSet<string> BetaRestOperationIds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ListRiskyAgents",
        "GetRiskyAgent",
        "DismissRiskyAgents",
        "ConfirmRiskyAgentsCompromised",
        "ConfirmRiskyAgentsSafe",
        "ListAgentRiskDetections",
        "GetAgentRiskDetection",
        "ListAgentIdentityInheritedAppRoleAssignments",
        "ListAgentIdentityInheritedOAuth2PermissionGrants",
        "EvaluateConditionalAccess",
        "ListAgentInstances",
        "CreateAgentInstance",
        "GetAgentInstance",
        "UpdateAgentInstance",
        "DeleteAgentInstance",
        "GetAgentInstanceCardManifest",
        "ListAgentInstanceCollections",
        "AddAgentInstanceToCollection",
        "RemoveAgentInstanceFromCollection",
        "ListAgentCardManifests",
        "GetAgentCardManifest",
        "UpdateAgentCardManifest",
        "ListAgentCollections",
        "CreateAgentCollection",
        "GetAgentCollection",
        "UpdateAgentCollection",
        "ListAgentCollectionMembers"
    };

    /// <summary>OData type cast segments for the four Agent ID resource types.</summary>
    private const string BlueprintCast = "microsoft.graph.agentIdentityBlueprint";
    private const string BlueprintPrincipalCast = "microsoft.graph.agentIdentityBlueprintPrincipal";
    private const string AgentIdentityCast = "microsoft.graph.agentIdentity";
    private const string AgentUserCast = "microsoft.graph.agentUser";

    /// <summary>The passthrough tool refuses any path outside the Agent ID surface.</summary>
    private static readonly string[] AllowedPathPrefixes =
    {
        "/applications",
        "/servicePrincipals",
        "/users",
        "/groups",
        "/directoryObjects",
        "/directory/deletedItems",
        "/oauth2PermissionGrants",
        "/roleManagement",
        "/auditLogs",
        "/identityProtection",
        "/identity/conditionalAccess",
        "/identityGovernance",
        "/agentRegistry"
    };

    /// <summary>
    /// Microsoft Graph permissions that Entra refuses to grant to an agent identity.
    /// Including any of these in a blueprint's requiredResourceAccess returns HTTP 400,
    /// so check_blocked_permissions screens a proposed permission set before the call.
    /// </summary>
    private static readonly HashSet<string> BlockedAgentPermissions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AgentIdentity.Create",
        "AgentIdentity.Create.All",
        "AgentIdentity.CreateAsManager",
        "AgentIdentityBlueprint.Create",
        "AgentIdentityBlueprint.CreateAsManager",
        "AgentIdentityBlueprint.ReadWrite.All",
        "AgentIdentityBlueprintPrincipal.Create",
        "Application.ReadWrite.All",
        "Application.ReadWrite.OwnedBy",
        "AppRoleAssignment.ReadWrite.All",
        "BitlockerKey.Read.All",
        "Calendars.Read",
        "ChannelMessage.Read.All",
        "ChannelMessage.Read.Group",
        "Chat.Read.All",
        "Chat.ReadWrite.All",
        "CustomSecAttributeAssignment.ReadWrite.All",
        "CustomSecAttributeDefinition.ReadWrite.All",
        "DelegatedPermissionGrant.ReadWrite.All",
        "Directory.AccessAsUser.All",
        "Directory.ReadWrite.All",
        "Directory.Write.Restricted",
        "Domain.ReadWrite.All",
        "EntitlementManagement.ReadWrite.All",
        "Files.Read.All",
        "Files.ReadWrite.All",
        "Group.ReadWrite.All",
        "GroupMember.ReadWrite.All",
        "RoleManagement.ReadWrite.Directory",
        "Sites.Read.All",
        "Sites.ReadWrite.All",
        "User.DeleteRestore.All",
        "User.EnableDisableAccount.All",
        "User.ReadWrite.All",
        "UserAuthenticationMethod.ReadWrite.All"
    };

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "entra-agent-id-mcp-server",
            Version = "1.0.0",
            Title = "Microsoft Entra Agent ID",
            Description = "The Microsoft Entra Agent ID lifecycle through Microsoft Graph — blueprints, blueprint principals, agent identities, and agent users, with the owners, sponsors, credentials, permission grants, and restore operations that govern them."
        },

        ProtocolVersion = "2026-07-28",
        SupportedProtocolVersions = new List<string> { "2026-07-28", "2025-11-25", "2025-06-18" },

        Capabilities = new McpCapabilities
        {
            Tools = true,
            Resources = true,
            Prompts = true,
            Completions = true
        },

        ListCacheTtlMs = 300000,
        ListCacheScope = "public",
        ResourceCacheTtlMs = 60000,
        ResourceCacheScope = "private",
        DiscoverCacheTtlMs = 3600000,

        Instructions =
            "Use this server to manage Microsoft Entra Agent ID.\n\n" +
            "The model has four layers and they must be built in order:\n" +
            "1. A **blueprint** (`agentIdentityBlueprint`) is the application template for a class of agent. It carries the " +
            "permissions that agent identities inherit.\n" +
            "2. A **blueprint principal** (`agentIdentityBlueprintPrincipal`) records that blueprint's addition to a tenant. " +
            "It must exist in the tenant before any agent identity can be created from that blueprint.\n" +
            "3. An **agent identity** (`agentIdentity`) is what the agent actually authenticates as. Create it with the " +
            "blueprint's `appId` — not its object id — in `agentIdentityBlueprintId`.\n" +
            "4. An **agent user** (`agentUser`) is optional, and only needed when the agent must appear as a person: a " +
            "mailbox, a Teams presence, a place in the org chart. One agent identity may have at most one agent user.\n\n" +
            "Use `provision_agent` to run steps 2 through 4 in one call rather than sequencing them yourself.\n\n" +
            "Every blueprint and agent identity requires at least one **sponsor** — a named human or group accountable " +
            "for the agent. Sponsors cannot be service principals or agent users. On agent identities, Microsoft Graph " +
            "supports the sponsor collection with application permissions only, so `list_agent_identity_sponsors` and " +
            "its add and remove counterparts fail with 403 on a delegated connection; read sponsors through " +
            "`get_agent_overview` instead, which expands them from the identity itself.\n\n" +
            "Deletes are soft, and they cascade downward but not upward. Deleting a blueprint or blueprint principal " +
            "triggers an asynchronous background cleanup that soft-deletes every child agent identity and agent user — " +
            "this can lag by hours or days. Deleting an agent identity on its own does NOT remove its agent user, so " +
            "clean that up explicitly. Everything is restorable for 30 days through `list_deleted_agent_objects` and " +
            "`restore_agent_object`, but restoring a blueprint principal does not reverse cascade deletions that already " +
            "ran — each child must then be restored individually.\n\n" +
            "Before granting permissions to an agent, screen them with `check_blocked_permissions`. Entra refuses a long " +
            "list of high-risk Graph permissions for agents and returns an opaque 400 if you try.\n\n" +
            "For agents running outside Entra — on AWS, n8n, Kubernetes, or any OIDC platform — prefer workload identity " +
            "federation over a stored client secret. `add_blueprint_federated_credential` configures the trust, and the " +
            "`entra-agent-id://federation/patterns` resource explains which pattern suits which platform. Use " +
            "`list_inheritable_permissions` and `set_inheritable_permissions` to control the scopes agent identities pick " +
            "up from a blueprint without a separate consent prompt.\n\n" +
            "For anything this server does not name, use `agent_id_graph_request` to call the Graph endpoint directly.\n\n" +
            "Some tools call Microsoft Graph **beta**, which is subject to change and unsupported for production. These are " +
            "the risk tools (`list_risky_agents`, `list_agent_risk_detections`, `confirm_agents_compromised` and friends, " +
            "which additionally need a Microsoft Agent 365 license), `list_agent_inherited_permissions`, " +
            "`evaluate_conditional_access`, and the agent registry tools. Say so when you report their results.\n\n" +
            "Two governance traps worth knowing: an agent's effective permissions include what it inherits from its " +
            "blueprint, so `list_agent_identity_app_role_assignments` alone understates its reach — check " +
            "`list_agent_inherited_permissions` too. And confirming an agent compromised or quarantining its registry " +
            "entry does not stop it authenticating; only `set_agent_identity_enabled` does that."
    };

    // ── Entry Point ──────────────────────────────────────────────────────
    //
    //    InvokeMCP  -> the MCP JSON-RPC server.
    //    Everything else -> a direct Microsoft Graph v1.0 call, forwarded as-is.
    //

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        var operationId = this.Context.OperationId ?? string.Empty;

        try
        {
            HttpResponseMessage response;

            if (string.Equals(operationId, "InvokeMCP", StringComparison.OrdinalIgnoreCase))
            {
                response = await HandleMcpAsync(correlationId).ConfigureAwait(false);
            }
            else
            {
                if (BetaRestOperationIds.Contains(operationId))
                    SetGraphVersion(this.Context.Request, "beta");

                response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
            }

            var elapsed = DateTime.UtcNow - startTime;
            this.Context.Logger.LogInformation($"[{correlationId}] {operationId} -> {(int)response.StatusCode} in {elapsed.TotalMilliseconds}ms");
            _ = LogToAppInsights("OperationCompleted", new { OperationId = operationId, Status = (int)response.StatusCode, DurationMs = elapsed.TotalMilliseconds }, correlationId);

            return response;
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"[{correlationId}] {operationId} failed: {ex.Message}");
            _ = LogToAppInsights("OperationError", new { OperationId = operationId, Error = ex.Message }, correlationId);

            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(
                    new JObject { ["error"] = new JObject { ["message"] = ex.Message } }.ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8, "application/json")
            };
        }
    }

    private async Task<HttpResponseMessage> HandleMcpAsync(string correlationId)
    {
        var handler = new McpRequestHandler(Options);
        RegisterCapabilities(handler);

        handler.OnLog = (eventName, data) =>
        {
            this.Context.Logger.LogInformation($"[{correlationId}] {eventName}");
            _ = LogToAppInsights(eventName, data, correlationId);
        };

        var body = this.Context.Request.Content == null
            ? string.Empty
            : await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

        var result = await handler.HandleAsync(
            body,
            McpTransportHeaders.FromRequest(this.Context.Request),
            this.CancellationToken).ConfigureAwait(false);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result, Encoding.UTF8, "application/json")
        };
    }

    // ── Capability Registration ─────────────────────────────────────────

    private void RegisterCapabilities(McpRequestHandler handler)
    {
        RegisterBlueprintTools(handler);          // 13
        RegisterBlueprintPrincipalTools(handler); //  6
        RegisterFederationTools(handler);         //  6
        RegisterAgentIdentityTools(handler);      // 18
        RegisterAgentUserTools(handler);          // 10
        RegisterLifecycleTools(handler);          //  7
        RegisterRiskAndRegistryTools(handler);    // 13  (beta)
                                                  // ---
                                                  //  73

        RegisterResources(handler);
        RegisterPrompts(handler);
    }

    // ── A. Blueprints (13) ───────────────────────────────────────────────

    private void RegisterBlueprintTools(McpRequestHandler h)
    {
        h.AddTool("list_blueprints",
            "List the agent identity blueprints registered in the tenant. A blueprint is the application template that defines a class of agent and the permissions its agent identities inherit. Start here when the user asks what kinds of agent exist.",
            schema: s => s
                .String("filter", "OData $filter, for example \"startswith(displayName,'Sales')\"")
                .String("select", "Comma-separated properties to return, for example \"id,appId,displayName,createdDateTime\"")
                .String("expand", "Navigation properties to expand, for example \"owners\" or \"sponsors\"")
                .String("orderby", "Sort expression, for example \"displayName asc\"")
                .Integer("top", "Maximum blueprints to return (default 25, Graph page maximum 100)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync($"/applications/{BlueprintCast}" + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; a["openWorldHint"] = true; });

        h.AddTool("get_blueprint",
            "Get one agent identity blueprint by its object id, including its requested permissions, credentials, and publisher. Use this before creating agent identities from it to confirm what those agents will inherit.",
            schema: s => s
                .String("blueprint_id", "The blueprint's object id (the id property, not the appId)", required: true)
                .String("select", "Comma-separated properties to return; omit for the full record")
                .String("expand", "Navigation properties to expand, for example \"owners,sponsors\""),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_id");
                return await GraphGetAsync($"/applications/{Esc(id)}/{BlueprintCast}" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("create_blueprint",
            "Register a new agent identity blueprint. At least one sponsor is required, and the caller automatically becomes an owner. Creating a blueprint is a governance decision — confirm the display name, the sponsors, and the requested permissions with the user before calling this.",
            schema: s => s
                .String("display_name", "Human-readable name for the blueprint, maximum 256 characters", required: true)
                .Array("sponsor_ids", "Object ids of the users or groups accountable for this blueprint. At least one is required.", new JObject { ["type"] = "string" }, required: true)
                .String("description", "Free-text description of what agents built on this blueprint do, maximum 1024 characters")
                .String("sign_in_audience", "Which tenants may use the blueprint", enumValues: new[] { "AzureADMyOrg", "AzureADMultipleOrgs", "AzureADandPersonalMicrosoftAccount", "PersonalMicrosoftAccount" })
                .Array("tags", "Categorization tags for the blueprint", new JObject { ["type"] = "string" })
                .Array("sponsor_group_ids", "Object ids of groups to sponsor the blueprint, if you are supplying groups separately from users", new JObject { ["type"] = "string" }),
            handler: async (args, ct) =>
            {
                var body = new JObject { ["displayName"] = RequireArgument(args, "display_name") };

                var description = GetArgument(args, "description");
                if (!string.IsNullOrWhiteSpace(description)) body["description"] = description;

                var audience = GetArgument(args, "sign_in_audience");
                if (!string.IsNullOrWhiteSpace(audience)) body["signInAudience"] = audience;

                var tags = args["tags"] as JArray;
                if (tags != null && tags.Count > 0) body["tags"] = tags;

                body["sponsors@odata.bind"] = BuildSponsorBindings(
                    RequireArray(args, "sponsor_ids"), args["sponsor_group_ids"] as JArray);

                return await GraphSendAsync(HttpMethod.Post, $"/applications/{BlueprintCast}", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("update_blueprint",
            "Update a blueprint's branding or requested permissions. Password credentials cannot be set here — use add_blueprint_password. Changing requiredResourceAccess changes what every agent identity built on this blueprint inherits, so confirm before calling.",
            schema: s => s
                .String("blueprint_id", "The blueprint's object id", required: true)
                .String("display_name", "New display name")
                .String("description", "New description")
                .Array("tags", "Replacement set of categorization tags", new JObject { ["type"] = "string" })
                .String("required_resource_access_json", "Replacement requiredResourceAccess array as JSON, for example [{\"resourceAppId\":\"00000003-0000-0000-c000-000000000000\",\"resourceAccess\":[{\"id\":\"...\",\"type\":\"Role\"}]}]"),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_id");
                var body = new JObject();

                var displayName = GetArgument(args, "display_name");
                if (!string.IsNullOrWhiteSpace(displayName)) body["displayName"] = displayName;

                var description = GetArgument(args, "description");
                if (!string.IsNullOrWhiteSpace(description)) body["description"] = description;

                var tags = args["tags"] as JArray;
                if (tags != null) body["tags"] = tags;

                var access = GetArgument(args, "required_resource_access_json");
                if (!string.IsNullOrWhiteSpace(access))
                    body["requiredResourceAccess"] = ParseJsonArrayArgument(args, "required_resource_access_json");

                if (!body.HasValues)
                    throw new ArgumentException("Supply at least one property to update");

                return await GraphSendAsync(new HttpMethod("PATCH"), $"/applications/{Esc(id)}/{BlueprintCast}", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("delete_blueprint",
            "Soft-delete a blueprint. It stays restorable for 30 days. This triggers an asynchronous cascade that soft-deletes every blueprint principal, agent identity, and agent user derived from it — the cleanup can lag by hours or days, and restoring the blueprint afterwards does not bring the children back automatically. This is a wide-blast-radius action: enumerate the affected agents and confirm with the user before calling.",
            schema: s => s.String("blueprint_id", "The blueprint's object id", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_id");
                return await GraphSendAsync(HttpMethod.Delete, $"/applications/{Esc(id)}/{BlueprintCast}", null, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("add_blueprint_password",
            "Add a client secret to a blueprint and return its value. The secret is shown exactly once and can never be retrieved again, so surface it to the user immediately and tell them to store it securely. Prefer a certificate or federated credential over a secret for production agents.",
            schema: s => s
                .String("blueprint_id", "The blueprint's object id", required: true)
                .String("display_name", "Friendly name for the secret, for example \"Runtime secret 2026\"")
                .String("end_date_time", "Expiry in ISO 8601 format, for example 2027-01-01T00:00:00Z. Defaults to two years."),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_id");
                var credential = new JObject();

                var displayName = GetArgument(args, "display_name");
                if (!string.IsNullOrWhiteSpace(displayName)) credential["displayName"] = displayName;

                var endDate = GetArgument(args, "end_date_time");
                if (!string.IsNullOrWhiteSpace(endDate)) credential["endDateTime"] = endDate;

                var body = new JObject { ["passwordCredential"] = credential };
                return await GraphSendAsync(HttpMethod.Post, $"/applications/{Esc(id)}/{BlueprintCast}/addPassword", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("remove_blueprint_password",
            "Remove a client secret from a blueprint by its keyId. Any agent runtime still using that secret stops authenticating immediately.",
            schema: s => s
                .String("blueprint_id", "The blueprint's object id", required: true)
                .String("key_id", "The keyId of the password credential to remove", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_id");
                var body = new JObject { ["keyId"] = RequireArgument(args, "key_id") };
                return await GraphSendAsync(HttpMethod.Post, $"/applications/{Esc(id)}/{BlueprintCast}/removePassword", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = false; });

        h.AddTool("list_blueprint_owners",
            "List who may modify a blueprint. Owners can manage the blueprint and its agent identities without holding an Agent ID directory role, so this is the first thing to check in a governance review.",
            schema: s => s
                .String("blueprint_id", "The blueprint's object id", required: true)
                .String("select", "Comma-separated properties to return")
                .Integer("top", "Maximum owners to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_id");
                return await GraphGetAsync($"/applications/{Esc(id)}/{BlueprintCast}/owners" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("add_blueprint_owner",
            "Add an owner to a blueprint. Keep at least two owners so the blueprint never becomes orphaned when one person leaves.",
            schema: s => s
                .String("blueprint_id", "The blueprint's object id", required: true)
                .String("owner_id", "Object id of the user or service principal to add as owner", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_id");
                var body = DirectoryObjectRef(RequireArgument(args, "owner_id"));
                return await GraphSendAsync(HttpMethod.Post, $"/applications/{Esc(id)}/{BlueprintCast}/owners/$ref", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("remove_blueprint_owner",
            "Remove an owner from a blueprint. Check list_blueprint_owners first — removing the last owner leaves the blueprint manageable only by an Agent ID Administrator.",
            schema: s => s
                .String("blueprint_id", "The blueprint's object id", required: true)
                .String("owner_id", "Object id of the owner to remove", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_id");
                var ownerId = RequireArgument(args, "owner_id");
                return await GraphSendAsync(HttpMethod.Delete, $"/applications/{Esc(id)}/{BlueprintCast}/owners/{Esc(ownerId)}/$ref", null, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_blueprint_sponsors",
            "List the humans and groups accountable for a blueprint's lifecycle. Sponsors receive access package expiry notifications and approve renewals, so an unsponsored blueprint is a governance gap.",
            schema: s => s
                .String("blueprint_id", "The blueprint's object id", required: true)
                .String("select", "Comma-separated properties to return")
                .Integer("top", "Maximum sponsors to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_id");
                return await GraphGetAsync($"/applications/{Esc(id)}/{BlueprintCast}/sponsors" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("add_blueprint_sponsor",
            "Add a sponsor to a blueprint. Sponsors must be users or groups — never service principals or agent users.",
            schema: s => s
                .String("blueprint_id", "The blueprint's object id", required: true)
                .String("sponsor_id", "Object id of the user or group to add as sponsor", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_id");
                var body = DirectoryObjectRef(RequireArgument(args, "sponsor_id"));
                return await GraphSendAsync(HttpMethod.Post, $"/applications/{Esc(id)}/{BlueprintCast}/sponsors/$ref", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("remove_blueprint_sponsor",
            "Remove a sponsor from a blueprint. At least one sponsor must remain, so add the replacement before removing the outgoing one.",
            schema: s => s
                .String("blueprint_id", "The blueprint's object id", required: true)
                .String("sponsor_id", "Object id of the sponsor to remove", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_id");
                var sponsorId = RequireArgument(args, "sponsor_id");
                return await GraphSendAsync(HttpMethod.Delete, $"/applications/{Esc(id)}/{BlueprintCast}/sponsors/{Esc(sponsorId)}/$ref", null, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });
    }

    // ── B. Blueprint Principals (6) ──────────────────────────────────────

    private void RegisterBlueprintPrincipalTools(McpRequestHandler h)
    {
        h.AddTool("list_blueprint_principals",
            "List the blueprint principals in this tenant. A blueprint principal is the record of a blueprint being added here, and it must exist before any agent identity can be created from that blueprint. Use this to answer whether a given blueprint is available in the tenant.",
            schema: s => s
                .String("filter", "OData $filter, for example \"appId eq '00001111-aaaa-2222-bbbb-3333cccc4444'\"")
                .String("select", "Comma-separated properties to return")
                .String("expand", "Navigation properties to expand, for example \"owners,sponsors\"")
                .String("orderby", "Sort expression, for example \"displayName asc\"")
                .Integer("top", "Maximum blueprint principals to return (default 25, Graph page maximum 100)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync($"/servicePrincipals/{BlueprintPrincipalCast}" + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_blueprint_principal",
            "Get one blueprint principal by its object id, including the publisher, the app roles it exposes, and whether it is enabled.",
            schema: s => s
                .String("blueprint_principal_id", "The blueprint principal's object id", required: true)
                .String("select", "Comma-separated properties to return")
                .String("expand", "Navigation properties to expand, for example \"owners,sponsors\""),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_principal_id");
                return await GraphGetAsync($"/servicePrincipals/{Esc(id)}/{BlueprintPrincipalCast}" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("create_blueprint_principal",
            "Instantiate an existing blueprint into this tenant by its appId. Do this once per blueprint per tenant — it is the prerequisite for creating agent identities from that blueprint here. If the principal already exists, Graph returns an error rather than a duplicate.",
            schema: s => s
                .String("blueprint_app_id", "The appId of the blueprint to instantiate, not its object id", required: true)
                .String("display_name", "Display name for the blueprint principal in this tenant")
                .Boolean("app_role_assignment_required", "Whether users and service principals must hold an app role assignment to sign in"),
            handler: async (args, ct) =>
            {
                var body = new JObject { ["appId"] = RequireArgument(args, "blueprint_app_id") };

                var displayName = GetArgument(args, "display_name");
                if (!string.IsNullOrWhiteSpace(displayName)) body["displayName"] = displayName;

                if (args["app_role_assignment_required"] != null)
                    body["appRoleAssignmentRequired"] = args.Value<bool?>("app_role_assignment_required") ?? false;

                return await GraphSendAsync(HttpMethod.Post, $"/servicePrincipals/{BlueprintPrincipalCast}", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("update_blueprint_principal",
            "Update a blueprint principal. Setting account_enabled to false blocks every agent identity created from this blueprint in this tenant, so treat it as a tenant-wide kill switch and confirm before calling. Properties that synchronize from the blueprint may be overwritten on the next sync.",
            schema: s => s
                .String("blueprint_principal_id", "The blueprint principal's object id", required: true)
                .String("display_name", "New display name")
                .Boolean("account_enabled", "Set false to disable the blueprint principal tenant-wide")
                .Boolean("app_role_assignment_required", "Whether an app role assignment is required to sign in"),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_principal_id");
                var body = new JObject();

                var displayName = GetArgument(args, "display_name");
                if (!string.IsNullOrWhiteSpace(displayName)) body["displayName"] = displayName;

                if (args["account_enabled"] != null)
                    body["accountEnabled"] = args.Value<bool?>("account_enabled") ?? true;

                if (args["app_role_assignment_required"] != null)
                    body["appRoleAssignmentRequired"] = args.Value<bool?>("app_role_assignment_required") ?? false;

                if (!body.HasValues)
                    throw new ArgumentException("Supply at least one property to update");

                return await GraphSendAsync(new HttpMethod("PATCH"), $"/servicePrincipals/{Esc(id)}/{BlueprintPrincipalCast}", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("delete_blueprint_principal",
            "Soft-delete a blueprint principal, removing the blueprint from this tenant. Restorable for 30 days. Microsoft Entra then runs an asynchronous cascade that soft-deletes every agent identity and agent user created from it, so this stops all of those agents. Restoring the principal before the cleanup runs leaves the children untouched; restoring it afterwards does not undo the cascade, and each child must be restored individually. Permanently deleting the principal before the cleanup runs orphans its children — they cannot authenticate but still consume quota until the 30 day window expires.",
            schema: s => s.String("blueprint_principal_id", "The blueprint principal's object id", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_principal_id");
                return await GraphSendAsync(HttpMethod.Delete, $"/servicePrincipals/{Esc(id)}/{BlueprintPrincipalCast}", null, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_blueprint_principal_agents",
            "List the agent identities a blueprint principal has created. This is how you measure the blast radius before disabling or deleting the principal, because deleting it cascades to every agent listed here. Run this first whenever a user asks to remove a blueprint.",
            schema: s => s
                .String("blueprint_principal_id", "The blueprint principal's object id", required: true)
                .Integer("top", "Maximum objects to return (default 50)", defaultValue: 50),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_principal_id");
                return await GraphGetAsync($"/servicePrincipals/{Esc(id)}/{BlueprintPrincipalCast}/createdObjects" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }

    // ── C. Federation and Inherited Permissions (6) ──────────────────────
    //
    //    A blueprint doubles as a token factory. Workload identity federation
    //    lets an agent running on AWS, n8n, Kubernetes, or any OIDC platform
    //    exchange its native token for an Entra token, with no stored secret.
    //

    private void RegisterFederationTools(McpRequestHandler h)
    {
        h.AddTool("list_blueprint_federated_credentials",
            "List the workload identity federation trusts on a blueprint. Each one lets an external workload — an AWS role, a Kubernetes service account, a GitHub workflow, or another Entra principal — exchange its own token for an Entra token without a stored secret. Read this before adding a trust, because the issuer and subject pair must be unique.",
            schema: s => s
                .String("blueprint_id", "The blueprint's object id", required: true)
                .String("filter", "OData $filter, for example \"name eq 'aws-prod'\"")
                .Integer("top", "Maximum credentials to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_id");
                return await GraphGetAsync($"/applications/{Esc(id)}/federatedIdentityCredentials" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("add_blueprint_federated_credential",
            "Add a workload identity federation trust to a blueprint so an external agent can authenticate without a client secret. Prefer this over add_blueprint_password for anything running outside Entra. Use the entra_agent_identity platform for the token-factory pattern where the blueprint issues tokens for its own agent identities; use custom for AWS, n8n, Kubernetes, or any other OIDC provider, supplying the issuer and subject that provider puts in its tokens. A blueprint holds at most 20 credentials.",
            schema: s => s
                .String("blueprint_id", "The blueprint's object id", required: true)
                .String("name", "Unique, URL-friendly name for the trust, maximum 120 characters. Immutable once created.", required: true)
                .String("platform", "Which federation pattern to configure", required: true,
                    enumValues: new[] { "entra_agent_identity", "custom" })
                .String("tenant_id", "For entra_agent_identity: the Entra tenant GUID used to build the issuer URL")
                .String("agent_identity_id", "For entra_agent_identity: object id of the agent identity that becomes the token subject")
                .String("issuer", "For custom: the external identity provider URL, matching the issuer claim of its tokens")
                .String("subject", "For custom: the external workload identifier, matching the sub claim of its tokens")
                .String("audience", "Audience accepted in the aud claim. Defaults to api://AzureADTokenExchange, which is the value Microsoft Entra ID expects.")
                .String("description", "Free-text note describing what this trust is for, maximum 600 characters"),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_id");
                var platform = RequireArgument(args, "platform");

                string issuer;
                string subject;

                if (string.Equals(platform, "entra_agent_identity", StringComparison.OrdinalIgnoreCase))
                {
                    issuer = $"https://login.microsoftonline.com/{RequireArgumentFor(args, "tenant_id", platform)}/v2.0";
                    subject = RequireArgumentFor(args, "agent_identity_id", platform);
                }
                else
                {
                    issuer = RequireArgumentFor(args, "issuer", platform);
                    subject = RequireArgumentFor(args, "subject", platform);
                }

                var body = new JObject
                {
                    ["name"] = RequireArgument(args, "name"),
                    ["issuer"] = issuer,
                    ["subject"] = subject,
                    ["audiences"] = new JArray(GetArgument(args, "audience", "api://AzureADTokenExchange"))
                };

                AddIfPresent(body, args, "description", "description");

                return await GraphSendAsync(HttpMethod.Post, $"/applications/{Esc(id)}/federatedIdentityCredentials", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("remove_blueprint_federated_credential",
            "Remove a workload identity federation trust from a blueprint. Every external workload relying on it loses the ability to acquire tokens immediately, so confirm which agents depend on it first.",
            schema: s => s
                .String("blueprint_id", "The blueprint's object id", required: true)
                .String("federated_credential_id", "Object id of the federated identity credential to remove", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_id");
                var ficId = RequireArgument(args, "federated_credential_id");
                return await GraphSendAsync(HttpMethod.Delete, $"/applications/{Esc(id)}/federatedIdentityCredentials/{Esc(ficId)}", null, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_inheritable_permissions",
            "List the delegated scopes that agent identities inherit from a blueprint automatically, with no separate consent prompt. This is the fastest answer to \"what is every agent on this blueprint granted by default\", and it is the first place to look when an agent has more access than expected.",
            schema: s => s
                .String("blueprint_id", "The blueprint's object id", required: true)
                .String("filter", "OData $filter, for example \"resourceAppId eq '00000003-0000-0000-c000-000000000000'\""),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_id");
                return await GraphGetAsync($"/applications/{Esc(id)}/{BlueprintCast}/inheritablePermissions" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("set_inheritable_permissions",
            "Configure which delegated scopes of a resource application agent identities inherit automatically from a blueprint. Choose enumerated with an explicit scope list for least privilege; all_allowed inherits everything the resource publishes and is rarely the right answer. This widens or narrows access for every agent built on the blueprint at once, so state the effect and confirm before calling.",
            schema: s => s
                .String("blueprint_id", "The blueprint's object id", required: true)
                .String("resource_app_id", "The appId of the resource application, for example 00000003-0000-0000-c000-000000000000 for Microsoft Graph", required: true)
                .String("pattern", "Which inheritance pattern to apply", required: true,
                    enumValues: new[] { "enumerated", "all_allowed", "none" })
                .Array("scopes", "For the enumerated pattern: the delegated scopes to inherit, for example [\"User.Read\",\"Mail.Read\"]. Required and non-empty for that pattern.", new JObject { ["type"] = "string" })
                .Boolean("update_existing", "Update an existing rule for this resource instead of creating a new one"),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_id");
                var resourceAppId = RequireArgument(args, "resource_app_id");
                var pattern = RequireArgument(args, "pattern");

                var scopes = new JObject();
                if (string.Equals(pattern, "enumerated", StringComparison.OrdinalIgnoreCase))
                {
                    scopes["@odata.type"] = "microsoft.graph.enumeratedScopes";
                    scopes["scopes"] = RequireArray(args, "scopes");
                }
                else if (string.Equals(pattern, "all_allowed", StringComparison.OrdinalIgnoreCase))
                {
                    scopes["@odata.type"] = "microsoft.graph.allAllowedScopes";
                }
                else if (string.Equals(pattern, "none", StringComparison.OrdinalIgnoreCase))
                {
                    scopes["@odata.type"] = "microsoft.graph.noScopes";
                }
                else
                {
                    throw new ArgumentException($"'pattern' must be enumerated, all_allowed, or none, not '{pattern}'");
                }

                var basePath = $"/applications/{Esc(id)}/{BlueprintCast}/inheritablePermissions";

                if (args.Value<bool?>("update_existing") == true)
                {
                    return await GraphSendAsync(new HttpMethod("PATCH"),
                        $"{basePath}/{Esc(resourceAppId)}",
                        new JObject { ["inheritableScopes"] = scopes }, ct);
                }

                return await GraphSendAsync(HttpMethod.Post, basePath, new JObject
                {
                    ["resourceAppId"] = resourceAppId,
                    ["inheritableScopes"] = scopes
                }, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("remove_inheritable_permission",
            "Remove an inheritance rule from a blueprint. Agent identities stop inheriting those scopes automatically and must obtain fresh consent if they still need them, so existing agents may start failing — confirm before calling.",
            schema: s => s
                .String("blueprint_id", "The blueprint's object id", required: true)
                .String("resource_app_id", "The appId of the resource application whose rule should be removed", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "blueprint_id");
                var resourceAppId = RequireArgument(args, "resource_app_id");
                return await GraphSendAsync(HttpMethod.Delete,
                    $"/applications/{Esc(id)}/{BlueprintCast}/inheritablePermissions/{Esc(resourceAppId)}", null, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });
    }

    // ── D. Agent Identities (18) ─────────────────────────────────────────

    private void RegisterAgentIdentityTools(McpRequestHandler h)
    {
        h.AddTool("list_agent_identities",
            "List the agent identities in the tenant. An agent identity is what an agent authenticates as, and it is the object Conditional Access, sign-in logs, and access reviews act on. This is the main inventory tool — start here for \"what agents do we have\".",
            schema: s => s
                .String("filter", "OData $filter, for example \"accountEnabled eq false\" or \"startswith(displayName,'Sales')\"")
                .String("select", "Comma-separated properties to return, for example \"id,displayName,accountEnabled,agentIdentityBlueprintId,createdDateTime\"")
                .String("expand", "Navigation properties to expand, for example \"owners\"")
                .String("orderby", "Sort expression, for example \"displayName asc\"")
                .Integer("top", "Maximum agent identities to return (default 25, Graph page maximum 100)", defaultValue: 25)
                .Boolean("count", "Return the total matching count alongside the page"),
            handler: async (args, ct) => await GraphGetAsync($"/servicePrincipals/{AgentIdentityCast}" + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; a["openWorldHint"] = true; });

        h.AddTool("search_agent_identities",
            "Find agent identities by a free-text term matched against the display name. Use this when the user names an agent rather than supplying a GUID, then pass the resolved id to the other tools.",
            schema: s => s
                .String("query", "Search term, for example part of the agent's display name", required: true)
                .Integer("top", "Maximum agent identities to return (default 25)", defaultValue: 25)
                .String("select", "Comma-separated properties to return"),
            handler: async (args, ct) =>
            {
                var q = RequireArgument(args, "query").Replace("'", "''");
                var top = args.Value<int?>("top") ?? 25;
                var select = GetArgument(args, "select");
                var path = $"/servicePrincipals/{AgentIdentityCast}?$filter={Uri.EscapeDataString($"startswith(displayName,'{q}')")}&$top={top}";
                if (!string.IsNullOrWhiteSpace(select)) path += "&$select=" + Uri.EscapeDataString(select);
                return await GraphGetAsync(path, ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_agent_identity",
            "Get one agent identity by its object id. Ask for the sponsors and owners through the expand argument to see the accountability chain in a single call.",
            schema: s => s
                .String("agent_identity_id", "The agent identity's object id", required: true)
                .String("select", "Comma-separated properties to return; omit for the default set")
                .String("expand", "Navigation properties to expand, for example \"owners,sponsors\""),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_identity_id");
                return await GraphGetAsync($"/servicePrincipals/{Esc(id)}/{AgentIdentityCast}" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_agent_overview",
            "Build a complete governance picture of one agent in a single call: its identity, the blueprint behind it, its owners and sponsors, its app role grants, its group memberships, and its agent user if it has one. Use this to answer \"who is accountable for this agent and what can it reach\".",
            schema: s => s
                .String("agent_identity_id", "The agent identity's object id", required: true),
            handler: async (args, ct) =>
            {
                var id = Esc(RequireArgument(args, "agent_identity_id"));
                var overview = new JObject();

                overview["identity"] = await GraphGetAsync(
                    $"/servicePrincipals/{id}/{AgentIdentityCast}?$expand=owners,sponsors", ct).ConfigureAwait(false);

                overview["appRoleAssignments"] = await TryGraphGetAsync($"/servicePrincipals/{id}/appRoleAssignments", ct).ConfigureAwait(false);
                overview["oauth2PermissionGrants"] = await TryGraphGetAsync($"/servicePrincipals/{id}/oauth2PermissionGrants", ct).ConfigureAwait(false);
                overview["memberOf"] = await TryGraphGetAsync($"/servicePrincipals/{id}/{AgentIdentityCast}/memberOf", ct).ConfigureAwait(false);

                var blueprintAppId = overview["identity"]?["agentIdentityBlueprintId"]?.ToString();
                if (!string.IsNullOrWhiteSpace(blueprintAppId))
                {
                    overview["blueprint"] = await TryGraphGetAsync(
                        $"/applications/{BlueprintCast}?$filter={Uri.EscapeDataString($"appId eq '{blueprintAppId}'")}", ct).ConfigureAwait(false);
                }

                overview["agentUser"] = await TryGraphGetAsync(
                    $"/users/{AgentUserCast}?$filter={Uri.EscapeDataString($"identityParentId eq '{RequireArgument(args, "agent_identity_id")}'")}", ct).ConfigureAwait(false);

                return overview;
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("create_agent_identity",
            "Create an agent identity from a blueprint. Supply the blueprint's appId — not its object id — and at least one sponsor. A blueprint principal for that blueprint must already exist in this tenant; create_blueprint_principal handles that. Prefer provision_agent when you also need an agent user.",
            schema: s => s
                .String("display_name", "Human-readable name for the agent", required: true)
                .String("blueprint_app_id", "The appId of the blueprint this agent is built on", required: true)
                .Array("sponsor_ids", "Object ids of the users accountable for this agent. At least one is required.", new JObject { ["type"] = "string" }, required: true)
                .Array("sponsor_group_ids", "Object ids of groups to sponsor the agent. Maximum 5, and role-assignable groups are rejected.", new JObject { ["type"] = "string" })
                .Boolean("account_enabled", "Whether the agent may authenticate straight away")
                .Array("tags", "Categorization tags for the agent", new JObject { ["type"] = "string" }),
            handler: async (args, ct) => await CreateAgentIdentityAsync(args, ct),
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("update_agent_identity",
            "Rename an agent identity or change its tags.",
            schema: s => s
                .String("agent_identity_id", "The agent identity's object id", required: true)
                .String("display_name", "New display name")
                .Array("tags", "Replacement set of categorization tags", new JObject { ["type"] = "string" }),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_identity_id");
                var body = new JObject();

                var displayName = GetArgument(args, "display_name");
                if (!string.IsNullOrWhiteSpace(displayName)) body["displayName"] = displayName;

                var tags = args["tags"] as JArray;
                if (tags != null) body["tags"] = tags;

                if (!body.HasValues)
                    throw new ArgumentException("Supply at least one property to update");

                return await GraphSendAsync(new HttpMethod("PATCH"), $"/servicePrincipals/{Esc(id)}/{AgentIdentityCast}", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("set_agent_identity_enabled",
            "Enable or disable an agent identity. Disabling blocks every sign-in immediately and is the correct first response to a misbehaving or compromised agent — it is reversible, unlike deletion. Use this before reaching for delete_agent_identity.",
            schema: s => s
                .String("agent_identity_id", "The agent identity's object id", required: true)
                .Boolean("enabled", "True to allow the agent to authenticate, false to block it", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_identity_id");
                if (args["enabled"] == null)
                    throw new ArgumentException("'enabled' is required");

                var body = new JObject { ["accountEnabled"] = args.Value<bool>("enabled") };
                return await GraphSendAsync(new HttpMethod("PATCH"), $"/servicePrincipals/{Esc(id)}/{AgentIdentityCast}", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("delete_agent_identity",
            "Soft-delete an agent identity. Restorable for 30 days. The linked agent user, if any, is NOT deleted and keeps its mailbox and group memberships — call delete_agent_user separately. Prefer set_agent_identity_enabled for anything short of permanent decommissioning.",
            schema: s => s.String("agent_identity_id", "The agent identity's object id", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_identity_id");
                return await GraphSendAsync(HttpMethod.Delete, $"/servicePrincipals/{Esc(id)}/{AgentIdentityCast}", null, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_agent_identity_owners",
            "List who may modify an agent identity. Owners manage it without needing an Agent ID directory role, so an unexpected owner is a privilege escalation path worth flagging.",
            schema: s => s
                .String("agent_identity_id", "The agent identity's object id", required: true)
                .String("select", "Comma-separated properties to return")
                .Integer("top", "Maximum owners to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_identity_id");
                return await GraphGetAsync($"/servicePrincipals/{Esc(id)}/{AgentIdentityCast}/owners" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("add_agent_identity_owner",
            "Add an owner to an agent identity. Keep at least two owners.",
            schema: s => s
                .String("agent_identity_id", "The agent identity's object id", required: true)
                .String("owner_id", "Object id of the user or service principal to add as owner", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_identity_id");
                var body = DirectoryObjectRef(RequireArgument(args, "owner_id"));
                return await GraphSendAsync(HttpMethod.Post, $"/servicePrincipals/{Esc(id)}/{AgentIdentityCast}/owners/$ref", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("remove_agent_identity_owner",
            "Remove an owner from an agent identity.",
            schema: s => s
                .String("agent_identity_id", "The agent identity's object id", required: true)
                .String("owner_id", "Object id of the owner to remove", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_identity_id");
                var ownerId = RequireArgument(args, "owner_id");
                return await GraphSendAsync(HttpMethod.Delete, $"/servicePrincipals/{Esc(id)}/{AgentIdentityCast}/owners/{Esc(ownerId)}/$ref", null, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_agent_identity_sponsors",
            "List the humans accountable for an agent identity. Microsoft Graph supports this collection with application permissions only, so on a delegated connection it returns 403 — fall back to get_agent_overview, which reads sponsors by expanding them from the identity itself.",
            schema: s => s
                .String("agent_identity_id", "The agent identity's object id", required: true)
                .Integer("top", "Maximum sponsors to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_identity_id");
                return await GraphGetAsync($"/servicePrincipals/{Esc(id)}/{AgentIdentityCast}/sponsors" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("add_agent_identity_sponsor",
            "Add a sponsor to an agent identity. Application permissions only — a delegated connection returns 403. Maximum 100 sponsors, of which at most 5 may be groups, and role-assignable groups are rejected.",
            schema: s => s
                .String("agent_identity_id", "The agent identity's object id", required: true)
                .String("sponsor_id", "Object id of the user or group to add as sponsor", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_identity_id");
                var body = DirectoryObjectRef(RequireArgument(args, "sponsor_id"));
                return await GraphSendAsync(HttpMethod.Post, $"/servicePrincipals/{Esc(id)}/{AgentIdentityCast}/sponsors/$ref", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("remove_agent_identity_sponsor",
            "Remove a sponsor from an agent identity. Application permissions only. At least one sponsor must remain, so add the replacement first — this is the usual sequence when a sponsor leaves the organization.",
            schema: s => s
                .String("agent_identity_id", "The agent identity's object id", required: true)
                .String("sponsor_id", "Object id of the sponsor to remove", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_identity_id");
                var sponsorId = RequireArgument(args, "sponsor_id");
                return await GraphSendAsync(HttpMethod.Delete, $"/servicePrincipals/{Esc(id)}/{AgentIdentityCast}/sponsors/{Esc(sponsorId)}/$ref", null, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_agent_identity_memberships",
            "List the groups and directory roles an agent identity belongs to. Group membership is how agents usually pick up resource access, so this is the second thing to check after app role assignments.",
            schema: s => s
                .String("agent_identity_id", "The agent identity's object id", required: true)
                .Boolean("transitive", "Include nested group memberships rather than direct memberships only")
                .Integer("top", "Maximum groups to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_identity_id");
                var relationship = args.Value<bool?>("transitive") == true ? "transitiveMemberOf" : "memberOf";
                return await GraphGetAsync($"/servicePrincipals/{Esc(id)}/{AgentIdentityCast}/{relationship}" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_agent_identity_app_role_assignments",
            "List the application permissions an agent identity holds on other applications. This is the definitive answer to \"what can this agent actually do\" — read it before approving or renewing an agent.",
            schema: s => s
                .String("agent_identity_id", "The agent identity's object id", required: true)
                .Integer("top", "Maximum assignments to return (default 50)", defaultValue: 50),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_identity_id");
                return await GraphGetAsync($"/servicePrincipals/{Esc(id)}/appRoleAssignments" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("grant_agent_identity_app_role",
            "Grant an application permission to an agent identity. This expands what the agent can reach without a human in the loop, so state the permission and the target resource plainly and get confirmation first. Screen the permission with check_blocked_permissions — Entra rejects the high-risk ones with an opaque 400.",
            schema: s => s
                .String("agent_identity_id", "Object id of the agent identity receiving the permission", required: true)
                .String("resource_id", "Object id of the service principal that exposes the app role, for example the Microsoft Graph service principal", required: true)
                .String("app_role_id", "Id of the app role to grant. Use 00000000-0000-0000-0000-000000000000 for a default assignment.", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_identity_id");
                var body = new JObject
                {
                    ["principalId"] = id,
                    ["resourceId"] = RequireArgument(args, "resource_id"),
                    ["appRoleId"] = RequireArgument(args, "app_role_id")
                };
                return await GraphSendAsync(HttpMethod.Post, $"/servicePrincipals/{Esc(id)}/appRoleAssignments", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("revoke_agent_identity_app_role",
            "Revoke an application permission previously granted to an agent identity. Call list_agent_identity_app_role_assignments first to get the assignment id.",
            schema: s => s
                .String("agent_identity_id", "The agent identity's object id", required: true)
                .String("app_role_assignment_id", "Id of the app role assignment to revoke", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_identity_id");
                var assignmentId = RequireArgument(args, "app_role_assignment_id");
                return await GraphSendAsync(HttpMethod.Delete, $"/servicePrincipals/{Esc(id)}/appRoleAssignments/{Esc(assignmentId)}", null, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });
    }

    // ── E. Agent Users (10) ───────────────────────────────────────────────

    private void RegisterAgentUserTools(McpRequestHandler h)
    {
        h.AddTool("list_agent_users",
            "List the agent users in the tenant. An agent user is the optional user-shaped account an agent gets when it needs a mailbox, a Teams presence, or a place in the org chart. Note that $skip is not supported here — page with the returned nextLink.",
            schema: s => s
                .String("filter", "OData $filter, for example \"accountEnabled eq false\" or \"identityParentId eq '{agentIdentityId}'\"")
                .String("select", "Comma-separated properties to return, for example \"id,displayName,userPrincipalName,identityParentId,accountEnabled\"")
                .String("orderby", "Sort expression, for example \"displayName asc\"")
                .Integer("top", "Maximum agent users to return (default 25)", defaultValue: 25)
                .Boolean("count", "Return the total matching count alongside the page"),
            handler: async (args, ct) => await GraphGetAsync($"/users/{AgentUserCast}" + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_agent_user",
            "Get one agent user by its object id, including its UPN, its parent agent identity, and its profile fields.",
            schema: s => s
                .String("agent_user_id", "The agent user's object id", required: true)
                .String("select", "Comma-separated properties to return; omit for the default set")
                .String("expand", "Navigation properties to expand, for example \"manager\""),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_user_id");
                return await GraphGetAsync($"/users/{AgentUserCast}/{Esc(id)}" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("create_agent_user",
            "Create an agent user bound to an existing agent identity. The UPN domain must already be verified in the tenant, and an agent identity may have only one agent user — a second attempt returns 400. Agent users cannot authenticate with a password by design.",
            schema: s => s
                .String("display_name", "Name shown in the address book, maximum 256 characters", required: true)
                .String("user_principal_name", "UPN in the form alias@verifieddomain. No accented characters.", required: true)
                .String("mail_nickname", "Mail alias, maximum 64 characters", required: true)
                .String("agent_identity_id", "Object id of the agent identity this user belongs to", required: true)
                .Boolean("account_enabled", "Whether the account is active. Defaults to true.")
                .String("job_title", "Job title, for example \"Automated sales assistant\"")
                .String("department", "Department the agent reports into")
                .String("usage_location", "Two-letter ISO 3166 country code, required before assigning licenses")
                .String("manager_id", "Object id of the human manager to place this agent under in the org chart"),
            handler: async (args, ct) => await CreateAgentUserAsync(args, ct),
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("update_agent_user",
            "Update an agent user's profile or enabled state. The parent agent identity cannot be changed after creation.",
            schema: s => s
                .String("agent_user_id", "The agent user's object id", required: true)
                .String("display_name", "New display name")
                .String("job_title", "New job title")
                .String("department", "New department")
                .String("usage_location", "Two-letter ISO 3166 country code")
                .String("employee_type", "Employee category, for example Agent or Contractor")
                .Boolean("account_enabled", "Set false to block the agent user account"),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_user_id");
                var body = new JObject { ["@odata.type"] = "#microsoft.graph.agentUser" };

                AddIfPresent(body, args, "displayName", "display_name");
                AddIfPresent(body, args, "jobTitle", "job_title");
                AddIfPresent(body, args, "department", "department");
                AddIfPresent(body, args, "usageLocation", "usage_location");
                AddIfPresent(body, args, "employeeType", "employee_type");

                if (args["account_enabled"] != null)
                    body["accountEnabled"] = args.Value<bool?>("account_enabled") ?? true;

                if (body.Properties().Count() == 1)
                    throw new ArgumentException("Supply at least one property to update");

                return await GraphSendAsync(new HttpMethod("PATCH"), $"/users/{AgentUserCast}/{Esc(id)}", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("delete_agent_user",
            "Delete an agent user. Restorable for 30 days. This does not delete the parent agent identity, which keeps authenticating — delete_agent_identity handles that half.",
            schema: s => s.String("agent_user_id", "The agent user's object id", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_user_id");
                return await GraphSendAsync(HttpMethod.Delete, $"/users/{AgentUserCast}/{Esc(id)}", null, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_agent_user_sponsors",
            "List the humans and groups accountable for an agent user's privileges.",
            schema: s => s
                .String("agent_user_id", "The agent user's object id", required: true)
                .Integer("top", "Maximum sponsors to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_user_id");
                return await GraphGetAsync($"/users/{Esc(id)}/sponsors" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("add_agent_user_sponsor",
            "Add a sponsor to an agent user.",
            schema: s => s
                .String("agent_user_id", "The agent user's object id", required: true)
                .String("sponsor_id", "Object id of the user or group to add as sponsor", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_user_id");
                var body = DirectoryObjectRef(RequireArgument(args, "sponsor_id"));
                return await GraphSendAsync(HttpMethod.Post, $"/users/{Esc(id)}/sponsors/$ref", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("set_agent_user_manager",
            "Assign a human manager to an agent user, placing the agent in that person's organizational hierarchy. This is what makes an agent show up under a manager in Teams and in access reviews.",
            schema: s => s
                .String("agent_user_id", "The agent user's object id", required: true)
                .String("manager_id", "Object id of the user to assign as manager", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_user_id");
                var body = new JObject
                {
                    ["@odata.id"] = $"{GraphV1}/users/{Esc(RequireArgument(args, "manager_id"))}"
                };
                return await GraphSendAsync(HttpMethod.Post, $"/users/{Esc(id)}/manager/$ref", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("list_agent_user_memberships",
            "List the groups, directory roles, and administrative units an agent user belongs to. Agent users often pick up resource access through group membership rather than explicit grants, so check this alongside the agent identity's own permissions when auditing what an agent can reach.",
            schema: s => s
                .String("agent_user_id", "The agent user's object id", required: true)
                .Boolean("transitive", "Include nested group memberships rather than direct memberships only")
                .Integer("top", "Maximum groups to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_user_id");
                var relationship = args.Value<bool?>("transitive") == true ? "transitiveMemberOf" : "memberOf";
                return await GraphGetAsync($"/users/{Esc(id)}/{relationship}" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("remove_agent_user_manager",
            "Remove the manager assigned to an agent user, detaching it from the org chart.",
            schema: s => s.String("agent_user_id", "The agent user's object id", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_user_id");
                return await GraphSendAsync(HttpMethod.Delete, $"/users/{Esc(id)}/manager/$ref", null, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });
    }

    // ── F. Lifecycle, Governance, and Passthrough (7) ────────────────────

    private void RegisterLifecycleTools(McpRequestHandler h)
    {
        h.AddTool("provision_agent",
            "Provision a complete agent in one call: instantiate the blueprint into this tenant if needed, create the agent identity with its sponsors, and optionally create the agent user and assign its manager. This performs the whole ordered sequence and reports what it did at each step, including any step that was already satisfied. Confirm the blueprint, the display name, and the sponsors with the user before calling.",
            schema: s => s
                .String("display_name", "Human-readable name for the agent", required: true)
                .String("blueprint_app_id", "The appId of the blueprint to build the agent on", required: true)
                .Array("sponsor_ids", "Object ids of the users accountable for this agent. At least one is required.", new JObject { ["type"] = "string" }, required: true)
                .Array("sponsor_group_ids", "Object ids of groups to sponsor the agent. Maximum 5.", new JObject { ["type"] = "string" })
                .Boolean("create_agent_user", "Also create an agent user so the agent has a mailbox and a place in the org chart")
                .String("user_principal_name", "UPN for the agent user, required when create_agent_user is true")
                .String("mail_nickname", "Mail alias for the agent user. Defaults to the display name with spaces removed.")
                .String("manager_id", "Object id of the human manager for the agent user")
                .String("job_title", "Job title for the agent user"),
            handler: async (args, ct) =>
            {
                var steps = new JArray();
                var blueprintAppId = RequireArgument(args, "blueprint_app_id");

                // Step 1 — make sure the blueprint exists in this tenant.
                var existing = await TryGraphGetAsync(
                    $"/servicePrincipals/{BlueprintPrincipalCast}?$filter={Uri.EscapeDataString($"appId eq '{blueprintAppId}'")}", ct).ConfigureAwait(false);

                var alreadyPresent = (existing?["value"] as JArray)?.Count > 0;
                if (alreadyPresent)
                {
                    steps.Add(new JObject
                    {
                        ["step"] = "blueprintPrincipal",
                        ["action"] = "reused",
                        ["blueprintPrincipal"] = existing["value"][0]
                    });
                }
                else
                {
                    var created = await GraphSendAsync(HttpMethod.Post, $"/servicePrincipals/{BlueprintPrincipalCast}",
                        new JObject { ["appId"] = blueprintAppId }, ct).ConfigureAwait(false);
                    steps.Add(new JObject { ["step"] = "blueprintPrincipal", ["action"] = "created", ["blueprintPrincipal"] = created });
                }

                // Step 2 — the agent identity itself.
                var identity = await CreateAgentIdentityAsync(args, ct).ConfigureAwait(false);
                steps.Add(new JObject { ["step"] = "agentIdentity", ["action"] = "created", ["agentIdentity"] = identity });

                var identityId = identity["id"]?.ToString();

                // Step 3 — the optional agent user, and its optional manager.
                if (args.Value<bool?>("create_agent_user") == true)
                {
                    if (string.IsNullOrWhiteSpace(identityId))
                        throw new Exception("The agent identity was created but returned no id, so the agent user cannot be linked to it.");

                    var userArgs = new JObject(args);
                    userArgs["agent_identity_id"] = identityId;

                    if (string.IsNullOrWhiteSpace(GetArgument(args, "mail_nickname")))
                        userArgs["mail_nickname"] = RequireArgument(args, "display_name").Replace(" ", string.Empty);

                    var agentUser = await CreateAgentUserAsync(userArgs, ct).ConfigureAwait(false);
                    steps.Add(new JObject { ["step"] = "agentUser", ["action"] = "created", ["agentUser"] = agentUser });
                }

                return new JObject
                {
                    ["agentIdentityId"] = identityId,
                    ["displayName"] = RequireArgument(args, "display_name"),
                    ["steps"] = steps
                };
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("check_blocked_permissions",
            "Screen a proposed set of Microsoft Graph permissions against the list Entra refuses to grant to agents. Run this before grant_agent_identity_app_role or before putting permissions in a blueprint — Entra rejects a blocked permission with an opaque HTTP 400 that does not name the offender.",
            schema: s => s
                .Array("permissions", "Permission names to check, for example [\"Mail.Read\",\"Directory.ReadWrite.All\"]", new JObject { ["type"] = "string" }, required: true),
            handler: async (args, ct) =>
            {
                var permissions = RequireArray(args, "permissions");
                var blocked = new JArray();
                var allowed = new JArray();

                foreach (var permission in permissions)
                {
                    var name = permission.ToString().Trim();
                    if (BlockedAgentPermissions.Contains(name)) blocked.Add(name);
                    else allowed.Add(name);
                }

                return await Task.FromResult(new JObject
                {
                    ["blocked"] = blocked,
                    ["allowed"] = allowed,
                    ["anyBlocked"] = blocked.Count > 0,
                    ["note"] = blocked.Count > 0
                        ? "Entra rejects the blocked permissions above for agent identities. Remove them and request a narrower scope, or design the agent to act on behalf of a user instead."
                        : "None of these permissions are on the documented block list. They may still require admin consent."
                });
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_deleted_agent_objects",
            "List soft-deleted agent objects still inside the 30 day restore window. Use this to answer \"can we get that agent back\" and to find the object id needed by restore_agent_object.",
            schema: s => s
                .String("object_type", "Which kind of deleted object to list", required: true,
                    enumValues: new[] { "agentIdentity", "agentIdentityBlueprint", "agentIdentityBlueprintPrincipal", "agentUser" })
                .Integer("top", "Maximum objects to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var objectType = RequireArgument(args, "object_type");
                string cast;
                if (string.Equals(objectType, "agentIdentity", StringComparison.OrdinalIgnoreCase)) cast = AgentIdentityCast;
                else if (string.Equals(objectType, "agentIdentityBlueprint", StringComparison.OrdinalIgnoreCase)) cast = BlueprintCast;
                else if (string.Equals(objectType, "agentIdentityBlueprintPrincipal", StringComparison.OrdinalIgnoreCase)) cast = BlueprintPrincipalCast;
                else if (string.Equals(objectType, "agentUser", StringComparison.OrdinalIgnoreCase)) cast = AgentUserCast;
                else throw new ArgumentException($"'object_type' must be agentIdentity, agentIdentityBlueprint, agentIdentityBlueprintPrincipal, or agentUser, not '{objectType}'");

                return await GraphGetAsync($"/directory/deletedItems/{cast}" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("restore_agent_object",
            "Restore a soft-deleted blueprint, blueprint principal, agent identity, or agent user. Only works inside the 30 day window; after that the object is gone permanently.",
            schema: s => s.String("object_id", "Object id of the deleted item to restore", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "object_id");
                return await GraphSendAsync(HttpMethod.Post, $"/directory/deletedItems/{Esc(id)}/restore", null, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("permanently_delete_agent_object",
            "Permanently delete a soft-deleted agent object before its 30 day window expires. This is irreversible and cannot be undone by any administrator — always confirm the exact object with the user first.",
            schema: s => s.String("object_id", "Object id of the deleted item to purge", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "object_id");
                return await GraphSendAsync(HttpMethod.Delete, $"/directory/deletedItems/{Esc(id)}", null, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("find_agent_by_user_principal_name",
            "Resolve an agent from its agent user's UPN back to the agent identity behind it. Use this when the user names an agent by its email address rather than its display name.",
            schema: s => s.String("user_principal_name", "The agent user's UPN, for example salesagent@contoso.com", required: true),
            handler: async (args, ct) =>
            {
                var upn = RequireArgument(args, "user_principal_name").Replace("'", "''");
                var users = await GraphGetAsync(
                    $"/users/{AgentUserCast}?$filter={Uri.EscapeDataString($"userPrincipalName eq '{upn}'")}", ct).ConfigureAwait(false);

                var match = (users["value"] as JArray)?.FirstOrDefault();
                if (match == null)
                    return new JObject { ["found"] = false, ["message"] = $"No agent user has the UPN '{upn}'." };

                var result = new JObject { ["found"] = true, ["agentUser"] = match };

                var parentId = match["identityParentId"]?.ToString();
                if (!string.IsNullOrWhiteSpace(parentId))
                {
                    result["agentIdentity"] = await TryGraphGetAsync(
                        $"/servicePrincipals/{Esc(parentId)}/{AgentIdentityCast}", ct).ConfigureAwait(false);
                }

                return result;
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("agent_id_graph_request",
            "Call any Microsoft Graph endpoint inside the identity surface directly. Use this for anything the named tools do not cover — Conditional Access policies, sign-in logs filtered by agentType, access packages, directory roles. Paths are restricted to the identity surface, so this cannot be used to read mail or files.",
            schema: s => s
                .String("method", "HTTP method", required: true, enumValues: new[] { "GET", "POST", "PATCH", "PUT", "DELETE" })
                .String("path", "Graph path beginning with a slash and excluding the version, for example /servicePrincipals/microsoft.graph.agentIdentity?$top=5", required: true)
                .String("version", "Which Graph version to call. Defaults to v1.0; use beta for risk, inherited permissions, and registry endpoints.", enumValues: new[] { "v1.0", "beta" })
                .String("body_json", "Request body as a JSON object string, for methods that take one"),
            handler: async (args, ct) =>
            {
                var method = RequireArgument(args, "method").ToUpperInvariant();
                var path = NormalizeGraphPath(RequireArgument(args, "path"));
                var version = GetArgument(args, "version", "v1.0");
                var body = ParseJsonArgument(args, "body_json");

                return await GraphSendAsync(new HttpMethod(method), path, body, ct, version);
            },
            annotations: a => { a["readOnlyHint"] = false; a["openWorldHint"] = true; a["idempotentHint"] = false; });
    }

    // ── Resources ────────────────────────────────────────────────────────

    private void RegisterResources(McpRequestHandler handler)
    {
        handler.AddResource("entra-agent-id://model/lifecycle", "Agent ID Lifecycle Model",
            "The four layers of Microsoft Entra Agent ID, the order they must be created in, and how deletion behaves.",
            handler: async (ct) => new JArray
            {
                new JObject
                {
                    ["uri"] = "entra-agent-id://model/lifecycle",
                    ["mimeType"] = "application/json",
                    ["text"] = new JObject
                    {
                        ["layers"] = new JArray
                        {
                            new JObject
                            {
                                ["order"] = 1,
                                ["name"] = "agentIdentityBlueprint",
                                ["endpoint"] = "/applications/microsoft.graph.agentIdentityBlueprint",
                                ["purpose"] = "The application template for a class of agent, carrying the permissions its agent identities inherit."
                            },
                            new JObject
                            {
                                ["order"] = 2,
                                ["name"] = "agentIdentityBlueprintPrincipal",
                                ["endpoint"] = "/servicePrincipals/microsoft.graph.agentIdentityBlueprintPrincipal",
                                ["purpose"] = "The record of a blueprint being added to a tenant. Required before any agent identity can be created from that blueprint here.",
                                ["createdWith"] = "The blueprint's appId"
                            },
                            new JObject
                            {
                                ["order"] = 3,
                                ["name"] = "agentIdentity",
                                ["endpoint"] = "/servicePrincipals/microsoft.graph.agentIdentity",
                                ["purpose"] = "What the agent authenticates as. Conditional Access, sign-in logs, and access reviews all act on this object.",
                                ["createdWith"] = "displayName, agentIdentityBlueprintId (the blueprint's appId), and at least one sponsor"
                            },
                            new JObject
                            {
                                ["order"] = 4,
                                ["name"] = "agentUser",
                                ["endpoint"] = "/users/microsoft.graph.agentUser",
                                ["purpose"] = "The optional user-shaped account for agents that need a mailbox, a Teams presence, or a place in the org chart.",
                                ["createdWith"] = "accountEnabled, displayName, mailNickname, userPrincipalName, and identityParentId",
                                ["constraint"] = "One agent identity may have at most one agent user."
                            }
                        },
                        ["deletion"] = new JObject
                        {
                            ["soft"] = "All four types soft-delete and stay restorable for 30 days through /directory/deletedItems.",
                            ["cascade"] = "Deleting a blueprint or blueprint principal triggers an asynchronous background cleanup that soft-deletes every child agent identity and agent user. The cleanup can lag by hours or days, and each deletion appears in the audit log with the actor 'Delete Agent Identities Task' and a blank app id. Deleting an agent identity on its own does NOT remove its agent user.",
                            ["restoreNuance"] = "Restoring a blueprint principal before the cleanup runs leaves children untouched. Restoring it afterwards does not reverse deletions that already happened — each child must be restored individually.",
                            ["orphans"] = "Permanently deleting a blueprint principal before the cleanup runs orphans its agent identities and agent users. Orphans cannot authenticate but still count toward directory quota until the 30 day retention expires.",
                            ["quota"] = "Soft-deleted objects keep consuming quota. At the 250 agent identity per blueprint limit for app-only permissions, deleting an identity frees nothing until it is permanently deleted — use permanently_delete_agent_object to reclaim room immediately."
                        },
                        ["sponsors"] = new JObject
                        {
                            ["required"] = "At least one sponsor is required on blueprints and agent identities at creation time.",
                            ["limits"] = "Maximum 100 sponsors, of which at most 5 may be groups. Groups must be dynamic-membership or Microsoft 365 groups; role-assignable groups are rejected.",
                            ["forbidden"] = "Service principals and agent users cannot be sponsors.",
                            ["delegatedGap"] = "On agentIdentity the sponsors collection supports application permissions only, so delegated connections get 403 from the sponsor tools."
                        }
                    }.ToString(Newtonsoft.Json.Formatting.Indented)
                }
            });

        handler.AddResource("entra-agent-id://permissions/blocked", "Permissions Blocked for Agents",
            "Microsoft Graph permissions that Entra refuses to grant to an agent identity. Requesting one returns HTTP 400.",
            handler: async (ct) => new JArray
            {
                new JObject
                {
                    ["uri"] = "entra-agent-id://permissions/blocked",
                    ["mimeType"] = "application/json",
                    ["text"] = new JObject
                    {
                        ["count"] = BlockedAgentPermissions.Count,
                        ["permissions"] = new JArray(BlockedAgentPermissions.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)),
                        ["note"] = "Entra rejects these for agent identities to limit the blast radius of an autonomous or compromised agent. The rejection is an opaque HTTP 400 that does not name the offending permission, so screen the set with check_blocked_permissions first."
                    }.ToString(Newtonsoft.Json.Formatting.Indented)
                }
            });

        handler.AddResource("entra-agent-id://permissions/scopes", "Required Graph Permissions",
            "The delegated Microsoft Graph permission scopes this connector uses, and what each one unlocks.",
            handler: async (ct) => new JArray
            {
                new JObject
                {
                    ["uri"] = "entra-agent-id://permissions/scopes",
                    ["mimeType"] = "application/json",
                    ["text"] = new JObject
                    {
                        ["AgentIdentity.ReadWrite.All"] = "Read and manage agent identities",
                        ["AgentIdentity.Create.All"] = "Create agent identities from a blueprint",
                        ["AgentIdentity.EnableDisable.All"] = "Enable and disable agent identities",
                        ["AgentIdentity.DeleteRestore.All"] = "Delete and restore agent identities",
                        ["AgentIdentityBlueprint.ReadWrite.All"] = "Read and manage blueprints, including their requested permissions",
                        ["AgentIdentityBlueprint.Create"] = "Register new blueprints",
                        ["AgentIdentityBlueprint.AddRemoveCreds.All"] = "Add and remove blueprint secrets and certificates",
                        ["AgentIdentityBlueprint.UpdateBranding.All"] = "Update blueprint display name, description, and other branding",
                        ["AgentIdentityBlueprint.DeleteRestore.All"] = "Delete and restore blueprints",
                        ["AgentIdentityBlueprintPrincipal.ReadWrite.All"] = "Read and manage blueprint principals",
                        ["AgentIdentityBlueprintPrincipal.Create"] = "Instantiate a blueprint into the tenant",
                        ["AgentIdentityBlueprintPrincipal.EnableDisable.All"] = "Enable and disable blueprint principals",
                        ["AgentIdentityBlueprintPrincipal.DeleteRestore.All"] = "Delete and restore blueprint principals",
                        ["AgentIdUser.ReadWrite.All"] = "Read and manage agent users, their sponsors, and their managers",
                        ["AppRoleAssignment.ReadWrite.All"] = "Grant and revoke application permissions for agents",
                        ["User.Read"] = "Sign in and read the signed-in administrator's profile",
                        ["offline_access"] = "Maintain the connection with a refresh token"
                    }.ToString(Newtonsoft.Json.Formatting.Indented)
                }
            });

        handler.AddResource("entra-agent-id://roles/directory", "Required Directory Roles",
            "The Microsoft Entra directory roles a signed-in administrator needs for each class of operation.",
            handler: async (ct) => new JArray
            {
                new JObject
                {
                    ["uri"] = "entra-agent-id://roles/directory",
                    ["mimeType"] = "application/json",
                    ["text"] = new JObject
                    {
                        ["Agent ID Developer"] = "Create blueprints and blueprint principals",
                        ["Agent ID Administrator"] = "Full management of blueprints, blueprint principals, agent identities, and agent users",
                        ["Application Administrator"] = "Read blueprints and agent identities as a fallback",
                        ["Cloud Application Administrator"] = "Read blueprints and agent identities as a fallback",
                        ["Attribute Assignment Administrator"] = "Read and write customSecurityAttributes on any agent object",
                        ["User Administrator"] = "Delete agent users, alongside Agent ID Administrator",
                        ["ownerBypass"] = "A principal that creates a blueprint or blueprint principal automatically becomes its owner, and owners can manage the derived agent identities without holding any Agent ID role. Audit owners as carefully as role assignments."
                    }.ToString(Newtonsoft.Json.Formatting.Indented)
                }
            });
        handler.AddResource("entra-agent-id://federation/patterns", "Third-Party Agent Federation Patterns",
            "How agents on AWS, n8n, Kubernetes, and other external platforms authenticate to Microsoft Entra without a stored secret.",
            handler: async (ct) => new JArray
            {
                new JObject
                {
                    ["uri"] = "entra-agent-id://federation/patterns",
                    ["mimeType"] = "application/json",
                    ["text"] = new JObject
                    {
                        ["patterns"] = new JArray
                        {
                            new JObject
                            {
                                ["name"] = "Workload identity federation",
                                ["howItWorks"] = "The external platform's own token — AWS STS, a Kubernetes service account, a GCP workload identity — is exchanged directly for a Microsoft Entra token. Configure it with a federated identity credential on the blueprint.",
                                ["bestFor"] = "AWS agents using STS and OIDC, organizations with existing federation infrastructure, and agents that cannot run containers.",
                                ["requires"] = "An identity provider that issues OIDC or STS tokens, and a federated identity credential whose issuer and subject match that provider's claims.",
                                ["configureWith"] = "add_blueprint_federated_credential with platform 'custom'"
                            },
                            new JObject
                            {
                                ["name"] = "Microsoft Entra ID Auth SDK sidecar",
                                ["howItWorks"] = "A companion container runs alongside the agent and acquires tokens on its behalf, so the agent code never handles credentials.",
                                ["bestFor"] = "Containerized agents on Docker or Kubernetes, AWS Bedrock agents in your own orchestration, and local development with Docker Compose.",
                                ["requires"] = "Running and managing a second container.",
                                ["configureWith"] = "An agent identity with the permissions it needs. This connector provisions that identity; the sidecar itself is deployed outside Power Platform."
                            },
                            new JObject
                            {
                                ["name"] = "Blueprint as token factory",
                                ["howItWorks"] = "The blueprint holds a federated identity credential trusting Entra itself, and issues tokens for its own agent identities. This is the pattern the n8n integration uses for both app-only and on-behalf-of flows.",
                                ["bestFor"] = "Platforms with a community node or connector that acquires tokens per workflow run, such as n8n.",
                                ["configureWith"] = "add_blueprint_federated_credential with platform 'entra_agent_identity'"
                            }
                        },
                        ["audience"] = "Set the federated credential audience to api://AzureADTokenExchange for Microsoft Entra ID. This is the value Entra expects in the aud claim of the incoming token.",
                        ["whyNotSecrets"] = "Client secrets on a blueprint are retrievable once and must then be stored by the agent platform. Federation removes the stored credential entirely, which is why it is preferred for anything running outside Entra.",
                        ["limit"] = "A blueprint holds a maximum of 20 federated identity credentials, and each issuer and subject pair must be unique."
                    }.ToString(Newtonsoft.Json.Formatting.Indented)
                }
            });

        handler.AddResource("entra-agent-id://beta/surface", "Beta Surface and Licensing",
            "Which tools call Microsoft Graph beta, what they require, and what they do not do.",
            handler: async (ct) => new JArray
            {
                new JObject
                {
                    ["uri"] = "entra-agent-id://beta/surface",
                    ["mimeType"] = "application/json",
                    ["text"] = new JObject
                    {
                        ["warning"] = "Everything listed here calls the Microsoft Graph beta endpoint. Beta APIs change without notice and are not supported for production use. Report that caveat whenever you surface their results.",
                        ["areas"] = new JArray
                        {
                            new JObject
                            {
                                ["area"] = "Agent risk",
                                ["tools"] = new JArray("list_risky_agents", "get_risky_agent", "list_agent_risk_detections", "get_agent_risk_detection", "confirm_agents_compromised", "confirm_agents_safe", "dismiss_agent_risk"),
                                ["requires"] = "A Microsoft Agent 365 license, plus IdentityRiskyAgent.Read.All or IdentityRiskyAgent.ReadWrite.All and IdentityRiskEvent.Read.All. Least-privileged role: Security Reader to read, Security Administrator to act.",
                                ["caveat"] = "Confirming an agent compromised records a security assertion and raises its risk level. It does NOT block sign-in — use set_agent_identity_enabled for that."
                            },
                            new JObject
                            {
                                ["area"] = "Inherited permissions",
                                ["tools"] = new JArray("list_agent_inherited_permissions"),
                                ["requires"] = "Application.Read.All for app role assignments, Directory.Read.All for delegated grants.",
                                ["caveat"] = "Read-only. The route places the type cast before the id, unlike every other agentIdentity path. Paging and OData query parameters are unsupported, and only tenant-wide admin-consented delegated grants are returned. To change what agents inherit, edit the blueprint's inheritable permissions instead."
                            },
                            new JObject
                            {
                                ["area"] = "Conditional Access what-if",
                                ["tools"] = new JArray("evaluate_conditional_access"),
                                ["requires"] = "Policy.Read.ConditionalAccess.",
                                ["caveat"] = "Simulation only. It changes nothing, which makes it safe to run before rolling out a policy that targets agents."
                            },
                            new JObject
                            {
                                ["area"] = "Agent registry",
                                ["tools"] = new JArray("list_agent_instances", "get_agent_instance", "list_agent_collections", "quarantine_agent_instance"),
                                ["requires"] = "AgentInstance.Read.All or AgentInstance.ReadWrite.All, and AgentCollection.ReadWrite.All to change collection membership. Least-privileged role: Agent Registry Administrator.",
                                ["deprecation"] = "Microsoft replaces these APIs with the Agent Registry powered by Microsoft Agent 365 from May 2026. Treat them as transitional.",
                                ["reservedCollections"] = new JObject
                                {
                                    ["Global"] = GlobalCollectionId,
                                    ["Quarantined"] = QuarantinedCollectionId,
                                    ["note"] = "Both always resolve, cannot be updated or deleted, and reject creation of a collection with a matching name."
                                },
                                ["caveat"] = "Quarantining is a registry label. It does not stop the agent authenticating."
                            }
                        }
                    }.ToString(Newtonsoft.Json.Formatting.Indented)
                }
            });

        handler.AddResource("entra-agent-id://errors/codes", "Agent ID Error Codes",
            "The documented Microsoft agent identity platform error codes and the concrete remedy for each.",
            handler: async (ct) => new JArray
            {
                new JObject
                {
                    ["uri"] = "entra-agent-id://errors/codes",
                    ["mimeType"] = "application/json",
                    ["text"] = new JObject
                    {
                        ["count"] = AgentIdErrorRemedies.Count,
                        ["codes"] = new JObject(
                            AgentIdErrorRemedies
                                .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                                .Select(e => new JProperty(e.Key, e.Value))),
                        ["note"] = "These codes arrive in the Graph error response. The connector already appends the remedy to any failed call, so treat the explanation in a tool error as authoritative and act on it rather than retrying the same request."
                    }.ToString(Newtonsoft.Json.Formatting.Indented)
                }
            });
    }

    // ── Prompts ──────────────────────────────────────────────────────────

    private void RegisterPrompts(McpRequestHandler handler)
    {
        handler.AddPrompt("onboard_agent",
            "Walk through provisioning a governed agent identity from a blueprint, with sponsors, owners, and least-privilege permissions.",
            arguments: new List<McpPromptArgument>
            {
                new McpPromptArgument { Name = "agent_name", Description = "What the agent should be called", Required = true },
                new McpPromptArgument { Name = "purpose", Description = "What the agent will do and which systems it needs", Required = false }
            },
            handler: async (args, ct) =>
            {
                var agentName = args.Value<string>("agent_name") ?? string.Empty;
                var purpose = args.Value<string>("purpose");
                var purposeLine = string.IsNullOrWhiteSpace(purpose)
                    ? "\n\nAsk me what the agent will do before choosing permissions."
                    : $"\n\nIts stated purpose is: {purpose}";

                return new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JObject
                        {
                            ["type"] = "text",
                            ["text"] =
                                $"Onboard a new agent called \"{agentName}\" into Microsoft Entra Agent ID.{purposeLine}\n\n" +
                                "Work through these steps and confirm with me before anything that creates or grants:\n" +
                                "1. Use list_blueprints to show me the blueprints available, and recommend one that matches the purpose. If none fits, say so rather than inventing one.\n" +
                                "2. Use list_blueprint_principals to check whether that blueprint is already instantiated in this tenant.\n" +
                                "3. Ask me who the sponsors and owners should be. Sponsors must be named humans or groups — never service principals. Insist on at least one sponsor and recommend two owners.\n" +
                                "4. Draft the permission set the agent needs, then run check_blocked_permissions on it. Report anything blocked and propose a narrower alternative.\n" +
                                "5. Show me the full plan — blueprint, display name, sponsors, owners, permissions, and whether an agent user is needed — and wait for my approval.\n" +
                                "6. On approval, call provision_agent. Then use get_agent_overview to confirm the result and report the agent identity id back to me.\n\n" +
                                "Only create an agent user if the agent genuinely needs a mailbox, Teams presence, or a place in the org chart. Most agents do not."
                        }
                    }
                };
            });

        handler.AddPrompt("audit_agent_governance",
            "Audit the agents in the tenant for missing sponsors, orphaned ownership, excessive permissions, and dormant identities.",
            arguments: new List<McpPromptArgument>
            {
                new McpPromptArgument { Name = "scope", Description = "Optional filter, for example a blueprint name or a display name prefix", Required = false }
            },
            handler: async (args, ct) =>
            {
                var scope = args.Value<string>("scope");
                var scopeLine = string.IsNullOrWhiteSpace(scope)
                    ? "Cover every agent identity in the tenant."
                    : $"Limit the audit to agents matching: {scope}.";

                return new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JObject
                        {
                            ["type"] = "text",
                            ["text"] =
                                $"Audit the governance posture of the agents in this tenant. {scopeLine}\n\n" +
                                "Produce a table with one row per agent and a finding column, working through:\n" +
                                "1. Use list_agent_identities to enumerate the agents. Note any with accountEnabled false — those are already parked.\n" +
                                "2. For each agent, use get_agent_overview. It returns the identity, blueprint, owners, sponsors, app role grants, and group memberships in one call, so prefer it over separate lookups.\n" +
                                "3. Flag every agent with no sponsor, with a single owner, or with an owner or sponsor who no longer appears to be active.\n" +
                                "4. Flag every agent holding a broad application permission — anything ending in .ReadWrite.All or granting directory-wide read. Name the permission and the resource.\n" +
                                "5. Flag agents that belong to groups granting resource access they do not appear to need for their stated purpose.\n" +
                                "6. Use list_deleted_agent_objects to note anything deleted recently that may still be restorable.\n\n" +
                                "Rank the findings by risk and recommend a concrete remediation for each. Do not change anything — this is a read-only audit."
                        }
                    }
                };
            });

        handler.AddPrompt("federate_third_party_agent",
            "Connect an agent running on AWS, n8n, Kubernetes, or another external platform to Entra Agent ID without storing a secret.",
            arguments: new List<McpPromptArgument>
            {
                new McpPromptArgument { Name = "platform", Description = "Where the agent runs, for example AWS Bedrock, n8n, or Kubernetes", Required = true },
                new McpPromptArgument { Name = "agent_name", Description = "What the agent should be called in Entra", Required = false }
            },
            handler: async (args, ct) =>
            {
                var platform = args.Value<string>("platform") ?? string.Empty;
                var agentName = args.Value<string>("agent_name");
                var nameLine = string.IsNullOrWhiteSpace(agentName)
                    ? "Ask me what the agent should be called before creating anything."
                    : $"The agent should be called \"{agentName}\".";

                return new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JObject
                        {
                            ["type"] = "text",
                            ["text"] =
                                $"Connect an agent running on {platform} to Microsoft Entra Agent ID. {nameLine}\n\n" +
                                "Work through these steps and confirm with me before anything that creates or grants:\n" +
                                "1. Read the entra-agent-id://federation/patterns resource and tell me which pattern fits " +
                                $"{platform} — direct workload identity federation, the Auth SDK sidecar, or the blueprint-as-token-factory pattern. Explain the tradeoff in one or two sentences.\n" +
                                "2. Identify or create the blueprint this agent belongs to, and confirm its blueprint principal exists in this tenant.\n" +
                                "3. Do not reach for add_blueprint_password. A secret has to be stored on the external platform, which is the thing federation exists to avoid. Only fall back to a secret if I tell you the platform cannot issue OIDC tokens.\n" +
                                $"4. Ask me for the issuer URL and subject claim that {platform} puts in its tokens. Do not guess them — they are specific to that platform's configuration, and a wrong value fails at runtime with an unhelpful error. Then call add_blueprint_federated_credential.\n" +
                                "5. Use list_inheritable_permissions to show me what agent identities on this blueprint already inherit, and set_inheritable_permissions with the enumerated pattern if the agent needs a specific scope set. Avoid all_allowed.\n" +
                                "6. Run check_blocked_permissions over anything you plan to grant, then use provision_agent to create the agent identity.\n" +
                                "7. Finish with get_agent_overview and report the agent identity id, the federation trust name, and the inherited scopes back to me."
                        }
                    }
                };
            });

        handler.AddPrompt("investigate_agent_risk",
            "Investigate an at-risk agent end to end: what was detected, what the agent can reach, and how to contain it.",
            arguments: new List<McpPromptArgument>
            {
                new McpPromptArgument { Name = "agent", Description = "Agent display name, UPN, or object id. Omit to triage every at-risk agent.", Required = false },
                new McpPromptArgument { Name = "risk_level", Description = "Minimum risk level to include, for example high", Required = false }
            },
            handler: async (args, ct) =>
            {
                var agent = args.Value<string>("agent");
                var riskLevel = args.Value<string>("risk_level") ?? "high";
                var scopeLine = string.IsNullOrWhiteSpace(agent)
                    ? $"Triage every agent at {riskLevel} risk or above."
                    : $"Investigate the agent \"{agent}\".";

                return new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JObject
                        {
                            ["type"] = "text",
                            ["text"] =
                                $"{scopeLine}\n\n" +
                                "These risk tools are Microsoft Graph beta and need a Microsoft Agent 365 license — say so up front if they fail.\n\n" +
                                "Work through this and confirm with me before any containment step:\n" +
                                "1. Use list_risky_agents to establish who is at risk, their risk level, and their risk state. Note the identityType, because agent identities, agent users, and blueprint principals are all in scope.\n" +
                                "2. Use list_agent_risk_detections to explain WHY each one is flagged — the specific event types, evidence, and when they were detected. A risk level with no explanation is not an investigation.\n" +
                                "3. For each affected agent, use get_agent_overview for its owners, sponsors, and direct permissions, then list_agent_inherited_permissions for what it additionally inherits from its blueprint. Report the union — the inherited half is invisible to the ordinary assignment tools and is where over-permissioning usually hides.\n" +
                                "4. If several agents share a blueprint, say so. That points at a blueprint-level problem, and list_blueprint_principal_agents will show the full population at risk.\n" +
                                "5. Recommend containment in this order, and be explicit that only the first actually stops the agent: set_agent_identity_enabled false to block sign-in; quarantine_agent_instance to flag it in the registry; confirm_agents_compromised to record the security assertion.\n" +
                                "6. If the evidence shows a false positive, recommend confirm_agents_safe rather than dismiss_agent_risk, and explain that confirming safe feeds a signal back to ID Protection while dismissing does not.\n" +
                                "7. Close with the named human accountable for each agent — its sponsor — so there is someone to notify."
                        }
                    }
                };
            });

        handler.AddPrompt("offboard_agent",
            "Safely decommission an agent, disabling before deleting and cleaning up the agent user that deletion leaves behind.",
            arguments: new List<McpPromptArgument>
            {
                new McpPromptArgument { Name = "agent", Description = "Agent display name, UPN, or agent identity object id", Required = true },
                new McpPromptArgument { Name = "reason", Description = "Why the agent is being decommissioned", Required = false }
            },
            handler: async (args, ct) =>
            {
                var agent = args.Value<string>("agent") ?? string.Empty;
                var reason = args.Value<string>("reason") ?? "not stated";

                return new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JObject
                        {
                            ["type"] = "text",
                            ["text"] =
                                $"Decommission the agent \"{agent}\". Reason: {reason}.\n\n" +
                                "Follow this sequence and confirm with me before any destructive step:\n" +
                                "1. Resolve the agent to a single agent identity. Use search_agent_identities for a name, or find_agent_by_user_principal_name for an email address. If more than one matches, list them and stop.\n" +
                                "2. Use get_agent_overview to show me exactly what you are about to remove — the identity, its agent user if it has one, its owners, its sponsors, its permission grants, and its group memberships.\n" +
                                "3. Recommend set_agent_identity_enabled with enabled false as the first action. It stops the agent immediately and is fully reversible, so it should always precede deletion.\n" +
                                "4. Once I confirm the agent is genuinely finished, revoke its app role assignments with revoke_agent_identity_app_role so nothing is left granted if it is ever restored.\n" +
                                "5. Delete the agent user first with delete_agent_user, then the agent identity with delete_agent_identity. Deleting an agent identity does not remove its agent user, so skipping the first step leaves an orphaned account with a mailbox.\n" +
                                "6. Do not reach for delete_blueprint to remove one agent. Deleting a blueprint or blueprint principal cascades to every agent built on it, which is almost never what is wanted when decommissioning a single agent.\n" +
                                "7. Remind me that both objects stay restorable for 30 days, that they keep consuming directory quota until permanently deleted, and tell me which blueprint and blueprint principal remain in place for other agents."
                        }
                    }
                };
            });
    }

    // ── G. Risk, Inherited Permissions, and Registry (13, beta) ──────────
    //
    //    Everything here is Microsoft Graph beta. The APIs are subject to
    //    change, and the risk endpoints additionally require a Microsoft
    //    Agent 365 license. Each tool says so in its description so the model
    //    can set expectations before it calls.
    //

    private void RegisterRiskAndRegistryTools(McpRequestHandler h)
    {
        h.AddTool("list_risky_agents",
            "List the agents Microsoft Entra ID Protection currently considers at risk, with their risk level and state. This is the single best starting point for an incident: it covers agent identities, agent users, and blueprint principals in one call. Beta API and requires a Microsoft Agent 365 license.",
            schema: s => s
                .String("filter", "OData $filter, for example \"riskLevel eq 'high'\", \"riskState eq 'atRisk'\", or \"identityType eq 'agentIdentity'\"")
                .String("select", "Comma-separated properties to return")
                .String("orderby", "Sort expression, for example \"riskLastModifiedDateTime desc\"")
                .Integer("top", "Maximum risky agents to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/identityProtection/riskyAgents" + BuildQuery(args), ct, "beta"),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; a["openWorldHint"] = true; });

        h.AddTool("get_risky_agent",
            "Get the risk record for one agent, including its level, state, whether it is still enabled, and which blueprint it came from. Beta API.",
            schema: s => s
                .String("risky_agent_id", "Object id of the risky agent", required: true)
                .String("select", "Comma-separated properties to return"),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "risky_agent_id");
                return await GraphGetAsync($"/identityProtection/riskyAgents/{Esc(id)}" + BuildQuery(args), ct, "beta");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_agent_risk_detections",
            "List the individual risk detections behind agent risk — the specific events ID Protection observed, such as suspicious credential usage, directory reconnaissance, or unfamiliar resource access. Use this after list_risky_agents to explain *why* an agent is flagged. Beta API.",
            schema: s => s
                .String("filter", "OData $filter, for example \"riskEventType eq 'suspiciousCredentialUsage'\" or \"detectedDateTime ge 2026-01-01T00:00:00Z\"")
                .String("orderby", "Sort expression, for example \"detectedDateTime desc\"")
                .Integer("top", "Maximum detections to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/identityProtection/agentRiskDetections" + BuildQuery(args), ct, "beta"),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_agent_risk_detection",
            "Get one agent risk detection, including its evidence, the activity time, and how it was detected. Beta API.",
            schema: s => s.String("risk_detection_id", "Id of the risk detection", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "risk_detection_id");
                return await GraphGetAsync($"/identityProtection/agentRiskDetections/{Esc(id)}", ct, "beta");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("confirm_agents_compromised",
            "Confirm one or more agents as compromised, which sets their risk level to high and feeds the signal back into ID Protection. Use this only when you have established the agent really was misused — it is a strong, tenant-visible security assertion. Pair it with set_agent_identity_enabled to actually stop the agent, because confirming risk alone does not block sign-in. Beta API.",
            schema: s => s
                .Array("agent_ids", "Object ids of the agents to mark as high risk", new JObject { ["type"] = "string" }, required: true),
            handler: async (args, ct) =>
            {
                var body = new JObject { ["agentIds"] = RequireArray(args, "agent_ids") };
                return await GraphSendAsync(HttpMethod.Post, "/identityProtection/riskyAgents/confirmCompromised", body, ct, "beta");
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("confirm_agents_safe",
            "Confirm one or more agents as safe, clearing the risk that ID Protection assigned them. Use this when you have investigated and established the detection was a false positive. Beta API.",
            schema: s => s
                .Array("agent_ids", "Object ids of the agents to confirm safe", new JObject { ["type"] = "string" }, required: true),
            handler: async (args, ct) =>
            {
                var body = new JObject { ["agentIds"] = RequireArray(args, "agent_ids") };
                return await GraphSendAsync(HttpMethod.Post, "/identityProtection/riskyAgents/confirmSafe", body, ct, "beta");
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("dismiss_agent_risk",
            "Dismiss the risk on one or more agents without asserting they are safe. This clears the risk state but does not feed a training signal back into ID Protection, so prefer confirm_agents_safe when you are confident the detection was wrong. Beta API.",
            schema: s => s
                .Array("agent_ids", "Object ids of the agents whose risk should be dismissed", new JObject { ["type"] = "string" }, required: true),
            handler: async (args, ct) =>
            {
                var body = new JObject { ["agentIds"] = RequireArray(args, "agent_ids") };
                return await GraphSendAsync(HttpMethod.Post, "/identityProtection/riskyAgents/dismiss", body, ct, "beta");
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("list_agent_inherited_permissions",
            "List the permissions an agent identity inherits from its blueprint principal — the effective app roles and delegated grants applied when its token is issued. These do not appear in the agent's own appRoleAssignments, so an audit that skips this understates what the agent can reach. The collection is read-only; change inheritance on the blueprint instead. Beta API.",
            schema: s => s
                .String("agent_identity_id", "The agent identity's object id", required: true)
                .String("permission_type", "Which inherited collection to read", required: true,
                    enumValues: new[] { "appRoleAssignments", "oauth2PermissionGrants" }),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_identity_id");
                var permissionType = RequireArgument(args, "permission_type");

                string relationship;
                if (string.Equals(permissionType, "appRoleAssignments", StringComparison.OrdinalIgnoreCase))
                    relationship = "inheritedAppRoleAssignments";
                else if (string.Equals(permissionType, "oauth2PermissionGrants", StringComparison.OrdinalIgnoreCase))
                    relationship = "inheritedOauth2PermissionGrants";
                else
                    throw new ArgumentException($"'permission_type' must be appRoleAssignments or oauth2PermissionGrants, not '{permissionType}'");

                // Note the cast precedes the id on these two routes, unlike every other agentIdentity path.
                return await GraphGetAsync($"/servicePrincipals/{AgentIdentityCast}/{Esc(id)}/{relationship}", ct, "beta");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("evaluate_conditional_access",
            "Simulate which Conditional Access policies would apply to an agent signing in, and what controls they would enforce, without changing anything. Run this before rolling out a policy that targets agents, so you can see whether it would lock a production agent out. Beta API.",
            schema: s => s
                .String("agent_identity_id", "Object id of the agent identity to simulate a sign-in for", required: true)
                .Array("include_applications", "AppIds of the target applications, for example [\"00000003-0000-0000-c000-000000000000\"] for Microsoft Graph", new JObject { ["type"] = "string" }, required: true)
                .String("risk_level", "Simulated service principal risk level", enumValues: new[] { "low", "medium", "high", "none" })
                .String("country", "Simulated two-letter country code for the sign-in, for example CA")
                .String("ip_address", "Simulated source IP address for the sign-in")
                .Boolean("applied_policies_only", "Return only the policies that would actually apply"),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["signInIdentity"] = new JObject
                    {
                        ["@odata.type"] = "#microsoft.graph.servicePrincipalSignIn",
                        ["servicePrincipalId"] = RequireArgument(args, "agent_identity_id")
                    },
                    ["signInContext"] = new JObject
                    {
                        ["@odata.type"] = "#microsoft.graph.applicationContext",
                        ["includeApplications"] = RequireArray(args, "include_applications")
                    }
                };

                var conditions = new JObject();
                var riskLevel = GetArgument(args, "risk_level");
                if (!string.IsNullOrWhiteSpace(riskLevel)) conditions["servicePrincipalRiskLevel"] = riskLevel;
                AddIfPresent(conditions, args, "country", "country");
                AddIfPresent(conditions, args, "ipAddress", "ip_address");
                if (conditions.HasValues) body["signInConditions"] = conditions;

                if (args["applied_policies_only"] != null)
                    body["appliedPoliciesOnly"] = args.Value<bool?>("applied_policies_only") ?? false;

                return await GraphSendAsync(HttpMethod.Post, "/identity/conditionalAccess/evaluate", body, ct, "beta");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_agent_instances",
            "List the deployed agent instances in the Microsoft Entra Agent Registry — the catalogue of agents running in the tenant, including ones from Copilot Studio and other stores, each linked to its agent identity. Beta API, and Microsoft is replacing the Agent Registry with Microsoft Agent 365 APIs from May 2026.",
            schema: s => s
                .String("filter", "OData $filter, for example \"originatingStore eq 'Copilot Studio'\"")
                .String("select", "Comma-separated properties to return")
                .String("expand", "Navigation properties to expand, for example \"agentCardManifest\" or \"collections\"")
                .Integer("top", "Maximum agent instances to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/agentRegistry/agentInstances" + BuildQuery(args), ct, "beta"),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_agent_instance",
            "Get one registered agent instance, including its endpoint URL, transport, owners, and the agent identity and blueprint behind it. Use this to connect a running agent back to the identity this connector governs. Beta API.",
            schema: s => s
                .String("agent_instance_id", "Id of the agent instance", required: true)
                .String("expand", "Navigation properties to expand, for example \"agentCardManifest,collections\""),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "agent_instance_id");
                return await GraphGetAsync($"/agentRegistry/agentInstances/{Esc(id)}" + BuildQuery(args), ct, "beta");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_agent_collections",
            "List the agent collections in the registry. Collections group agent instances for management, and two are reserved in every tenant: Global (00000000-0000-0000-0000-000000000001) for generally available agents, and Quarantined (00000000-0000-0000-0000-000000000002) for blocked or review-pending agents. Beta API.",
            schema: s => s
                .String("filter", "OData $filter, for example \"displayName eq 'Quarantined'\"")
                .Integer("top", "Maximum collections to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/agentRegistry/agentCollections" + BuildQuery(args), ct, "beta"),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("quarantine_agent_instance",
            "Move a registered agent instance into the reserved Quarantined collection, or into any other collection you name. Quarantining is a registry-level containment step and does NOT stop the agent authenticating — pair it with set_agent_identity_enabled to actually block sign-in. Beta API.",
            schema: s => s
                .String("agent_instance_id", "Id of the agent instance to move", required: true)
                .String("collection_id", "Target collection id. Defaults to the reserved Quarantined collection.")
                .Boolean("remove", "Remove the instance from the collection instead of adding it"),
            handler: async (args, ct) =>
            {
                var instanceId = RequireArgument(args, "agent_instance_id");
                var collectionId = GetArgument(args, "collection_id", QuarantinedCollectionId);
                var path = $"/agentRegistry/agentInstances/{Esc(instanceId)}/collections/{Esc(collectionId)}/members";

                if (args.Value<bool?>("remove") == true)
                    return await GraphSendAsync(HttpMethod.Delete, $"{path}/{Esc(instanceId)}/$ref", null, ct, "beta");

                var body = new JObject
                {
                    ["@odata.id"] = $"{GraphBeta}/agentRegistry/agentInstances('{instanceId}')"
                };
                return await GraphSendAsync(HttpMethod.Post, $"{path}/$ref", body, ct, "beta");
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });
    }

    // ── Shared Operations ────────────────────────────────────────────────
    //
    //    provision_agent reuses these, so the single-purpose tools and the
    //    composite tool cannot drift apart.
    //

    /// <summary>Create an agent identity from the standard creation arguments.</summary>
    private async Task<JObject> CreateAgentIdentityAsync(JObject args, CancellationToken ct)
    {
        var body = new JObject
        {
            ["displayName"] = RequireArgument(args, "display_name"),
            ["agentIdentityBlueprintId"] = RequireArgument(args, "blueprint_app_id"),
            ["sponsors@odata.bind"] = BuildSponsorBindings(
                RequireArray(args, "sponsor_ids"), args["sponsor_group_ids"] as JArray)
        };

        if (args["account_enabled"] != null)
            body["accountEnabled"] = args.Value<bool?>("account_enabled") ?? true;

        var tags = args["tags"] as JArray;
        if (tags != null && tags.Count > 0) body["tags"] = tags;

        return await GraphSendAsync(HttpMethod.Post, $"/servicePrincipals/{AgentIdentityCast}", body, ct).ConfigureAwait(false);
    }

    /// <summary>Create an agent user from the standard creation arguments, optionally assigning a manager.</summary>
    private async Task<JObject> CreateAgentUserAsync(JObject args, CancellationToken ct)
    {
        var body = new JObject
        {
            ["accountEnabled"] = args.Value<bool?>("account_enabled") ?? true,
            ["displayName"] = RequireArgument(args, "display_name"),
            ["mailNickname"] = RequireArgument(args, "mail_nickname"),
            ["userPrincipalName"] = RequireArgument(args, "user_principal_name"),
            ["identityParentId"] = RequireArgument(args, "agent_identity_id")
        };

        AddIfPresent(body, args, "jobTitle", "job_title");
        AddIfPresent(body, args, "department", "department");
        AddIfPresent(body, args, "usageLocation", "usage_location");

        var created = await GraphSendAsync(HttpMethod.Post, $"/users/{AgentUserCast}", body, ct).ConfigureAwait(false);

        var managerId = GetArgument(args, "manager_id");
        var agentUserId = created["id"]?.ToString();

        if (!string.IsNullOrWhiteSpace(managerId) && !string.IsNullOrWhiteSpace(agentUserId))
        {
            try
            {
                await GraphSendAsync(HttpMethod.Post, $"/users/{Esc(agentUserId)}/manager/$ref",
                    new JObject { ["@odata.id"] = $"{GraphV1}/users/{Esc(managerId)}" }, ct).ConfigureAwait(false);
                created["managerAssigned"] = true;
            }
            catch (Exception ex)
            {
                // The account exists either way — report the failure rather than losing it.
                created["managerAssigned"] = false;
                created["managerAssignmentError"] = ex.Message;
            }
        }

        return created;
    }

    // ── Microsoft Graph ──────────────────────────────────────────────────

    /// <summary>Issue a GET against Microsoft Graph and return the parsed response.</summary>
    private async Task<JObject> GraphGetAsync(string path, CancellationToken ct, string version = "v1.0")
    {
        return await GraphSendAsync(HttpMethod.Get, path, null, ct, version).ConfigureAwait(false);
    }

    /// <summary>
    /// Issue a GET that reports its own failure instead of throwing, so a composite tool
    /// can return a partial picture rather than nothing when one collection is forbidden.
    /// </summary>
    private async Task<JToken> TryGraphGetAsync(string path, CancellationToken ct, string version = "v1.0")
    {
        try
        {
            return await GraphSendAsync(HttpMethod.Get, path, null, ct, version).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new JObject { ["unavailable"] = true, ["reason"] = ex.Message };
        }
    }

    /// <summary>
    /// Issue a request against Microsoft Graph, forwarding the connector's bearer token.
    /// Non-success responses are raised as exceptions so the MCP framework converts them
    /// into tool errors the model can read and correct.
    /// </summary>
    private async Task<JObject> GraphSendAsync(
        HttpMethod method, string path, JObject body, CancellationToken ct, string version = "v1.0")
    {
        var baseUrl = string.Equals(version, "beta", StringComparison.OrdinalIgnoreCase) ? GraphBeta : GraphV1;
        var request = new HttpRequestMessage(method, baseUrl + path);

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        // Several agent enums are evolvable; without this header Graph omits the newer members.
        request.Headers.TryAddWithoutValidation("Prefer", "include-unknown-enum-members");

        // $count, $search, and advanced $filter all require the eventual consistency header.
        if (path.IndexOf("$count", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("$search", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            request.Headers.TryAddWithoutValidation("ConsistencyLevel", "eventual");
        }

        if (body != null && method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            request.Content = new StringContent(
                body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(request, ct).ConfigureAwait(false);
        var content = response.Content == null
            ? string.Empty
            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var detail = content;
            var errorCode = string.Empty;
            try
            {
                var error = JObject.Parse(content)["error"];
                if (error != null)
                {
                    errorCode = error["code"]?.ToString() ?? string.Empty;
                    detail = $"{errorCode}: {error["message"]}";
                }
            }
            catch { }

            throw new Exception($"Graph {version} {method.Method} {path} failed ({(int)response.StatusCode}): {detail}{ExplainGraphFailure(response.StatusCode, path, errorCode)}");
        }

        if (string.IsNullOrWhiteSpace(content))
            return new JObject { ["success"] = true, ["status"] = (int)response.StatusCode };

        try
        {
            var parsed = JToken.Parse(content);
            return parsed as JObject ?? new JObject { ["value"] = parsed };
        }
        catch
        {
            return new JObject { ["text"] = content };
        }
    }

    /// <summary>
    /// The documented Microsoft agent identity platform error codes, mapped to the concrete
    /// next step. Graph returns these as opaque code strings; without the remedy the model
    /// tends to retry the identical call.
    /// </summary>
    private static readonly Dictionary<string, string> AgentIdErrorRemedies =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Agent_Directory_QuotaExceeded"] =
            "The tenant is above 95% of its combined blueprint and agent identity quota. Soft-deleted objects still count, so use list_deleted_agent_objects and permanently_delete_agent_object to reclaim room before creating more.",
        ["AgentBlueprint_LimitExceeded"] =
            "The tenant has reached its maximum number of blueprints, counting soft-deleted ones. Permanently delete unneeded blueprints with permanently_delete_agent_object to free capacity.",
        ["AgentIdentity_LimitExceeded"] =
            "The tenant has reached its maximum number of agent identities, counting soft-deleted ones. Permanently delete unneeded identities with permanently_delete_agent_object to free capacity.",

        ["AgentBlueprint_IncompatibleProperty"] =
            "A property in the request cannot be set on a blueprint. Blueprints accept only a subset of application properties — remove the offending property and retry.",
        ["AgentBlueprint_IncompatibleProperty_NullPropertyName"] =
            "A property in the request cannot be set on a blueprint. Blueprints accept only a subset of application properties — remove the offending property and retry.",
        ["AgentBlueprint_NotSupportedOnApiVersion"] =
            "Blueprints are not supported on the API version used. This connector targets Graph v1.0, so report this rather than retrying.",

        ["AgentBlueprintPrincipal_AgentIdentity_IncompatibleProperty"] =
            "A property in the request cannot be set on an agent identity. Remove it and retry.",
        ["AgentBlueprintPrincipal_IncompatibleProperty"] =
            "A property in the request cannot be set on a blueprint principal. Remove it and retry.",
        ["AgentBlueprintPrincipal_NotSupportedOnApiVersion"] =
            "Blueprint principals are not supported on the API version used. This connector targets Graph v1.0, so report this rather than retrying.",
        ["AgentBlueprintPrincipal_RequireAgentBlueprint"] =
            "Blueprint principals can only be created for a blueprint. The appId supplied belongs to an ordinary application — confirm it with list_blueprints.",

        ["AgentIdentity_AgentBlueprintPrincipalDoesNotExist"] =
            "The blueprint has no blueprint principal in this tenant, which is a prerequisite for creating agent identities from it. Call create_blueprint_principal with the blueprint's appId first, or use provision_agent which handles the ordering.",
        ["AgentIdentity_CredentialsNotSupported"] =
            "Agent identities cannot hold credentials. Every secret, certificate, and federation trust belongs on the blueprint — use add_blueprint_password or add_blueprint_federated_credential instead.",
        ["AgentIdentity_IncompatibleParentType"] =
            "The appId in agentIdentityBlueprintId does not belong to a blueprint. Supply the blueprint's appId, not its object id and not an ordinary application's appId.",
        ["AgentIdentity_NotSupportedOnApiVersion"] =
            "Agent identities are not supported on the API version used. This connector targets Graph v1.0, so report this rather than retrying.",

        ["Error_AgentBlueprintCannotCreateAssociatedIdentity"] =
            "A blueprint cannot create agent identities belonging to a different blueprint. Use the matching blueprint, or sign in as a principal holding Agent ID Administrator.",
        ["Error_AgentIdentitiesCreatingAgentIdentitiesNotAllowed"] =
            "Agent identities cannot create other agent identities. Reconnect as a blueprint principal or a non-agent service principal with the required permissions.",
        ["Error_AgentIdentitySelfCreateRequired"] =
            "An application may only create agent identities under itself. The agentIdentityBlueprintId supplied does not match the calling application's appId."
    };

    /// <summary>
    /// Turn an Agent ID failure into a plain explanation with the concrete next step, so the
    /// model corrects course instead of retrying the same call.
    /// </summary>
    private static string ExplainGraphFailure(HttpStatusCode statusCode, string path, string errorCode)
    {
        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            string remedy;
            if (AgentIdErrorRemedies.TryGetValue(errorCode, out remedy))
                return " — " + remedy;
        }

        if (statusCode == HttpStatusCode.Forbidden && path.IndexOf("/sponsors", StringComparison.OrdinalIgnoreCase) >= 0
            && path.IndexOf(AgentIdentityCast, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return " — Microsoft Graph supports the agentIdentity sponsors collection with application permissions only. "
                 + "On a delegated connection, read sponsors with get_agent_overview instead.";
        }

        if (statusCode == HttpStatusCode.BadRequest && path.IndexOf("appRoleAssignments", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return " — this permission may be on the list Entra blocks for agent identities. "
                 + "Run check_blocked_permissions on it before retrying.";
        }

        return string.Empty;
    }

    /// <summary>Rewrite the version segment of an absolute Graph URL before forwarding a REST call.</summary>
    private static void SetGraphVersion(HttpRequestMessage request, string version)
    {
        if (request?.RequestUri == null || !request.RequestUri.IsAbsoluteUri)
            throw new InvalidOperationException("The Graph request URI must be absolute before selecting an API version.");

        var builder = new UriBuilder(request.RequestUri);
        var updatedPath = Regex.Replace(
            builder.Path,
            @"^/(?:beta|v1\.0)(?=/|$)",
            "/" + version,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (string.Equals(updatedPath, builder.Path, StringComparison.Ordinal)
            && !builder.Path.StartsWith("/" + version + "/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The Graph request path '{builder.Path}' does not contain a supported API version.");
        }

        builder.Path = updatedPath;
        request.RequestUri = builder.Uri;
    }

    /// <summary>Reduce a caller-supplied path to a validated, version-free Graph path.</summary>
    private static string NormalizeGraphPath(string path)
    {
        var trimmed = (path ?? string.Empty).Trim();

        if (trimmed.StartsWith("https://graph.microsoft.com", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(trimmed);
            trimmed = uri.PathAndQuery;
            if (trimmed.StartsWith("/beta", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed.Substring(5);
            else if (trimmed.StartsWith("/v1.0", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed.Substring(5);
        }

        if (!trimmed.StartsWith("/")) trimmed = "/" + trimmed;

        if (trimmed.Contains(".."))
            throw new ArgumentException("'path' must not contain relative segments");

        var allowed = AllowedPathPrefixes.Any(prefix =>
            trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (!allowed)
            throw new ArgumentException(
                $"'{trimmed}' is outside the identity surface of this connector. Paths must begin with one of: {string.Join(", ", AllowedPathPrefixes)}.");

        return trimmed;
    }

    // ── Argument Helpers ─────────────────────────────────────────────────

    /// <summary>Build an OData query string from the standard paging and shaping arguments.</summary>
    private static string BuildQuery(JObject args, int defaultTop = 0)
    {
        if (args == null) return string.Empty;

        var parts = new List<string>();

        var filter = args.Value<string>("filter");
        if (!string.IsNullOrWhiteSpace(filter)) parts.Add("$filter=" + Uri.EscapeDataString(filter));

        var select = args.Value<string>("select");
        if (!string.IsNullOrWhiteSpace(select)) parts.Add("$select=" + Uri.EscapeDataString(select));

        var expand = args.Value<string>("expand");
        if (!string.IsNullOrWhiteSpace(expand)) parts.Add("$expand=" + Uri.EscapeDataString(expand));

        var orderby = args.Value<string>("orderby");
        if (!string.IsNullOrWhiteSpace(orderby)) parts.Add("$orderby=" + Uri.EscapeDataString(orderby));

        var search = args.Value<string>("search");
        if (!string.IsNullOrWhiteSpace(search)) parts.Add("$search=" + Uri.EscapeDataString("\"" + search + "\""));

        var top = args.Value<int?>("top");
        if (top.HasValue && top.Value > 0) parts.Add("$top=" + top.Value);
        else if (defaultTop > 0) parts.Add("$top=" + defaultTop);

        if (args.Value<bool?>("count") == true) parts.Add("$count=true");

        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    /// <summary>Build the sponsors@odata.bind array Graph expects when creating a blueprint or agent identity.</summary>
    private static JArray BuildSponsorBindings(JArray userIds, JArray groupIds)
    {
        var bindings = new JArray();

        foreach (var id in userIds ?? new JArray())
        {
            var value = id?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                bindings.Add($"{GraphV1}/users/{Esc(value)}");
        }

        foreach (var id in groupIds ?? new JArray())
        {
            var value = id?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                bindings.Add($"{GraphV1}/groups/{Esc(value)}");
        }

        if (bindings.Count == 0)
            throw new ArgumentException("At least one sponsor is required. Supply 'sponsor_ids', 'sponsor_group_ids', or both.");

        return bindings;
    }

    /// <summary>Build the $ref body Graph expects when linking a directory object.</summary>
    private static JObject DirectoryObjectRef(string objectId)
    {
        return new JObject { ["@odata.id"] = $"{GraphV1}/directoryObjects/{Esc(objectId)}" };
    }

    /// <summary>Copy an optional string argument onto a request body under its Graph property name.</summary>
    private static void AddIfPresent(JObject body, JObject args, string graphProperty, string argumentName)
    {
        var value = GetArgument(args, argumentName);
        if (!string.IsNullOrWhiteSpace(value)) body[graphProperty] = value;
    }

    /// <summary>Escape a path segment so an identifier cannot break out of its position.</summary>
    private static string Esc(string segment)
    {
        return Uri.EscapeDataString(segment ?? string.Empty);
    }

    /// <summary>Get a required array argument; throws ArgumentException if absent or empty.</summary>
    private static JArray RequireArray(JObject args, string name)
    {
        var array = args?[name] as JArray;
        if (array == null || array.Count == 0)
            throw new ArgumentException($"'{name}' is required and must contain at least one entry");
        return array;
    }

    /// <summary>Parse a JSON object argument that may arrive as a string or an object.</summary>
    private static JObject ParseJsonArgument(JObject args, string name)
    {
        var token = args?[name];
        if (token == null || token.Type == JTokenType.Null) return null;
        if (token is JObject obj) return obj;

        var text = token.ToString();
        if (string.IsNullOrWhiteSpace(text)) return null;

        try { return JObject.Parse(text); }
        catch (JsonException ex) { throw new ArgumentException($"'{name}' is not valid JSON: {ex.Message}"); }
    }

    /// <summary>Parse a JSON array argument that may arrive as a string or an array.</summary>
    private static JArray ParseJsonArrayArgument(JObject args, string name)
    {
        var token = args?[name];
        if (token == null || token.Type == JTokenType.Null)
            throw new ArgumentException($"'{name}' is required");
        if (token is JArray array) return array;

        var text = token.ToString();
        try { return JArray.Parse(text); }
        catch (JsonException ex) { throw new ArgumentException($"'{name}' is not a valid JSON array: {ex.Message}"); }
    }

    /// <summary>Get a required string argument; throws ArgumentException if missing.</summary>
    private static string RequireArgument(JObject args, string name)
    {
        var value = args?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{name}' is required");
        return value;
    }

    /// <summary>Get an argument that a particular mode makes mandatory, naming the mode in the error.</summary>
    private static string RequireArgumentFor(JObject args, string name, string mode)
    {
        var value = args?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{name}' is required when the platform is '{mode}'");
        return value;
    }

    /// <summary>Get an optional string argument with a default fallback.</summary>
    private static string GetArgument(JObject args, string name, string defaultValue = null)
    {
        var value = args?[name]?.ToString();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    // ── Application Insights (Optional) ──────────────────────────────────

    private async Task LogToAppInsights(string eventName, object properties, string correlationId)
    {
        try
        {
            var instrumentationKey = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var ingestionEndpoint = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint")
                ?? "https://dc.services.visualstudio.com/";

            if (string.IsNullOrEmpty(instrumentationKey))
                return;

            var propsDict = new Dictionary<string, string>
            {
                ["ServerName"] = Options.ServerInfo.Name,
                ["ServerVersion"] = Options.ServerInfo.Version,
                ["CorrelationId"] = correlationId
            };

            if (properties != null)
            {
                var propsJson = JsonConvert.SerializeObject(properties);
                var propsObj = JObject.Parse(propsJson);
                foreach (var prop in propsObj.Properties())
                {
                    propsDict[prop.Name] = prop.Value?.ToString() ?? "";
                }
            }

            var telemetryData = new
            {
                name = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                time = DateTime.UtcNow.ToString("o"),
                iKey = instrumentationKey,
                data = new
                {
                    baseType = "EventData",
                    baseData = new
                    {
                        ver = 2,
                        name = eventName,
                        properties = propsDict
                    }
                }
            };

            var json = JsonConvert.SerializeObject(telemetryData);
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");

            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Suppress telemetry errors
        }
    }

    private static string ExtractConnectionStringPart(string connectionString, string key)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        var prefix = key + "=";
        var parts = connectionString.Split(';');
        foreach (var part in parts)
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part.Substring(prefix.Length);
        }
        return null;
    }
}

// ║  SECTION 2: MCP FRAMEWORK                                                  ║
// ║                                                                            ║
// ║  Built-in McpRequestHandler that brings MCP C# SDK patterns to Power       ║
// ║  Platform. If Microsoft enables the official SDK namespaces, this section   ║
// ║  becomes a using statement instead of inline code.                          ║
// ║                                                                            ║
// ║  Spec coverage: MCP 2026-07-28, dual-era                                   ║
// ║                                                                            ║
// ║  2026-07-28 removed the initialize handshake and made MCP stateless: every ║
// ║  request carries its own protocol version, identity, and capabilities in   ║
// ║  _meta. Power Platform connectors are stateless by construction, so the    ║
// ║  runtime and the protocol now agree rather than fight.                     ║
// ║                                                                            ║
// ║  This handler serves BOTH eras from one endpoint:                          ║
// ║    modern (2026-07-28)  server/discover, per-request _meta, resultType,    ║
// ║                         ttlMs/cacheScope, MRTR input_required              ║
// ║    legacy (2025-11-25-) initialize, ping, logging/setLevel,                ║
// ║                         resources/subscribe                                ║
// ║                                                                            ║
// ║  The era is chosen per request. Copilot Studio is a legacy client and is   ║
// ║  answered exactly as it was before this revision.                          ║
// ║                                                                            ║
// ║  Out of reach for a request/response connector (no open stream):           ║
// ║   - subscriptions/listen and every server-to-client notification           ║
// ║   - the io.modelcontextprotocol/tasks extension (needs durable state)      ║
// ║                                                                            ║
// ║  Do not modify unless extending the framework itself.                      ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

// ── Protocol Constants ───────────────────────────────────────────────────────

/// <summary>Reserved _meta keys, extension identifiers, and URI schemes defined by MCP.</summary>
public static class McpKeys
{
    public const string ProtocolVersion = "io.modelcontextprotocol/protocolVersion";
    public const string ClientInfo = "io.modelcontextprotocol/clientInfo";
    public const string ClientCapabilities = "io.modelcontextprotocol/clientCapabilities";
    public const string ServerInfo = "io.modelcontextprotocol/serverInfo";
    public const string LogLevel = "io.modelcontextprotocol/logLevel";
    public const string SubscriptionId = "io.modelcontextprotocol/subscriptionId";

    // Official extensions, advertised through capabilities.extensions.
    public const string ExtensionTasks = "io.modelcontextprotocol/tasks";
    public const string ExtensionUi = "io.modelcontextprotocol/ui";

    // Agent Skills over MCP.
    public const string SkillScheme = "skill://";
    public const string SkillIndexUri = "skill://index.json";
}

// ── Configuration Types ──────────────────────────────────────────────────────

/// <summary>Server identity reported by server/discover and in each result's _meta.</summary>
public class McpServerInfo
{
    public string Name { get; set; } = "mcp-server";
    public string Version { get; set; } = "1.0.0";
    public string Title { get; set; }
    public string Description { get; set; }

    /// <summary>Optional icons array, per the icons field in the MCP base spec.</summary>
    public JArray Icons { get; set; }
}

/// <summary>Capabilities advertised by server/discover and initialize.</summary>
public class McpCapabilities
{
    public bool Tools { get; set; } = true;
    public bool Resources { get; set; }
    public bool Prompts { get; set; }
    public bool Completions { get; set; }

    /// <summary>Deprecated in 2026-07-28. Advertised to legacy clients only.</summary>
    public bool Logging { get; set; }

    /// <summary>
    /// Optional extension map keyed by extension identifier, for example
    /// { "io.modelcontextprotocol/ui": { "mimeTypes": [ "text/html;profile=mcp-app" ] } }.
    /// </summary>
    public JObject Extensions { get; set; }
}

/// <summary>Top-level configuration for the MCP handler.</summary>
public class McpServerOptions
{
    public McpServerInfo ServerInfo { get; set; } = new McpServerInfo();

    /// <summary>Preferred revision. Listed first by server/discover.</summary>
    public string ProtocolVersion { get; set; } = "2026-07-28";

    /// <summary>Every revision this server accepts. Must contain ProtocolVersion.</summary>
    public List<string> SupportedProtocolVersions { get; set; } =
        new List<string> { "2026-07-28", "2025-11-25", "2025-06-18" };

    public McpCapabilities Capabilities { get; set; } = new McpCapabilities();

    /// <summary>Natural-language guidance for the model, returned by server/discover.</summary>
    public string Instructions { get; set; }

    // ── Cache hints (2026-07-28 CacheableResult) ──────────────────────────

    /// <summary>Freshness hint in milliseconds for tools/prompts/resources list results.</summary>
    public int ListCacheTtlMs { get; set; } = 300000;

    /// <summary>"public" when list results are identical for every caller, otherwise "private".</summary>
    public string ListCacheScope { get; set; } = "public";

    public int ResourceCacheTtlMs { get; set; } = 60000;

    /// <summary>"private" by default: resource content commonly varies by authenticated user.</summary>
    public string ResourceCacheScope { get; set; } = "private";

    public int DiscoverCacheTtlMs { get; set; } = 3600000;

    /// <summary>
    /// Compare the Mcp-Method and Mcp-Name headers against the request body and reject a
    /// mismatch with HeaderMismatch. Absent headers are ignored, because the Power Platform
    /// gateway makes no guarantee that it forwards them.
    /// </summary>
    public bool ValidateRequestHeaders { get; set; } = true;
}

// ── Transport Headers ────────────────────────────────────────────────────────

/// <summary>
/// The Streamable HTTP headers the framework cares about. Captured in the connector
/// entry point and handed to HandleAsync so the framework never touches ScriptBase.
/// </summary>
public class McpTransportHeaders
{
    public string ProtocolVersion { get; set; }
    public string Method { get; set; }
    public string Name { get; set; }

    public static McpTransportHeaders FromRequest(HttpRequestMessage request)
    {
        var headers = new McpTransportHeaders();
        if (request == null) return headers;

        headers.ProtocolVersion = FirstOrNull(request, "MCP-Protocol-Version");
        headers.Method = FirstOrNull(request, "Mcp-Method");
        headers.Name = FirstOrNull(request, "Mcp-Name");
        return headers;
    }

    private static string FirstOrNull(HttpRequestMessage request, string headerName)
    {
        IEnumerable<string> values;
        if (!request.Headers.TryGetValues(headerName, out values)) return null;
        foreach (var value in values) return value;
        return null;
    }
}

// ── Error Handling ───────────────────────────────────────────────────────────

/// <summary>JSON-RPC 2.0 and MCP error codes.</summary>
public enum McpErrorCode
{
    // JSON-RPC 2.0.
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603,

    // Legacy implementation-defined range (-32000 to -32019). 2026-07-28 forbids
    // allocating new codes here; this one predates the policy.
    RequestTimeout = -32000,

    // Reserved for the MCP specification (-32020 to -32099).
    HeaderMismatch = -32020,
    MissingRequiredClientCapability = -32021,
    UnsupportedProtocolVersion = -32022
}

/// <summary>
/// Throw from tool methods to surface a structured MCP error.
/// Mirrors ModelContextProtocol.McpException from the official SDK.
/// </summary>
public class McpException : Exception
{
    public McpErrorCode Code { get; }
    public McpException(McpErrorCode code, string message) : base(message) => Code = code;
}

// ── Schema Builder (Fluent API) ──────────────────────────────────────────────

/// <summary>
/// Fluent builder for the JSON Schema objects used in tool inputSchema and outputSchema.
///
/// Deliberately narrower than JSON Schema 2020-12 permits. Copilot Studio drops any tool
/// whose schema contains a reference type, and truncates schemas that use multi-type
/// arrays, so this builder never emits $ref, $defs, or "type": [...]. Nested objects are
/// inlined instead. If you hand-write a schema, keep to the same rules — and if you add
/// numeric bounds, note that Copilot Studio expects the draft-04 boolean form of
/// exclusiveMinimum, not the 2020-12 numeric form.
/// </summary>
public class McpSchemaBuilder
{
    private readonly JObject _properties = new JObject();
    private readonly JArray _required = new JArray();

    public McpSchemaBuilder String(string name, string description, bool required = false, string format = null, string[] enumValues = null)
    {
        var prop = new JObject { ["type"] = "string", ["description"] = description };
        if (format != null) prop["format"] = format;
        if (enumValues != null) prop["enum"] = new JArray(enumValues);
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Integer(string name, string description, bool required = false, int? defaultValue = null)
    {
        var prop = new JObject { ["type"] = "integer", ["description"] = description };
        if (defaultValue.HasValue) prop["default"] = defaultValue.Value;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Number(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "number", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Boolean(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "boolean", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Array(string name, string description, JObject itemSchema, bool required = false)
    {
        _properties[name] = new JObject
        {
            ["type"] = "array",
            ["description"] = description,
            ["items"] = itemSchema
        };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Object(string name, string description, Action<McpSchemaBuilder> nestedConfig, bool required = false)
    {
        var nested = new McpSchemaBuilder();
        nestedConfig?.Invoke(nested);
        var obj = nested.Build();
        obj["description"] = description;
        _properties[name] = obj;
        if (required) _required.Add(name);
        return this;
    }

    public JObject Build()
    {
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = _properties
        };

        // Recommended empty-schema form for a tool that takes no parameters.
        if (_properties.Count == 0) schema["additionalProperties"] = false;
        if (_required.Count > 0) schema["required"] = _required;

        return schema;
    }
}

// ── Request Context ──────────────────────────────────────────────────────────

/// <summary>
/// Everything the framework knows about one request. This replaces the connection
/// state that 2026-07-28 removed: era, negotiated version, client identity, and the
/// answers a client attaches when it retries a multi round-trip request.
/// </summary>
public class McpRequestContext
{
    public JToken Id { get; set; }
    public string Method { get; set; }

    /// <summary>True when the caller speaks 2026-07-28 or later.</summary>
    public bool IsModern { get; set; }

    public string ProtocolVersion { get; set; }
    public JObject ClientInfo { get; set; }
    public JObject ClientCapabilities { get; set; }
    public string LogLevel { get; set; }

    /// <summary>Answers supplied on an MRTR retry, keyed by input request name.</summary>
    public JObject InputResponses { get; set; }

    /// <summary>Opaque state handed back with a previous input_required result.</summary>
    public string RequestState { get; set; }

    /// <summary>True when the client declared the given extension identifier.</summary>
    public bool SupportsExtension(string extensionId)
    {
        var extensions = ClientCapabilities?["extensions"] as JObject;
        return extensions != null && extensions[extensionId] != null;
    }
}

// ── Agent Skills ─────────────────────────────────────────────────────────────

/// <summary>One resource file bundled with a skill.</summary>
internal class McpSkillResource
{
    public string Path { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public Func<string> Content { get; set; }
}

/// <summary>Fluent collector for a skill's sibling resource files.</summary>
public class McpSkillResourceBuilder
{
    internal readonly List<McpSkillResource> Resources = new List<McpSkillResource>();

    /// <summary>
    /// Add a file served alongside SKILL.md. Use the relative path the skill body
    /// references, for example "references/policy.md" or "assets/template.md".
    /// </summary>
    public McpSkillResourceBuilder Resource(string path, string description, Func<string> content, string mimeType = "text/markdown")
    {
        Resources.Add(new McpSkillResource
        {
            Path = path,
            Description = description,
            MimeType = mimeType,
            Content = content
        });
        return this;
    }
}

internal class McpSkillDefinition
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string Instructions { get; set; }
    public string License { get; set; }
    public string Compatibility { get; set; }
    public JObject Metadata { get; set; }
    public List<McpSkillResource> Resources { get; set; } = new List<McpSkillResource>();

    public string SkillUri { get { return McpKeys.SkillScheme + Name + "/SKILL.md"; } }

    public string ResourceUri(string path) { return McpKeys.SkillScheme + Name + "/" + path; }

    /// <summary>Render SKILL.md: YAML frontmatter followed by the markdown body.</summary>
    public string RenderSkillMarkdown()
    {
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("name: ").Append(Name).Append('\n');
        sb.Append("description: ").Append(EscapeYaml(Description)).Append('\n');

        if (!string.IsNullOrWhiteSpace(License))
            sb.Append("license: ").Append(EscapeYaml(License)).Append('\n');

        if (!string.IsNullOrWhiteSpace(Compatibility))
            sb.Append("compatibility: ").Append(EscapeYaml(Compatibility)).Append('\n');

        if (Metadata != null && Metadata.Count > 0)
        {
            sb.Append("metadata:\n");
            foreach (var prop in Metadata.Properties())
                sb.Append("  ").Append(prop.Name).Append(": ").Append(EscapeYaml(prop.Value?.ToString())).Append('\n');
        }

        sb.Append("---\n\n");
        sb.Append(Instructions ?? string.Empty);
        return sb.ToString();
    }

    /// <summary>Quote any scalar that would otherwise break the frontmatter block.</summary>
    private static string EscapeYaml(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        var flattened = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return "\"" + flattened.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}

// ── Internal Tool Registration ───────────────────────────────────────────────

internal class McpToolDefinition
{
    public string Name { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public JObject InputSchema { get; set; }
    public JObject OutputSchema { get; set; }
    public JObject Annotations { get; set; }
    public Func<JObject, CancellationToken, Task<object>> Handler { get; set; }
}

// ── Internal Resource Registration ───────────────────────────────────────────

internal class McpResourceDefinition
{
    public string Uri { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public JObject Annotations { get; set; }
    public Func<CancellationToken, Task<JArray>> Handler { get; set; }
}

internal class McpResourceTemplateDefinition
{
    public string UriTemplate { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public JObject Annotations { get; set; }
    public Func<string, CancellationToken, Task<JArray>> Handler { get; set; }
}

// ── Internal Prompt Registration ─────────────────────────────────────────────

/// <summary>Describes a single prompt argument.</summary>
public class McpPromptArgument
{
    public string Name { get; set; }
    public string Description { get; set; }
    public bool Required { get; set; }
}

internal class McpPromptDefinition
{
    public string Name { get; set; }
    public string Description { get; set; }
    public List<McpPromptArgument> Arguments { get; set; } = new List<McpPromptArgument>();
    public Func<JObject, CancellationToken, Task<JArray>> Handler { get; set; }
}

// ── McpRequestHandler ────────────────────────────────────────────────────────
//
//    The core bridge class. Stateless, no DI, no ASP.NET Core.
//    Takes a JSON-RPC string in → returns a JSON-RPC string out.
//    This is the class that does not exist in the official SDK today.
//

/// <summary>
/// Stateless MCP request handler that bridges the official SDK's patterns
/// to Power Platform's ScriptBase.ExecuteAsync() model.
/// 
/// Handles all JSON-RPC 2.0 routing, protocol negotiation, tool discovery,
/// parameter binding, and response formatting internally.
/// </summary>
public class McpRequestHandler
{
    private readonly McpServerOptions _options;

    // Dictionaries give O(1) dispatch. The parallel lists preserve registration order,
    // which 2026-07-28 asks for so clients can cache list results reliably.
    private readonly Dictionary<string, McpToolDefinition> _tools;
    private readonly List<McpToolDefinition> _toolOrder;
    private readonly Dictionary<string, McpResourceDefinition> _resources;
    private readonly List<McpResourceDefinition> _resourceOrder;
    private readonly List<McpResourceTemplateDefinition> _resourceTemplates;
    private readonly Dictionary<string, McpPromptDefinition> _prompts;
    private readonly List<McpPromptDefinition> _promptOrder;
    private readonly List<McpSkillDefinition> _skills;

    /// <summary>
    /// Optional logging callback. Wire this up to Application Insights,
    /// Context.Logger, or any other telemetry sink.
    /// </summary>
    public Action<string, object> OnLog { get; set; }

    public McpRequestHandler(McpServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = new Dictionary<string, McpToolDefinition>(StringComparer.OrdinalIgnoreCase);
        _toolOrder = new List<McpToolDefinition>();
        _resources = new Dictionary<string, McpResourceDefinition>(StringComparer.OrdinalIgnoreCase);
        _resourceOrder = new List<McpResourceDefinition>();
        _resourceTemplates = new List<McpResourceTemplateDefinition>();
        _prompts = new Dictionary<string, McpPromptDefinition>(StringComparer.OrdinalIgnoreCase);
        _promptOrder = new List<McpPromptDefinition>();
        _skills = new List<McpSkillDefinition>();
    }

    // ── Tool Registration ────────────────────────────────────────────────

    /// <summary>
    /// Register a tool using the fluent API.
    /// Define the schema with McpSchemaBuilder, provide a handler, and optionally set annotations.
    /// </summary>
    public McpRequestHandler AddTool(
        string name,
        string description,
        Action<McpSchemaBuilder> schema,
        Func<JObject, CancellationToken, Task<JObject>> handler,
        Action<JObject> annotations = null,
        string title = null,
        Action<McpSchemaBuilder> outputSchemaConfig = null)
    {
        var builder = new McpSchemaBuilder();
        schema?.Invoke(builder);

        JObject annotationsObject = null;
        if (annotations != null)
        {
            annotationsObject = new JObject();
            annotations(annotationsObject);
        }

        JObject outputSchema = null;
        if (outputSchemaConfig != null)
        {
            var outBuilder = new McpSchemaBuilder();
            outputSchemaConfig(outBuilder);
            outputSchema = outBuilder.Build();
        }

        var definition = new McpToolDefinition
        {
            Name = name,
            Title = title,
            Description = description,
            InputSchema = builder.Build(),
            OutputSchema = outputSchema,
            Annotations = annotationsObject,
            Handler = async (args, ct) => await handler(args, ct).ConfigureAwait(false)
        };

        McpToolDefinition previous;
        if (_tools.TryGetValue(name, out previous)) _toolOrder.Remove(previous);

        _tools[name] = definition;
        _toolOrder.Add(definition);

        return this;
    }

    // ── Resource Registration ─────────────────────────────────────────────

    /// <summary>
    /// Register a static resource. The handler returns the resource contents
    /// as a JArray of {uri, text, mimeType} or {uri, blob, mimeType} objects.
    /// </summary>
    public McpRequestHandler AddResource(
        string uri,
        string name,
        string description,
        Func<CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotationsConfig = null)
    {
        JObject annotations = null;
        if (annotationsConfig != null)
        {
            annotations = new JObject();
            annotationsConfig(annotations);
        }

        var definition = new McpResourceDefinition
        {
            Uri = uri,
            Name = name,
            Description = description,
            MimeType = mimeType,
            Annotations = annotations,
            Handler = handler
        };

        McpResourceDefinition previous;
        if (_resources.TryGetValue(uri, out previous)) _resourceOrder.Remove(previous);

        _resources[uri] = definition;
        _resourceOrder.Add(definition);

        return this;
    }

    /// <summary>
    /// Register a resource template. The handler receives the resolved URI
    /// and returns the resource contents as a JArray.
    /// </summary>
    public McpRequestHandler AddResourceTemplate(
        string uriTemplate,
        string name,
        string description,
        Func<string, CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotationsConfig = null)
    {
        JObject annotations = null;
        if (annotationsConfig != null)
        {
            annotations = new JObject();
            annotationsConfig(annotations);
        }

        _resourceTemplates.Add(new McpResourceTemplateDefinition
        {
            UriTemplate = uriTemplate,
            Name = name,
            Description = description,
            MimeType = mimeType,
            Annotations = annotations,
            Handler = handler
        });

        return this;
    }

    // ── Prompt Registration ──────────────────────────────────────────────

    /// <summary>
    /// Register a prompt. The handler receives the argument values as a JObject
    /// and returns a JArray of message objects ({role, content: {type, text}}).
    ///
    /// Copilot Studio does not consume MCP prompts. Register them for clients that do.
    /// </summary>
    public McpRequestHandler AddPrompt(
        string name,
        string description,
        List<McpPromptArgument> arguments,
        Func<JObject, CancellationToken, Task<JArray>> handler)
    {
        var definition = new McpPromptDefinition
        {
            Name = name,
            Description = description,
            Arguments = arguments ?? new List<McpPromptArgument>(),
            Handler = handler
        };

        McpPromptDefinition previous;
        if (_prompts.TryGetValue(name, out previous)) _promptOrder.Remove(previous);

        _prompts[name] = definition;
        _promptOrder.Add(definition);

        return this;
    }

    // ── Skill Registration ───────────────────────────────────────────────

    /// <summary>
    /// Publish an Agent Skill over MCP.
    ///
    /// The skill is exposed as ordinary MCP resources under the skill:// scheme, and a
    /// skill://index.json discovery document is generated from every registered skill.
    /// Agent Framework (.NET UseMcpSkills, Python MCPSkillsSource) and Microsoft Foundry
    /// Toolbox read that index and fetch SKILL.md on demand. Only the skill-md
    /// distribution type is produced; archives are not supported.
    /// </summary>
    /// <param name="name">Lowercase letters, digits, and single hyphens. Max 64 characters.</param>
    /// <param name="description">What the skill does and when to use it. Max 1024 characters.</param>
    /// <param name="instructions">The markdown body of SKILL.md. Keep it under 500 lines.</param>
    /// <param name="resources">Sibling files the instructions reference.</param>
    public McpRequestHandler AddSkill(
        string name,
        string description,
        string instructions,
        Action<McpSkillResourceBuilder> resources = null,
        string license = null,
        string compatibility = null,
        JObject metadata = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Skill name is required.");
        if (name.Length > 64)
            throw new ArgumentException($"Skill name '{name}' exceeds 64 characters.");
        if (!Regex.IsMatch(name, "^[a-z0-9]+(-[a-z0-9]+)*$"))
            throw new ArgumentException($"Skill name '{name}' must use lowercase letters, digits, and single hyphens, with no leading or trailing hyphen.");
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException($"Skill '{name}' requires a description.");
        if (description.Length > 1024)
            throw new ArgumentException($"Skill '{name}' description exceeds 1024 characters.");
        if (!string.IsNullOrEmpty(compatibility) && compatibility.Length > 500)
            throw new ArgumentException($"Skill '{name}' compatibility exceeds 500 characters.");

        var resourceBuilder = new McpSkillResourceBuilder();
        resources?.Invoke(resourceBuilder);

        var skill = new McpSkillDefinition
        {
            Name = name,
            Description = description,
            Instructions = instructions,
            License = license,
            Compatibility = compatibility,
            Metadata = metadata,
            Resources = resourceBuilder.Resources
        };

        _skills.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        _skills.Add(skill);

        // The index handler runs at request time, so it always sees every skill
        // registered so far regardless of registration order.
        if (!_resources.ContainsKey(McpKeys.SkillIndexUri))
        {
            AddResource(McpKeys.SkillIndexUri, "Agent Skills index",
                "Discovery document listing the Agent Skills this server publishes.",
                handler: (ct) => Task.FromResult(BuildSkillIndexContents()));
        }

        AddResource(skill.SkillUri, name, description,
            handler: (ct) => Task.FromResult(new JArray
            {
                new JObject
                {
                    ["uri"] = skill.SkillUri,
                    ["mimeType"] = "text/markdown",
                    ["text"] = skill.RenderSkillMarkdown()
                }
            }),
            mimeType: "text/markdown");

        foreach (var resource in skill.Resources)
        {
            var uri = skill.ResourceUri(resource.Path);
            var captured = resource;

            AddResource(uri, resource.Path, resource.Description ?? resource.Path,
                handler: (ct) => Task.FromResult(new JArray
                {
                    new JObject
                    {
                        ["uri"] = uri,
                        ["mimeType"] = captured.MimeType,
                        ["text"] = captured.Content != null ? captured.Content() : string.Empty
                    }
                }),
                mimeType: resource.MimeType);
        }

        return this;
    }

    /// <summary>Build the skill://index.json contents from every registered skill.</summary>
    private JArray BuildSkillIndexContents()
    {
        var entries = new JArray();

        foreach (var skill in _skills)
        {
            var entry = new JObject
            {
                ["type"] = "skill-md",
                ["name"] = skill.Name,
                ["description"] = skill.Description,
                ["uri"] = skill.SkillUri
            };

            if (skill.Resources.Count > 0)
            {
                var resourceArray = new JArray();
                foreach (var resource in skill.Resources)
                {
                    resourceArray.Add(new JObject
                    {
                        ["path"] = resource.Path,
                        ["uri"] = skill.ResourceUri(resource.Path),
                        ["description"] = resource.Description,
                        ["mimeType"] = resource.MimeType
                    });
                }
                entry["resources"] = resourceArray;
            }

            entries.Add(entry);
        }

        var index = new JObject { ["skills"] = entries };

        return new JArray
        {
            new JObject
            {
                ["uri"] = McpKeys.SkillIndexUri,
                ["mimeType"] = "application/json",
                ["text"] = index.ToString(Newtonsoft.Json.Formatting.Indented)
            }
        };
    }

    // ── Main Handler ─────────────────────────────────────────────────────

    /// <summary>
    /// Process a raw JSON-RPC 2.0 request string and return a JSON-RPC response string.
    /// This is the single method that bridges the gap.
    /// </summary>
    public Task<string> HandleAsync(string body, CancellationToken cancellationToken)
    {
        return HandleAsync(body, null, cancellationToken);
    }

    /// <summary>
    /// Process a request, using the Streamable HTTP headers to detect the protocol era
    /// and to check Mcp-Method / Mcp-Name against the body.
    /// </summary>
    public async Task<string> HandleAsync(string body, McpTransportHeaders headers, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body))
            return SerializeError(null, McpErrorCode.InvalidRequest, "Empty request body");

        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch (JsonException)
        {
            return SerializeError(null, McpErrorCode.ParseError, "Invalid JSON");
        }

        var ctx = BuildContext(request, headers);

        Log("McpRequestReceived", new
        {
            Method = ctx.Method,
            HasId = ctx.Id != null,
            Era = ctx.IsModern ? "modern" : "legacy",
            ProtocolVersion = ctx.ProtocolVersion
        });

        // 2026-07-28 negotiates per request rather than once per session.
        if (ctx.IsModern && !IsSupportedVersion(ctx.ProtocolVersion))
        {
            Log("McpUnsupportedProtocolVersion", new { Requested = ctx.ProtocolVersion });
            return SerializeError(ctx.Id, McpErrorCode.UnsupportedProtocolVersion,
                "Unsupported protocol version",
                new JObject
                {
                    ["supported"] = new JArray(_options.SupportedProtocolVersions),
                    ["requested"] = ctx.ProtocolVersion
                });
        }

        var headerError = ValidateHeaders(ctx, request, headers);
        if (headerError != null) return headerError;

        try
        {
            switch (ctx.Method)
            {
                // Discovery — servers MUST implement this in 2026-07-28.
                case "server/discover":
                    return HandleDiscover(ctx);

                // The legacy handshake. A client that opens this way is asking for legacy
                // semantics even if it also sent a version marker, so this never 404s.
                case "initialize":
                    return HandleInitialize(ctx, request);

                case "initialized":
                case "notifications/initialized":
                    return SerializeSuccess(ctx, new JObject());

                case "notifications/roots/list_changed":
                    return ctx.IsModern
                        ? MethodRemoved(ctx, ctx.Method)
                        : SerializeSuccess(ctx, new JObject());

                // Removed in 2026-07-28: ping and logging/setLevel are gone, and
                // resource subscriptions moved to the subscriptions/listen stream,
                // which a request/response connector cannot hold open.
                case "ping":
                case "logging/setLevel":
                case "resources/subscribe":
                case "resources/unsubscribe":
                    return ctx.IsModern
                        ? MethodRemoved(ctx, ctx.Method)
                        : SerializeSuccess(ctx, new JObject());

                // Valid in both eras. Copilot Studio expects a JSON-RPC body for every
                // request including notifications, so this always answers.
                case "notifications/cancelled":
                    return SerializeSuccess(ctx, new JObject());

                // Tools
                case "tools/list":
                    return HandleToolsList(ctx);

                case "tools/call":
                    return await HandleToolsCallAsync(ctx, request, cancellationToken).ConfigureAwait(false);

                // Resources
                case "resources/list":
                    return HandleResourcesList(ctx);

                case "resources/templates/list":
                    return HandleResourceTemplatesList(ctx);

                case "resources/read":
                    return await HandleResourcesReadAsync(ctx, request, cancellationToken).ConfigureAwait(false);

                // Prompts
                case "prompts/list":
                    return HandlePromptsList(ctx);

                case "prompts/get":
                    return await HandlePromptsGetAsync(ctx, request, cancellationToken).ConfigureAwait(false);

                // Completions
                case "completion/complete":
                    return SerializeSuccess(ctx, new JObject
                    {
                        ["completion"] = new JObject
                        {
                            ["values"] = new JArray(),
                            ["total"] = 0,
                            ["hasMore"] = false
                        }
                    });

                default:
                    Log("McpMethodNotFound", new { Method = ctx.Method });
                    return SerializeError(ctx.Id, McpErrorCode.MethodNotFound, "Method not found", ctx.Method);
            }
        }
        catch (McpException ex)
        {
            Log("McpError", new { Method = ctx.Method, Code = (int)ex.Code, Message = ex.Message });
            return SerializeError(ctx.Id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            Log("McpError", new { Method = ctx.Method, Error = ex.Message });
            return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
        }
    }

    // ── Era Detection ────────────────────────────────────────────────────

    /// <summary>
    /// Decide which protocol era this request belongs to and capture its metadata.
    ///
    /// The spec picks the era from how the client opens the conversation, so an
    /// initialize-style request is always legacy no matter what else it carries — a client
    /// mid-upgrade might send MCP-Protocol-Version while still using the handshake, and
    /// rejecting its initialize would break the connection for no benefit.
    ///
    /// Otherwise a request is modern when it carries a protocol version in _meta or in the
    /// MCP-Protocol-Version header, or when it calls server/discover. Everything else,
    /// Copilot Studio included, is served under legacy semantics.
    /// </summary>
    private McpRequestContext BuildContext(JObject request, McpTransportHeaders headers)
    {
        var paramsObj = request["params"] as JObject;
        var meta = paramsObj?["_meta"] as JObject;
        var method = request.Value<string>("method") ?? string.Empty;

        var metaVersion = meta?[McpKeys.ProtocolVersion]?.ToString();
        var version = !string.IsNullOrWhiteSpace(metaVersion) ? metaVersion : headers?.ProtocolVersion;

        var opensLegacy = method == "initialize"
            || method == "initialized"
            || method == "notifications/initialized";

        var isModern = !opensLegacy
            && (!string.IsNullOrWhiteSpace(version) || method == "server/discover");

        return new McpRequestContext
        {
            Id = request["id"],
            Method = method,
            IsModern = isModern,
            ProtocolVersion = !string.IsNullOrWhiteSpace(version) ? version : _options.ProtocolVersion,
            ClientInfo = meta?[McpKeys.ClientInfo] as JObject,
            ClientCapabilities = meta?[McpKeys.ClientCapabilities] as JObject,
            LogLevel = meta?[McpKeys.LogLevel]?.ToString(),
            InputResponses = paramsObj?["inputResponses"] as JObject,
            RequestState = paramsObj?["requestState"]?.ToString()
        };
    }

    private bool IsSupportedVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return true;

        var supported = _options.SupportedProtocolVersions;
        if (supported == null || supported.Count == 0)
            return string.Equals(version, _options.ProtocolVersion, StringComparison.Ordinal);

        foreach (var candidate in supported)
            if (string.Equals(candidate, version, StringComparison.Ordinal)) return true;

        return false;
    }

    /// <summary>
    /// 2026-07-28 requires Mcp-Method and Mcp-Name on Streamable HTTP so gateways can route
    /// without parsing the body. A header that disagrees with the body is a HeaderMismatch.
    /// An absent header is ignored: the Power Platform gateway makes no guarantee that it
    /// forwards custom headers, and failing closed would break every deployment.
    /// </summary>
    private string ValidateHeaders(McpRequestContext ctx, JObject request, McpTransportHeaders headers)
    {
        if (!ctx.IsModern || !_options.ValidateRequestHeaders || headers == null) return null;

        if (!string.IsNullOrWhiteSpace(headers.Method) &&
            !string.Equals(headers.Method, ctx.Method, StringComparison.Ordinal))
        {
            Log("McpHeaderMismatch", new { Header = headers.Method, Body = ctx.Method });
            return SerializeError(ctx.Id, McpErrorCode.HeaderMismatch,
                "Mcp-Method header does not match the request method",
                new JObject { ["header"] = headers.Method, ["body"] = ctx.Method });
        }

        if (!string.IsNullOrWhiteSpace(headers.Name))
        {
            var bodyName = (request["params"] as JObject)?.Value<string>("name");
            if (!string.IsNullOrWhiteSpace(bodyName) &&
                !string.Equals(headers.Name, bodyName, StringComparison.Ordinal))
            {
                Log("McpHeaderMismatch", new { Header = headers.Name, Body = bodyName });
                return SerializeError(ctx.Id, McpErrorCode.HeaderMismatch,
                    "Mcp-Name header does not match the request target",
                    new JObject { ["header"] = headers.Name, ["body"] = bodyName });
            }
        }

        return null;
    }

    /// <summary>Report a method that 2026-07-28 removed, rather than a bare not-found.</summary>
    private string MethodRemoved(McpRequestContext ctx, string method)
    {
        Log("McpMethodRemoved", new { Method = method, ProtocolVersion = ctx.ProtocolVersion });
        return SerializeError(ctx.Id, McpErrorCode.MethodNotFound,
            $"'{method}' was removed in MCP 2026-07-28", method);
    }

    // ── Protocol Handlers ────────────────────────────────────────────────

    /// <summary>
    /// server/discover — mandatory in 2026-07-28. Reports supported versions,
    /// capabilities, and identity in a single round trip.
    /// </summary>
    private string HandleDiscover(McpRequestContext ctx)
    {
        var result = new JObject
        {
            ["supportedVersions"] = new JArray(_options.SupportedProtocolVersions),
            ["capabilities"] = BuildCapabilities(isModern: true)
        };

        if (!string.IsNullOrWhiteSpace(_options.Instructions))
            result["instructions"] = _options.Instructions;

        Log("McpDiscovered", new
        {
            Server = _options.ServerInfo.Name,
            Versions = _options.SupportedProtocolVersions.Count
        });

        return SerializeSuccess(ctx, result, McpCacheKind.Discover);
    }

    private JObject BuildCapabilities(bool isModern)
    {
        var capabilities = new JObject();

        if (_options.Capabilities.Tools)
            capabilities["tools"] = new JObject { ["listChanged"] = false };

        if (_options.Capabilities.Resources)
        {
            // subscribe is meaningless in the modern era without a subscriptions/listen stream.
            capabilities["resources"] = isModern
                ? new JObject { ["listChanged"] = false }
                : new JObject { ["subscribe"] = false, ["listChanged"] = false };
        }

        if (_options.Capabilities.Prompts)
            capabilities["prompts"] = new JObject { ["listChanged"] = false };

        if (_options.Capabilities.Completions)
            capabilities["completions"] = new JObject();

        // Logging is deprecated in 2026-07-28, so it is offered to legacy clients only.
        if (_options.Capabilities.Logging && !isModern)
            capabilities["logging"] = new JObject();

        if (_options.Capabilities.Extensions != null && _options.Capabilities.Extensions.Count > 0)
            capabilities["extensions"] = _options.Capabilities.Extensions;

        return capabilities;
    }

    private JObject BuildServerInfo(bool includeDetail)
    {
        var serverInfo = new JObject
        {
            ["name"] = _options.ServerInfo.Name,
            ["version"] = _options.ServerInfo.Version
        };

        if (!includeDetail) return serverInfo;

        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Title))
            serverInfo["title"] = _options.ServerInfo.Title;
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Description))
            serverInfo["description"] = _options.ServerInfo.Description;
        if (_options.ServerInfo.Icons != null && _options.ServerInfo.Icons.Count > 0)
            serverInfo["icons"] = _options.ServerInfo.Icons;

        return serverInfo;
    }

    /// <summary>The legacy handshake, for clients on 2025-11-25 and earlier.</summary>
    private string HandleInitialize(McpRequestContext ctx, JObject request)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString();
        var negotiated = !string.IsNullOrWhiteSpace(clientProtocolVersion)
            ? clientProtocolVersion
            : "2025-11-25";

        var result = new JObject
        {
            ["protocolVersion"] = negotiated,
            ["capabilities"] = BuildCapabilities(isModern: false),
            ["serverInfo"] = BuildServerInfo(includeDetail: true)
        };

        if (!string.IsNullOrWhiteSpace(_options.Instructions))
            result["instructions"] = _options.Instructions;

        Log("McpInitialized", new
        {
            Server = _options.ServerInfo.Name,
            Version = _options.ServerInfo.Version,
            ProtocolVersion = negotiated
        });

        return SerializeSuccess(ctx, result);
    }

    private string HandleToolsList(McpRequestContext ctx)
    {
        var toolsArray = new JArray();
        foreach (var tool in _toolOrder)
        {
            var toolObj = new JObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = tool.InputSchema
            };
            if (!string.IsNullOrWhiteSpace(tool.Title))
                toolObj["title"] = tool.Title;
            if (tool.OutputSchema != null)
                toolObj["outputSchema"] = tool.OutputSchema;
            if (tool.Annotations != null && tool.Annotations.Count > 0)
                toolObj["annotations"] = tool.Annotations;
            toolsArray.Add(toolObj);
        }

        Log("McpToolsListed", new { Count = _toolOrder.Count });
        return SerializeSuccess(ctx, new JObject { ["tools"] = toolsArray }, McpCacheKind.List);
    }

    private string HandleResourcesList(McpRequestContext ctx)
    {
        var resourcesArray = new JArray();
        foreach (var res in _resourceOrder)
        {
            var obj = new JObject
            {
                ["uri"] = res.Uri,
                ["name"] = res.Name
            };
            if (!string.IsNullOrWhiteSpace(res.Description))
                obj["description"] = res.Description;
            if (!string.IsNullOrWhiteSpace(res.MimeType))
                obj["mimeType"] = res.MimeType;
            if (res.Annotations != null && res.Annotations.Count > 0)
                obj["annotations"] = res.Annotations;
            resourcesArray.Add(obj);
        }

        Log("McpResourcesListed", new { Count = _resourceOrder.Count });
        return SerializeSuccess(ctx, new JObject { ["resources"] = resourcesArray }, McpCacheKind.List);
    }

    private string HandleResourceTemplatesList(McpRequestContext ctx)
    {
        var templatesArray = new JArray();
        foreach (var tmpl in _resourceTemplates)
        {
            var obj = new JObject
            {
                ["uriTemplate"] = tmpl.UriTemplate,
                ["name"] = tmpl.Name
            };
            if (!string.IsNullOrWhiteSpace(tmpl.Description))
                obj["description"] = tmpl.Description;
            if (!string.IsNullOrWhiteSpace(tmpl.MimeType))
                obj["mimeType"] = tmpl.MimeType;
            if (tmpl.Annotations != null && tmpl.Annotations.Count > 0)
                obj["annotations"] = tmpl.Annotations;
            templatesArray.Add(obj);
        }

        Log("McpResourceTemplatesListed", new { Count = _resourceTemplates.Count });
        return SerializeSuccess(ctx, new JObject { ["resourceTemplates"] = templatesArray }, McpCacheKind.List);
    }

    private async Task<string> HandleResourcesReadAsync(McpRequestContext ctx, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var uri = paramsObj?.Value<string>("uri");

        if (string.IsNullOrWhiteSpace(uri))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, "Resource URI is required");

        // 1. Try exact match on registered static resources
        if (_resources.TryGetValue(uri, out var resource))
        {
            Log("McpResourceReadStarted", new { Uri = uri });
            try
            {
                var contents = await resource.Handler(ct).ConfigureAwait(false);
                Log("McpResourceReadCompleted", new { Uri = uri });
                return SerializeSuccess(ctx, new JObject { ["contents"] = contents }, McpCacheKind.Resource);
            }
            catch (Exception ex)
            {
                Log("McpResourceReadError", new { Uri = uri, Error = ex.Message });
                return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
            }
        }

        // 2. Try matching against registered resource templates
        foreach (var tmpl in _resourceTemplates)
        {
            if (MatchesUriTemplate(tmpl.UriTemplate, uri))
            {
                Log("McpResourceReadStarted", new { Uri = uri, Template = tmpl.UriTemplate });
                try
                {
                    var contents = await tmpl.Handler(uri, ct).ConfigureAwait(false);
                    Log("McpResourceReadCompleted", new { Uri = uri });
                    return SerializeSuccess(ctx, new JObject { ["contents"] = contents }, McpCacheKind.Resource);
                }
                catch (Exception ex)
                {
                    Log("McpResourceReadError", new { Uri = uri, Error = ex.Message });
                    return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
                }
            }
        }

        // 2026-07-28 moved resource-not-found from -32002 to -32602 (Invalid Params).
        return SerializeError(ctx.Id, McpErrorCode.InvalidParams, $"Resource not found: {uri}");
    }

    /// <summary>
    /// Simple URI template matcher. Checks if a concrete URI matches a template
    /// with {param} placeholders (e.g., "data://records/{id}" matches "data://records/123").
    /// </summary>
    private static bool MatchesUriTemplate(string template, string uri)
    {
        // Split both on '/' and compare segments
        var templateParts = template.Split('/');
        var uriParts = uri.Split('/');

        if (templateParts.Length != uriParts.Length) return false;

        for (int i = 0; i < templateParts.Length; i++)
        {
            var seg = templateParts[i];
            if (seg.StartsWith("{") && seg.EndsWith("}")) continue; // wildcard
            if (!string.Equals(seg, uriParts[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>
    /// Extract named parameters from a URI given a template pattern.
    /// E.g., template "data://records/{id}" with uri "data://records/123" returns { "id": "123" }.
    /// </summary>
    public static Dictionary<string, string> ExtractUriParameters(string template, string uri)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var templateParts = template.Split('/');
        var uriParts = uri.Split('/');

        if (templateParts.Length != uriParts.Length) return result;

        for (int i = 0; i < templateParts.Length; i++)
        {
            var seg = templateParts[i];
            if (seg.StartsWith("{") && seg.EndsWith("}"))
            {
                var paramName = seg.Substring(1, seg.Length - 2);
                result[paramName] = uriParts[i];
            }
        }
        return result;
    }

    private string HandlePromptsList(McpRequestContext ctx)
    {
        var promptsArray = new JArray();
        foreach (var prompt in _promptOrder)
        {
            var obj = new JObject
            {
                ["name"] = prompt.Name
            };
            if (!string.IsNullOrWhiteSpace(prompt.Description))
                obj["description"] = prompt.Description;

            if (prompt.Arguments.Count > 0)
            {
                var argsArray = new JArray();
                foreach (var arg in prompt.Arguments)
                {
                    var argObj = new JObject { ["name"] = arg.Name };
                    if (!string.IsNullOrWhiteSpace(arg.Description))
                        argObj["description"] = arg.Description;
                    if (arg.Required)
                        argObj["required"] = true;
                    argsArray.Add(argObj);
                }
                obj["arguments"] = argsArray;
            }

            promptsArray.Add(obj);
        }

        Log("McpPromptsListed", new { Count = _promptOrder.Count });
        return SerializeSuccess(ctx, new JObject { ["prompts"] = promptsArray }, McpCacheKind.List);
    }

    private async Task<string> HandlePromptsGetAsync(McpRequestContext ctx, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var promptName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(promptName))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, "Prompt name is required");

        if (!_prompts.TryGetValue(promptName, out var prompt))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, $"Prompt not found: {promptName}");

        Log("McpPromptGetStarted", new { Prompt = promptName });

        try
        {
            var messages = await prompt.Handler(arguments, ct).ConfigureAwait(false);
            Log("McpPromptGetCompleted", new { Prompt = promptName, MessageCount = messages.Count });

            var result = new JObject { ["messages"] = messages };
            if (!string.IsNullOrWhiteSpace(prompt.Description))
                result["description"] = prompt.Description;

            return SerializeSuccess(ctx, result);
        }
        catch (Exception ex)
        {
            Log("McpPromptGetError", new { Prompt = promptName, Error = ex.Message });
            return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
        }
    }

    private async Task<string> HandleToolsCallAsync(McpRequestContext ctx, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, "Tool name is required");

        if (!_tools.TryGetValue(toolName, out var tool))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, $"Unknown tool: {toolName}");

        Log("McpToolCallStarted", new { Tool = toolName, IsRetry = ctx.InputResponses != null });

        try
        {
            var result = await tool.Handler(arguments, ct).ConfigureAwait(false);

            // Multi round-trip request: the tool needs more input before it can finish.
            if (result is JObject marker && marker.Value<bool?>("__mcpInputRequired") == true)
                return SerializeInputRequired(ctx, toolName, marker);

            JObject callResult;

            // Support pre-formatted MCP tool results with rich content types
            // (image, audio, resource, or mixed content arrays).
            // If the handler returns { "content": [ { "type": "..." } ], ... },
            // pass it through directly instead of wrapping in text.
            if (result is JObject jobj && jobj["content"] is JArray contentArray
                && contentArray.Count > 0 && contentArray[0]?["type"] != null)
            {
                callResult = new JObject
                {
                    ["content"] = contentArray,
                    ["isError"] = jobj.Value<bool?>("isError") ?? false
                };

                // structuredContent may be any JSON value as of 2026-07-28.
                if (jobj["structuredContent"] != null)
                    callResult["structuredContent"] = jobj["structuredContent"];
            }
            else
            {
                string text;
                if (result is JObject plainObj)
                    text = plainObj.ToString(Newtonsoft.Json.Formatting.Indented);
                else if (result is string s)
                    text = s;
                else if (result == null)
                    text = "{}";
                else
                    text = JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);

                callResult = new JObject
                {
                    ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
                    ["isError"] = false
                };
            }

            Log("McpToolCallCompleted", new { Tool = toolName, IsError = callResult.Value<bool>("isError") });
            return SerializeSuccess(ctx, callResult);
        }
        catch (ArgumentException ex)
        {
            return SerializeSuccess(ctx, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
        catch (McpException ex)
        {
            return SerializeSuccess(ctx, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
        catch (Exception ex)
        {
            Log("McpToolCallError", new { Tool = toolName, Error = ex.Message });

            return SerializeSuccess(ctx, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
    }

    /// <summary>
    /// Emit an input_required result. Legacy clients cannot interpret resultType, so they
    /// receive a tool error naming what the tool still needs rather than a silent failure.
    /// </summary>
    private string SerializeInputRequired(McpRequestContext ctx, string toolName, JObject marker)
    {
        var inputRequests = marker["inputRequests"] as JObject ?? new JObject();

        if (!ctx.IsModern)
        {
            var needed = string.Join(", ", inputRequests.Properties().Select(p => p.Name));
            Log("McpInputRequiredUnsupported", new { Tool = toolName, Needed = needed });

            return SerializeSuccess(ctx, new JObject
            {
                ["content"] = new JArray
                {
                    TextContent($"'{toolName}' needs additional input ({needed}). That requires an MCP client on 2026-07-28 or later; supply the values as tool arguments instead.")
                },
                ["isError"] = true
            });
        }

        var result = new JObject { ["inputRequests"] = inputRequests };

        var requestState = marker.Value<string>("requestState");
        if (!string.IsNullOrWhiteSpace(requestState))
            result["requestState"] = requestState;

        Log("McpInputRequired", new { Tool = toolName, Count = inputRequests.Count });

        return SerializeSuccess(ctx, result, McpCacheKind.None, "input_required");
    }

    // ── Content Helpers ────────────────────────────────────────────────
    //
    //    Use these to build rich tool results with image, audio, or resource
    //    content. Return McpRequestHandler.ToolResult(...) from your handler
    //    to bypass automatic text wrapping.
    //

    /// <summary>Create a text content item.</summary>
    public static JObject TextContent(string text) =>
        new JObject { ["type"] = "text", ["text"] = text };

    /// <summary>Create an image content item (base64-encoded).</summary>
    public static JObject ImageContent(string base64Data, string mimeType) =>
        new JObject { ["type"] = "image", ["data"] = base64Data, ["mimeType"] = mimeType };

    /// <summary>Create an audio content item (base64-encoded).</summary>
    public static JObject AudioContent(string base64Data, string mimeType) =>
        new JObject { ["type"] = "audio", ["data"] = base64Data, ["mimeType"] = mimeType };

    /// <summary>Create an embedded resource content item.</summary>
    public static JObject ResourceContent(string uri, string text, string mimeType = "text/plain") =>
        new JObject
        {
            ["type"] = "resource",
            ["resource"] = new JObject { ["uri"] = uri, ["text"] = text, ["mimeType"] = mimeType }
        };

    /// <summary>
    /// Create a resource link. Points the client at a resource it can fetch itself,
    /// instead of embedding the bytes in the tool result.
    /// </summary>
    public static JObject ResourceLinkContent(string uri, string name, string description = null, string mimeType = null)
    {
        var link = new JObject { ["type"] = "resource_link", ["uri"] = uri, ["name"] = name };
        if (!string.IsNullOrWhiteSpace(description)) link["description"] = description;
        if (!string.IsNullOrWhiteSpace(mimeType)) link["mimeType"] = mimeType;
        return link;
    }

    /// <summary>
    /// Build a pre-formatted tool result with mixed content types.
    /// Return this from a tool handler to bypass automatic text wrapping.
    /// </summary>
    public static JObject ToolResult(JArray content, JToken structuredContent = null, bool isError = false)
    {
        var result = new JObject { ["content"] = content, ["isError"] = isError };
        if (structuredContent != null) result["structuredContent"] = structuredContent;
        return result;
    }

    // ── Multi Round-Trip Requests ──────────────────────────────────────

    /// <summary>
    /// Ask the client for more input before the tool can finish. Return this from a tool
    /// handler; the client answers by retrying the same tools/call with inputResponses
    /// attached, which arrive on McpRequestContext.InputResponses.
    ///
    /// This is how a stateless server performs elicitation as of 2026-07-28. Because the
    /// server holds no state between the two calls, encode anything you need to resume in
    /// requestState and read it back off the retry.
    /// </summary>
    public static JObject InputRequired(JObject inputRequests, string requestState = null)
    {
        var result = new JObject
        {
            ["__mcpInputRequired"] = true,
            ["inputRequests"] = inputRequests ?? new JObject()
        };
        if (!string.IsNullOrWhiteSpace(requestState)) result["requestState"] = requestState;
        return result;
    }

    /// <summary>Build a single elicitation entry for an InputRequired result.</summary>
    public static JObject ElicitationRequest(string message, Action<McpSchemaBuilder> schemaConfig, string mode = "form")
    {
        var builder = new McpSchemaBuilder();
        schemaConfig?.Invoke(builder);

        return new JObject
        {
            ["method"] = "elicitation/create",
            ["params"] = new JObject
            {
                ["mode"] = mode,
                ["message"] = message,
                ["requestedSchema"] = builder.Build()
            }
        };
    }

    // ── JSON-RPC Serialization ───────────────────────────────────────────

    private string SerializeSuccess(McpRequestContext ctx, JObject result)
    {
        return SerializeSuccess(ctx, result, McpCacheKind.None, "complete");
    }

    private string SerializeSuccess(McpRequestContext ctx, JObject result, McpCacheKind cache)
    {
        return SerializeSuccess(ctx, result, cache, "complete");
    }

    /// <summary>
    /// Envelope a successful result. Modern callers additionally get resultType, server
    /// identity in _meta, and cache hints. Legacy callers get exactly the shape they got
    /// before 2026-07-28, which is what keeps Copilot Studio working unchanged.
    /// </summary>
    private string SerializeSuccess(McpRequestContext ctx, JObject result, McpCacheKind cache, string resultType)
    {
        result = result ?? new JObject();

        if (ctx != null && ctx.IsModern)
        {
            result["resultType"] = resultType;

            var meta = result["_meta"] as JObject ?? new JObject();
            meta[McpKeys.ServerInfo] = BuildServerInfo(includeDetail: false);
            result["_meta"] = meta;

            // Only complete results are cacheable; input_required is interim.
            if (resultType == "complete") ApplyCacheHints(result, cache);
        }

        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = ctx?.Id,
            ["result"] = result
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private void ApplyCacheHints(JObject result, McpCacheKind cache)
    {
        switch (cache)
        {
            case McpCacheKind.List:
                result["ttlMs"] = Math.Max(0, _options.ListCacheTtlMs);
                result["cacheScope"] = _options.ListCacheScope ?? "public";
                break;

            case McpCacheKind.Resource:
                result["ttlMs"] = Math.Max(0, _options.ResourceCacheTtlMs);
                result["cacheScope"] = _options.ResourceCacheScope ?? "private";
                break;

            case McpCacheKind.Discover:
                result["ttlMs"] = Math.Max(0, _options.DiscoverCacheTtlMs);
                result["cacheScope"] = _options.ListCacheScope ?? "public";
                break;
        }
    }

    private string SerializeError(JToken id, McpErrorCode code, string message, JToken data = null)
    {
        var error = new JObject
        {
            ["code"] = (int)code,
            ["message"] = message
        };

        if (data != null && data.Type != JTokenType.Null)
            error["data"] = data;

        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private void Log(string eventName, object data)
    {
        OnLog?.Invoke(eventName, data);
    }
}

/// <summary>Which cache hints a result should carry, per the 2026-07-28 CacheableResult rules.</summary>
public enum McpCacheKind
{
    None,
    List,
    Resource,
    Discover
}

