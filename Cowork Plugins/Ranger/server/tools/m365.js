// M365 integration tools — OneDrive, Mail, Teams via Graph API (OBO flow)
import { readFile } from "./sandbox-client.js";

const TENANT_ID = process.env.OAUTH_TENANT_ID || "8d4b7da7-c036-46e8-a040-cf0ecdeeefea";
const CLIENT_ID = process.env.OAUTH_CLIENT_ID || "bfc48f06-4caa-4fee-9839-efce4e9ba653";
const CLIENT_SECRET = process.env.ENTRA_CLIENT_SECRET;
const GRAPH_BASE = "https://graph.microsoft.com/v1.0";

// OBO token cache: userToken → { graphToken, expiresAt }
const oboCache = new Map();

async function getGraphToken(userToken) {
    const cached = oboCache.get(userToken);
    if (cached && Date.now() < cached.expiresAt - 60000) return cached.graphToken;

    const body = new URLSearchParams({
        grant_type: "urn:ietf:params:oauth:grant-type:jwt-bearer",
        client_id: CLIENT_ID,
        client_secret: CLIENT_SECRET,
        assertion: userToken,
        scope: "Files.ReadWrite.All Mail.Send ChannelMessage.Send Channel.ReadBasic.All Team.ReadBasic.All Calendars.ReadWrite User.Read",
        requested_token_use: "on_behalf_of",
    });

    const res = await fetch(`https://login.microsoftonline.com/${TENANT_ID}/oauth2/v2.0/token`, {
        method: "POST",
        headers: { "Content-Type": "application/x-www-form-urlencoded" },
        body: body.toString(),
    });

    if (!res.ok) {
        const err = await res.text();
        throw new Error(`OBO token exchange failed: ${err.substring(0, 200)}`);
    }

    const data = await res.json();

    oboCache.set(userToken, { graphToken: data.access_token, expiresAt: Date.now() + (data.expires_in * 1000) });
    return data.access_token;
}

async function graphRequest(token, method, path, body) {
    const opts = {
        method,
        headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    };
    if (body && method !== "GET") opts.body = typeof body === "string" ? body : JSON.stringify(body);
    const res = await fetch(`${GRAPH_BASE}${path}`, opts);
    const text = await res.text();
    if (!res.ok) throw new Error(`Graph API error (${res.status}): ${text.substring(0, 200)}`);
    return text ? JSON.parse(text) : {};
}

