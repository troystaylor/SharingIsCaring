// ── User Mapper: Salesforce OwnerId → Entra ID object ID for ACLs ──

import { queryAll } from "./restClient";
import { AccessControlEntry, EVERYONE_ACL } from "../models/graphTypes";
import { Client } from "@microsoft/microsoft-graph-client";
import { TokenCredentialAuthenticationProvider } from "@microsoft/microsoft-graph-client/authProviders/azureTokenCredentials";
import { ClientSecretCredential } from "@azure/identity";

interface SalesforceUser {
  Id: string;
  Email: string;
  Name: string;
  IsActive: boolean;
}

/** Cache of SF UserId → Entra ID object ID (GUID) */
let userMap: Map<string, string> | null = null;

function getGraphClient(): Client {
  const credential = new ClientSecretCredential(
    process.env.AZURE_TENANT_ID!,
    process.env.AZURE_CLIENT_ID!,
    process.env.AZURE_CLIENT_SECRET!
  );
  const authProvider = new TokenCredentialAuthenticationProvider(credential, {
    scopes: ["https://graph.microsoft.com/.default"],
  });
  return Client.initWithMiddleware({ authProvider });
}

/**
 * Load all active Salesforce users, resolve their emails to Entra ID
 * object IDs, and build an SF UserId → Entra object ID map.
 */
export async function loadUserMap(): Promise<Map<string, string>> {
  console.log("[UserMapper] Loading Salesforce users...");
  const soql = "SELECT Id, Email, Name, IsActive FROM User WHERE IsActive = true AND Email != null";
  const sfUsers = await queryAll<SalesforceUser>(soql);
  console.log(`[UserMapper] Found ${sfUsers.length} active SF users with emails`);

  // Collect unique emails
  const emailToSfIds = new Map<string, string[]>();
  for (const u of sfUsers) {
    if (u.Email) {
      const email = u.Email.toLowerCase();
      const ids = emailToSfIds.get(email) || [];
      ids.push(u.Id);
      emailToSfIds.set(email, ids);
    }
  }

  // Resolve emails to Entra ID object IDs
  const client = getGraphClient();
  const emailToEntraId = new Map<string, string>();

  // Batch lookup — query each email via /users?$filter=mail eq '...' or userPrincipalName eq '...'
  for (const email of emailToSfIds.keys()) {
    try {
      const result = await client
        .api("/users")
        .filter(`mail eq '${email}' or userPrincipalName eq '${email}'`)
        .select("id,mail,userPrincipalName")
        .top(1)
        .get();

      if (result.value && result.value.length > 0) {
        emailToEntraId.set(email, result.value[0].id);
      }
    } catch (err: unknown) {
      // Skip users not found in Entra ID
      const message = err instanceof Error ? err.message : String(err);
      console.log(`[UserMapper] Could not resolve ${email} in Entra ID: ${message}`);
    }
  }

  console.log(`[UserMapper] Resolved ${emailToEntraId.size}/${emailToSfIds.size} SF emails to Entra ID users`);

  // Build SF UserId → Entra object ID map
  userMap = new Map();
  for (const [email, sfIds] of emailToSfIds) {
    const entraId = emailToEntraId.get(email);
    if (entraId) {
      for (const sfId of sfIds) {
        userMap.set(sfId, entraId);
      }
    }
  }

  console.log(`[UserMapper] Mapped ${userMap.size} SF user IDs to Entra object IDs`);
  return userMap;
}

/** Get the cached user map (call loadUserMap first) */
export function getUserMap(): Map<string, string> {
  if (!userMap) {
    throw new Error("User map not loaded. Call loadUserMap() first.");
  }
  return userMap;
}

/**
 * Build an ACL for a record based on its OwnerId.
 * - If the owner maps to an Entra ID user, returns a user-grant ACL with the Entra object ID.
 * - Falls back to EVERYONE_ACL if the owner can't be resolved.
 */
export function buildOwnerAcl(ownerId: string | undefined): AccessControlEntry[] {
  if (!ownerId || !userMap) {
    return EVERYONE_ACL;
  }

  const entraObjectId = userMap.get(ownerId);
  if (!entraObjectId) {
    return EVERYONE_ACL;
  }

  return [
    {
      type: "user",
      value: entraObjectId,
      accessType: "grant",
    },
  ];
}

/** Clear the user cache (e.g., between crawl runs) */
export function clearUserMap(): void {
  userMap = null;
}
