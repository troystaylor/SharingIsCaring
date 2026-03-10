using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private const string APP_INSIGHTS_CS = "";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var opId = this.Context.OperationId;
        var corrId = Guid.NewGuid().ToString();
        try
        {
            HttpResponseMessage response;
            if (opId == "InvokeMCP")
                response = await HandleMcpAsync(corrId);
            else
                response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
            await LogAsync(opId, (int)response.StatusCode, corrId);
            return response;
        }
        catch (Exception ex)
        {
            await LogAsync(opId, 500, corrId, ex.Message);
            throw;
        }
    }

    #region MCP Handler

    private async Task<HttpResponseMessage> HandleMcpAsync(string corrId)
    {
        var handler = new McpRequestHandler(new McpServerInfo
        {
            Name = "graph-mail-calendar",
            Version = "1.0.0",
            ProtocolVersion = "2025-03-26"
        });

        RegisterMailTools(handler);
        RegisterCalendarTools(handler);
        RegisterUserTools(handler);
        RegisterUserToolsLow(handler);

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = await handler.HandleAsync(body);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = CreateJsonContent(result.ToString(Newtonsoft.Json.Formatting.None))
        };
    }

    #endregion

    #region Mail Tools

    private void RegisterMailTools(McpRequestHandler h)
    {
        h.AddTool("list_messages",
            "List inbox messages for the signed-in user. Supports keyword search across subject, body, sender, and recipients. Use the search parameter for fast keyword matching or filter for precise OData queries.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    search = new { type = "string", description = "Keyword search (e.g. \"subject:quarterly\" or \"from:john\" or \"budget report\")" },
                    filter = new { type = "string", description = "OData filter (e.g. \"isRead eq false\")" },
                    top = new { type = "integer", description = "Max results (default 10)" },
                    orderby = new { type = "string", description = "Sort field (e.g. \"receivedDateTime desc\")" },
                    select = new { type = "string", description = "Fields to return (e.g. \"subject,from,receivedDateTime\")" }
                }
            },
            handler: async (args) => await GraphGetAsync($"/me/messages{BuildODataQuery(args)}"),
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("search_messages",
            "Search Outlook messages using KQL (Keyword Query Language) via Microsoft Graph Search API. More powerful than basic keyword search. Supports field-scoped queries like subject:, from:, to:, cc:, hasAttachment:true, received>=2025-01-01.",
            schema: new
            {
                type = "object",
                required = new[] { "query" },
                properties = new
                {
                    query = new { type = "string", description = "KQL query (e.g. \"subject:quarterly AND from:john\" or \"hasAttachment:true\")" },
                    size = new { type = "integer", description = "Max results (default 25)" },
                    from = new { type = "integer", description = "Offset for pagination" }
                }
            },
            handler: async (args) =>
            {
                var searchBody = new JObject
                {
                    ["requests"] = new JArray
                    {
                        new JObject
                        {
                            ["entityTypes"] = new JArray("message"),
                            ["query"] = new JObject { ["queryString"] = args["query"]?.ToString() ?? "" },
                            ["from"] = args["from"]?.Value<int>() ?? 0,
                            ["size"] = args["size"]?.Value<int>() ?? 25
                        }
                    }
                };
                return await GraphPostAsync("/search/query", searchBody);
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("get_message",
            "Get a specific email message by ID. Returns full message content including body, recipients, and metadata.",
            schema: new
            {
                type = "object",
                required = new[] { "messageId" },
                properties = new
                {
                    messageId = new { type = "string", description = "Message ID" },
                    select = new { type = "string", description = "Fields to return" },
                    preferHtml = new { type = "boolean", description = "Request HTML body (default: text)" }
                }
            },
            handler: async (args) =>
            {
                var id = args["messageId"]?.ToString();
                var sel = args["select"]?.ToString();
                var qs = string.IsNullOrEmpty(sel) ? "" : $"?$select={Uri.EscapeDataString(sel)}";
                var prefer = args["preferHtml"]?.Value<bool>() == true ? "outlook.body-content-type=\"html\"" : null;
                return await GraphGetAsync($"/me/messages/{id}{qs}", preferHeader: prefer);
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("send_mail",
            "Send an email message as the signed-in user. Specify recipients, subject, body (text or HTML), and optional CC/BCC.",
            schema: new
            {
                type = "object",
                required = new[] { "subject", "body", "toRecipients" },
                properties = new
                {
                    subject = new { type = "string", description = "Email subject" },
                    body = new { type = "string", description = "Message body content" },
                    bodyType = new { type = "string", description = "Body format: Text or HTML (default Text)" },
                    toRecipients = new { type = "array", description = "To recipients (email addresses)", items = new { type = "string" } },
                    ccRecipients = new { type = "array", description = "CC recipients", items = new { type = "string" } },
                    bccRecipients = new { type = "array", description = "BCC recipients", items = new { type = "string" } },
                    importance = new { type = "string", description = "low, normal, or high" },
                    saveToSentItems = new { type = "boolean", description = "Save to Sent Items (default true)" }
                }
            },
            handler: async (args) =>
            {
                var msg = BuildMessageObject(args);
                var sendBody = new JObject
                {
                    ["message"] = msg,
                    ["saveToSentItems"] = args["saveToSentItems"]?.Value<bool>() ?? true
                };
                return await GraphPostAsync("/me/sendMail", sendBody);
            },
            annotations: new { readOnlyHint = false, destructiveHint = false }
        );

        h.AddTool("create_draft",
            "Create a draft email message. The draft is saved but not sent. Use send_draft to send it later.",
            schema: new
            {
                type = "object",
                required = new[] { "subject" },
                properties = new
                {
                    subject = new { type = "string", description = "Email subject" },
                    body = new { type = "string", description = "Message body" },
                    bodyType = new { type = "string", description = "Text or HTML" },
                    toRecipients = new { type = "array", description = "To recipients", items = new { type = "string" } },
                    ccRecipients = new { type = "array", description = "CC recipients", items = new { type = "string" } },
                    importance = new { type = "string", description = "low, normal, or high" }
                }
            },
            handler: async (args) => await GraphPostAsync("/me/messages", BuildMessageObject(args)),
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("update_message",
            "Update an existing message's properties such as subject, body, categories, importance, or read status.",
            schema: new
            {
                type = "object",
                required = new[] { "messageId" },
                properties = new
                {
                    messageId = new { type = "string", description = "Message ID to update" },
                    subject = new { type = "string", description = "New subject" },
                    body = new { type = "string", description = "New body content" },
                    bodyType = new { type = "string", description = "Text or HTML" },
                    isRead = new { type = "boolean", description = "Mark as read/unread" },
                    importance = new { type = "string", description = "low, normal, or high" },
                    categories = new { type = "array", description = "Category labels", items = new { type = "string" } }
                }
            },
            handler: async (args) =>
            {
                var id = args["messageId"]?.ToString();
                var patch = new JObject();
                if (args["subject"] != null) patch["subject"] = args["subject"];
                if (args["body"] != null) patch["body"] = new JObject { ["contentType"] = args["bodyType"]?.ToString() ?? "Text", ["content"] = args["body"] };
                if (args["isRead"] != null) patch["isRead"] = args["isRead"];
                if (args["importance"] != null) patch["importance"] = args["importance"];
                if (args["categories"] != null) patch["categories"] = args["categories"];
                return await GraphPatchAsync($"/me/messages/{id}", patch);
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("delete_message",
            "Delete a message from the user's mailbox permanently.",
            schema: new
            {
                type = "object",
                required = new[] { "messageId" },
                properties = new
                {
                    messageId = new { type = "string", description = "Message ID to delete" }
                }
            },
            handler: async (args) => await GraphDeleteAsync($"/me/messages/{args["messageId"]}"),
            annotations: new { readOnlyHint = false, destructiveHint = true }
        );

        h.AddTool("send_draft",
            "Send an existing draft message by its ID.",
            schema: new
            {
                type = "object",
                required = new[] { "messageId" },
                properties = new
                {
                    messageId = new { type = "string", description = "Draft message ID to send" }
                }
            },
            handler: async (args) => await GraphPostEmptyAsync($"/me/messages/{args["messageId"]}/send"),
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("reply",
            "Reply to a message sender with a comment.",
            schema: new
            {
                type = "object",
                required = new[] { "messageId", "comment" },
                properties = new
                {
                    messageId = new { type = "string", description = "Message ID to reply to" },
                    comment = new { type = "string", description = "Reply text" }
                }
            },
            handler: async (args) =>
            {
                var replyBody = new JObject { ["comment"] = args["comment"]?.ToString() ?? "" };
                return await GraphPostAsync($"/me/messages/{args["messageId"]}/reply", replyBody);
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("reply_all",
            "Reply to all recipients of a message.",
            schema: new
            {
                type = "object",
                required = new[] { "messageId", "comment" },
                properties = new
                {
                    messageId = new { type = "string", description = "Message ID to reply all" },
                    comment = new { type = "string", description = "Reply text" }
                }
            },
            handler: async (args) =>
            {
                var replyBody = new JObject { ["comment"] = args["comment"]?.ToString() ?? "" };
                return await GraphPostAsync($"/me/messages/{args["messageId"]}/replyAll", replyBody);
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("forward",
            "Forward a message to specified recipients with an optional comment.",
            schema: new
            {
                type = "object",
                required = new[] { "messageId", "toRecipients" },
                properties = new
                {
                    messageId = new { type = "string", description = "Message ID to forward" },
                    comment = new { type = "string", description = "Optional forward comment" },
                    toRecipients = new { type = "array", description = "Recipient email addresses", items = new { type = "string" } }
                }
            },
            handler: async (args) =>
            {
                var fwdBody = new JObject
                {
                    ["comment"] = args["comment"]?.ToString() ?? "",
                    ["toRecipients"] = BuildRecipientsArray(args["toRecipients"] as JArray)
                };
                return await GraphPostAsync($"/me/messages/{args["messageId"]}/forward", fwdBody);
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("list_sent",
            "List messages from the Sent Items folder with keyword search.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    search = new { type = "string", description = "Keyword search in sent messages" },
                    filter = new { type = "string", description = "OData filter" },
                    top = new { type = "integer", description = "Max results" },
                    orderby = new { type = "string", description = "Sort order" },
                    select = new { type = "string", description = "Fields to return" }
                }
            },
            handler: async (args) => await GraphGetAsync($"/me/mailFolders/sentitems/messages{BuildODataQuery(args)}"),
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("list_folders",
            "List mail folders in the user's mailbox with optional keyword search.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    search = new { type = "string", description = "Search folders by name" },
                    top = new { type = "integer", description = "Max results" }
                }
            },
            handler: async (args) => await GraphGetAsync($"/me/mailFolders{BuildODataQuery(args)}"),
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("move_message",
            "Move a message to a different mail folder.",
            schema: new
            {
                type = "object",
                required = new[] { "messageId", "destinationId" },
                properties = new
                {
                    messageId = new { type = "string", description = "Message ID to move" },
                    destinationId = new { type = "string", description = "Destination folder ID or well-known name (inbox, drafts, sentitems, deleteditems, archive)" }
                }
            },
            handler: async (args) =>
            {
                var moveBody = new JObject { ["destinationId"] = args["destinationId"]?.ToString() };
                return await GraphPostAsync($"/me/messages/{args["messageId"]}/move", moveBody);
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("copy_message",
            "Copy a message to a different mail folder.",
            schema: new
            {
                type = "object",
                required = new[] { "messageId", "destinationId" },
                properties = new
                {
                    messageId = new { type = "string", description = "Message ID to copy" },
                    destinationId = new { type = "string", description = "Destination folder ID or well-known name" }
                }
            },
            handler: async (args) =>
            {
                var copyBody = new JObject { ["destinationId"] = args["destinationId"]?.ToString() };
                return await GraphPostAsync($"/me/messages/{args["messageId"]}/copy", copyBody);
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("list_attachments",
            "List all attachments on a message.",
            schema: new
            {
                type = "object",
                required = new[] { "messageId" },
                properties = new
                {
                    messageId = new { type = "string", description = "Message ID" }
                }
            },
            handler: async (args) => await GraphGetAsync($"/me/messages/{args["messageId"]}/attachments"),
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("get_writing_samples",
            "Retrieve recent sent emails to analyze a user's writing voice and style. Returns full message bodies so you can study tone, formality, greeting/closing patterns, sentence structure, vocabulary, and communication style. USAGE: Call this tool BEFORE drafting any email to match the sender's voice. Analyze the samples for: (1) greeting style (Hi/Hello/Hey/Dear), (2) closing style (Best/Thanks/Regards), (3) formality level (casual/professional/executive), (4) sentence length and paragraph structure, (5) use of bullet points or numbered lists, (6) directness vs. hedging language, (7) common phrases and expressions. When drafting, replicate these patterns faithfully. For leaders and executives, pay special attention to conciseness, decisiveness, and delegation patterns.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    search = new { type = "string", description = "Optional keyword to filter sent emails by topic (e.g. 'project update' to get samples about that topic)" },
                    recipientEmail = new { type = "string", description = "Optional recipient email to find emails previously sent to this person, for relationship-aware tone matching" },
                    count = new { type = "integer", description = "Number of samples to retrieve (default 10, max 25)" },
                    userId = new { type = "string", description = "User ID or email to analyze another user's sent mail (requires Mail.Read permission for that user)" }
                }
            },
            handler: async (args) =>
            {
                var count = args["count"]?.Value<int>() ?? 10;
                if (count > 25) count = 25;
                var userId = args["userId"]?.ToString();
                var basePath = string.IsNullOrEmpty(userId) ? "/me" : $"/users/{userId}";
                var parts = new List<string>();
                parts.Add($"$top={count}");
                parts.Add("$orderby=" + Uri.EscapeDataString("sentDateTime desc"));
                parts.Add("$select=" + Uri.EscapeDataString("subject,body,toRecipients,ccRecipients,sentDateTime,importance"));
                var search = args["search"]?.ToString();
                if (!string.IsNullOrEmpty(search))
                {
                    var sv = search.StartsWith("\"") ? search : $"\"{search}\"";
                    parts.Add("$search=" + Uri.EscapeDataString(sv));
                }
                var recipientEmail = args["recipientEmail"]?.ToString();
                if (!string.IsNullOrEmpty(recipientEmail))
                    parts.Add("$filter=" + Uri.EscapeDataString($"toRecipients/any(r:r/emailAddress/address eq '{recipientEmail}')"));
                var qs = "?" + string.Join("&", parts);
                var result = await GraphGetAsync($"{basePath}/mailFolders/sentitems/messages{qs}");
                var envelope = new JObject
                {
                    ["_instructions"] = "Analyze these sent email samples to understand the sender's writing voice. Study greeting/closing patterns, formality level, sentence structure, vocabulary, and tone. Mirror this style when drafting new emails or replies.",
                    ["sampleCount"] = (result as JObject)?["value"]?.Count() ?? 0,
                    ["messages"] = (result as JObject)?["value"]
                };
                return envelope;
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("draft_with_style_guide",
            "Prepare an email draft context using a company writing style guide combined with the sender's voice. Pass your organization's style guide rules (tone, terminology, formatting) and this tool will package them with recently sent emails for comprehensive voice matching. The agent should then use both the style guide rules AND the writing samples to draft the email. WORKFLOW: (1) User provides style guide rules and draft intent, (2) Tool fetches sent emails for voice analysis, (3) Agent drafts email matching both the style guide and the sender's personal voice.",
            schema: new
            {
                type = "object",
                required = new[] { "styleGuide" },
                properties = new
                {
                    styleGuide = new { type = "string", description = "Company writing style guide rules (e.g. 'Use active voice. Avoid jargon. Keep paragraphs under 3 sentences. Address recipients by first name. Sign off with Thanks.')" },
                    purpose = new { type = "string", description = "What the email should accomplish (e.g. 'Request Q3 budget approval from finance team')" },
                    audience = new { type = "string", description = "Who the email is for (e.g. 'Direct reports', 'C-suite', 'External client', 'Cross-functional team')" },
                    tone = new { type = "string", description = "Desired tone override (e.g. 'encouraging', 'urgent', 'diplomatic', 'casual')" },
                    sampleCount = new { type = "integer", description = "Number of sent email samples to retrieve (default 8)" },
                    search = new { type = "string", description = "Optional keyword to find topically-relevant sent samples" }
                }
            },
            handler: async (args) =>
            {
                var count = args["sampleCount"]?.Value<int>() ?? 8;
                if (count > 25) count = 25;
                var parts = new List<string>();
                parts.Add($"$top={count}");
                parts.Add("$orderby=" + Uri.EscapeDataString("sentDateTime desc"));
                parts.Add("$select=" + Uri.EscapeDataString("subject,body,toRecipients,sentDateTime,importance"));
                var search = args["search"]?.ToString();
                if (!string.IsNullOrEmpty(search))
                {
                    var sv = search.StartsWith("\"") ? search : $"\"{search}\"";
                    parts.Add("$search=" + Uri.EscapeDataString(sv));
                }
                var qs = "?" + string.Join("&", parts);
                var sentResult = await GraphGetAsync($"/me/mailFolders/sentitems/messages{qs}");
                var context = new JObject
                {
                    ["_instructions"] = "Use the style guide rules AND the writing samples together to draft the email. The style guide sets organizational standards; the writing samples show the sender's personal voice within those standards. Combine both: follow the guide's rules while matching the sender's characteristic patterns. If there is a conflict, the style guide takes precedence for formal communications and the sender's voice takes precedence for casual or internal ones.",
                    ["styleGuide"] = args["styleGuide"]?.ToString(),
                    ["purpose"] = args["purpose"]?.ToString() ?? "",
                    ["audience"] = args["audience"]?.ToString() ?? "",
                    ["toneOverride"] = args["tone"]?.ToString() ?? "",
                    ["writingSamples"] = (sentResult as JObject)?["value"]
                };
                return context;
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("get_attachment",
            "Get a specific attachment from a message including metadata and base64-encoded content. Use list_attachments first to get the attachment ID.",
            schema: new
            {
                type = "object",
                required = new[] { "messageId", "attachmentId" },
                properties = new
                {
                    messageId = new { type = "string", description = "The message ID" },
                    attachmentId = new { type = "string", description = "The attachment ID" }
                }
            },
            handler: async (args) =>
            {
                var mid = args["messageId"]?.ToString();
                var aid = args["attachmentId"]?.ToString();
                return await GraphGetAsync($"/me/messages/{mid}/attachments/{aid}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("get_attachment_content",
            "Download the raw binary content of a file attachment. Returns the file bytes directly. Use get_attachment instead if you need metadata (name, size, contentType) along with content.",
            schema: new
            {
                type = "object",
                required = new[] { "messageId", "attachmentId" },
                properties = new
                {
                    messageId = new { type = "string", description = "The message ID" },
                    attachmentId = new { type = "string", description = "The attachment ID" }
                }
            },
            handler: async (args) =>
            {
                var mid = args["messageId"]?.ToString();
                var aid = args["attachmentId"]?.ToString();
                return await GraphGetAsync($"/me/messages/{mid}/attachments/{aid}/$value");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("create_attachment",
            "Add a file attachment to a draft message. The message must be in draft state. Provide the file name, MIME content type, and base64-encoded file content.",
            schema: new
            {
                type = "object",
                required = new[] { "messageId", "name", "contentBytes" },
                properties = new
                {
                    messageId = new { type = "string", description = "The draft message ID" },
                    name = new { type = "string", description = "File name (e.g. report.pdf)" },
                    contentType = new { type = "string", description = "MIME type (e.g. application/pdf, image/png)" },
                    contentBytes = new { type = "string", description = "Base64-encoded file content" }
                }
            },
            handler: async (args) =>
            {
                var mid = args["messageId"]?.ToString();
                var payload = new JObject
                {
                    ["@odata.type"] = "#microsoft.graph.fileAttachment",
                    ["name"] = args["name"]?.ToString(),
                    ["contentBytes"] = args["contentBytes"]?.ToString()
                };
                var ct = args["contentType"]?.ToString();
                if (!string.IsNullOrEmpty(ct)) payload["contentType"] = ct;
                return await GraphPostAsync($"/me/messages/{mid}/attachments", payload);
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("delete_attachment",
            "Delete an attachment from a draft message. The message must be in draft state.",
            schema: new
            {
                type = "object",
                required = new[] { "messageId", "attachmentId" },
                properties = new
                {
                    messageId = new { type = "string", description = "The draft message ID" },
                    attachmentId = new { type = "string", description = "The attachment ID to delete" }
                }
            },
            handler: async (args) =>
            {
                var mid = args["messageId"]?.ToString();
                var aid = args["attachmentId"]?.ToString();
                return await GraphDeleteAsync($"/me/messages/{mid}/attachments/{aid}");
            },
            annotations: new { readOnlyHint = false, destructiveHint = true }
        );

        h.AddTool("get_mail_folder",
            "Get properties of a specific mail folder by ID or well-known name. Well-known names: inbox, drafts, sentitems, deleteditems, junkemail, archive.",
            schema: new
            {
                type = "object",
                required = new[] { "folderId" },
                properties = new
                {
                    folderId = new { type = "string", description = "Folder ID or well-known name (inbox, drafts, sentitems, deleteditems, junkemail, archive)" },
                    select = new { type = "string", description = "Properties to return (e.g. id,displayName,totalItemCount,unreadItemCount)" }
                }
            },
            handler: async (args) =>
            {
                var fid = args["folderId"]?.ToString();
                var sel = args["select"]?.ToString();
                var qs = string.IsNullOrEmpty(sel) ? "" : $"?$select={Uri.EscapeDataString(sel)}";
                return await GraphGetAsync($"/me/mailFolders/{fid}{qs}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("list_folder_messages",
            "List messages in a specific mail folder. Use well-known folder names (inbox, drafts, sentitems, deleteditems, junkemail, archive) or a folder ID from list_folders. Supports keyword search and OData filters.",
            schema: new
            {
                type = "object",
                required = new[] { "folderId" },
                properties = new
                {
                    folderId = new { type = "string", description = "Folder ID or well-known name" },
                    search = new { type = "string", description = "Keyword search within this folder" },
                    filter = new { type = "string", description = "OData filter (e.g. isRead eq false)" },
                    select = new { type = "string", description = "Comma-separated fields to return" },
                    top = new { type = "integer", description = "Number of messages (default 10)" },
                    orderby = new { type = "string", description = "Sort order (e.g. receivedDateTime desc)" }
                }
            },
            handler: async (args) =>
            {
                var fid = args["folderId"]?.ToString();
                var parts = new List<string>();
                var s = args["search"]?.ToString();
                if (!string.IsNullOrEmpty(s)) parts.Add($"$search=\"{Uri.EscapeDataString(s)}\"");
                var f = args["filter"]?.ToString();
                if (!string.IsNullOrEmpty(f)) parts.Add($"$filter={Uri.EscapeDataString(f)}");
                var sel = args["select"]?.ToString();
                if (!string.IsNullOrEmpty(sel)) parts.Add($"$select={Uri.EscapeDataString(sel)}");
                var top = args["top"]?.ToString();
                if (!string.IsNullOrEmpty(top)) parts.Add($"$top={top}");
                var ob = args["orderby"]?.ToString();
                if (!string.IsNullOrEmpty(ob)) parts.Add($"$orderby={Uri.EscapeDataString(ob)}");
                var qs = parts.Count > 0 ? "?" + string.Join("&", parts) : "";
                return await GraphGetAsync($"/me/mailFolders/{fid}/messages{qs}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("create_mail_folder",
            "Create a new mail folder in the user's mailbox. Can be top-level or a child of an existing folder.",
            schema: new
            {
                type = "object",
                required = new[] { "displayName" },
                properties = new
                {
                    displayName = new { type = "string", description = "Name for the new folder" },
                    parentFolderId = new { type = "string", description = "Parent folder ID to create as subfolder (optional, defaults to top-level)" },
                    isHidden = new { type = "boolean", description = "Whether the folder is hidden" }
                }
            },
            handler: async (args) =>
            {
                var payload = new JObject { ["displayName"] = args["displayName"]?.ToString() };
                var hidden = args["isHidden"]?.ToString();
                if (!string.IsNullOrEmpty(hidden)) payload["isHidden"] = bool.Parse(hidden);
                var parent = args["parentFolderId"]?.ToString();
                var path = string.IsNullOrEmpty(parent)
                    ? "/me/mailFolders"
                    : $"/me/mailFolders/{parent}/childFolders";
                return await GraphPostAsync(path, payload);
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("update_mail_folder",
            "Update properties of a mail folder, such as renaming it.",
            schema: new
            {
                type = "object",
                required = new[] { "folderId", "displayName" },
                properties = new
                {
                    folderId = new { type = "string", description = "The mail folder ID" },
                    displayName = new { type = "string", description = "New display name for the folder" }
                }
            },
            handler: async (args) =>
            {
                var fid = args["folderId"]?.ToString();
                var payload = new JObject { ["displayName"] = args["displayName"]?.ToString() };
                return await GraphPatchAsync($"/me/mailFolders/{fid}", payload);
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("delete_mail_folder",
            "Delete a mail folder and all its contents. Cannot delete well-known folders (inbox, drafts, sentitems, deleteditems).",
            schema: new
            {
                type = "object",
                required = new[] { "folderId" },
                properties = new
                {
                    folderId = new { type = "string", description = "The mail folder ID to delete" }
                }
            },
            handler: async (args) =>
            {
                var fid = args["folderId"]?.ToString();
                return await GraphDeleteAsync($"/me/mailFolders/{fid}");
            },
            annotations: new { readOnlyHint = false, destructiveHint = true }
        );

        h.AddTool("list_child_folders",
            "List the child (sub) folders of a specific mail folder. Use folder ID or well-known name (inbox, drafts, sentitems, deleteditems, junkemail, archive).",
            schema: new
            {
                type = "object",
                required = new[] { "folderId" },
                properties = new
                {
                    folderId = new { type = "string", description = "Parent folder ID or well-known name" },
                    top = new { type = "integer", description = "Number of child folders to return" }
                }
            },
            handler: async (args) =>
            {
                var fid = args["folderId"]?.ToString();
                var top = args["top"]?.ToString();
                var qs = string.IsNullOrEmpty(top) ? "" : $"?$top={top}";
                return await GraphGetAsync($"/me/mailFolders/{fid}/childFolders{qs}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("create_draft_in_folder",
            "Create a draft message directly in a specific mail folder. Same as create_draft but targets a specific folder.",
            schema: new
            {
                type = "object",
                required = new[] { "folderId", "subject" },
                properties = new
                {
                    folderId = new { type = "string", description = "Folder ID or well-known name to create the draft in" },
                    subject = new { type = "string", description = "Email subject" },
                    body = new { type = "string", description = "Email body content" },
                    bodyType = new { type = "string", description = "Body content type: Text or HTML (default Text)" },
                    toRecipients = new { type = "string", description = "Comma-separated To email addresses" },
                    ccRecipients = new { type = "string", description = "Comma-separated CC email addresses" },
                    importance = new { type = "string", description = "low, normal, or high" }
                }
            },
            handler: async (args) =>
            {
                var fid = args["folderId"]?.ToString();
                var msg = new JObject { ["subject"] = args["subject"]?.ToString() };
                var body = args["body"]?.ToString();
                if (!string.IsNullOrEmpty(body))
                {
                    var bt = args["bodyType"]?.ToString() ?? "Text";
                    msg["body"] = new JObject { ["contentType"] = bt, ["content"] = body };
                }
                var to = args["toRecipients"]?.ToString();
                if (!string.IsNullOrEmpty(to)) msg["toRecipients"] = BuildRecipientsArray(to);
                var cc = args["ccRecipients"]?.ToString();
                if (!string.IsNullOrEmpty(cc)) msg["ccRecipients"] = BuildRecipientsArray(cc);
                var imp = args["importance"]?.ToString();
                if (!string.IsNullOrEmpty(imp)) msg["importance"] = imp;
                return await GraphPostAsync($"/me/mailFolders/{fid}/messages", msg);
            },
            annotations: new { readOnlyHint = false }
        );

        // --- Create Reply / Reply All / Forward Drafts ---
        h.AddTool("create_reply_draft",
            "Create a draft reply to a message. Returns the draft message which can be edited then sent via Send Draft.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    messageId = new { type = "string", description = "ID of the message to reply to" },
                    comment = new { type = "string", description = "Optional comment to include in the reply body" }
                },
                required = new[] { "messageId" }
            },
            handler: async (args) =>
            {
                var mid = args["messageId"].ToString();
                var body = new JObject();
                var comment = args["comment"]?.ToString();
                if (!string.IsNullOrEmpty(comment)) body["comment"] = comment;
                return await GraphPostAsync($"/me/messages/{mid}/createReply", body);
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("create_reply_all_draft",
            "Create a draft reply-all to a message. Returns the draft message which can be edited then sent via Send Draft.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    messageId = new { type = "string", description = "ID of the message to reply-all to" },
                    comment = new { type = "string", description = "Optional comment to include" }
                },
                required = new[] { "messageId" }
            },
            handler: async (args) =>
            {
                var mid = args["messageId"].ToString();
                var body = new JObject();
                var comment = args["comment"]?.ToString();
                if (!string.IsNullOrEmpty(comment)) body["comment"] = comment;
                return await GraphPostAsync($"/me/messages/{mid}/createReplyAll", body);
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("create_forward_draft",
            "Create a draft forward of a message. Add recipients and modify the draft before sending via Send Draft.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    messageId = new { type = "string", description = "ID of the message to forward" },
                    comment = new { type = "string", description = "Optional comment to include" },
                    toRecipients = new { type = "string", description = "Semicolon-separated email addresses to forward to" }
                },
                required = new[] { "messageId" }
            },
            handler: async (args) =>
            {
                var mid = args["messageId"].ToString();
                var body = new JObject();
                var comment = args["comment"]?.ToString();
                if (!string.IsNullOrEmpty(comment)) body["comment"] = comment;
                var to = args["toRecipients"]?.ToString();
                if (!string.IsNullOrEmpty(to)) body["toRecipients"] = BuildRecipientsArray(to);
                return await GraphPostAsync($"/me/messages/{mid}/createForward", body);
            },
            annotations: new { readOnlyHint = false }
        );

        // --- Mail Tips ---
        h.AddTool("get_mail_tips",
            "Get MailTips for recipients including out-of-office status, mailbox full, delivery restrictions, and custom tips.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    emailAddresses = new { type = "string", description = "Semicolon-separated email addresses to check" },
                    mailTipsOptions = new { type = "string", description = "Comma-separated: automaticReplies, mailboxFullStatus, customMailTip, deliveryRestriction, externalMemberCount, maxMessageSize, totalMemberCount" }
                },
                required = new[] { "emailAddresses" }
            },
            handler: async (args) =>
            {
                var emails = args["emailAddresses"].ToString().Split(';').Select(e => e.Trim()).Where(e => e.Length > 0).ToArray();
                var opts = args["mailTipsOptions"]?.ToString() ?? "automaticReplies, mailboxFullStatus, customMailTip, deliveryRestriction";
                var body = new JObject
                {
                    ["EmailAddresses"] = new JArray(emails),
                    ["MailTipsOptions"] = opts
                };
                return await GraphPostAsync("/me/getMailTips", body);
            },
            annotations: new { readOnlyHint = true }
        );

        // --- Message Rules ---
        h.AddTool("list_message_rules",
            "List all inbox message rules for the signed-in user.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    top = new { type = "integer", description = "Number of rules to return" }
                }
            },
            handler: async (args) =>
            {
                var q = new System.Text.StringBuilder("/me/mailFolders/inbox/messageRules");
                var top = args["top"]?.ToString();
                if (!string.IsNullOrEmpty(top)) q.Append($"?$top={top}");
                return await GraphGetAsync(q.ToString());
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("create_message_rule",
            "Create a new inbox message rule with conditions and actions. Example: move emails from a sender to a folder, or mark certain subjects as read.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    displayName = new { type = "string", description = "Name for the rule" },
                    sequence = new { type = "integer", description = "Order in which the rule is evaluated" },
                    isEnabled = new { type = "boolean", description = "Whether the rule is enabled (default true)" },
                    subjectContains = new { type = "string", description = "Semicolon-separated strings to match in subject" },
                    senderContains = new { type = "string", description = "Semicolon-separated strings to match in sender" },
                    fromAddresses = new { type = "string", description = "Semicolon-separated from email addresses" },
                    hasAttachments = new { type = "boolean", description = "Whether message must have attachments" },
                    moveToFolder = new { type = "string", description = "Folder ID to move matching messages to" },
                    copyToFolder = new { type = "string", description = "Folder ID to copy matching messages to" },
                    markAsRead = new { type = "boolean", description = "Mark matching messages as read" },
                    markImportance = new { type = "string", description = "Set importance: low, normal, high" },
                    delete_ = new { type = "boolean", description = "Delete matching messages" },
                    stopProcessingRules = new { type = "boolean", description = "Stop processing more rules after this one" },
                    forwardTo = new { type = "string", description = "Semicolon-separated emails to forward to" }
                },
                required = new[] { "displayName" }
            },
            handler: async (args) =>
            {
                var rule = new JObject();
                rule["displayName"] = args["displayName"].ToString();
                var seq = args["sequence"]?.ToString();
                if (!string.IsNullOrEmpty(seq)) rule["sequence"] = int.Parse(seq);
                rule["isEnabled"] = args["isEnabled"]?.ToObject<bool>() ?? true;

                var conditions = new JObject();
                var sc = args["subjectContains"]?.ToString();
                if (!string.IsNullOrEmpty(sc)) conditions["subjectContains"] = new JArray(sc.Split(';').Select(s => s.Trim()));
                var snc = args["senderContains"]?.ToString();
                if (!string.IsNullOrEmpty(snc)) conditions["senderContains"] = new JArray(snc.Split(';').Select(s => s.Trim()));
                var fa = args["fromAddresses"]?.ToString();
                if (!string.IsNullOrEmpty(fa)) conditions["fromAddresses"] = BuildRecipientsArray(fa);
                var ha = args["hasAttachments"]?.ToString();
                if (!string.IsNullOrEmpty(ha)) conditions["hasAttachments"] = bool.Parse(ha);
                if (conditions.HasValues) rule["conditions"] = conditions;

                var actions = new JObject();
                var mtf = args["moveToFolder"]?.ToString();
                if (!string.IsNullOrEmpty(mtf)) actions["moveToFolder"] = mtf;
                var ctf = args["copyToFolder"]?.ToString();
                if (!string.IsNullOrEmpty(ctf)) actions["copyToFolder"] = ctf;
                var mar = args["markAsRead"]?.ToString();
                if (!string.IsNullOrEmpty(mar)) actions["markAsRead"] = bool.Parse(mar);
                var mi = args["markImportance"]?.ToString();
                if (!string.IsNullOrEmpty(mi)) actions["markImportance"] = mi;
                var del = args["delete_"]?.ToString();
                if (!string.IsNullOrEmpty(del)) actions["delete"] = bool.Parse(del);
                var spr = args["stopProcessingRules"]?.ToString();
                if (!string.IsNullOrEmpty(spr)) actions["stopProcessingRules"] = bool.Parse(spr);
                var fwd = args["forwardTo"]?.ToString();
                if (!string.IsNullOrEmpty(fwd)) actions["forwardTo"] = BuildRecipientsArray(fwd);
                if (actions.HasValues) rule["actions"] = actions;

                return await GraphPostAsync("/me/mailFolders/inbox/messageRules", rule);
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("get_message_rule",
            "Get a specific inbox message rule by ID.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    ruleId = new { type = "string", description = "The ID of the message rule" }
                },
                required = new[] { "ruleId" }
            },
            handler: async (args) =>
            {
                return await GraphGetAsync($"/me/mailFolders/inbox/messageRules/{args["ruleId"]}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("update_message_rule",
            "Update properties of an inbox message rule.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    ruleId = new { type = "string", description = "The ID of the message rule to update" },
                    displayName = new { type = "string", description = "Updated name" },
                    isEnabled = new { type = "boolean", description = "Enable or disable the rule" },
                    sequence = new { type = "integer", description = "Updated execution order" }
                },
                required = new[] { "ruleId" }
            },
            handler: async (args) =>
            {
                var rid = args["ruleId"].ToString();
                var patch = new JObject();
                var dn = args["displayName"]?.ToString();
                if (!string.IsNullOrEmpty(dn)) patch["displayName"] = dn;
                var ie = args["isEnabled"]?.ToString();
                if (!string.IsNullOrEmpty(ie)) patch["isEnabled"] = bool.Parse(ie);
                var seq = args["sequence"]?.ToString();
                if (!string.IsNullOrEmpty(seq)) patch["sequence"] = int.Parse(seq);
                return await GraphPatchAsync($"/me/mailFolders/inbox/messageRules/{rid}", patch);
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("delete_message_rule",
            "Delete an inbox message rule.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    ruleId = new { type = "string", description = "The ID of the message rule to delete" }
                },
                required = new[] { "ruleId" }
            },
            handler: async (args) => await GraphDeleteAsync($"/me/mailFolders/inbox/messageRules/{args["ruleId"]}"),
            annotations: new { readOnlyHint = false }
        );
    }

    #endregion

    #region Calendar Tools

    private void RegisterCalendarTools(McpRequestHandler h)
    {
        h.AddTool("list_events",
            "List calendar events for the signed-in user with keyword search, OData filter, and sorting.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    search = new { type = "string", description = "Keyword search (e.g. \"team meeting\")" },
                    filter = new { type = "string", description = "OData filter" },
                    top = new { type = "integer", description = "Max results" },
                    orderby = new { type = "string", description = "Sort order (e.g. \"start/dateTime\")" },
                    select = new { type = "string", description = "Fields to return" }
                }
            },
            handler: async (args) => await GraphGetAsync($"/me/events{BuildODataQuery(args)}"),
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("calendar_view",
            "Get calendar events within a specific date/time range, including recurring event occurrences. Use this to see what is on the calendar for a specific day, week, or time span.",
            schema: new
            {
                type = "object",
                required = new[] { "startDateTime", "endDateTime" },
                properties = new
                {
                    startDateTime = new { type = "string", description = "Start of time range (ISO 8601, e.g. 2026-03-09T00:00:00Z)" },
                    endDateTime = new { type = "string", description = "End of time range (ISO 8601, e.g. 2026-03-09T23:59:59Z)" },
                    userId = new { type = "string", description = "Optional user ID or email to check another user's calendar" },
                    top = new { type = "integer", description = "Max results" },
                    orderby = new { type = "string", description = "Sort order" },
                    select = new { type = "string", description = "Fields to return" }
                }
            },
            handler: async (args) =>
            {
                var start = Uri.EscapeDataString(args["startDateTime"]?.ToString() ?? "");
                var end = Uri.EscapeDataString(args["endDateTime"]?.ToString() ?? "");
                var userId = args["userId"]?.ToString();
                var basePath = string.IsNullOrEmpty(userId) ? "/me" : $"/users/{userId}";
                var qs = $"?startDateTime={start}&endDateTime={end}";
                var top = args["top"]?.ToString();
                if (!string.IsNullOrEmpty(top)) qs += $"&$top={top}";
                var orderby = args["orderby"]?.ToString();
                if (!string.IsNullOrEmpty(orderby)) qs += $"&$orderby={Uri.EscapeDataString(orderby)}";
                var sel = args["select"]?.ToString();
                if (!string.IsNullOrEmpty(sel)) qs += $"&$select={Uri.EscapeDataString(sel)}";
                return await GraphGetAsync($"{basePath}/calendarView{qs}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("get_event",
            "Get a specific calendar event by ID.",
            schema: new
            {
                type = "object",
                required = new[] { "eventId" },
                properties = new
                {
                    eventId = new { type = "string", description = "Event ID" },
                    select = new { type = "string", description = "Fields to return" }
                }
            },
            handler: async (args) =>
            {
                var id = args["eventId"]?.ToString();
                var sel = args["select"]?.ToString();
                var qs = string.IsNullOrEmpty(sel) ? "" : $"?$select={Uri.EscapeDataString(sel)}";
                return await GraphGetAsync($"/me/events/{id}{qs}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("create_event",
            "Create a new calendar event. Supports attendees, Teams online meetings, recurring events, all-day events, and reminders.",
            schema: new
            {
                type = "object",
                required = new[] { "subject", "start", "end" },
                properties = new
                {
                    subject = new { type = "string", description = "Event title" },
                    body = new { type = "string", description = "Event description" },
                    bodyType = new { type = "string", description = "Text or HTML" },
                    start = new { type = "string", description = "Start date/time (ISO 8601)" },
                    end = new { type = "string", description = "End date/time (ISO 8601)" },
                    timeZone = new { type = "string", description = "Time zone (e.g. Pacific Standard Time, UTC)" },
                    location = new { type = "string", description = "Location display name" },
                    attendees = new { type = "array", description = "Attendee email addresses", items = new { type = "string" } },
                    isOnlineMeeting = new { type = "boolean", description = "Create a Teams meeting" },
                    isAllDay = new { type = "boolean", description = "All-day event" },
                    importance = new { type = "string", description = "low, normal, or high" },
                    showAs = new { type = "string", description = "free, tentative, busy, oof, workingElsewhere" },
                    reminderMinutesBeforeStart = new { type = "integer", description = "Reminder in minutes before start" },
                    recurrence = new { type = "object", description = "Recurrence pattern in Graph API format" },
                    transactionId = new { type = "string", description = "Idempotence key to prevent duplicates" }
                }
            },
            handler: async (args) => await GraphPostAsync("/me/events", BuildEventObject(args)),
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("update_event",
            "Update an existing calendar event's properties (subject, time, location, attendees, etc.).",
            schema: new
            {
                type = "object",
                required = new[] { "eventId" },
                properties = new
                {
                    eventId = new { type = "string", description = "Event ID to update" },
                    subject = new { type = "string", description = "New subject" },
                    body = new { type = "string", description = "New body" },
                    bodyType = new { type = "string", description = "Text or HTML" },
                    start = new { type = "string", description = "New start date/time" },
                    end = new { type = "string", description = "New end date/time" },
                    timeZone = new { type = "string", description = "Time zone" },
                    location = new { type = "string", description = "New location" },
                    attendees = new { type = "array", description = "Updated attendee emails", items = new { type = "string" } },
                    isOnlineMeeting = new { type = "boolean", description = "Toggle Teams meeting" },
                    importance = new { type = "string", description = "low, normal, or high" },
                    showAs = new { type = "string", description = "free, tentative, busy, oof, workingElsewhere" },
                    reminderMinutesBeforeStart = new { type = "integer", description = "Reminder in minutes" }
                }
            },
            handler: async (args) =>
            {
                var id = args["eventId"]?.ToString();
                return await GraphPatchAsync($"/me/events/{id}", BuildEventObject(args));
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("delete_event",
            "Delete a calendar event permanently.",
            schema: new
            {
                type = "object",
                required = new[] { "eventId" },
                properties = new
                {
                    eventId = new { type = "string", description = "Event ID to delete" }
                }
            },
            handler: async (args) => await GraphDeleteAsync($"/me/events/{args["eventId"]}"),
            annotations: new { readOnlyHint = false, destructiveHint = true }
        );

        h.AddTool("accept_event",
            "Accept a meeting invitation and optionally send a response to the organizer.",
            schema: new
            {
                type = "object",
                required = new[] { "eventId" },
                properties = new
                {
                    eventId = new { type = "string", description = "Event ID to accept" },
                    comment = new { type = "string", description = "Optional response message" },
                    sendResponse = new { type = "boolean", description = "Send response to organizer (default true)" }
                }
            },
            handler: async (args) =>
            {
                var respBody = new JObject();
                if (args["comment"] != null) respBody["comment"] = args["comment"];
                respBody["sendResponse"] = args["sendResponse"]?.Value<bool>() ?? true;
                return await GraphPostAsync($"/me/events/{args["eventId"]}/accept", respBody);
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("decline_event",
            "Decline a meeting invitation.",
            schema: new
            {
                type = "object",
                required = new[] { "eventId" },
                properties = new
                {
                    eventId = new { type = "string", description = "Event ID to decline" },
                    comment = new { type = "string", description = "Optional response message" },
                    sendResponse = new { type = "boolean", description = "Send response to organizer (default true)" }
                }
            },
            handler: async (args) =>
            {
                var respBody = new JObject();
                if (args["comment"] != null) respBody["comment"] = args["comment"];
                respBody["sendResponse"] = args["sendResponse"]?.Value<bool>() ?? true;
                return await GraphPostAsync($"/me/events/{args["eventId"]}/decline", respBody);
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("tentatively_accept",
            "Tentatively accept a meeting invitation.",
            schema: new
            {
                type = "object",
                required = new[] { "eventId" },
                properties = new
                {
                    eventId = new { type = "string", description = "Event ID to tentatively accept" },
                    comment = new { type = "string", description = "Optional response message" },
                    sendResponse = new { type = "boolean", description = "Send response to organizer (default true)" }
                }
            },
            handler: async (args) =>
            {
                var respBody = new JObject();
                if (args["comment"] != null) respBody["comment"] = args["comment"];
                respBody["sendResponse"] = args["sendResponse"]?.Value<bool>() ?? true;
                return await GraphPostAsync($"/me/events/{args["eventId"]}/tentativelyAccept", respBody);
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("cancel_event",
            "Cancel a meeting and notify attendees with an optional message.",
            schema: new
            {
                type = "object",
                required = new[] { "eventId" },
                properties = new
                {
                    eventId = new { type = "string", description = "Event ID to cancel" },
                    comment = new { type = "string", description = "Cancellation message to attendees" }
                }
            },
            handler: async (args) =>
            {
                var cancelBody = new JObject();
                if (args["comment"] != null) cancelBody["comment"] = args["comment"];
                return await GraphPostAsync($"/me/events/{args["eventId"]}/cancel", cancelBody);
            },
            annotations: new { readOnlyHint = false, destructiveHint = true }
        );

        h.AddTool("find_meeting_times",
            "Find available meeting times based on attendee availability, time constraints, and meeting duration.",
            schema: new
            {
                type = "object",
                required = new[] { "meetingDuration" },
                properties = new
                {
                    attendees = new { type = "array", description = "Attendee email addresses", items = new { type = "string" } },
                    meetingDuration = new { type = "string", description = "Duration in ISO 8601 (e.g. PT1H, PT30M)" },
                    startTime = new { type = "string", description = "Earliest candidate start (ISO 8601)" },
                    endTime = new { type = "string", description = "Latest candidate end (ISO 8601)" },
                    timeZone = new { type = "string", description = "Time zone for constraints" },
                    maxCandidates = new { type = "integer", description = "Max suggestions to return" },
                    isOrganizerOptional = new { type = "boolean", description = "Organizer attendance optional" },
                    minimumAttendeePercentage = new { type = "number", description = "Min attendee percentage (0-100)" }
                }
            },
            handler: async (args) =>
            {
                var fmtBody = new JObject { ["meetingDuration"] = args["meetingDuration"]?.ToString() };
                var emails = args["attendees"] as JArray;
                if (emails != null && emails.Count > 0)
                {
                    var atts = new JArray();
                    foreach (var e in emails)
                        atts.Add(new JObject { ["emailAddress"] = new JObject { ["address"] = e.ToString() }, ["type"] = "required" });
                    fmtBody["attendees"] = atts;
                }
                if (args["startTime"] != null || args["endTime"] != null)
                {
                    var tz = args["timeZone"]?.ToString() ?? "UTC";
                    var tc = new JObject { ["activityDomain"] = "work" };
                    var slots = new JArray
                    {
                        new JObject
                        {
                            ["start"] = new JObject { ["dateTime"] = args["startTime"]?.ToString(), ["timeZone"] = tz },
                            ["end"] = new JObject { ["dateTime"] = args["endTime"]?.ToString(), ["timeZone"] = tz }
                        }
                    };
                    tc["timeslots"] = slots;
                    fmtBody["timeConstraint"] = tc;
                }
                if (args["maxCandidates"] != null) fmtBody["maxCandidates"] = args["maxCandidates"];
                if (args["isOrganizerOptional"] != null) fmtBody["isOrganizerOptional"] = args["isOrganizerOptional"];
                if (args["minimumAttendeePercentage"] != null) fmtBody["minimumAttendeePercentage"] = args["minimumAttendeePercentage"];
                fmtBody["returnSuggestionReasons"] = true;
                return await GraphPostAsync("/me/findMeetingTimes", fmtBody);
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("get_schedule",
            "Get free/busy availability schedule for one or more users or distribution lists.",
            schema: new
            {
                type = "object",
                required = new[] { "schedules", "startTime", "endTime" },
                properties = new
                {
                    schedules = new { type = "array", description = "Email addresses to check", items = new { type = "string" } },
                    startTime = new { type = "string", description = "Start date/time (ISO 8601)" },
                    endTime = new { type = "string", description = "End date/time (ISO 8601)" },
                    timeZone = new { type = "string", description = "Time zone (default UTC)" },
                    availabilityViewInterval = new { type = "integer", description = "Slot size in minutes (default 30)" }
                }
            },
            handler: async (args) =>
            {
                var tz = args["timeZone"]?.ToString() ?? "UTC";
                var schedBody = new JObject
                {
                    ["schedules"] = args["schedules"],
                    ["startTime"] = new JObject { ["dateTime"] = args["startTime"]?.ToString(), ["timeZone"] = tz },
                    ["endTime"] = new JObject { ["dateTime"] = args["endTime"]?.ToString(), ["timeZone"] = tz }
                };
                if (args["availabilityViewInterval"] != null) schedBody["availabilityViewInterval"] = args["availabilityViewInterval"];
                return await GraphPostAsync("/me/calendar/getSchedule", schedBody);
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("list_calendars",
            "List all calendars for the signed-in user.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    search = new { type = "string", description = "Search calendars by name" },
                    top = new { type = "integer", description = "Max results" }
                }
            },
            handler: async (args) => await GraphGetAsync($"/me/calendars{BuildODataQuery(args)}"),
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("get_calendar",
            "Get properties of a specific calendar by ID.",
            schema: new
            {
                type = "object",
                required = new[] { "calendarId" },
                properties = new
                {
                    calendarId = new { type = "string", description = "The calendar ID" },
                    select = new { type = "string", description = "Properties to return" }
                }
            },
            handler: async (args) =>
            {
                var cid = args["calendarId"]?.ToString();
                var sel = args["select"]?.ToString();
                var qs = string.IsNullOrEmpty(sel) ? "" : $"?$select={Uri.EscapeDataString(sel)}";
                return await GraphGetAsync($"/me/calendars/{cid}{qs}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("create_calendar",
            "Create a new secondary calendar for the signed-in user.",
            schema: new
            {
                type = "object",
                required = new[] { "name" },
                properties = new
                {
                    name = new { type = "string", description = "Name for the new calendar" },
                    color = new { type = "string", description = "Color theme (auto, lightBlue, lightGreen, lightOrange, lightGray, lightYellow, lightTeal, lightPink, lightBrown, lightRed, maxColor)" }
                }
            },
            handler: async (args) =>
            {
                var payload = new JObject { ["name"] = args["name"]?.ToString() };
                var color = args["color"]?.ToString();
                if (!string.IsNullOrEmpty(color)) payload["color"] = color;
                return await GraphPostAsync("/me/calendars", payload);
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("update_calendar",
            "Update properties of a calendar (name, color).",
            schema: new
            {
                type = "object",
                required = new[] { "calendarId" },
                properties = new
                {
                    calendarId = new { type = "string", description = "The calendar ID" },
                    name = new { type = "string", description = "Updated calendar name" },
                    color = new { type = "string", description = "Updated color theme" }
                }
            },
            handler: async (args) =>
            {
                var cid = args["calendarId"]?.ToString();
                var payload = new JObject();
                var name = args["name"]?.ToString();
                if (!string.IsNullOrEmpty(name)) payload["name"] = name;
                var color = args["color"]?.ToString();
                if (!string.IsNullOrEmpty(color)) payload["color"] = color;
                return await GraphPatchAsync($"/me/calendars/{cid}", payload);
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("delete_calendar",
            "Delete a secondary calendar. Cannot delete the user's default calendar.",
            schema: new
            {
                type = "object",
                required = new[] { "calendarId" },
                properties = new
                {
                    calendarId = new { type = "string", description = "The calendar ID to delete" }
                }
            },
            handler: async (args) =>
            {
                var cid = args["calendarId"]?.ToString();
                return await GraphDeleteAsync($"/me/calendars/{cid}");
            },
            annotations: new { readOnlyHint = false, destructiveHint = true }
        );

        h.AddTool("list_event_instances",
            "Get the occurrences of a recurring event within a specified time range. Requires startDateTime and endDateTime.",
            schema: new
            {
                type = "object",
                required = new[] { "eventId", "startDateTime", "endDateTime" },
                properties = new
                {
                    eventId = new { type = "string", description = "The recurring event ID" },
                    startDateTime = new { type = "string", description = "Start of range (ISO 8601, e.g. 2024-01-01T00:00:00Z)" },
                    endDateTime = new { type = "string", description = "End of range (ISO 8601)" },
                    select = new { type = "string", description = "Properties to return" },
                    top = new { type = "integer", description = "Number of instances to return" }
                }
            },
            handler: async (args) =>
            {
                var eid = args["eventId"]?.ToString();
                var parts = new List<string>
                {
                    $"startDateTime={Uri.EscapeDataString(args["startDateTime"]?.ToString())}",
                    $"endDateTime={Uri.EscapeDataString(args["endDateTime"]?.ToString())}"
                };
                var sel = args["select"]?.ToString();
                if (!string.IsNullOrEmpty(sel)) parts.Add($"$select={Uri.EscapeDataString(sel)}");
                var top = args["top"]?.ToString();
                if (!string.IsNullOrEmpty(top)) parts.Add($"$top={top}");
                return await GraphGetAsync($"/me/events/{eid}/instances?{string.Join("&", parts)}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("list_event_attachments",
            "List all attachments on a calendar event.",
            schema: new
            {
                type = "object",
                required = new[] { "eventId" },
                properties = new
                {
                    eventId = new { type = "string", description = "The event ID" }
                }
            },
            handler: async (args) =>
            {
                var eid = args["eventId"]?.ToString();
                return await GraphGetAsync($"/me/events/{eid}/attachments");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("create_event_attachment",
            "Add a file attachment to a calendar event. Provide base64-encoded content.",
            schema: new
            {
                type = "object",
                required = new[] { "eventId", "name", "contentBytes" },
                properties = new
                {
                    eventId = new { type = "string", description = "The event ID" },
                    name = new { type = "string", description = "File name (e.g. agenda.pdf)" },
                    contentType = new { type = "string", description = "MIME type (e.g. application/pdf)" },
                    contentBytes = new { type = "string", description = "Base64-encoded file content" }
                }
            },
            handler: async (args) =>
            {
                var eid = args["eventId"]?.ToString();
                var payload = new JObject
                {
                    ["@odata.type"] = "#microsoft.graph.fileAttachment",
                    ["name"] = args["name"]?.ToString(),
                    ["contentBytes"] = args["contentBytes"]?.ToString()
                };
                var ct = args["contentType"]?.ToString();
                if (!string.IsNullOrEmpty(ct)) payload["contentType"] = ct;
                return await GraphPostAsync($"/me/events/{eid}/attachments", payload);
            },
            annotations: new { readOnlyHint = false }
        );

        // --- Snooze / Dismiss Reminder ---
        h.AddTool("snooze_reminder",
            "Snooze the reminder for an event and reschedule it to a new time.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    eventId = new { type = "string", description = "ID of the event" },
                    newReminderDateTime = new { type = "string", description = "New reminder date/time (ISO 8601)" },
                    timeZone = new { type = "string", description = "Time zone (e.g. Pacific Standard Time, UTC)" }
                },
                required = new[] { "eventId", "newReminderDateTime", "timeZone" }
            },
            handler: async (args) =>
            {
                var body = new JObject
                {
                    ["newReminderTime"] = new JObject
                    {
                        ["dateTime"] = args["newReminderDateTime"].ToString(),
                        ["timeZone"] = args["timeZone"].ToString()
                    }
                };
                return await GraphPostAsync($"/me/events/{args["eventId"]}/snoozeReminder", body);
            },
            annotations: new { readOnlyHint = false }
        );

        h.AddTool("dismiss_reminder",
            "Dismiss the reminder for an event.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    eventId = new { type = "string", description = "ID of the event" }
                },
                required = new[] { "eventId" }
            },
            handler: async (args) => await GraphPostEmptyAsync($"/me/events/{args["eventId"]}/dismissReminder"),
            annotations: new { readOnlyHint = false }
        );

        // --- Event Attachment Get / Delete ---
        h.AddTool("get_event_attachment",
            "Get a specific attachment on a calendar event.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    eventId = new { type = "string", description = "ID of the event" },
                    attachmentId = new { type = "string", description = "ID of the attachment" }
                },
                required = new[] { "eventId", "attachmentId" }
            },
            handler: async (args) =>
            {
                return await GraphGetAsync($"/me/events/{args["eventId"]}/attachments/{args["attachmentId"]}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("delete_event_attachment",
            "Delete an attachment from a calendar event.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    eventId = new { type = "string", description = "ID of the event" },
                    attachmentId = new { type = "string", description = "ID of the attachment to delete" }
                },
                required = new[] { "eventId", "attachmentId" }
            },
            handler: async (args) => await GraphDeleteAsync($"/me/events/{args["eventId"]}/attachments/{args["attachmentId"]}"),
            annotations: new { readOnlyHint = false }
        );
    }

    #endregion

    #region User & Org Tools

    private void RegisterUserTools(McpRequestHandler h)
    {
        h.AddTool("get_my_profile",
            "Get the signed-in user's profile including display name, email, job title, department, and office location. Use this for self-knowledge and context about the current user.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    select = new { type = "string", description = "Comma-separated properties (e.g. displayName,mail,jobTitle,department,officeLocation)" },
                    expand = new { type = "string", description = "Expand: manager or directReports (only one per request)" }
                }
            },
            handler: async (args) =>
            {
                var parts = new List<string>();
                var sel = args["select"]?.ToString();
                if (!string.IsNullOrEmpty(sel)) parts.Add($"$select={Uri.EscapeDataString(sel)}");
                var exp = args["expand"]?.ToString();
                if (!string.IsNullOrEmpty(exp)) parts.Add($"$expand={Uri.EscapeDataString(exp)}");
                var qs = parts.Count > 0 ? "?" + string.Join("&", parts) : "";
                return await GraphGetAsync($"/me{qs}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("get_my_manager",
            "Get the manager of the signed-in user. Returns the manager's profile information.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    select = new { type = "string", description = "Comma-separated manager properties to return" }
                }
            },
            handler: async (args) =>
            {
                var sel = args["select"]?.ToString();
                var qs = string.IsNullOrEmpty(sel) ? "" : $"?$select={Uri.EscapeDataString(sel)}";
                return await GraphGetAsync($"/me/manager{qs}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("get_my_direct_reports",
            "List the direct reports of the signed-in user. Returns basic profile info for each direct report.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    select = new { type = "string", description = "Properties to return (e.g. id,displayName,mail,jobTitle,userPrincipalName)" },
                    top = new { type = "integer", description = "Number of direct reports to return" }
                }
            },
            handler: async (args) =>
            {
                var parts = new List<string>();
                var sel = args["select"]?.ToString();
                if (!string.IsNullOrEmpty(sel)) parts.Add($"$select={Uri.EscapeDataString(sel)}");
                var top = args["top"]?.ToString();
                if (!string.IsNullOrEmpty(top)) parts.Add($"$top={top}");
                var qs = parts.Count > 0 ? "?" + string.Join("&", parts) : "";
                return await GraphGetAsync($"/me/directReports{qs}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("get_user_profile",
            "Retrieve a specific user's profile by their object ID (GUID) or userPrincipalName (UPN). Do NOT use 'me' as the identifier — use get_my_profile instead. If only a display name is available, use list_users to look up the user first.",
            schema: new
            {
                type = "object",
                required = new[] { "userIdentifier" },
                properties = new
                {
                    userIdentifier = new { type = "string", description = "User object ID (GUID) or userPrincipalName (UPN). Do not use 'me'." },
                    select = new { type = "string", description = "Comma-separated properties to return" },
                    expand = new { type = "string", description = "Expand: manager or directReports (only one per request)" }
                }
            },
            handler: async (args) =>
            {
                var uid = args["userIdentifier"]?.ToString();
                var parts = new List<string>();
                var sel = args["select"]?.ToString();
                if (!string.IsNullOrEmpty(sel)) parts.Add($"$select={Uri.EscapeDataString(sel)}");
                var exp = args["expand"]?.ToString();
                if (!string.IsNullOrEmpty(exp)) parts.Add($"$expand={Uri.EscapeDataString(exp)}");
                var qs = parts.Count > 0 ? "?" + string.Join("&", parts) : "";
                return await GraphGetAsync($"/users/{uid}{qs}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("get_users_manager",
            "Retrieve the manager of a specified user. Provide the user's object ID or UPN. Do NOT use 'me' — use get_my_manager instead.",
            schema: new
            {
                type = "object",
                required = new[] { "userIdentifier" },
                properties = new
                {
                    userIdentifier = new { type = "string", description = "User object ID (GUID) or userPrincipalName (UPN)" },
                    select = new { type = "string", description = "Manager properties to return" }
                }
            },
            handler: async (args) =>
            {
                var uid = args["userIdentifier"]?.ToString();
                var sel = args["select"]?.ToString();
                var qs = string.IsNullOrEmpty(sel) ? "" : $"?$select={Uri.EscapeDataString(sel)}";
                return await GraphGetAsync($"/users/{uid}/manager{qs}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("get_direct_reports",
            "List the direct reports of a specified user. Provide the user's object ID or UPN. Do NOT use 'me' as the identifier.",
            schema: new
            {
                type = "object",
                required = new[] { "userIdentifier" },
                properties = new
                {
                    userIdentifier = new { type = "string", description = "User object ID (GUID) or userPrincipalName (UPN)" },
                    select = new { type = "string", description = "Properties to return (e.g. id,displayName,mail,jobTitle,userPrincipalName)" },
                    top = new { type = "integer", description = "Number of direct reports to return" }
                }
            },
            handler: async (args) =>
            {
                var uid = args["userIdentifier"]?.ToString();
                var parts = new List<string>();
                var sel = args["select"]?.ToString();
                if (!string.IsNullOrEmpty(sel)) parts.Add($"$select={Uri.EscapeDataString(sel)}");
                var top = args["top"]?.ToString();
                if (!string.IsNullOrEmpty(top)) parts.Add($"$top={top}");
                var qs = parts.Count > 0 ? "?" + string.Join("&", parts) : "";
                return await GraphGetAsync($"/users/{uid}/directReports{qs}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("list_users",
            "List users in the organization. Supports free-text search with automatic fallback to $filter if search returns no results. Search format: 'displayName:John' to find users named John. ConsistencyLevel is set to eventual by default for advanced query support.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    search = new { type = "string", description = "Free-text search (format: 'displayName:John'). Automatically retries with $filter if no results." },
                    filter = new { type = "string", description = "OData filter (e.g. startswith(displayName,'A'))" },
                    select = new { type = "string", description = "Comma-separated properties to return" },
                    top = new { type = "integer", description = "Number of users to return" },
                    orderby = new { type = "string", description = "Sort order (e.g. displayName)" },
                    count = new { type = "boolean", description = "Include count of items" }
                }
            },
            handler: async (args) =>
            {
                var searchVal = args["search"]?.ToString();
                var filterVal = args["filter"]?.ToString();
                var sel = args["select"]?.ToString();
                var top = args["top"]?.ToString();
                var orderby = args["orderby"]?.ToString();
                var count = args["count"]?.Value<bool>() ?? false;

                // Try $search first if provided
                if (!string.IsNullOrEmpty(searchVal))
                {
                    var result = await ListUsersQueryAsync(searchVal, null, sel, top, orderby, count);
                    var values = (result as JObject)?["value"] as JArray;
                    if (values != null && values.Count > 0)
                        return result;
                    // Fallback: convert search to $filter
                    var fallbackFilter = ConvertSearchToFilter(searchVal);
                    if (!string.IsNullOrEmpty(fallbackFilter))
                        return await ListUsersQueryAsync(null, fallbackFilter, sel, top, orderby, count);
                    return result;
                }
                return await ListUsersQueryAsync(null, filterVal, sel, top, orderby, count);
            },
            annotations: new { readOnlyHint = true }
        );
    }

    private async Task<JToken> ListUsersQueryAsync(string search, string filter, string select, string top, string orderby, bool count)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(search))
        {
            var sv = search.Contains(":") && !search.StartsWith("\"") ? $"\"{search}\"" : search;
            parts.Add($"$search={Uri.EscapeDataString(sv)}");
        }
        if (!string.IsNullOrEmpty(filter)) parts.Add($"$filter={Uri.EscapeDataString(filter)}");
        if (!string.IsNullOrEmpty(select)) parts.Add($"$select={Uri.EscapeDataString(select)}");
        if (!string.IsNullOrEmpty(top)) parts.Add($"$top={top}");
        if (!string.IsNullOrEmpty(orderby)) parts.Add($"$orderby={Uri.EscapeDataString(orderby)}");
        if (count) parts.Add("$count=true");
        var qs = parts.Count > 0 ? "?" + string.Join("&", parts) : "";
        return await GraphGetAsync($"/users{qs}", "eventual");
    }

    private string ConvertSearchToFilter(string search)
    {
        // Convert "displayName:John" to startswith(displayName,'John')
        var idx = search.IndexOf(':');
        if (idx > 0 && idx < search.Length - 1)
        {
            var prop = search.Substring(0, idx).Trim().Trim('"');
            var val = search.Substring(idx + 1).Trim().Trim('"');
            return $"startswith({prop},'{val}')";
        }
        return null;
    }

    // --- Photo Metadata & People ---
    private void RegisterUserToolsLow(McpRequestHandler h)
    {
        h.AddTool("get_my_photo",
            "Get the signed-in user's profile photo metadata including dimensions. Use to check if a user has a photo and its size.",
            schema: new
            {
                type = "object",
                properties = new { }
            },
            handler: async (args) =>
            {
                return await GraphGetAsync("/me/photo");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("list_people",
            "Get people relevant to the signed-in user, ordered by relevance (based on communication, collaboration, and business relationships). Great for finding frequent contacts or collaborators.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    search = new { type = "string", description = "Search by name or email" },
                    top = new { type = "integer", description = "Number of results (default 10)" },
                    filter = new { type = "string", description = "OData filter expression" },
                    select = new { type = "string", description = "Comma-separated properties to return" }
                }
            },
            handler: async (args) =>
            {
                var parts = new List<string>();
                var search = args["search"]?.ToString();
                if (!string.IsNullOrEmpty(search)) parts.Add($"$search=\"{search}\"");
                var top = args["top"]?.ToString();
                if (!string.IsNullOrEmpty(top)) parts.Add($"$top={top}");
                var filter = args["filter"]?.ToString();
                if (!string.IsNullOrEmpty(filter)) parts.Add($"$filter={Uri.EscapeDataString(filter)}");
                var sel = args["select"]?.ToString();
                if (!string.IsNullOrEmpty(sel)) parts.Add($"$select={Uri.EscapeDataString(sel)}");
                var qs = parts.Count > 0 ? "?" + string.Join("&", parts) : "";
                return await GraphGetAsync($"/me/people{qs}");
            },
            annotations: new { readOnlyHint = true }
        );

        // --- Delegated Mailbox (user context) ---
        h.AddTool("list_user_messages",
            "List messages from another user's mailbox (requires delegated mailbox permissions). Specify user by ID or email.",
            schema: new
            {
                type = "object",
                required = new[] { "userId" },
                properties = new
                {
                    userId = new { type = "string", description = "User ID or email address" },
                    search = new { type = "string", description = "Keyword search" },
                    filter = new { type = "string", description = "OData filter" },
                    top = new { type = "integer", description = "Max results (default 10)" },
                    orderby = new { type = "string", description = "Sort order (e.g. \"receivedDateTime desc\")" },
                    select = new { type = "string", description = "Fields to return" }
                }
            },
            handler: async (args) =>
            {
                var uid = args["userId"]?.ToString();
                return await GraphGetAsync($"/users/{uid}/messages{BuildODataQuery(args)}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("get_user_message",
            "Get a specific message from another user's mailbox by message ID (requires delegated mailbox permissions).",
            schema: new
            {
                type = "object",
                required = new[] { "userId", "messageId" },
                properties = new
                {
                    userId = new { type = "string", description = "User ID or email address" },
                    messageId = new { type = "string", description = "The message ID" },
                    select = new { type = "string", description = "Fields to return" }
                }
            },
            handler: async (args) =>
            {
                var uid = args["userId"]?.ToString();
                var mid = args["messageId"]?.ToString();
                var sel = args["select"]?.ToString();
                var qs = string.IsNullOrEmpty(sel) ? "" : $"?$select={Uri.EscapeDataString(sel)}";
                return await GraphGetAsync($"/users/{uid}/messages/{mid}{qs}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("send_user_mail",
            "Send an email on behalf of another user (requires delegated mailbox permissions).",
            schema: new
            {
                type = "object",
                required = new[] { "userId", "subject", "body", "toRecipients" },
                properties = new
                {
                    userId = new { type = "string", description = "User ID or email address" },
                    subject = new { type = "string", description = "Email subject" },
                    body = new { type = "string", description = "Message body content" },
                    bodyType = new { type = "string", description = "Body format: Text or HTML (default Text)" },
                    toRecipients = new { type = "array", description = "To recipients (email addresses)", items = new { type = "string" } },
                    ccRecipients = new { type = "array", description = "CC recipients", items = new { type = "string" } },
                    bccRecipients = new { type = "array", description = "BCC recipients", items = new { type = "string" } },
                    importance = new { type = "string", description = "low, normal, or high" },
                    saveToSentItems = new { type = "boolean", description = "Save to Sent Items (default true)" }
                }
            },
            handler: async (args) =>
            {
                var uid = args["userId"]?.ToString();
                var msg = BuildMessageObject(args);
                var sendBody = new JObject
                {
                    ["message"] = msg,
                    ["saveToSentItems"] = args["saveToSentItems"]?.Value<bool>() ?? true
                };
                return await GraphPostAsync($"/users/{uid}/sendMail", sendBody);
            },
            annotations: new { readOnlyHint = false, destructiveHint = false }
        );

        h.AddTool("list_user_events",
            "List calendar events from another user's calendar (requires delegated calendar permissions).",
            schema: new
            {
                type = "object",
                required = new[] { "userId" },
                properties = new
                {
                    userId = new { type = "string", description = "User ID or email address" },
                    search = new { type = "string", description = "Keyword search" },
                    filter = new { type = "string", description = "OData filter" },
                    top = new { type = "integer", description = "Max results" },
                    orderby = new { type = "string", description = "Sort order" },
                    select = new { type = "string", description = "Fields to return" }
                }
            },
            handler: async (args) =>
            {
                var uid = args["userId"]?.ToString();
                return await GraphGetAsync($"/users/{uid}/events{BuildODataQuery(args)}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("get_user_event",
            "Get a specific calendar event from another user's calendar by event ID (requires delegated calendar permissions).",
            schema: new
            {
                type = "object",
                required = new[] { "userId", "eventId" },
                properties = new
                {
                    userId = new { type = "string", description = "User ID or email address" },
                    eventId = new { type = "string", description = "The event ID" },
                    select = new { type = "string", description = "Fields to return" }
                }
            },
            handler: async (args) =>
            {
                var uid = args["userId"]?.ToString();
                var eid = args["eventId"]?.ToString();
                var sel = args["select"]?.ToString();
                var qs = string.IsNullOrEmpty(sel) ? "" : $"?$select={Uri.EscapeDataString(sel)}";
                return await GraphGetAsync($"/users/{uid}/events/{eid}{qs}");
            },
            annotations: new { readOnlyHint = true }
        );

        h.AddTool("list_user_calendar_view",
            "Get calendar events within a specific date range for another user, including recurring event occurrences (requires delegated calendar permissions).",
            schema: new
            {
                type = "object",
                required = new[] { "userId", "startDateTime", "endDateTime" },
                properties = new
                {
                    userId = new { type = "string", description = "User ID or email address" },
                    startDateTime = new { type = "string", description = "Start of time range (ISO 8601)" },
                    endDateTime = new { type = "string", description = "End of time range (ISO 8601)" },
                    top = new { type = "integer", description = "Max results" },
                    orderby = new { type = "string", description = "Sort order" },
                    select = new { type = "string", description = "Fields to return" }
                }
            },
            handler: async (args) =>
            {
                var uid = args["userId"]?.ToString();
                var start = Uri.EscapeDataString(args["startDateTime"]?.ToString() ?? "");
                var end = Uri.EscapeDataString(args["endDateTime"]?.ToString() ?? "");
                var qs = $"?startDateTime={start}&endDateTime={end}";
                var top = args["top"]?.ToString();
                if (!string.IsNullOrEmpty(top)) qs += $"&$top={top}";
                var orderby = args["orderby"]?.ToString();
                if (!string.IsNullOrEmpty(orderby)) qs += $"&$orderby={Uri.EscapeDataString(orderby)}";
                var sel = args["select"]?.ToString();
                if (!string.IsNullOrEmpty(sel)) qs += $"&$select={Uri.EscapeDataString(sel)}";
                return await GraphGetAsync($"/users/{uid}/calendarView{qs}");
            },
            annotations: new { readOnlyHint = true }
        );
    }

    #endregion

    #region Graph API Helpers

    private async Task<JToken> GraphGetAsync(string path, string consistencyLevel = null, string preferHeader = null)
    {
        var url = $"https://graph.microsoft.com/v1.0{path}";
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = this.Context.Request.Headers.Authorization;
        if (!string.IsNullOrEmpty(consistencyLevel))
            req.Headers.Add("ConsistencyLevel", consistencyLevel);
        if (!string.IsNullOrEmpty(preferHeader))
            req.Headers.Add("Prefer", preferHeader);
        var resp = await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        var content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Graph API error {(int)resp.StatusCode}: {content}");
        if (string.IsNullOrWhiteSpace(content)) return new JObject { ["status"] = "success" };
        return JToken.Parse(content);
    }

    private async Task<JToken> GraphPostAsync(string path, JObject body)
    {
        var url = $"https://graph.microsoft.com/v1.0{path}";
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = this.Context.Request.Headers.Authorization;
        if (body != null)
            req.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        var resp = await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        var content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Graph API error {(int)resp.StatusCode}: {content}");
        if (string.IsNullOrWhiteSpace(content)) return new JObject { ["status"] = "success" };
        return JToken.Parse(content);
    }

    private async Task<JToken> GraphPostEmptyAsync(string path)
    {
        var url = $"https://graph.microsoft.com/v1.0{path}";
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = this.Context.Request.Headers.Authorization;
        var resp = await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new Exception($"Graph API error {(int)resp.StatusCode}: {content}");
        }
        return new JObject { ["status"] = "success" };
    }

    private async Task<JToken> GraphPatchAsync(string path, JObject body)
    {
        var url = $"https://graph.microsoft.com/v1.0{path}";
        var req = new HttpRequestMessage(new HttpMethod("PATCH"), url);
        req.Headers.Authorization = this.Context.Request.Headers.Authorization;
        req.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        var resp = await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        var content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Graph API error {(int)resp.StatusCode}: {content}");
        if (string.IsNullOrWhiteSpace(content)) return new JObject { ["status"] = "success" };
        return JToken.Parse(content);
    }

    private async Task<JToken> GraphDeleteAsync(string path)
    {
        var url = $"https://graph.microsoft.com/v1.0{path}";
        var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.Authorization = this.Context.Request.Headers.Authorization;
        var resp = await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new Exception($"Graph API error {(int)resp.StatusCode}: {content}");
        }
        return new JObject { ["status"] = "deleted" };
    }

    #endregion

    #region Utility Helpers

    private string BuildODataQuery(JObject args)
    {
        var parts = new List<string>();
        var map = new Dictionary<string, string>
        {
            { "search", "$search" }, { "filter", "$filter" },
            { "top", "$top" }, { "orderby", "$orderby" }, { "select", "$select" }
        };
        foreach (var kv in map)
        {
            var val = args[kv.Key]?.ToString();
            if (string.IsNullOrEmpty(val)) continue;
            if (kv.Key == "search")
            {
                var sv = val.StartsWith("\"") ? val : $"\"{val}\"";
                parts.Add($"$search={Uri.EscapeDataString(sv)}");
            }
            else
            {
                parts.Add($"{kv.Value}={Uri.EscapeDataString(val)}");
            }
        }
        return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
    }

    private JObject BuildMessageObject(JObject args)
    {
        var msg = new JObject();
        if (args["subject"] != null) msg["subject"] = args["subject"];
        if (args["body"] != null)
            msg["body"] = new JObject
            {
                ["contentType"] = args["bodyType"]?.ToString() ?? "Text",
                ["content"] = args["body"]
            };
        if (args["importance"] != null) msg["importance"] = args["importance"];
        var to = args["toRecipients"] as JArray;
        if (to != null) msg["toRecipients"] = BuildRecipientsArray(to);
        var cc = args["ccRecipients"] as JArray;
        if (cc != null) msg["ccRecipients"] = BuildRecipientsArray(cc);
        var bcc = args["bccRecipients"] as JArray;
        if (bcc != null) msg["bccRecipients"] = BuildRecipientsArray(bcc);
        return msg;
    }

    private JArray BuildRecipientsArray(string emails)
    {
        if (string.IsNullOrEmpty(emails)) return new JArray();
        var arr = new JArray();
        foreach (var e in emails.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0))
            arr.Add(new JObject { ["emailAddress"] = new JObject { ["address"] = e } });
        return arr;
    }

    private JArray BuildRecipientsArray(JArray emails)
    {
        var arr = new JArray();
        if (emails == null) return arr;
        foreach (var e in emails)
            arr.Add(new JObject { ["emailAddress"] = new JObject { ["address"] = e.ToString() } });
        return arr;
    }

    private JObject BuildEventObject(JObject args)
    {
        var ev = new JObject();
        if (args["subject"] != null) ev["subject"] = args["subject"];
        if (args["body"] != null)
            ev["body"] = new JObject
            {
                ["contentType"] = args["bodyType"]?.ToString() ?? "Text",
                ["content"] = args["body"]
            };
        var tz = args["timeZone"]?.ToString() ?? "UTC";
        if (args["start"] != null)
            ev["start"] = new JObject { ["dateTime"] = args["start"], ["timeZone"] = tz };
        if (args["end"] != null)
            ev["end"] = new JObject { ["dateTime"] = args["end"], ["timeZone"] = tz };
        if (args["location"] != null)
            ev["location"] = new JObject { ["displayName"] = args["location"] };
        var atts = args["attendees"] as JArray;
        if (atts != null && atts.Count > 0)
        {
            var attendeesArr = new JArray();
            foreach (var a in atts)
                attendeesArr.Add(new JObject
                {
                    ["emailAddress"] = new JObject { ["address"] = a.ToString() },
                    ["type"] = "required"
                });
            ev["attendees"] = attendeesArr;
        }
        if (args["isOnlineMeeting"] != null)
        {
            ev["isOnlineMeeting"] = args["isOnlineMeeting"];
            ev["onlineMeetingProvider"] = "teamsForBusiness";
        }
        if (args["isAllDay"] != null) ev["isAllDay"] = args["isAllDay"];
        if (args["importance"] != null) ev["importance"] = args["importance"];
        if (args["showAs"] != null) ev["showAs"] = args["showAs"];
        if (args["reminderMinutesBeforeStart"] != null) ev["reminderMinutesBeforeStart"] = args["reminderMinutesBeforeStart"];
        if (args["recurrence"] != null) ev["recurrence"] = args["recurrence"];
        if (args["transactionId"] != null) ev["transactionId"] = args["transactionId"];
        return ev;
    }

    #endregion

    #region App Insights

    private async Task LogAsync(string operationId, int statusCode, string corrId, string error = null)
    {
        try
        {
            var parts = APP_INSIGHTS_CS.Split(';');
            var iKey = "";
            var endpoint = "";
            foreach (var p in parts)
            {
                if (p.StartsWith("InstrumentationKey=")) iKey = p.Substring(19);
                if (p.StartsWith("IngestionEndpoint=")) endpoint = p.Substring(18);
            }
            if (string.IsNullOrEmpty(iKey) || string.IsNullOrEmpty(endpoint) || iKey == "YOUR_KEY") return;

            var evt = new JObject
            {
                ["name"] = "Microsoft.ApplicationInsights.Event",
                ["time"] = DateTime.UtcNow.ToString("o"),
                ["iKey"] = iKey,
                ["data"] = new JObject
                {
                    ["baseType"] = "EventData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["name"] = $"GraphMailCalendar_{operationId}",
                        ["properties"] = new JObject
                        {
                            ["operationId"] = operationId,
                            ["statusCode"] = statusCode.ToString(),
                            ["correlationId"] = corrId,
                            ["connector"] = "Graph Mail and Calendar",
                            ["error"] = error ?? ""
                        }
                    }
                }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, endpoint.TrimEnd('/') + "/v2/track")
            {
                Content = new StringContent(
                    new JArray(evt).ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8,
                    "application/json")
            };
            await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        }
        catch { }
    }

    #endregion
}

#region MCP Framework

public class McpServerInfo
{
    public string Name { get; set; }
    public string Version { get; set; }
    public string ProtocolVersion { get; set; }
}

public class McpToolDefinition
{
    public string Name { get; set; }
    public string Description { get; set; }
    public JObject InputSchema { get; set; }
    public Func<JObject, Task<object>> Handler { get; set; }
    public JObject Annotations { get; set; }
}

public class McpRequestHandler
{
    private readonly McpServerInfo _serverInfo;
    private readonly List<McpToolDefinition> _tools = new List<McpToolDefinition>();

    public McpRequestHandler(McpServerInfo serverInfo)
    {
        _serverInfo = serverInfo;
    }

    public McpRequestHandler AddTool(string name, string description,
        object schema, Func<JObject, Task<object>> handler, object annotations = null)
    {
        _tools.Add(new McpToolDefinition
        {
            Name = name,
            Description = description,
            InputSchema = JObject.FromObject(schema),
            Handler = handler,
            Annotations = annotations != null ? JObject.FromObject(annotations) : null
        });
        return this;
    }

    public async Task<JObject> HandleAsync(string requestBody)
    {
        var request = JObject.Parse(requestBody);
        var method = request["method"]?.ToString();
        var id = request["id"];

        if (id == null || id.Type == JTokenType.Null || method == "notifications/initialized")
        {
            return new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = null,
                ["result"] = new JObject()
            };
        }

        switch (method)
        {
            case "initialize":
                return CreateResponse(id, new JObject
                {
                    ["protocolVersion"] = _serverInfo.ProtocolVersion,
                    ["capabilities"] = new JObject
                    {
                        ["tools"] = new JObject { ["listChanged"] = false }
                    },
                    ["serverInfo"] = new JObject
                    {
                        ["name"] = _serverInfo.Name,
                        ["version"] = _serverInfo.Version
                    }
                });

            case "tools/list":
                var toolArray = new JArray();
                foreach (var tool in _tools)
                {
                    var toolObj = new JObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["inputSchema"] = tool.InputSchema
                    };
                    if (tool.Annotations != null)
                        toolObj["annotations"] = tool.Annotations;
                    toolArray.Add(toolObj);
                }
                return CreateResponse(id, new JObject { ["tools"] = toolArray });

            case "tools/call":
                var toolName = request["params"]?["name"]?.ToString();
                var args = request["params"]?["arguments"] as JObject ?? new JObject();
                var matchedTool = _tools.FirstOrDefault(t => t.Name == toolName);
                if (matchedTool == null)
                    return CreateError(id, -32602, $"Unknown tool: {toolName}");
                try
                {
                    var result = await matchedTool.Handler(args);
                    var text = result is string s ? s
                        : JToken.FromObject(result).ToString(Newtonsoft.Json.Formatting.None);
                    return CreateResponse(id, new JObject
                    {
                        ["content"] = new JArray
                        {
                            new JObject { ["type"] = "text", ["text"] = text }
                        }
                    });
                }
                catch (Exception ex)
                {
                    return CreateResponse(id, new JObject
                    {
                        ["content"] = new JArray
                        {
                            new JObject { ["type"] = "text", ["text"] = $"Error: {ex.Message}" }
                        },
                        ["isError"] = true
                    });
                }

            case "ping":
                return CreateResponse(id, new JObject());

            default:
                return CreateError(id, -32601, $"Method not found: {method}");
        }
    }

    private JObject CreateResponse(JToken id, JObject result)
    {
        return new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
    }

    private JObject CreateError(JToken id, int code, string message)
    {
        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JObject { ["code"] = code, ["message"] = message }
        };
    }
}

#endregion