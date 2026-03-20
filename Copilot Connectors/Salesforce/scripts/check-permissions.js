require('dotenv').config({ path: require('path').join(__dirname, '..', '.env.local') });
const { ClientSecretCredential } = require('@azure/identity');
const { Client } = require('@microsoft/microsoft-graph-client');
const { TokenCredentialAuthenticationProvider } = require('@microsoft/microsoft-graph-client/authProviders/azureTokenCredentials');

async function main() {
  const cred = new ClientSecretCredential(
    process.env.AZURE_TENANT_ID,
    process.env.AZURE_CLIENT_ID,
    process.env.AZURE_CLIENT_SECRET
  );
  const authProvider = new TokenCredentialAuthenticationProvider(cred, {
    scopes: ['https://graph.microsoft.com/.default'],
  });
  const client = Client.initWithMiddleware({ authProvider });

  // Get MS Graph service principal app roles
  const graphFilter = "appId eq '00000003-0000-0000-c000-000000000000'";
  const graphSp = await client.api('/servicePrincipals').filter(graphFilter).select('id,appRoles').get();
  const graphSpId = graphSp.value[0].id;
  const appRoles = graphSp.value[0].appRoles;

  // Current role
  const currentRoleId = 'f431331c-49a6-499f-be1c-62af19c34a9d';
  const current = appRoles.find(r => r.id === currentRoleId);
  console.log('Currently granted:', current ? current.value : 'unknown');

  // Needed permissions
  const needed = [
    'ExternalItem.ReadWrite.All',
    'ExternalItem.ReadWrite.OwnedBy',
    'ExternalConnection.ReadWrite.OwnedBy',
  ];

  // Auto-discover our app's service principal ID from AZURE_CLIENT_ID
  const appFilter = `appId eq '${process.env.AZURE_CLIENT_ID}'`;
  const appSpResult = await client.api('/servicePrincipals').filter(appFilter).select('id').get();
  if (!appSpResult.value.length) {
    console.error(`Could not find service principal for appId ${process.env.AZURE_CLIENT_ID}`);
    process.exit(1);
  }
  const spId = appSpResult.value[0].id;
  console.log(`App service principal ID: ${spId}`);

  for (const perm of needed) {
    const role = appRoles.find(r => r.value === perm);
    if (role) {
      console.log(`${perm}: ${role.id}`);
    } else {
      console.log(`${perm}: NOT FOUND in app roles`);
    }
  }

  // List currently granted roles
  console.log('\nCurrently granted roles:');
  const assignments = await client.api(`/servicePrincipals/${spId}/appRoleAssignments`).get();
  for (const a of assignments.value) {
    const role = appRoles.find(r => r.id === a.appRoleId);
    console.log(`  ${role ? role.value : a.appRoleId} -> ${a.resourceDisplayName}`);
  }
}

main().catch(e => console.error(e));
