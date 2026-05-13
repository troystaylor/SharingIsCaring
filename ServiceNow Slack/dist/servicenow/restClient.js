"use strict";
// ── ServiceNow REST Client ──
// Generic Table API client with pagination and auth header injection.
Object.defineProperty(exports, "__esModule", { value: true });
exports.query = query;
exports.queryAll = queryAll;
exports.getRecord = getRecord;
exports.createRecord = createRecord;
const servicenowAuth_1 = require("../auth/servicenowAuth");
const connectorConfig_1 = require("../config/connectorConfig");
async function query(tableName, params = {}) {
    const config = (0, connectorConfig_1.getConfig)().servicenow;
    const token = await (0, servicenowAuth_1.getAccessToken)();
    const queryParams = new URLSearchParams();
    for (const [key, value] of Object.entries(params)) {
        queryParams.set(key, String(value));
    }
    const url = `${config.instanceUrl}/api/now/table/${tableName}?${queryParams.toString()}`;
    const response = await fetch(url, {
        method: "GET",
        headers: {
            Authorization: `Bearer ${token}`,
            Accept: "application/json",
        },
    });
    if (!response.ok) {
        const errorBody = await response.text();
        throw new Error(`ServiceNow Table API query failed (${response.status}): ${errorBody}`);
    }
    const data = (await response.json());
    return data.result;
}
async function queryAll(tableName, params = {}, pageSize = 1000) {
    const allRecords = [];
    let offset = 0;
    while (true) {
        const pageParams = {
            ...params,
            sysparm_limit: pageSize,
            sysparm_offset: offset,
        };
        const records = await query(tableName, pageParams);
        allRecords.push(...records);
        if (records.length < pageSize) {
            break;
        }
        offset += pageSize;
        console.log(`[SN] Fetched ${allRecords.length} records from ${tableName} so far...`);
    }
    return allRecords;
}
async function getRecord(tableName, sysId, params = {}) {
    const config = (0, connectorConfig_1.getConfig)().servicenow;
    const token = await (0, servicenowAuth_1.getAccessToken)();
    const queryParams = new URLSearchParams();
    for (const [key, value] of Object.entries(params)) {
        queryParams.set(key, String(value));
    }
    const url = `${config.instanceUrl}/api/now/table/${tableName}/${sysId}?${queryParams.toString()}`;
    const response = await fetch(url, {
        method: "GET",
        headers: {
            Authorization: `Bearer ${token}`,
            Accept: "application/json",
        },
    });
    if (response.status === 404) {
        return null;
    }
    if (!response.ok) {
        const errorBody = await response.text();
        throw new Error(`ServiceNow Table API get failed (${response.status}): ${errorBody}`);
    }
    const data = (await response.json());
    return data.result;
}
async function createRecord(tableName, body) {
    const config = (0, connectorConfig_1.getConfig)().servicenow;
    const token = await (0, servicenowAuth_1.getAccessToken)();
    const url = `${config.instanceUrl}/api/now/table/${tableName}`;
    const response = await fetch(url, {
        method: "POST",
        headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json",
            Accept: "application/json",
        },
        body: JSON.stringify(body),
    });
    if (!response.ok) {
        const errorBody = await response.text();
        throw new Error(`ServiceNow Table API create failed (${response.status}): ${errorBody}`);
    }
    const data = (await response.json());
    return data.result;
}
//# sourceMappingURL=restClient.js.map