export function registerM365Tools(server, z) {
    // ── save_to_onedrive ──
    server.tool("save_to_onedrive", "Upload a file from the sandbox to the user's OneDrive. Returns a sharing link.", {
        session_id: z.string().describe("Sandbox session containing the file"),
        file_path: z.string().describe("Full path in sandbox (e.g., /workspace/screenshot.png)"),
        onedrive_path: z.string().optional().describe("Destination path in OneDrive (e.g., /Cowork/screenshot.png). Default: /Cowork/<filename>"),
        user_token: z.string().optional().describe("User bearer token (injected by server)"),
    }, { readOnlyHint: false, destructiveHint: false, openWorldHint: true }, async ({ session_id, file_path, onedrive_path, user_token }) => {
        if (!CLIENT_SECRET) throw new Error("ENTRA_CLIENT_SECRET not configured — OBO unavailable");
        if (!user_token) throw new Error("No user token available for Graph API access");

        const graphToken = await getGraphToken(user_token);

        // Read file from sandbox as base64
        const b64content = await readFile(session_id, file_path);
        const buffer = Buffer.from(b64content, "base64");

        // Determine destination path
        const fileName = file_path.split("/").pop();
        const destPath = onedrive_path || `/${fileName}`;

        // First verify we can access the drive
        const driveCheck = await fetch(`${GRAPH_BASE}/me/drive`, {
            headers: { Authorization: `Bearer ${graphToken}` },
        });
        if (!driveCheck.ok) {
            const driveErr = await driveCheck.text();
            throw new Error(`Cannot access OneDrive (${driveCheck.status}): ${driveErr.substring(0, 200)}`);
        }

        // Upload to OneDrive using special folder (approot) or root path
        // Use /me/drive/root:/{path}:/content for path-based upload
        const uploadUrl = `${GRAPH_BASE}/me/drive/root:${destPath}:/content`;
        const uploadRes = await fetch(uploadUrl, {
            method: "PUT",
            headers: { Authorization: `Bearer ${graphToken}`, "Content-Type": "application/octet-stream" },
            body: buffer,
        });

        if (!uploadRes.ok) {
            const err = await uploadRes.text();
            throw new Error(`OneDrive upload failed (${uploadRes.status}): ${err.substring(0, 200)}`);
        }

        const fileInfo = await uploadRes.json();

        // Create a sharing link
        const linkRes = await graphRequest(graphToken, "POST", `/me/drive/items/${fileInfo.id}/createLink`, {
            type: "view", scope: "organization"
        });

        return { content: [{ type: "text", text: JSON.stringify({
            success: true, file_name: fileName, onedrive_path: destPath,
            size_bytes: buffer.length, web_url: fileInfo.webUrl,
            share_link: linkRes.link?.webUrl || fileInfo.webUrl
        }) }] };
    });

    // ── send_email ──
    server.tool("send_email", "Send an email via Microsoft 365. Supports plain text, HTML body, and file attachments from sandbox.", {
        to: z.array(z.string()).describe("Recipient email addresses"),
        subject: z.string().describe("Email subject"),
        body: z.string().describe("Email body (plain text or HTML)"),
        body_type: z.string().optional().describe("'text' or 'html'. Default: text"),
        cc: z.array(z.string()).optional().describe("CC recipients"),
        attachment_session_id: z.string().optional().describe("Sandbox session ID containing attachment files"),
        attachment_paths: z.array(z.string()).optional().describe("File paths in sandbox to attach"),
        user_token: z.string().optional().describe("User bearer token (injected by server)"),
    }, { readOnlyHint: false, destructiveHint: true, openWorldHint: true }, async ({ to, subject, body, body_type, cc, attachment_session_id, attachment_paths, user_token }) => {
        if (!CLIENT_SECRET) throw new Error("ENTRA_CLIENT_SECRET not configured — OBO unavailable");
        if (!user_token) throw new Error("No user token available for Graph API access");

        const graphToken = await getGraphToken(user_token);

        const message = {
            subject,
            body: { contentType: body_type === "html" ? "HTML" : "Text", content: body },
            toRecipients: to.map(email => ({ emailAddress: { address: email } })),
        };
        if (cc && cc.length > 0) {
            message.ccRecipients = cc.map(email => ({ emailAddress: { address: email } }));
        }

        // Add attachments if specified
        if (attachment_session_id && attachment_paths && attachment_paths.length > 0) {
            message.attachments = [];
            for (const path of attachment_paths) {
                const b64 = await readFile(attachment_session_id, path);
                const fileName = path.split("/").pop();
                message.attachments.push({
                    "@odata.type": "#microsoft.graph.fileAttachment",
                    name: fileName,
                    contentBytes: b64,
                });
            }
        }

        await graphRequest(graphToken, "POST", "/me/sendMail", { message, saveToSentItems: true });

        return { content: [{ type: "text", text: JSON.stringify({
            success: true, to, subject, attachments: attachment_paths?.length || 0
        }) }] };
    });

    // ── create_teams_message ──
    server.tool("create_teams_message", "Post a message to a Microsoft Teams channel.", {
        team_name: z.string().optional().describe("Team display name (used to look up team ID)"),
        team_id: z.string().optional().describe("Team ID (if known, skips lookup)"),
        channel_name: z.string().optional().describe("Channel name. Default: General"),
        channel_id: z.string().optional().describe("Channel ID (if known, skips lookup)"),
        message: z.string().describe("Message content (supports HTML)"),
        content_type: z.string().optional().describe("'text' or 'html'. Default: html"),
        user_token: z.string().optional().describe("User bearer token (injected by server)"),
    }, { readOnlyHint: false, destructiveHint: false, openWorldHint: true }, async ({ team_name, team_id, channel_name, channel_id, message, content_type, user_token }) => {
        if (!CLIENT_SECRET) throw new Error("ENTRA_CLIENT_SECRET not configured — OBO unavailable");
        if (!user_token) throw new Error("No user token available for Graph API access");

        const graphToken = await getGraphToken(user_token);

        // Resolve team ID
        let tId = team_id;
        if (!tId && team_name) {
            const teams = await graphRequest(graphToken, "GET", `/me/joinedTeams`);
            const team = teams.value?.find(t => t.displayName.toLowerCase() === team_name.toLowerCase());
            if (!team) throw new Error(`Team "${team_name}" not found in your joined teams`);
            tId = team.id;
        }
        if (!tId) throw new Error("team_name or team_id is required");

        // Resolve channel ID
        let chId = channel_id;
        if (!chId) {
            const chName = channel_name || "General";
            const channels = await graphRequest(graphToken, "GET", `/teams/${tId}/channels`);
            const ch = channels.value?.find(c => c.displayName.toLowerCase() === chName.toLowerCase());
            if (!ch) throw new Error(`Channel "${chName}" not found in team`);
            chId = ch.id;
        }

        // Post message
        const result = await graphRequest(graphToken, "POST", `/teams/${tId}/channels/${chId}/messages`, {
            body: { contentType: content_type === "text" ? "text" : "html", content: message }
        });

        return { content: [{ type: "text", text: JSON.stringify({
            success: true, team_id: tId, channel_id: chId, message_id: result.id
        }) }] };
    });

    // ── Calendar Tools ──

    server.tool("list_calendar_events", "List calendar events for a date range.", {
        start_date: z.string().describe("Start date/time (ISO 8601, e.g., 2026-07-17T00:00:00)"),
        end_date: z.string().describe("End date/time (ISO 8601, e.g., 2026-07-18T00:00:00)"),
        user_token: z.string().optional().describe("User bearer token (injected by server)"),
    }, { readOnlyHint: true, destructiveHint: false, openWorldHint: true }, async ({ start_date, end_date, user_token }) => {
        if (!CLIENT_SECRET) throw new Error("ENTRA_CLIENT_SECRET not configured");
        if (!user_token) throw new Error("No user token available");
        const graphToken = await getGraphToken(user_token);
        const events = await graphRequest(graphToken, "GET", `/me/calendarView?startDateTime=${encodeURIComponent(start_date)}&endDateTime=${encodeURIComponent(end_date)}&$select=subject,start,end,location,organizer,attendees&$top=25&$orderby=start/dateTime`);
        return { content: [{ type: "text", text: JSON.stringify({ count: events.value?.length || 0, events: events.value || [] }) }] };
    });

    server.tool("create_calendar_event", "Create a calendar event/meeting.", {
        subject: z.string().describe("Event subject/title"),
        start: z.string().describe("Start date/time (ISO 8601)"),
        end: z.string().describe("End date/time (ISO 8601)"),
        attendees: z.array(z.string()).optional().describe("Attendee email addresses"),
        body: z.string().optional().describe("Event body/description (HTML supported)"),
        location: z.string().optional().describe("Location name"),
        is_online: z.boolean().optional().describe("Create as Teams meeting. Default: true"),
        user_token: z.string().optional().describe("User bearer token (injected by server)"),
    }, { readOnlyHint: false, destructiveHint: false, openWorldHint: true }, async ({ subject, start, end, attendees, body, location, is_online, user_token }) => {
        if (!CLIENT_SECRET) throw new Error("ENTRA_CLIENT_SECRET not configured");
        if (!user_token) throw new Error("No user token available");
        const graphToken = await getGraphToken(user_token);
        const event = {
            subject,
            start: { dateTime: start, timeZone: "UTC" },
            end: { dateTime: end, timeZone: "UTC" },
            isOnlineMeeting: is_online !== false,
        };
        if (attendees) event.attendees = attendees.map(email => ({ emailAddress: { address: email }, type: "required" }));
        if (body) event.body = { contentType: "HTML", content: body };
        if (location) event.location = { displayName: location };
        const result = await graphRequest(graphToken, "POST", "/me/events", event);
        return { content: [{ type: "text", text: JSON.stringify({ success: true, event_id: result.id, subject: result.subject, webLink: result.webLink, onlineMeeting: result.onlineMeeting?.joinUrl }) }] };
    });

    server.tool("find_free_busy", "Check availability/free-busy status for attendees.", {
        attendees: z.array(z.string()).describe("Email addresses to check"),
        start: z.string().describe("Start of time window (ISO 8601)"),
        end: z.string().describe("End of time window (ISO 8601)"),
        user_token: z.string().optional().describe("User bearer token (injected by server)"),
    }, { readOnlyHint: true, destructiveHint: false, openWorldHint: true }, async ({ attendees, start, end, user_token }) => {
        if (!CLIENT_SECRET) throw new Error("ENTRA_CLIENT_SECRET not configured");
        if (!user_token) throw new Error("No user token available");
        const graphToken = await getGraphToken(user_token);
        const result = await graphRequest(graphToken, "POST", "/me/calendar/getSchedule", {
            schedules: attendees,
            startTime: { dateTime: start, timeZone: "UTC" },
            endTime: { dateTime: end, timeZone: "UTC" },
            availabilityViewInterval: 30,
        });
        return { content: [{ type: "text", text: JSON.stringify({ schedules: result.value || [] }) }] };
    });

    server.tool("delete_calendar_event", "Delete/cancel a calendar event.", {
        event_id: z.string().describe("Event ID to delete"),
        user_token: z.string().optional().describe("User bearer token (injected by server)"),
    }, { readOnlyHint: false, destructiveHint: true, openWorldHint: true }, async ({ event_id, user_token }) => {
        if (!CLIENT_SECRET) throw new Error("ENTRA_CLIENT_SECRET not configured");
        if (!user_token) throw new Error("No user token available");
        const graphToken = await getGraphToken(user_token);
        await graphRequest(graphToken, "DELETE", `/me/events/${event_id}`);
        return { content: [{ type: "text", text: JSON.stringify({ success: true, deleted: event_id }) }] };
    });

    // ── OneDrive Workspace Tools ──

    server.tool("read_onedrive_file", "Read a file from OneDrive. Returns text content or base64 for binary files.", {
        path: z.string().describe("File path in OneDrive (e.g., /Documents/notes.txt)"),
        user_token: z.string().optional().describe("User bearer token (injected by server)"),
    }, { readOnlyHint: true, destructiveHint: false, openWorldHint: true }, async ({ path: filePath, user_token }) => {
        if (!CLIENT_SECRET) throw new Error("ENTRA_CLIENT_SECRET not configured");
        if (!user_token) throw new Error("No user token available");
        const graphToken = await getGraphToken(user_token);
        const res = await fetch(`${GRAPH_BASE}/me/drive/root:${filePath}:/content`, {
            headers: { Authorization: `Bearer ${graphToken}` },
        });
        if (!res.ok) throw new Error(`OneDrive read failed (${res.status}): ${(await res.text()).substring(0, 200)}`);
        const buffer = Buffer.from(await res.arrayBuffer());
        // Try text first, fall back to base64 for binary
        const text = buffer.toString("utf8");
        const isBinary = text.includes("\0") || buffer.length > 100000;
        return { content: [{ type: "text", text: JSON.stringify({ path: filePath, size_bytes: buffer.length, encoding: isBinary ? "base64" : "utf8", content: isBinary ? buffer.toString("base64") : text }) }] };
    });

    server.tool("list_onedrive_folder", "List files and folders in a OneDrive directory.", {
        path: z.string().optional().describe("Folder path (e.g., /Documents). Default: root"),
        user_token: z.string().optional().describe("User bearer token (injected by server)"),
    }, { readOnlyHint: true, destructiveHint: false, openWorldHint: true }, async ({ path: folderPath, user_token }) => {
        if (!CLIENT_SECRET) throw new Error("ENTRA_CLIENT_SECRET not configured");
        if (!user_token) throw new Error("No user token available");
        const graphToken = await getGraphToken(user_token);
        const endpoint = folderPath ? `/me/drive/root:${folderPath}:/children` : "/me/drive/root/children";
        const result = await graphRequest(graphToken, "GET", `${endpoint}?$select=name,size,lastModifiedDateTime,folder,file`);
        const items = (result.value || []).map(i => ({
            name: i.name, size: i.size, modified: i.lastModifiedDateTime,
            type: i.folder ? "folder" : "file", mimeType: i.file?.mimeType,
        }));
        return { content: [{ type: "text", text: JSON.stringify({ path: folderPath || "/", count: items.length, items }) }] };
    });

    server.tool("delete_onedrive_file", "Delete a file or folder from OneDrive.", {
        path: z.string().describe("Path to delete (e.g., /Documents/old-file.txt)"),
        user_token: z.string().optional().describe("User bearer token (injected by server)"),
    }, { readOnlyHint: false, destructiveHint: true, openWorldHint: true }, async ({ path: filePath, user_token }) => {
        if (!CLIENT_SECRET) throw new Error("ENTRA_CLIENT_SECRET not configured");
        if (!user_token) throw new Error("No user token available");
        const graphToken = await getGraphToken(user_token);
        await graphRequest(graphToken, "DELETE", `/me/drive/root:${filePath}:`);
        return { content: [{ type: "text", text: JSON.stringify({ success: true, deleted: filePath }) }] };
    });
}
