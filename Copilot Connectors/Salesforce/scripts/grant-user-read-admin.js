// Grant User.Read.All using admin user token (ROPC flow)
//
// Requires: AZURE_TENANT_ID, AZURE_CLIENT_ID in .env.local
// Requires: ADMIN_USER, ADMIN_PASSWORD env vars
// Optionally set APP_SP_ID/GRAPH_SP_ID to skip auto-discovery.

require("dotenv").config({ path: ".env.local" });

const TENANT_ID = process.env.AZURE_TENANT_ID;
const CLIENT_ID = process.env.AZURE_CLIENT_ID;
const ADMIN_USER = process.env.ADMIN_USER;
const ADMIN_PASS = process.env.ADMIN_PASSWORD;
const USER_READ_ALL_ROLE = "df021288-bdef-4463-88db-98f22de89214";

// Well-known Microsoft Graph PowerShell client ID (public client, no secret needed)
const PS_CLIENT_ID = "14d82eec-204b-4c2f-b7e8-296a70dab67e";

async function findSp(token, filter) {
  const res = await fetch(
    `https://graph.microsoft.com/v1.0/servicePrincipals?$filter=${encodeURIComponent(filter)}&$select=id,appId,displayName`,
    { headers: { Authorization: `Bearer ${token}` } }
  );
  const data = await res.json();
  return data.value?.[0];
}

async function main() {
  if (!ADMIN_USER || !ADMIN_PASS) {
    console.log("Set ADMIN_USER and ADMIN_PASSWORD env vars.");
    console.log("Usage: ADMIN_USER=user@tenant.onmicrosoft.com ADMIN_PASSWORD=<pass> node scripts/grant-user-read-admin.js");
    process.exit(1);
  }

  // Get admin token via ROPC
  const tokenUrl = `https://login.microsoftonline.com/${TENANT_ID}/oauth2/v2.0/token`;
  const params = new URLSearchParams({
    grant_type: "password",
    client_id: PS_CLIENT_ID,
    scope: "https://graph.microsoft.com/.default",
    username: ADMIN_USER,
    password: ADMIN_PASS,
  });

  const tokenRes = await fetch(tokenUrl, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: params.toString(),
  });

  const tokenData = await tokenRes.json();
  if (!tokenData.access_token) {
    console.log("Failed to get admin token:", JSON.stringify(tokenData, null, 2));
    process.exit(1);
  }
  const token = tokenData.access_token;
  console.log("Got admin token successfully");

  // Auto-discover service principal IDs
  const appSp = process.env.APP_SP_ID
    ? { id: process.env.APP_SP_ID }
    : await findSp(token, `appId eq '${CLIENT_ID}'`);
  const graphSp = process.env.GRAPH_SP_ID
    ? { id: process.env.GRAPH_SP_ID }
    : await findSp(token, "appId eq '00000003-0000-0000-c000-000000000000'");

  if (!appSp) { console.error(`Could not find service principal for appId ${CLIENT_ID}`); process.exit(1); }
  if (!graphSp) { console.error("Could not find Microsoft Graph service principal"); process.exit(1); }

  console.log(`App SP: ${appSp.id}  |  Graph SP: ${graphSp.id}`);

  // Grant User.Read.All
  const body = JSON.stringify({
    principalId: appSp.id,
    resourceId: graphSp.id,
    appRoleId: USER_READ_ALL_ROLE,
  });

  const res = await fetch(
    `https://graph.microsoft.com/v1.0/servicePrincipals/${appSp.id}/appRoleAssignments`,
    {
      method: "POST",
      headers: {
        Authorization: `Bearer ${token}`,
        "Content-Type": "application/json",
      },
      body,
    }
  );

  const text = await res.text();
  console.log("Grant status:", res.status);
  try {
    const json = JSON.parse(text);
    if (res.status === 201) {
      console.log("User.Read.All granted successfully!");
      console.log("Assignment ID:", json.id);
    } else {
      console.log(JSON.stringify(json, null, 2));
    }
  } catch {
    console.log(text);
  }
}
main();
