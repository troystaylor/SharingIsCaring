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
export declare function loadConfig(): AppConfig;
export declare function getConfig(): AppConfig;
//# sourceMappingURL=connectorConfig.d.ts.map