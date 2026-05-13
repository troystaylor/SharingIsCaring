// ── Connector Configuration ──
// Loads environment variables for ServiceNow and Graph credentials.

import * as dotenv from "dotenv";
import * as path from "path";

dotenv.config({ path: path.resolve(__dirname, "../../.env.local"), override: false });

export type SnAuthFlow = "client_credentials" | "password";

export interface ServiceNowConfig {
  instanceUrl: string;
  clientId: string;
  clientSecret: string;
  username: string;
  password: string;
  authFlow: SnAuthFlow;
  slackIndexedTable: string;
}

export interface ConnectorConfig {
  connectorId: string;
  connectorName: string;
  connectorDescription: string;
}

export interface AppConfig {
  servicenow: ServiceNowConfig;
  connector: ConnectorConfig;
}

function requireEnv(name: string): string {
  const value = process.env[name];
  if (!value) {
    throw new Error(`Missing required environment variable: ${name}`);
  }
  return value;
}

export function loadConfig(): AppConfig {
  const authFlow = (process.env.SN_AUTH_FLOW || "client_credentials") as SnAuthFlow;

  if (authFlow !== "client_credentials" && authFlow !== "password") {
    throw new Error(`Invalid SN_AUTH_FLOW: ${authFlow}. Must be "client_credentials" or "password".`);
  }

  return {
    servicenow: {
      instanceUrl: requireEnv("SN_INSTANCE_URL").replace(/\/+$/, ""),
      clientId: requireEnv("SN_CLIENT_ID"),
      clientSecret: requireEnv("SN_CLIENT_SECRET"),
      username: authFlow === "password" ? requireEnv("SN_USERNAME") : process.env.SN_USERNAME || "",
      password: authFlow === "password" ? requireEnv("SN_PASSWORD") : process.env.SN_PASSWORD || "",
      authFlow,
      slackIndexedTable: process.env.SN_SLACK_INDEXED_TABLE || "u_slack_content",
    },
    connector: {
      connectorId: requireEnv("CONNECTOR_ID"),
      connectorName: process.env.CONNECTOR_NAME || "ServiceNow Slack",
      connectorDescription:
        process.env.CONNECTOR_DESCRIPTION ||
        "Slack messages and attachments from public channels indexed by ServiceNow AI Search",
    },
  };
}

let _config: AppConfig | null = null;

export function getConfig(): AppConfig {
  if (!_config) {
    _config = loadConfig();
  }
  return _config;
}
