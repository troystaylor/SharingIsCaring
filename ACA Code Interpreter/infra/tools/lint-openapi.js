// lint-openapi.js — OpenAPI spec linter with Power Platform rules
// Usage: node /opt/tools/lint-openapi.js "<openapi-json-or-yaml>"
const input = process.argv[2];

try {
    let spec;
    // Try JSON first, then YAML
    try {
        spec = JSON.parse(input);
    } catch {
        // If not JSON, treat as YAML (basic key:value parsing for simple cases)
        console.log(JSON.stringify({
            valid: false,
            errors: ["Input must be valid JSON. YAML support requires writing to file first."],
            warnings: [],
            score: 0
        }, null, 2));
        process.exit(1);
    }

    const errors = [];
    const warnings = [];
    let score = 100;

    // Check required top-level fields
    if (!spec.swagger && !spec.openapi) {
        errors.push("Missing 'swagger' or 'openapi' version field");
        score -= 20;
    }

    if (!spec.info) {
        errors.push("Missing 'info' object");
        score -= 10;
    } else {
        if (!spec.info.title) errors.push("Missing info.title");
        if (!spec.info.description) warnings.push("Missing info.description — helps Copilot Studio understand the connector");
        if (!spec.info.version) warnings.push("Missing info.version");
    }

    if (!spec.host && !spec.servers) {
        errors.push("Missing 'host' (Swagger 2.0) or 'servers' (OpenAPI 3.0)");
        score -= 10;
    }

    // Check paths
    if (!spec.paths || Object.keys(spec.paths).length === 0) {
        errors.push("No paths/operations defined");
        score -= 20;
    } else {
        for (const [path, methods] of Object.entries(spec.paths)) {
            for (const [method, op] of Object.entries(methods)) {
                if (method.startsWith("x-")) continue;
                const opId = op.operationId || `${method.toUpperCase()} ${path}`;

                // x-ms-summary check
                if (!op.summary && !op["x-ms-summary"]) {
                    warnings.push(`${opId}: Missing summary/x-ms-summary`);
                    score -= 2;
                }

                // Description check
                if (!op.description) {
                    warnings.push(`${opId}: Missing description — Copilot Studio uses this for tool selection`);
                    score -= 1;
                }

                // Parameter checks
                if (op.parameters) {
                    for (const param of op.parameters) {
                        if (param.in === "path" && !param["x-ms-url-encoding"]) {
                            warnings.push(`${opId}: Path param '${param.name}' missing x-ms-url-encoding: "single"`);
                            score -= 3;
                        }
                        if (!param["x-ms-summary"] && !param.summary && param.in !== "body") {
                            warnings.push(`${opId}: Param '${param.name}' missing x-ms-summary`);
                            score -= 1;
                        }
                    }
                }

                // Response schema check
                if (op.responses) {
                    const successResp = op.responses["200"] || op.responses["201"];
                    if (successResp && !successResp.schema && !successResp.content) {
                        warnings.push(`${opId}: Success response has no schema — Power Automate can't map fields`);
                        score -= 3;
                    }
                }
            }
        }
    }

    // Check definitions for array items
    const defs = spec.definitions || spec.components?.schemas || {};
    for (const [name, schema] of Object.entries(defs)) {
        checkArrayItems(schema, `definitions.${name}`, errors, warnings, score);
    }

    // Check securityDefinitions
    if (!spec.securityDefinitions && !spec.components?.securitySchemes) {
        warnings.push("No security definitions — connector will need auth configuration");
        score -= 5;
    }

    score = Math.max(0, score);

    const result = {
        valid: errors.length === 0,
        errors,
        warnings,
        score,
        operationCount: spec.paths ? Object.values(spec.paths).reduce((sum, m) =>
            sum + Object.keys(m).filter(k => !k.startsWith("x-")).length, 0) : 0
    };

    console.log(JSON.stringify(result, null, 2));
    process.exit(errors.length === 0 ? 0 : 1);
} catch (e) {
    console.log(JSON.stringify({
        valid: false,
        errors: [`Unexpected error: ${e.message}`],
        warnings: [],
        score: 0
    }, null, 2));
    process.exit(1);
}

function checkArrayItems(schema, path, errors, warnings) {
    if (!schema || typeof schema !== "object") return;

    if (schema.type === "array" && !schema.items) {
        errors.push(`${path}: type 'array' missing 'items' — required by Power Platform`);
    }

    if (schema.properties) {
        for (const [prop, propSchema] of Object.entries(schema.properties)) {
            checkArrayItems(propSchema, `${path}.${prop}`, errors, warnings);
        }
    }

    if (schema.items && typeof schema.items === "object") {
        checkArrayItems(schema.items, `${path}.items`, errors, warnings);
    }
}
