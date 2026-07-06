// validate-card.js — Adaptive Card JSON validator
// Usage: node /opt/tools/validate-card.js "<card-json>"
const input = process.argv[2];

try {
    const card = JSON.parse(input);
    const errors = [];
    const warnings = [];

    // Schema version check
    if (!card.type || card.type !== "AdaptiveCard") {
        errors.push("Missing or invalid 'type' — must be 'AdaptiveCard'");
    }

    if (!card.version) {
        errors.push("Missing 'version' field");
    } else {
        const ver = parseFloat(card.version);
        if (ver > 1.6) {
            warnings.push(`Version ${card.version} may not be supported in all hosts`);
        }
    }

    if (!card.body || !Array.isArray(card.body) || card.body.length === 0) {
        errors.push("Card 'body' must be a non-empty array");
    }

    // Check for common issues
    if (card.body) {
        validateElements(card.body, errors, warnings, "body");
    }
    if (card.actions) {
        validateActions(card.actions, errors, warnings, "actions");
    }

    const result = {
        valid: errors.length === 0,
        errors,
        warnings,
        cardVersion: card.version || "unknown",
        elementCount: countElements(card.body || []),
        actionCount: (card.actions || []).length
    };

    console.log(JSON.stringify(result, null, 2));
    process.exit(errors.length === 0 ? 0 : 1);
} catch (e) {
    console.log(JSON.stringify({
        valid: false,
        errors: [`JSON parse error: ${e.message}`],
        warnings: [],
        cardVersion: "unknown",
        elementCount: 0,
        actionCount: 0
    }, null, 2));
    process.exit(1);
}

function validateElements(elements, errors, warnings, path) {
    for (let i = 0; i < elements.length; i++) {
        const el = elements[i];
        const elPath = `${path}[${i}]`;

        if (!el.type) {
            errors.push(`${elPath}: Missing 'type'`);
        }

        // TextBlock checks
        if (el.type === "TextBlock" && !el.text && el.text !== "") {
            errors.push(`${elPath}: TextBlock missing 'text'`);
        }

        // Image checks
        if (el.type === "Image" && !el.url) {
            errors.push(`${elPath}: Image missing 'url'`);
        }

        // Container recursion
        if (el.items && Array.isArray(el.items)) {
            validateElements(el.items, errors, warnings, `${elPath}.items`);
        }
        if (el.columns && Array.isArray(el.columns)) {
            el.columns.forEach((col, ci) => {
                if (col.items) validateElements(col.items, errors, warnings, `${elPath}.columns[${ci}].items`);
            });
        }
    }
}

function validateActions(actions, errors, warnings, path) {
    for (let i = 0; i < actions.length; i++) {
        const action = actions[i];
        const aPath = `${path}[${i}]`;

        if (!action.type) {
            errors.push(`${aPath}: Missing 'type'`);
        }
        if (action.type === "Action.OpenUrl" && !action.url) {
            errors.push(`${aPath}: Action.OpenUrl missing 'url'`);
        }
        if (action.type === "Action.Submit" && !action.data && !action.title) {
            warnings.push(`${aPath}: Action.Submit has no 'data' or 'title'`);
        }
    }
}

function countElements(elements) {
    let count = 0;
    for (const el of elements) {
        count++;
        if (el.items) count += countElements(el.items);
        if (el.columns) {
            for (const col of el.columns) {
                if (col.items) count += countElements(col.items);
            }
        }
    }
    return count;
}
