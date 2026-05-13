"use strict";
// ── Transformer ──
// Converts ServiceNow Slack content records to Microsoft Graph external items.
Object.defineProperty(exports, "__esModule", { value: true });
exports.transformSlackRecord = transformSlackRecord;
const types_1 = require("../models/types");
function sanitizeId(raw) {
    // Graph external item IDs must be alphanumeric, hyphens, underscores only
    return raw.replace(/[^a-zA-Z0-9\-_]/g, "_").substring(0, 128);
}
function truncate(text, maxLength) {
    if (!text)
        return "";
    return text.length > maxLength ? text.substring(0, maxLength - 3) + "..." : text;
}
function transformSlackRecord(record) {
    const id = sanitizeId(record.document_id || record.sys_id);
    const channelName = record.channel_name || "unknown-channel";
    const authorName = record.author_name || "Unknown";
    const messageText = record.text || record.title || "";
    const titlePreview = truncate(messageText, 100);
    const title = `#${channelName} — ${titlePreview || "Slack message"}`;
    const messageTimestamp = record.message_timestamp
        ? new Date(record.message_timestamp).toISOString()
        : record.sys_created_on
            ? new Date(record.sys_created_on).toISOString()
            : new Date().toISOString();
    const lastModified = record.sys_updated_on
        ? new Date(record.sys_updated_on).toISOString()
        : messageTimestamp;
    const properties = {
        title,
        description: truncate(messageText, 1000),
        url: record.url || "",
        channelName,
        channelId: record.channel_id || "",
        authorName,
        authorId: record.author_id || "",
        messageTimestamp,
        threadTimestamp: record.thread_timestamp || "",
        hasAttachments: record.has_attachments === "true" || !!record.file_name,
        attachmentNames: record.attachment_names || record.file_name || "",
        reactionCount: parseInt(record.reaction_count || "0", 10),
        replyCount: parseInt(record.reply_count || "0", 10),
        contentType: record.content_type || "message",
        lastModifiedDateTime: lastModified,
    };
    const content = messageText
        ? { value: messageText, type: "text" }
        : undefined;
    return {
        id,
        acl: types_1.EVERYONE_ACL,
        properties,
        content,
    };
}
//# sourceMappingURL=transformer.js.map