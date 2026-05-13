"use strict";
// ── Connector Configuration ──
// Loads environment variables for ServiceNow and Graph credentials.
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.loadConfig = loadConfig;
exports.getConfig = getConfig;
const dotenv = __importStar(require("dotenv"));
const path = __importStar(require("path"));
dotenv.config({ path: path.resolve(__dirname, "../../.env.local"), override: false });
function requireEnv(name) {
    const value = process.env[name];
    if (!value) {
        throw new Error(`Missing required environment variable: ${name}`);
    }
    return value;
}
function loadConfig() {
    const authFlow = (process.env.SN_AUTH_FLOW || "client_credentials");
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
            connectorDescription: process.env.CONNECTOR_DESCRIPTION ||
                "Slack messages and attachments from public channels indexed by ServiceNow AI Search",
        },
    };
}
let _config = null;
function getConfig() {
    if (!_config) {
        _config = loadConfig();
    }
    return _config;
}
//# sourceMappingURL=connectorConfig.js.map