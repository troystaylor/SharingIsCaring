import * as dotenv from "dotenv";
import * as path from "path";

// Azure Functions injects env vars from local.settings.json automatically.
// .env.local is used for standalone/script execution (e.g. sf-auth-setup).
dotenv.config({ path: path.resolve(__dirname, "../../.env.local"), override: false });

export type SfAuthFlow = "client_credentials" | "refresh_token";

export interface SalesforceConfig {
  instanceUrl: string;
  clientId: string;
  clientSecret: string;
  refreshToken: string;
  apiVersion: string;
  authFlow: SfAuthFlow;
}

export interface ConnectorConfig {
  connectorId: string;
  connectorName: string;
  connectorDescription: string;
}

export interface AppConfig {
  salesforce: SalesforceConfig;
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
  const authFlow = (process.env.SF_AUTH_FLOW || "refresh_token") as SfAuthFlow;

  if (authFlow !== "client_credentials" && authFlow !== "refresh_token") {
    throw new Error(`Invalid SF_AUTH_FLOW: ${authFlow}. Must be "client_credentials" or "refresh_token".`);
  }

  // refresh_token is required only for the refresh_token flow
  const refreshToken = authFlow === "refresh_token"
    ? requireEnv("SF_REFRESH_TOKEN")
    : process.env.SF_REFRESH_TOKEN || "";

  return {
    salesforce: {
      instanceUrl: requireEnv("SF_INSTANCE_URL"),
      clientId: requireEnv("SF_CLIENT_ID"),
      clientSecret: requireEnv("SF_CLIENT_SECRET"),
      refreshToken,
      apiVersion: process.env.SF_API_VERSION || "v66.0",
      authFlow,
    },
    connector: {
      connectorId: requireEnv("CONNECTOR_ID"),
      connectorName: process.env.CONNECTOR_NAME || "Salesforce",
      connectorDescription:
        process.env.CONNECTOR_DESCRIPTION ||
        "Salesforce CRM data for Microsoft 365 Copilot",
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
