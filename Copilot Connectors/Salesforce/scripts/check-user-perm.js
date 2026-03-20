const { ClientSecretCredential } = require("@azure/identity");
const { Client } = require("@microsoft/microsoft-graph-client");
const { TokenCredentialAuthenticationProvider } = require("@microsoft/microsoft-graph-client/authProviders/azureTokenCredentials");

async function main() {
  const cred = new ClientSecretCredential(
    process.env.AZURE_TENANT_ID,
    process.env.AZURE_CLIENT_ID,
    process.env.AZURE_CLIENT_SECRET
  );
  const auth = new TokenCredentialAuthenticationProvider(cred, {
    scopes: ["https://graph.microsoft.com/.default"],
  });
  const client = Client.initWithMiddleware({ authProvider: auth });

  try {
    const r = await client.api("/users").top(2).select("id,mail,userPrincipalName").get();
    console.log("User.Read.All works! Found users:");
    r.value.forEach((u) => console.log(" ", u.id, u.mail, u.userPrincipalName));
  } catch (err) {
    console.log("User query failed:", err.statusCode, err.code);
    console.log("Need to grant User.Read.All permission to the app.");
  }
}
main();
