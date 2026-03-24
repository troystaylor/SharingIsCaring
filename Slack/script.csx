using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 1: CONNECTOR ENTRY POINT                                          ║
// ║                                                                            ║
// ║  Slack — Power Mission Control (Hybrid MCP)                                ║
// ║                                                                            ║
// ║  Hybrid pattern: PMC v3 orchestration (scan/launch/sequence) for broad     ║
// ║  Slack API coverage plus typed tools for high-value messaging operations.  ║
// ║                                                                            ║
// ║  Slack API quirks handled:                                                 ║
// ║    - All methods are POST (even reads like conversations.list)             ║
// ║    - URL pattern: https://slack.com/api/{method.name}                      ║
// ║    - Responses: { "ok": true/false, "error": "..." }                       ║
// ║    - Pagination: cursor / next_cursor pattern                              ║
// ║    - Rate limiting: Tier-based, HTTP 429 with Retry-After header           ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    // ── Server Configuration ─────────────────────────────────────────────

    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "slack-mcp",
            Version = "1.0.0",
            Title = "Slack MCP Server",
            Description = "Power Platform custom connector for Slack with progressive API discovery and typed tools for messaging, channels, users, search, reactions, and files."
        },
        ProtocolVersion = "2025-11-25",
        Capabilities = new McpCapabilities
        {
            Tools = true,
            Resources = true,
            Prompts = true,
            Logging = true,
            Completions = true
        }
    };

    // ── Mission Control Configuration ─────────────────────────────────────
    //
    //    Slack uses a flat API: all methods are POST to https://slack.com/api/{method}
    //    The ApiProxy is customized via SlackApiProxy to handle this pattern.
    //

    private static readonly MissionControlOptions McOptions = new MissionControlOptions
    {
        ServiceName = "slack",
        BaseApiUrl = "https://slack.com/api",
        DiscoveryMode = DiscoveryMode.Static,
        BatchMode = BatchMode.Sequential,
        MaxBatchSize = 20,
        DefaultPageSize = 100,
        CacheExpiryMinutes = 10,
        MaxDiscoverResults = 5,
        SummarizeResponses = true,
        MaxBodyLength = 500,
        MaxTextLength = 1000,
    };

    // ── Capability Index ─────────────────────────────────────────────────

    private const string CAPABILITY_INDEX = @"[
        {
            ""cid"": ""chat_postMessage"",
            ""endpoint"": ""chat.postMessage"",
            ""method"": ""POST"",
            ""outcome"": ""Send a message to a channel, DM, or group conversation"",
            ""domain"": ""messaging"",
            ""requiredParams"": [""channel"", ""text""],
            ""optionalParams"": [""blocks"", ""thread_ts"", ""reply_broadcast"", ""unfurl_links"", ""unfurl_media"", ""mrkdwn"", ""metadata""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\"",\""description\"":\""Channel ID, DM ID, or MPDM ID\""},\""text\"":{\""type\"":\""string\"",\""description\"":\""Message text (supports mrkdwn)\""},\""blocks\"":{\""type\"":\""array\"",\""description\"":\""Array of Block Kit blocks\""},\""thread_ts\"":{\""type\"":\""string\"",\""description\"":\""Timestamp of parent message to reply in thread\""},\""reply_broadcast\"":{\""type\"":\""boolean\"",\""description\"":\""Also post to channel when replying in thread\""},\""unfurl_links\"":{\""type\"":\""boolean\""},\""unfurl_media\"":{\""type\"":\""boolean\""},\""mrkdwn\"":{\""type\"":\""boolean\""}},\""required\"":[\""channel\"",\""text\""]}""
        },
        {
            ""cid"": ""chat_update"",
            ""endpoint"": ""chat.update"",
            ""method"": ""POST"",
            ""outcome"": ""Update an existing message in a channel"",
            ""domain"": ""messaging"",
            ""requiredParams"": [""channel"", ""ts"", ""text""],
            ""optionalParams"": [""blocks"", ""attachments""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""ts\"":{\""type\"":\""string\"",\""description\"":\""Timestamp of message to update\""},\""text\"":{\""type\"":\""string\""},\""blocks\"":{\""type\"":\""array\""}},\""required\"":[\""channel\"",\""ts\"",\""text\""]}""
        },
        {
            ""cid"": ""chat_delete"",
            ""endpoint"": ""chat.delete"",
            ""method"": ""POST"",
            ""outcome"": ""Delete a message from a channel"",
            ""domain"": ""messaging"",
            ""requiredParams"": [""channel"", ""ts""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""ts\"":{\""type\"":\""string\"",\""description\"":\""Timestamp of message to delete\""}},\""required\"":[\""channel\"",\""ts\""]}""
        },
        {
            ""cid"": ""chat_getPermalink"",
            ""endpoint"": ""chat.getPermalink"",
            ""method"": ""POST"",
            ""outcome"": ""Get a permanent URL link to a specific message"",
            ""domain"": ""messaging"",
            ""requiredParams"": [""channel"", ""message_ts""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""message_ts\"":{\""type\"":\""string\""}},\""required\"":[\""channel\"",\""message_ts\""]}""
        },
        {
            ""cid"": ""chat_scheduleMessage"",
            ""endpoint"": ""chat.scheduleMessage"",
            ""method"": ""POST"",
            ""outcome"": ""Schedule a message for future delivery to a channel"",
            ""domain"": ""messaging"",
            ""requiredParams"": [""channel"", ""text"", ""post_at""],
            ""optionalParams"": [""blocks"", ""thread_ts""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""text\"":{\""type\"":\""string\""},\""post_at\"":{\""type\"":\""integer\"",\""description\"":\""Unix timestamp for when to send\""},\""blocks\"":{\""type\"":\""array\""},\""thread_ts\"":{\""type\"":\""string\""}},\""required\"":[\""channel\"",\""text\"",\""post_at\""]}""
        },
        {
            ""cid"": ""chat_postEphemeral"",
            ""endpoint"": ""chat.postEphemeral"",
            ""method"": ""POST"",
            ""outcome"": ""Send an ephemeral message visible only to a specific user in a channel"",
            ""domain"": ""messaging"",
            ""requiredParams"": [""channel"", ""text"", ""user""],
            ""optionalParams"": [""blocks"", ""thread_ts""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""text\"":{\""type\"":\""string\""},\""user\"":{\""type\"":\""string\"",\""description\"":\""User ID to show ephemeral message to\""},\""blocks\"":{\""type\"":\""array\""},\""thread_ts\"":{\""type\"":\""string\""}},\""required\"":[\""channel\"",\""text\"",\""user\""]}""
        },
        {
            ""cid"": ""chat_scheduledMessages_list"",
            ""endpoint"": ""chat.scheduledMessages.list"",
            ""method"": ""POST"",
            ""outcome"": ""List all scheduled messages"",
            ""domain"": ""messaging"",
            ""requiredParams"": [],
            ""optionalParams"": [""channel"", ""cursor"", ""latest"", ""limit"", ""oldest""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""cursor\"":{\""type\"":\""string\""},\""limit\"":{\""type\"":\""integer\""}}}""
        },
        {
            ""cid"": ""conversations_list"",
            ""endpoint"": ""conversations.list"",
            ""method"": ""POST"",
            ""outcome"": ""List all channels (public, private, DM, group DM) in a workspace"",
            ""domain"": ""channels"",
            ""requiredParams"": [],
            ""optionalParams"": [""cursor"", ""exclude_archived"", ""limit"", ""types"", ""team_id""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""cursor\"":{\""type\"":\""string\""},\""exclude_archived\"":{\""type\"":\""boolean\""},\""limit\"":{\""type\"":\""integer\"",\""default\"":100},\""types\"":{\""type\"":\""string\"",\""description\"":\""Comma-separated: public_channel, private_channel, mpim, im\""},\""team_id\"":{\""type\"":\""string\""}}}""
        },
        {
            ""cid"": ""conversations_info"",
            ""endpoint"": ""conversations.info"",
            ""method"": ""POST"",
            ""outcome"": ""Get detailed information about a channel"",
            ""domain"": ""channels"",
            ""requiredParams"": [""channel""],
            ""optionalParams"": [""include_locale"", ""include_num_members""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""include_locale\"":{\""type\"":\""boolean\""},\""include_num_members\"":{\""type\"":\""boolean\""}},\""required\"":[\""channel\""]}""
        },
        {
            ""cid"": ""conversations_history"",
            ""endpoint"": ""conversations.history"",
            ""method"": ""POST"",
            ""outcome"": ""Get message history from a channel or conversation"",
            ""domain"": ""channels"",
            ""requiredParams"": [""channel""],
            ""optionalParams"": [""cursor"", ""inclusive"", ""latest"", ""limit"", ""oldest""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""cursor\"":{\""type\"":\""string\""},\""inclusive\"":{\""type\"":\""boolean\""},\""latest\"":{\""type\"":\""string\"",\""description\"":\""End of time range (Unix ts)\""},\""limit\"":{\""type\"":\""integer\"",\""default\"":100},\""oldest\"":{\""type\"":\""string\"",\""description\"":\""Start of time range (Unix ts)\""}},\""required\"":[\""channel\""]}""
        },
        {
            ""cid"": ""conversations_replies"",
            ""endpoint"": ""conversations.replies"",
            ""method"": ""POST"",
            ""outcome"": ""Get replies in a message thread"",
            ""domain"": ""channels"",
            ""requiredParams"": [""channel"", ""ts""],
            ""optionalParams"": [""cursor"", ""inclusive"", ""latest"", ""limit"", ""oldest""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""ts\"":{\""type\"":\""string\"",\""description\"":\""Timestamp of parent message\""},\""cursor\"":{\""type\"":\""string\""},\""limit\"":{\""type\"":\""integer\""}},\""required\"":[\""channel\"",\""ts\""]}""
        },
        {
            ""cid"": ""conversations_members"",
            ""endpoint"": ""conversations.members"",
            ""method"": ""POST"",
            ""outcome"": ""List members of a channel"",
            ""domain"": ""channels"",
            ""requiredParams"": [""channel""],
            ""optionalParams"": [""cursor"", ""limit""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""cursor\"":{\""type\"":\""string\""},\""limit\"":{\""type\"":\""integer\""}},\""required\"":[\""channel\""]}""
        },
        {
            ""cid"": ""conversations_create"",
            ""endpoint"": ""conversations.create"",
            ""method"": ""POST"",
            ""outcome"": ""Create a new public or private channel"",
            ""domain"": ""channels"",
            ""requiredParams"": [""name""],
            ""optionalParams"": [""is_private"", ""team_id""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""name\"":{\""type\"":\""string\"",\""description\"":\""Channel name (no spaces, lowercase)\""},\""is_private\"":{\""type\"":\""boolean\""},\""team_id\"":{\""type\"":\""string\""}},\""required\"":[\""name\""]}""
        },
        {
            ""cid"": ""conversations_archive"",
            ""endpoint"": ""conversations.archive"",
            ""method"": ""POST"",
            ""outcome"": ""Archive a channel"",
            ""domain"": ""channels"",
            ""requiredParams"": [""channel""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""}},\""required\"":[\""channel\""]}""
        },
        {
            ""cid"": ""conversations_unarchive"",
            ""endpoint"": ""conversations.unarchive"",
            ""method"": ""POST"",
            ""outcome"": ""Unarchive a channel"",
            ""domain"": ""channels"",
            ""requiredParams"": [""channel""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""}},\""required\"":[\""channel\""]}""
        },
        {
            ""cid"": ""conversations_join"",
            ""endpoint"": ""conversations.join"",
            ""method"": ""POST"",
            ""outcome"": ""Join an existing public channel"",
            ""domain"": ""channels"",
            ""requiredParams"": [""channel""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""}},\""required\"":[\""channel\""]}""
        },
        {
            ""cid"": ""conversations_leave"",
            ""endpoint"": ""conversations.leave"",
            ""method"": ""POST"",
            ""outcome"": ""Leave a channel"",
            ""domain"": ""channels"",
            ""requiredParams"": [""channel""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""}},\""required\"":[\""channel\""]}""
        },
        {
            ""cid"": ""conversations_invite"",
            ""endpoint"": ""conversations.invite"",
            ""method"": ""POST"",
            ""outcome"": ""Invite users to a channel"",
            ""domain"": ""channels"",
            ""requiredParams"": [""channel"", ""users""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""users\"":{\""type\"":\""string\"",\""description\"":\""Comma-separated list of user IDs\""}},\""required\"":[\""channel\"",\""users\""]}""
        },
        {
            ""cid"": ""conversations_kick"",
            ""endpoint"": ""conversations.kick"",
            ""method"": ""POST"",
            ""outcome"": ""Remove a user from a channel"",
            ""domain"": ""channels"",
            ""requiredParams"": [""channel"", ""user""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""user\"":{\""type\"":\""string\""}},\""required\"":[\""channel\"",\""user\""]}""
        },
        {
            ""cid"": ""conversations_rename"",
            ""endpoint"": ""conversations.rename"",
            ""method"": ""POST"",
            ""outcome"": ""Rename a channel"",
            ""domain"": ""channels"",
            ""requiredParams"": [""channel"", ""name""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""name\"":{\""type\"":\""string\""}},\""required\"":[\""channel\"",\""name\""]}""
        },
        {
            ""cid"": ""conversations_setPurpose"",
            ""endpoint"": ""conversations.setPurpose"",
            ""method"": ""POST"",
            ""outcome"": ""Set the purpose/description of a channel"",
            ""domain"": ""channels"",
            ""requiredParams"": [""channel"", ""purpose""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""purpose\"":{\""type\"":\""string\""}},\""required\"":[\""channel\"",\""purpose\""]}""
        },
        {
            ""cid"": ""conversations_setTopic"",
            ""endpoint"": ""conversations.setTopic"",
            ""method"": ""POST"",
            ""outcome"": ""Set the topic of a channel"",
            ""domain"": ""channels"",
            ""requiredParams"": [""channel"", ""topic""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""topic\"":{\""type\"":\""string\""}},\""required\"":[\""channel\"",\""topic\""]}""
        },
        {
            ""cid"": ""conversations_open"",
            ""endpoint"": ""conversations.open"",
            ""method"": ""POST"",
            ""outcome"": ""Open or resume a direct message or multi-person DM"",
            ""domain"": ""channels"",
            ""requiredParams"": [],
            ""optionalParams"": [""channel"", ""users"", ""return_im""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""users\"":{\""type\"":\""string\"",\""description\"":\""Comma-separated user IDs to open DM with\""},\""return_im\"":{\""type\"":\""boolean\""}}}""
        },
        {
            ""cid"": ""conversations_close"",
            ""endpoint"": ""conversations.close"",
            ""method"": ""POST"",
            ""outcome"": ""Close a direct message or multi-person DM"",
            ""domain"": ""channels"",
            ""requiredParams"": [""channel""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""}},\""required\"":[\""channel\""]}""
        },
        {
            ""cid"": ""conversations_mark"",
            ""endpoint"": ""conversations.mark"",
            ""method"": ""POST"",
            ""outcome"": ""Set the read cursor position in a channel"",
            ""domain"": ""channels"",
            ""requiredParams"": [""channel"", ""ts""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""ts\"":{\""type\"":\""string\"",\""description\"":\""Timestamp to mark as read\""}},\""required\"":[\""channel\"",\""ts\""]}""
        },
        {
            ""cid"": ""users_list"",
            ""endpoint"": ""users.list"",
            ""method"": ""POST"",
            ""outcome"": ""List all users in the workspace"",
            ""domain"": ""users"",
            ""requiredParams"": [],
            ""optionalParams"": [""cursor"", ""include_locale"", ""limit""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""cursor\"":{\""type\"":\""string\""},\""include_locale\"":{\""type\"":\""boolean\""},\""limit\"":{\""type\"":\""integer\"",\""default\"":100}}}""
        },
        {
            ""cid"": ""users_info"",
            ""endpoint"": ""users.info"",
            ""method"": ""POST"",
            ""outcome"": ""Get detailed profile information about a user"",
            ""domain"": ""users"",
            ""requiredParams"": [""user""],
            ""optionalParams"": [""include_locale""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""user\"":{\""type\"":\""string\"",\""description\"":\""User ID\""},\""include_locale\"":{\""type\"":\""boolean\""}},\""required\"":[\""user\""]}""
        },
        {
            ""cid"": ""users_lookupByEmail"",
            ""endpoint"": ""users.lookupByEmail"",
            ""method"": ""POST"",
            ""outcome"": ""Find a user by their email address"",
            ""domain"": ""users"",
            ""requiredParams"": [""email""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""email\"":{\""type\"":\""string\"",\""format\"":\""email\""}},\""required\"":[\""email\""]}""
        },
        {
            ""cid"": ""users_getPresence"",
            ""endpoint"": ""users.getPresence"",
            ""method"": ""POST"",
            ""outcome"": ""Get a user's current presence status (active or away)"",
            ""domain"": ""users"",
            ""requiredParams"": [""user""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""user\"":{\""type\"":\""string\""}},\""required\"":[\""user\""]}""
        },
        {
            ""cid"": ""users_setPresence"",
            ""endpoint"": ""users.setPresence"",
            ""method"": ""POST"",
            ""outcome"": ""Manually set user presence to auto or away"",
            ""domain"": ""users"",
            ""requiredParams"": [""presence""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""presence\"":{\""type\"":\""string\"",\""enum\"":[\""auto\"",\""away\""],\""description\"":\""Set to auto or away\""}},\""required\"":[\""presence\""]}""
        },
        {
            ""cid"": ""users_profile_get"",
            ""endpoint"": ""users.profile.get"",
            ""method"": ""POST"",
            ""outcome"": ""Get a user's profile information including custom status"",
            ""domain"": ""users"",
            ""requiredParams"": [],
            ""optionalParams"": [""user"", ""include_labels""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""user\"":{\""type\"":\""string\""},\""include_labels\"":{\""type\"":\""boolean\""}}}""
        },
        {
            ""cid"": ""users_profile_set"",
            ""endpoint"": ""users.profile.set"",
            ""method"": ""POST"",
            ""outcome"": ""Set a user's profile information including custom status"",
            ""domain"": ""users"",
            ""requiredParams"": [],
            ""optionalParams"": [""name"", ""value"", ""profile""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""name\"":{\""type\"":\""string\"",\""description\"":\""Profile field name\""},\""value\"":{\""type\"":\""string\"",\""description\"":\""Profile field value\""},\""profile\"":{\""type\"":\""object\"",\""description\"":\""Profile object with fields to set\""}}}""
        },
        {
            ""cid"": ""users_conversations"",
            ""endpoint"": ""users.conversations"",
            ""method"": ""POST"",
            ""outcome"": ""List channels the calling user is a member of"",
            ""domain"": ""users"",
            ""requiredParams"": [],
            ""optionalParams"": [""cursor"", ""exclude_archived"", ""limit"", ""types"", ""user""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""cursor\"":{\""type\"":\""string\""},\""exclude_archived\"":{\""type\"":\""boolean\""},\""limit\"":{\""type\"":\""integer\""},\""types\"":{\""type\"":\""string\""},\""user\"":{\""type\"":\""string\""}}}""
        },
        {
            ""cid"": ""reactions_add"",
            ""endpoint"": ""reactions.add"",
            ""method"": ""POST"",
            ""outcome"": ""Add an emoji reaction to a message"",
            ""domain"": ""reactions"",
            ""requiredParams"": [""channel"", ""name"", ""timestamp""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""name\"":{\""type\"":\""string\"",\""description\"":\""Emoji name without colons (e.g. thumbsup)\""},\""timestamp\"":{\""type\"":\""string\"",\""description\"":\""Message timestamp\""}},\""required\"":[\""channel\"",\""name\"",\""timestamp\""]}""
        },
        {
            ""cid"": ""reactions_get"",
            ""endpoint"": ""reactions.get"",
            ""method"": ""POST"",
            ""outcome"": ""Get all reactions for a specific message"",
            ""domain"": ""reactions"",
            ""requiredParams"": [""channel"", ""timestamp""],
            ""optionalParams"": [""full""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""timestamp\"":{\""type\"":\""string\""},\""full\"":{\""type\"":\""boolean\""}},\""required\"":[\""channel\"",\""timestamp\""]}""
        },
        {
            ""cid"": ""reactions_list"",
            ""endpoint"": ""reactions.list"",
            ""method"": ""POST"",
            ""outcome"": ""List all reactions made by a user"",
            ""domain"": ""reactions"",
            ""requiredParams"": [],
            ""optionalParams"": [""cursor"", ""full"", ""limit"", ""user""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""cursor\"":{\""type\"":\""string\""},\""full\"":{\""type\"":\""boolean\""},\""limit\"":{\""type\"":\""integer\""},\""user\"":{\""type\"":\""string\""}}}""
        },
        {
            ""cid"": ""reactions_remove"",
            ""endpoint"": ""reactions.remove"",
            ""method"": ""POST"",
            ""outcome"": ""Remove an emoji reaction from a message"",
            ""domain"": ""reactions"",
            ""requiredParams"": [""channel"", ""name"", ""timestamp""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""name\"":{\""type\"":\""string\""},\""timestamp\"":{\""type\"":\""string\""}},\""required\"":[\""channel\"",\""name\"",\""timestamp\""]}""
        },
        {
            ""cid"": ""pins_add"",
            ""endpoint"": ""pins.add"",
            ""method"": ""POST"",
            ""outcome"": ""Pin a message to a channel"",
            ""domain"": ""pins"",
            ""requiredParams"": [""channel"", ""timestamp""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""timestamp\"":{\""type\"":\""string\""}},\""required\"":[\""channel\"",\""timestamp\""]}""
        },
        {
            ""cid"": ""pins_list"",
            ""endpoint"": ""pins.list"",
            ""method"": ""POST"",
            ""outcome"": ""List all pinned items in a channel"",
            ""domain"": ""pins"",
            ""requiredParams"": [""channel""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""}},\""required\"":[\""channel\""]}""
        },
        {
            ""cid"": ""pins_remove"",
            ""endpoint"": ""pins.remove"",
            ""method"": ""POST"",
            ""outcome"": ""Remove a pinned item from a channel"",
            ""domain"": ""pins"",
            ""requiredParams"": [""channel"", ""timestamp""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""timestamp\"":{\""type\"":\""string\""}},\""required\"":[\""channel\"",\""timestamp\""]}""
        },
        {
            ""cid"": ""files_list"",
            ""endpoint"": ""files.list"",
            ""method"": ""POST"",
            ""outcome"": ""List files shared in a workspace, channel, or by a user"",
            ""domain"": ""files"",
            ""requiredParams"": [],
            ""optionalParams"": [""channel"", ""count"", ""page"", ""ts_from"", ""ts_to"", ""types"", ""user""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""count\"":{\""type\"":\""integer\""},\""page\"":{\""type\"":\""integer\""},\""ts_from\"":{\""type\"":\""string\""},\""ts_to\"":{\""type\"":\""string\""},\""types\"":{\""type\"":\""string\"",\""description\"":\""Filter by type: all, spaces, snippets, images, gdocs, zips, pdfs\""},\""user\"":{\""type\"":\""string\""}}}""
        },
        {
            ""cid"": ""files_info"",
            ""endpoint"": ""files.info"",
            ""method"": ""POST"",
            ""outcome"": ""Get information about a file"",
            ""domain"": ""files"",
            ""requiredParams"": [""file""],
            ""optionalParams"": [""count"", ""cursor"", ""limit"", ""page""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""file\"":{\""type\"":\""string\"",\""description\"":\""File ID\""}},\""required\"":[\""file\""]}""
        },
        {
            ""cid"": ""files_delete"",
            ""endpoint"": ""files.delete"",
            ""method"": ""POST"",
            ""outcome"": ""Delete a file"",
            ""domain"": ""files"",
            ""requiredParams"": [""file""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""file\"":{\""type\"":\""string\""}},\""required\"":[\""file\""]}""
        },
        {
            ""cid"": ""files_upload"",
            ""endpoint"": ""files.upload"",
            ""method"": ""POST"",
            ""outcome"": ""Upload or create a file and share it to channels (deprecated — use files.getUploadURLExternal + files.completeUploadExternal for new integrations)"",
            ""domain"": ""files"",
            ""requiredParams"": [],
            ""optionalParams"": [""channels"", ""content"", ""filename"", ""filetype"", ""initial_comment"", ""title"", ""thread_ts""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channels\"":{\""type\"":\""string\"",\""description\"":\""Comma-separated channel IDs to share to\""},\""content\"":{\""type\"":\""string\"",\""description\"":\""File content (for text-based files)\""},\""filename\"":{\""type\"":\""string\""},\""filetype\"":{\""type\"":\""string\""},\""initial_comment\"":{\""type\"":\""string\""},\""title\"":{\""type\"":\""string\""},\""thread_ts\"":{\""type\"":\""string\""}}}""
        },
        {
            ""cid"": ""files_getUploadURLExternal"",
            ""endpoint"": ""files.getUploadURLExternal"",
            ""method"": ""POST"",
            ""outcome"": ""Get an upload URL for a new file (step 1 of v2 upload flow)"",
            ""domain"": ""files"",
            ""requiredParams"": [""filename"", ""length""],
            ""optionalParams"": [""alt_txt"", ""snippet_type""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""filename\"":{\""type\"":\""string\"",\""description\"":\""Name of the file\""},\""length\"":{\""type\"":\""integer\"",\""description\"":\""File size in bytes\""},\""alt_txt\"":{\""type\"":\""string\""},\""snippet_type\"":{\""type\"":\""string\""}},\""required\"":[\""filename\"",\""length\""]}""
        },
        {
            ""cid"": ""files_completeUploadExternal"",
            ""endpoint"": ""files.completeUploadExternal"",
            ""method"": ""POST"",
            ""outcome"": ""Complete a file upload and share to channels (step 2 of v2 upload flow)"",
            ""domain"": ""files"",
            ""requiredParams"": [""files""],
            ""optionalParams"": [""channel_id"", ""initial_comment"", ""thread_ts""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""files\"":{\""type\"":\""array\"",\""description\"":\""Array of file objects with id and title\""},\""channel_id\"":{\""type\"":\""string\"",\""description\"":\""Channel to share to\""},\""initial_comment\"":{\""type\"":\""string\""},\""thread_ts\"":{\""type\"":\""string\""}},\""required\"":[\""files\""]}""
        },
        {
            ""cid"": ""search_all"",
            ""endpoint"": ""search.all"",
            ""method"": ""POST"",
            ""outcome"": ""Search for messages and files matching a query"",
            ""domain"": ""search"",
            ""requiredParams"": [""query""],
            ""optionalParams"": [""count"", ""highlight"", ""page"", ""sort"", ""sort_dir""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""query\"":{\""type\"":\""string\"",\""description\"":\""Search query text\""},\""count\"":{\""type\"":\""integer\""},\""highlight\"":{\""type\"":\""boolean\""},\""page\"":{\""type\"":\""integer\""},\""sort\"":{\""type\"":\""string\"",\""enum\"":[\""score\"",\""timestamp\""]},\""sort_dir\"":{\""type\"":\""string\"",\""enum\"":[\""asc\"",\""desc\""]}},\""required\"":[\""query\""]}""
        },
        {
            ""cid"": ""search_messages"",
            ""endpoint"": ""search.messages"",
            ""method"": ""POST"",
            ""outcome"": ""Search for messages matching a query"",
            ""domain"": ""search"",
            ""requiredParams"": [""query""],
            ""optionalParams"": [""count"", ""highlight"", ""page"", ""sort"", ""sort_dir""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""query\"":{\""type\"":\""string\""},\""count\"":{\""type\"":\""integer\""},\""highlight\"":{\""type\"":\""boolean\""},\""page\"":{\""type\"":\""integer\""},\""sort\"":{\""type\"":\""string\"",\""enum\"":[\""score\"",\""timestamp\""]},\""sort_dir\"":{\""type\"":\""string\"",\""enum\"":[\""asc\"",\""desc\""]}},\""required\"":[\""query\""]}""
        },
        {
            ""cid"": ""search_files"",
            ""endpoint"": ""search.files"",
            ""method"": ""POST"",
            ""outcome"": ""Search for files matching a query"",
            ""domain"": ""search"",
            ""requiredParams"": [""query""],
            ""optionalParams"": [""count"", ""highlight"", ""page"", ""sort"", ""sort_dir""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""query\"":{\""type\"":\""string\""},\""count\"":{\""type\"":\""integer\""},\""highlight\"":{\""type\"":\""boolean\""},\""page\"":{\""type\"":\""integer\""},\""sort\"":{\""type\"":\""string\"",\""enum\"":[\""score\"",\""timestamp\""]},\""sort_dir\"":{\""type\"":\""string\"",\""enum\"":[\""asc\"",\""desc\""]}},\""required\"":[\""query\""]}""
        },
        {
            ""cid"": ""bookmarks_add"",
            ""endpoint"": ""bookmarks.add"",
            ""method"": ""POST"",
            ""outcome"": ""Add a bookmark to a channel"",
            ""domain"": ""bookmarks"",
            ""requiredParams"": [""channel_id"", ""title"", ""type""],
            ""optionalParams"": [""emoji"", ""link""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel_id\"":{\""type\"":\""string\""},\""title\"":{\""type\"":\""string\""},\""type\"":{\""type\"":\""string\"",\""enum\"":[\""link\""]},\""emoji\"":{\""type\"":\""string\""},\""link\"":{\""type\"":\""string\""}},\""required\"":[\""channel_id\"",\""title\"",\""type\""]}""
        },
        {
            ""cid"": ""bookmarks_list"",
            ""endpoint"": ""bookmarks.list"",
            ""method"": ""POST"",
            ""outcome"": ""List bookmarks for a channel"",
            ""domain"": ""bookmarks"",
            ""requiredParams"": [""channel_id""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel_id\"":{\""type\"":\""string\""}},\""required\"":[\""channel_id\""]}""
        },
        {
            ""cid"": ""bookmarks_remove"",
            ""endpoint"": ""bookmarks.remove"",
            ""method"": ""POST"",
            ""outcome"": ""Remove a bookmark from a channel"",
            ""domain"": ""bookmarks"",
            ""requiredParams"": [""bookmark_id"", ""channel_id""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""bookmark_id\"":{\""type\"":\""string\""},\""channel_id\"":{\""type\"":\""string\""}},\""required\"":[\""bookmark_id\"",\""channel_id\""]}""
        },
        {
            ""cid"": ""reminders_add"",
            ""endpoint"": ""reminders.add"",
            ""method"": ""POST"",
            ""outcome"": ""Create a reminder for a user"",
            ""domain"": ""reminders"",
            ""requiredParams"": [""text"", ""time""],
            ""optionalParams"": [""user""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""text\"":{\""type\"":\""string\"",\""description\"":\""Reminder text\""},\""time\"":{\""type\"":\""string\"",\""description\"":\""When to remind (Unix timestamp or natural language like in 15 minutes)\""},\""user\"":{\""type\"":\""string\""}},\""required\"":[\""text\"",\""time\""]}""
        },
        {
            ""cid"": ""reminders_list"",
            ""endpoint"": ""reminders.list"",
            ""method"": ""POST"",
            ""outcome"": ""List all reminders for the authenticated user"",
            ""domain"": ""reminders"",
            ""requiredParams"": [],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{}}""
        },
        {
            ""cid"": ""reminders_complete"",
            ""endpoint"": ""reminders.complete"",
            ""method"": ""POST"",
            ""outcome"": ""Mark a reminder as complete"",
            ""domain"": ""reminders"",
            ""requiredParams"": [""reminder""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""reminder\"":{\""type\"":\""string\"",\""description\"":\""Reminder ID\""}},\""required\"":[\""reminder\""]}""
        },
        {
            ""cid"": ""reminders_delete"",
            ""endpoint"": ""reminders.delete"",
            ""method"": ""POST"",
            ""outcome"": ""Delete a reminder"",
            ""domain"": ""reminders"",
            ""requiredParams"": [""reminder""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""reminder\"":{\""type\"":\""string\""}},\""required\"":[\""reminder\""]}""
        },
        {
            ""cid"": ""emoji_list"",
            ""endpoint"": ""emoji.list"",
            ""method"": ""POST"",
            ""outcome"": ""List custom emoji for the workspace"",
            ""domain"": ""emoji"",
            ""requiredParams"": [],
            ""optionalParams"": [""include_categories""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""include_categories\"":{\""type\"":\""boolean\""}}}""
        },
        {
            ""cid"": ""dnd_info"",
            ""endpoint"": ""dnd.info"",
            ""method"": ""POST"",
            ""outcome"": ""Get a user's Do Not Disturb status"",
            ""domain"": ""dnd"",
            ""requiredParams"": [],
            ""optionalParams"": [""user""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""user\"":{\""type\"":\""string\""}}}""
        },
        {
            ""cid"": ""dnd_setSnooze"",
            ""endpoint"": ""dnd.setSnooze"",
            ""method"": ""POST"",
            ""outcome"": ""Turn on Do Not Disturb mode for a specified duration"",
            ""domain"": ""dnd"",
            ""requiredParams"": [""num_minutes""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""num_minutes\"":{\""type\"":\""integer\"",\""description\"":\""Number of minutes to snooze\""}},\""required\"":[\""num_minutes\""]}""
        },
        {
            ""cid"": ""dnd_endSnooze"",
            ""endpoint"": ""dnd.endSnooze"",
            ""method"": ""POST"",
            ""outcome"": ""End the current Do Not Disturb snooze session"",
            ""domain"": ""dnd"",
            ""requiredParams"": [],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{}}""
        },
        {
            ""cid"": ""stars_add"",
            ""endpoint"": ""stars.add"",
            ""method"": ""POST"",
            ""outcome"": ""Save an item for later (star a message, file, or channel)"",
            ""domain"": ""stars"",
            ""requiredParams"": [],
            ""optionalParams"": [""channel"", ""file"", ""timestamp""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""file\"":{\""type\"":\""string\""},\""timestamp\"":{\""type\"":\""string\""}}}""
        },
        {
            ""cid"": ""stars_list"",
            ""endpoint"": ""stars.list"",
            ""method"": ""POST"",
            ""outcome"": ""List items saved for later (starred items)"",
            ""domain"": ""stars"",
            ""requiredParams"": [],
            ""optionalParams"": [""count"", ""cursor"", ""limit"", ""page""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""count\"":{\""type\"":\""integer\""},\""cursor\"":{\""type\"":\""string\""},\""limit\"":{\""type\"":\""integer\""},\""page\"":{\""type\"":\""integer\""}}}""
        },
        {
            ""cid"": ""stars_remove"",
            ""endpoint"": ""stars.remove"",
            ""method"": ""POST"",
            ""outcome"": ""Remove a saved item (unstar)"",
            ""domain"": ""stars"",
            ""requiredParams"": [],
            ""optionalParams"": [""channel"", ""file"", ""timestamp""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""channel\"":{\""type\"":\""string\""},\""file\"":{\""type\"":\""string\""},\""timestamp\"":{\""type\"":\""string\""}}}""
        },
        {
            ""cid"": ""usergroups_list"",
            ""endpoint"": ""usergroups.list"",
            ""method"": ""POST"",
            ""outcome"": ""List all user groups in the workspace"",
            ""domain"": ""usergroups"",
            ""requiredParams"": [],
            ""optionalParams"": [""include_count"", ""include_disabled"", ""include_users""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""include_count\"":{\""type\"":\""boolean\""},\""include_disabled\"":{\""type\"":\""boolean\""},\""include_users\"":{\""type\"":\""boolean\""}}}""
        },
        {
            ""cid"": ""usergroups_create"",
            ""endpoint"": ""usergroups.create"",
            ""method"": ""POST"",
            ""outcome"": ""Create a new user group"",
            ""domain"": ""usergroups"",
            ""requiredParams"": [""name""],
            ""optionalParams"": [""channels"", ""description"", ""handle"", ""include_count""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""name\"":{\""type\"":\""string\""},\""channels\"":{\""type\"":\""string\""},\""description\"":{\""type\"":\""string\""},\""handle\"":{\""type\"":\""string\""}},\""required\"":[\""name\""]}""
        },
        {
            ""cid"": ""usergroups_users_list"",
            ""endpoint"": ""usergroups.users.list"",
            ""method"": ""POST"",
            ""outcome"": ""List all users in a user group"",
            ""domain"": ""usergroups"",
            ""requiredParams"": [""usergroup""],
            ""optionalParams"": [""include_disabled""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""usergroup\"":{\""type\"":\""string\"",\""description\"":\""User group ID\""},\""include_disabled\"":{\""type\"":\""boolean\""}},\""required\"":[\""usergroup\""]}""
        },
        {
            ""cid"": ""team_info"",
            ""endpoint"": ""team.info"",
            ""method"": ""POST"",
            ""outcome"": ""Get information about the workspace"",
            ""domain"": ""team"",
            ""requiredParams"": [],
            ""optionalParams"": [""team""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""team\"":{\""type\"":\""string\""}}}""
        },
        {
            ""cid"": ""auth_test"",
            ""endpoint"": ""auth.test"",
            ""method"": ""POST"",
            ""outcome"": ""Test authentication and get identity information"",
            ""domain"": ""auth"",
            ""requiredParams"": [],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{}}""
        }
    ]";

    // ── Entry Point ──────────────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        // Route REST (Swagger) operations — usable from Power Automate / Power Apps
        switch (this.Context.OperationId)
        {
            case "SendMessage":        return await HandleRestOperationAsync("chat.postMessage");
            case "SearchMessages":     return await HandleRestOperationAsync("search.messages");
            case "ListChannels":       return await HandleRestOperationAsync("conversations.list");
            case "GetChannelHistory":  return await HandleRestOperationAsync("conversations.history");
            case "GetUserInfo":        return await HandleRestOperationAsync("users.info");
            case "ListUsers":          return await HandleRestOperationAsync("users.list");
            case "AddReaction":        return await HandleRestOperationAsync("reactions.add");
            case "UploadFile":         return await HandleRestOperationAsync("files.upload");
        }

        // MCP handler (InvokeMCP) — Copilot Studio
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        // 1. Create the handler
        var handler = new McpRequestHandler(Options);

        // 2. Register mission control tools (scan_slack, launch_slack, sequence_slack)
        //    Uses SlackMissionControl which overrides the proxy for Slack's API pattern
        SlackMissionControl.RegisterMission(handler, McOptions, CAPABILITY_INDEX, this);

        // 3. Register typed tools for high-value operations
        RegisterTypedTools(handler);

        // 4. Wire up logging
        handler.OnLog = (eventName, data) =>
        {
            this.Context.Logger.LogInformation($"[{correlationId}] {eventName}");
            _ = LogToAppInsights(eventName, data, correlationId);
        };

        // 5. Handle the request
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = await handler.HandleAsync(body, this.CancellationToken).ConfigureAwait(false);

        var duration = DateTime.UtcNow - startTime;
        this.Context.Logger.LogInformation($"[{correlationId}] Completed in {duration.TotalMilliseconds}ms");

        // 6. Return the response
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result, Encoding.UTF8, "application/json")
        };
    }

    // ── REST Operation Handler ────────────────────────────────────────────
    //
    //    Generic handler for Swagger REST operations. Reads the JSON body,
    //    calls the Slack API method, and returns the result.
    //

    private async Task<HttpResponseMessage> HandleRestOperationAsync(string slackMethod)
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var requestBody = !string.IsNullOrWhiteSpace(body) ? JObject.Parse(body) : new JObject();
        var result = await CallSlackApiAsync(slackMethod, requestBody).ConfigureAwait(false);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    // ── Typed Tools ──────────────────────────────────────────────────────
    //
    //    High-value operations exposed as explicit MCP tools for faster
    //    invocation without needing scan first.
    //

    private void RegisterTypedTools(McpRequestHandler handler)
    {
        // ── send_message ─────────────────────────────────────────────────
        handler.AddTool("send_message",
            "Send a message to a Slack channel or conversation. Use this when the user wants to post, send, or write a message.",
            schema: s => s
                .String("channel", "Channel ID, DM ID, or conversation ID to send the message to", required: true)
                .String("text", "Message text (supports Slack mrkdwn formatting)", required: true)
                .String("thread_ts", "Timestamp of parent message to reply in a thread (optional)", required: false)
                .Boolean("reply_broadcast", "Also post to the channel when replying in a thread", required: false)
                .Boolean("unfurl_links", "Enable link previews", required: false),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["channel"] = RequireArgument(args, "channel"),
                    ["text"] = RequireArgument(args, "text")
                };
                CopyOptional(args, body, "thread_ts", "reply_broadcast", "unfurl_links");
                return await CallSlackApiAsync("chat.postMessage", body).ConfigureAwait(false);
            });

        // ── search_messages ──────────────────────────────────────────────
        handler.AddTool("search_messages",
            "Search for messages across the Slack workspace. Use this when the user wants to find, look up, or search for messages.",
            schema: s => s
                .String("query", "Search query text", required: true)
                .Integer("count", "Number of results per page (default 20)", required: false, defaultValue: 20)
                .String("sort", "Sort by: score or timestamp", required: false, enumValues: new[] { "score", "timestamp" })
                .String("sort_dir", "Sort direction: asc or desc", required: false, enumValues: new[] { "asc", "desc" }),
            handler: async (args, ct) =>
            {
                var body = new JObject { ["query"] = RequireArgument(args, "query") };
                CopyOptional(args, body, "count", "sort", "sort_dir");
                return await CallSlackApiAsync("search.messages", body).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; });

        // ── list_channels ────────────────────────────────────────────────
        handler.AddTool("list_channels",
            "List channels in the Slack workspace. Use this when the user wants to see, browse, or find channels.",
            schema: s => s
                .Boolean("exclude_archived", "Exclude archived channels (default true)", required: false)
                .String("types", "Comma-separated channel types: public_channel, private_channel, mpim, im", required: false)
                .Integer("limit", "Maximum channels to return (default 100, max 1000)", required: false, defaultValue: 100)
                .String("cursor", "Pagination cursor for next page", required: false),
            handler: async (args, ct) =>
            {
                var body = new JObject();
                if (args["exclude_archived"] == null) body["exclude_archived"] = true;
                CopyOptional(args, body, "exclude_archived", "types", "limit", "cursor");
                return await CallSlackApiAsync("conversations.list", body).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── get_channel_history ──────────────────────────────────────────
        handler.AddTool("get_channel_history",
            "Get recent messages from a Slack channel or conversation. Use this when the user wants to read, view, or check messages in a channel.",
            schema: s => s
                .String("channel", "Channel ID to get history from", required: true)
                .Integer("limit", "Number of messages to return (default 25, max 1000)", required: false, defaultValue: 25)
                .String("oldest", "Start of time range (Unix timestamp)", required: false)
                .String("latest", "End of time range (Unix timestamp)", required: false)
                .String("cursor", "Pagination cursor for next page", required: false),
            handler: async (args, ct) =>
            {
                var body = new JObject { ["channel"] = RequireArgument(args, "channel") };
                CopyOptional(args, body, "limit", "oldest", "latest", "cursor");
                return await CallSlackApiAsync("conversations.history", body).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; });

        // ── get_user_info ────────────────────────────────────────────────
        handler.AddTool("get_user_info",
            "Get detailed profile information about a Slack user. Use this when the user asks about someone's profile, status, or contact info.",
            schema: s => s
                .String("user", "User ID (e.g., U0123456789)", required: true)
                .Boolean("include_locale", "Include locale information", required: false),
            handler: async (args, ct) =>
            {
                var body = new JObject { ["user"] = RequireArgument(args, "user") };
                CopyOptional(args, body, "include_locale");
                return await CallSlackApiAsync("users.info", body).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── list_users ───────────────────────────────────────────────────
        handler.AddTool("list_users",
            "List all users in the Slack workspace. Use this when the user wants to see team members or find people.",
            schema: s => s
                .Integer("limit", "Maximum users to return (default 100)", required: false, defaultValue: 100)
                .String("cursor", "Pagination cursor for next page", required: false)
                .Boolean("include_locale", "Include locale information", required: false),
            handler: async (args, ct) =>
            {
                var body = new JObject();
                CopyOptional(args, body, "limit", "cursor", "include_locale");
                return await CallSlackApiAsync("users.list", body).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── add_reaction ─────────────────────────────────────────────────
        handler.AddTool("add_reaction",
            "Add an emoji reaction to a message. Use this when the user wants to react to or emoji a message.",
            schema: s => s
                .String("channel", "Channel ID containing the message", required: true)
                .String("name", "Emoji name without colons (e.g., thumbsup, heart, rocket)", required: true)
                .String("timestamp", "Timestamp of the message to react to", required: true),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["channel"] = RequireArgument(args, "channel"),
                    ["name"] = RequireArgument(args, "name"),
                    ["timestamp"] = RequireArgument(args, "timestamp")
                };
                return await CallSlackApiAsync("reactions.add", body).ConfigureAwait(false);
            });

        // ── upload_file ──────────────────────────────────────────────────
        handler.AddTool("upload_file",
            "Upload a text-based file to Slack and optionally share it to channels. Use this when the user wants to share a file, snippet, or document.",
            schema: s => s
                .String("content", "File content as text", required: true)
                .String("filename", "Name for the file (e.g., report.txt)", required: false)
                .String("filetype", "File type (e.g., text, javascript, python, csv, json)", required: false)
                .String("title", "Title for the file", required: false)
                .String("channels", "Comma-separated channel IDs to share the file to", required: false)
                .String("initial_comment", "Message text to accompany the file", required: false),
            handler: async (args, ct) =>
            {
                var body = new JObject { ["content"] = RequireArgument(args, "content") };
                CopyOptional(args, body, "filename", "filetype", "title", "channels", "initial_comment");
                return await CallSlackApiAsync("files.upload", body).ConfigureAwait(false);
            });
    }

    // ── Slack API Helper ─────────────────────────────────────────────────
    //
    //    All Slack API methods are POST to https://slack.com/api/{method}
    //    with Bearer token in Authorization header and JSON body.
    //

    private async Task<JObject> CallSlackApiAsync(string method, JObject body)
    {
        var url = $"https://slack.com/api/{method}";
        var request = new HttpRequestMessage(HttpMethod.Post, url);

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        JObject result;
        try { result = JObject.Parse(content); }
        catch { return new JObject { ["ok"] = false, ["error"] = "invalid_response", ["raw"] = content }; }

        // Slack always returns 200 with ok:true/false — check the ok field
        if (result.Value<bool?>("ok") != true)
        {
            var error = result.Value<string>("error") ?? "unknown_error";
            return new JObject
            {
                ["success"] = false,
                ["error"] = error,
                ["friendlyMessage"] = GetSlackErrorMessage(error),
                ["suggestion"] = GetSlackErrorSuggestion(error)
            };
        }

        // Check for cursor-based pagination
        var responseMetadata = result["response_metadata"] as JObject;
        var nextCursor = responseMetadata?.Value<string>("next_cursor");
        if (!string.IsNullOrWhiteSpace(nextCursor))
        {
            result["hasMore"] = true;
            result["nextCursor"] = nextCursor;
            result["nextPageHint"] = "Pass the nextCursor value as the 'cursor' parameter to get the next page.";
        }

        result["success"] = true;
        return result;
    }

    /// <summary>Copy non-null optional arguments from args to body.</summary>
    private static void CopyOptional(JObject args, JObject body, params string[] keys)
    {
        foreach (var key in keys)
        {
            var val = args?[key];
            if (val != null && val.Type != JTokenType.Null)
                body[key] = val;
        }
    }

    /// <summary>Get a required string argument; throws ArgumentException if missing.</summary>
    private static string RequireArgument(JObject args, string name)
    {
        var value = args?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{name}' is required");
        return value;
    }

    /// <summary>Get an optional string argument with a default fallback.</summary>
    private static string GetArgument(JObject args, string name, string defaultValue = null)
    {
        var value = args?[name]?.ToString();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    /// <summary>Map Slack error codes to friendly messages.</summary>
    private static string GetSlackErrorMessage(string error)
    {
        switch (error)
        {
            case "channel_not_found": return "The specified channel was not found. It may have been deleted or you may not have access.";
            case "not_in_channel": return "You are not a member of this channel. Join the channel first.";
            case "is_archived": return "This channel has been archived and cannot receive new messages.";
            case "msg_too_long": return "The message is too long. Slack messages are limited to 40,000 characters.";
            case "no_text": return "No message text was provided. Please include a message.";
            case "too_many_attachments": return "Too many attachments. Messages are limited to 100 attachments.";
            case "not_authed": return "Authentication failed. Your token may be invalid or expired.";
            case "invalid_auth": return "The authentication token is invalid.";
            case "account_inactive": return "The token belongs to a deactivated user account.";
            case "token_revoked": return "The token has been revoked. Please reconnect.";
            case "missing_scope": return "Your token does not have the required scope for this operation.";
            case "user_not_found": return "The specified user was not found.";
            case "file_not_found": return "The specified file was not found.";
            case "already_reacted": return "You have already added this reaction to this message.";
            case "too_many_reactions": return "This message has reached the maximum number of reactions.";
            case "ratelimited": return "Rate limited. Please wait and try again.";
            default: return $"Slack API error: {error}";
        }
    }

    /// <summary>Map Slack error codes to actionable suggestions.</summary>
    private static string GetSlackErrorSuggestion(string error)
    {
        switch (error)
        {
            case "channel_not_found": return "Use list_channels to find valid channel IDs.";
            case "not_in_channel": return "Use scan_slack to find conversations.join and join the channel first.";
            case "is_archived": return "Use scan_slack to find conversations.unarchive to restore the channel.";
            case "not_authed":
            case "invalid_auth":
            case "token_revoked": return "Reconnect the Slack connector in Power Platform.";
            case "missing_scope": return "The connector needs additional OAuth scopes. Check the required scopes for this operation.";
            case "user_not_found": return "Use list_users to find valid user IDs.";
            case "already_reacted": return "This reaction was already added. No action needed.";
            case "ratelimited": return "Wait a moment and try again. Slack rate limits are tier-based.";
            default: return "Check the Slack API documentation for this error.";
        }
    }

    // ── Application Insights (Optional) ──────────────────────────────────

    private async Task LogToAppInsights(string eventName, object properties, string correlationId)
    {
        try
        {
            var instrumentationKey = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var ingestionEndpoint = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint")
                ?? "https://dc.services.visualstudio.com/";

            if (string.IsNullOrEmpty(instrumentationKey))
                return;

            var propsDict = new Dictionary<string, string>
            {
                ["ServerName"] = Options.ServerInfo.Name,
                ["ServerVersion"] = Options.ServerInfo.Version,
                ["CorrelationId"] = correlationId
            };

            if (properties != null)
            {
                var propsJson = JsonConvert.SerializeObject(properties);
                var propsObj = JObject.Parse(propsJson);
                foreach (var prop in propsObj.Properties())
                    propsDict[prop.Name] = prop.Value?.ToString() ?? "";
            }

            var telemetryData = new
            {
                name = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                time = DateTime.UtcNow.ToString("o"),
                iKey = instrumentationKey,
                data = new
                {
                    baseType = "EventData",
                    baseData = new { ver = 2, name = eventName, properties = propsDict }
                }
            };

            var json = JsonConvert.SerializeObject(telemetryData);
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");

            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch { }
    }

    private static string ExtractConnectionStringPart(string connectionString, string key)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        var prefix = key + "=";
        var parts = connectionString.Split(';');
        foreach (var part in parts)
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part.Substring(prefix.Length);
        }
        return null;
    }
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 2: SLACK API PROXY                                                ║
// ║                                                                            ║
// ║  Custom API proxy for Slack's unique API pattern:                           ║
// ║  - All methods are POST to /api/{method.name}                              ║
// ║  - Responses always contain "ok": true/false                               ║
// ║  - Pagination uses cursor/next_cursor                                      ║
// ║  - Rate limiting via HTTP 429 with Retry-After header                      ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class SlackApiProxy
{
    private readonly MissionControlOptions _options;
    private const int MAX_RETRIES = 3;

    public SlackApiProxy(MissionControlOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Execute a Slack API method. The endpoint is the method name (e.g., "chat.postMessage").
    /// All Slack methods are POST with JSON body and Bearer token auth.
    /// </summary>
    public async Task<JObject> InvokeAsync(
        ScriptBase context,
        string endpoint,
        string method,
        JObject body = null,
        JObject queryParams = null,
        string apiVersion = null,
        CapabilityIndex index = null)
    {
        // Slack API: all calls are POST to /api/{method_name}
        var slackMethod = endpoint.TrimStart('/');
        var url = $"{_options.BaseApiUrl.TrimEnd('/')}/{slackMethod}";

        // Merge query params into body (Slack accepts everything in body)
        body = body ?? new JObject();
        if (queryParams != null)
        {
            foreach (var prop in queryParams.Properties())
            {
                if (body[prop.Name] == null)
                    body[prop.Name] = prop.Value;
            }
        }

        return await ExecuteWithRetryAsync(context, url, body).ConfigureAwait(false);
    }

    /// <summary>Execute multiple Slack API calls sequentially.</summary>
    public async Task<JObject> BatchInvokeAsync(
        ScriptBase context,
        JArray requests,
        string apiVersion = null,
        CapabilityIndex index = null)
    {
        if (requests == null || requests.Count == 0)
            return new JObject { ["success"] = false, ["error"] = "No requests provided" };

        if (requests.Count > _options.MaxBatchSize)
            return new JObject { ["success"] = false, ["error"] = $"Batch exceeds maximum size of {_options.MaxBatchSize}" };

        var responses = new JArray();
        int successCount = 0, errorCount = 0;

        foreach (var req in requests)
        {
            var id = req.Value<string>("id") ?? Guid.NewGuid().ToString("N").Substring(0, 8);
            var endpoint = req.Value<string>("endpoint") ?? "";
            var body = req["body"] as JObject;
            var qp = req["query_params"] as JObject;

            try
            {
                var result = await InvokeAsync(context, endpoint, "POST", body, qp).ConfigureAwait(false);
                var success = result.Value<bool?>("success") ?? false;
                if (success) successCount++; else errorCount++;

                responses.Add(new JObject
                {
                    ["id"] = id,
                    ["success"] = success,
                    ["data"] = result
                });
            }
            catch (Exception ex)
            {
                errorCount++;
                responses.Add(new JObject
                {
                    ["id"] = id,
                    ["success"] = false,
                    ["error"] = ex.Message
                });
            }
        }

        return new JObject
        {
            ["success"] = errorCount == 0,
            ["batchSize"] = requests.Count,
            ["successCount"] = successCount,
            ["errorCount"] = errorCount,
            ["responses"] = responses
        };
    }

    private async Task<JObject> ExecuteWithRetryAsync(
        ScriptBase context, string url, JObject body, int retryCount = 0)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);

        // Forward auth
        if (context.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = context.Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await context.Context.SendAsync(request, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new JObject
            {
                ["success"] = false,
                ["error"] = "connection_error",
                ["friendlyMessage"] = $"Failed to connect to Slack API: {ex.Message}",
                ["suggestion"] = "Check your network connection and try again."
            };
        }

        var statusCode = (int)response.StatusCode;
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        // 429: Rate limited — retry with backoff
        if (statusCode == 429 && retryCount < MAX_RETRIES)
        {
            var retryAfter = 5;
            if (response.Headers.TryGetValues("Retry-After", out var retryValues))
            {
                var val = retryValues.FirstOrDefault();
                if (int.TryParse(val, out var seconds))
                    retryAfter = Math.Min(seconds, 30);
            }
            await Task.Delay(retryAfter * 1000).ConfigureAwait(false);
            return await ExecuteWithRetryAsync(context, url, body, retryCount + 1).ConfigureAwait(false);
        }

        // Parse response
        JObject result;
        try { result = JObject.Parse(responseBody); }
        catch { return new JObject { ["success"] = false, ["error"] = "invalid_response", ["raw"] = responseBody }; }

        // Slack returns 200 with ok:true/false
        if (result.Value<bool?>("ok") != true)
        {
            var error = result.Value<string>("error") ?? "unknown_error";
            return new JObject
            {
                ["success"] = false,
                ["error"] = error,
                ["friendlyMessage"] = $"Slack API error: {error}",
                ["suggestion"] = "Check the parameters and try again."
            };
        }

        // Summarize response
        if (_options.SummarizeResponses)
            SummarizeToken(result, _options.MaxBodyLength, _options.MaxTextLength);

        // Handle pagination (cursor-based)
        var responseMetadata = result["response_metadata"] as JObject;
        var nextCursor = responseMetadata?.Value<string>("next_cursor");
        if (!string.IsNullOrWhiteSpace(nextCursor))
        {
            result["hasMore"] = true;
            result["nextCursor"] = nextCursor;
            result["nextPageHint"] = "Pass the nextCursor value as the 'cursor' parameter to get the next page.";
        }

        result["success"] = true;
        return result;
    }

    // ── Response Summarization ───────────────────────────────────────────

    private void SummarizeToken(JToken token, int maxBodyLength, int maxTextLength)
    {
        if (token is JObject obj)
        {
            foreach (var prop in obj.Properties().ToList())
            {
                var name = prop.Name.ToLowerInvariant();
                if ((name == "body" || name == "bodypreview" || name == "description" || name == "purpose" || name == "topic") && prop.Value.Type == JTokenType.String)
                {
                    var val = prop.Value.ToString();
                    var stripped = StripHtml(val);
                    if (stripped.Length > maxBodyLength)
                    {
                        obj[prop.Name] = stripped.Substring(0, maxBodyLength) + "...";
                        obj[prop.Name + "_truncated"] = true;
                    }
                    else
                    {
                        obj[prop.Name] = stripped;
                    }
                }
                else if (prop.Value.Type == JTokenType.String)
                {
                    var val = prop.Value.ToString();
                    if (val.Length > maxTextLength)
                    {
                        obj[prop.Name] = val.Substring(0, maxTextLength) + "...";
                        obj[prop.Name + "_truncated"] = true;
                    }
                }
                else if (prop.Value is JObject || prop.Value is JArray)
                {
                    SummarizeToken(prop.Value, maxBodyLength, maxTextLength);
                }
            }
        }
        else if (token is JArray arr)
        {
            foreach (var item in arr)
                SummarizeToken(item, maxBodyLength, maxTextLength);
        }
    }

    public static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return html ?? "";
        html = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<[^>]+>", " ");
        html = html.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<")
                    .Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&#39;", "'");
        html = Regex.Replace(html, @"\s+", " ").Trim();
        return html;
    }
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 3: SLACK MISSION CONTROL (REGISTRATION)                           ║
// ║                                                                            ║
// ║  Registers scan_slack, launch_slack, sequence_slack using the Slack-        ║
// ║  specific API proxy instead of the generic PMC proxy.                      ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public static class SlackMissionControl
{
    public static void RegisterMission(
        McpRequestHandler handler,
        MissionControlOptions options,
        string capabilityIndexJson,
        ScriptBase context)
    {
        var index = !string.IsNullOrWhiteSpace(capabilityIndexJson)
            ? new CapabilityIndex(capabilityIndexJson)
            : null;

        var proxy = new SlackApiProxy(options);
        var discovery = new SlackDiscoveryEngine(options, index);
        var serviceName = options.ServiceName ?? "slack";

        // ── scan_slack ────────────────────────────────────────────────────

        var scanDescription = $"Scan for available Slack API operations matching your intent. " +
            $"Always call this before launch_{serviceName} to find the correct method name and required parameters. " +
            $"Returns operation summaries with method names, parameters, and descriptions. " +
            $"Use include_schema=true to get full parameter details. " +
            $"Domains: messaging, channels, users, reactions, pins, files, search, bookmarks, reminders, usergroups, emoji, dnd, stars, team, auth.";

        handler.AddTool($"scan_{serviceName}", scanDescription,
            schema: s => s
                .String("query", "Natural language description of what you want to do (e.g., 'send a message', 'list channels', 'find a user by email')", required: true)
                .String("domain", "Filter by domain category: messaging, channels, users, reactions, pins, files, search, bookmarks, reminders, usergroups, emoji, dnd, stars, team, auth", required: false)
                .Boolean("include_schema", "Set true to include full input parameter schemas (costs more tokens)", required: false),
            handler: async (args, ct) =>
            {
                var query = args.Value<string>("query") ?? "";
                var domain = args.Value<string>("domain");
                var includeSchema = args.Value<bool?>("include_schema") ?? false;

                return discovery.Discover(query, domain, includeSchema);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── launch_slack ─────────────────────────────────────────────────

        var launchDescription = $"Launch a Slack API method. " +
            $"Use scan_{serviceName} first to find the correct method name. " +
            $"The endpoint is the Slack method name (e.g., 'chat.postMessage', 'conversations.list'). " +
            $"All Slack methods use POST. Pass parameters in the body object.";

        handler.AddTool($"launch_{serviceName}", launchDescription,
            schema: s => s
                .String("endpoint", "Slack API method name (e.g., 'chat.postMessage', 'conversations.list')", required: true)
                .String("method", "Always POST for Slack API", required: false, enumValues: new[] { "POST" })
                .Object("body", "Request parameters as key-value pairs", nested => { }, required: false)
                .Object("query_params", "Additional parameters (merged into body)", nested => { }, required: false),
            handler: async (args, ct) =>
            {
                var endpoint = args.Value<string>("endpoint") ?? "";
                var body = args["body"] as JObject;
                var queryParams = args["query_params"] as JObject;

                return await proxy.InvokeAsync(context, endpoint, "POST", body, queryParams, null, index).ConfigureAwait(false);
            });

        // ── sequence_slack ───────────────────────────────────────────────

        var batchDescription = $"Execute multiple Slack API operations in a single call. " +
            $"Maximum {options.MaxBatchSize} requests per sequence. " +
            $"Each request needs an endpoint (Slack method name) and body (parameters).";

        handler.AddTool($"sequence_{serviceName}", batchDescription,
            schema: s => s
                .Array("requests", "Array of Slack API requests to execute",
                    itemSchema: new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["id"] = new JObject { ["type"] = "string", ["description"] = "Unique identifier for this request" },
                            ["endpoint"] = new JObject { ["type"] = "string", ["description"] = "Slack API method name (e.g., 'chat.postMessage')" },
                            ["method"] = new JObject { ["type"] = "string", ["description"] = "Always POST for Slack" },
                            ["body"] = new JObject { ["type"] = "object", ["description"] = "Request parameters" },
                            ["query_params"] = new JObject { ["type"] = "object", ["description"] = "Additional parameters (merged into body)" }
                        },
                        ["required"] = new JArray("endpoint")
                    }, required: true),
            handler: async (args, ct) =>
            {
                var requests = args["requests"] as JArray;
                return await proxy.BatchInvokeAsync(context, requests).ConfigureAwait(false);
            });
    }
}

// ── Slack Discovery Engine (Static Only) ─────────────────────────────────────

public class SlackDiscoveryEngine
{
    private readonly MissionControlOptions _options;
    private readonly CapabilityIndex _index;

    public SlackDiscoveryEngine(MissionControlOptions options, CapabilityIndex index)
    {
        _options = options;
        _index = index;
    }

    public JObject Discover(string query, string domain, bool includeSchema)
    {
        if (_index == null)
            return new JObject { ["success"] = false, ["error"] = "No capability index configured" };

        var matches = _index.Search(query, domain, _options.MaxDiscoverResults);
        var operations = new JArray();

        foreach (var entry in matches)
        {
            var op = new JObject
            {
                ["cid"] = entry.Cid,
                ["method_name"] = entry.Endpoint,
                ["http_method"] = "POST",
                ["outcome"] = entry.Outcome,
                ["domain"] = entry.Domain,
                ["launch_hint"] = $"Use launch_slack with endpoint=\"{entry.Endpoint}\" and method=\"POST\""
            };

            if (entry.RequiredParams != null && entry.RequiredParams.Length > 0)
                op["requiredParams"] = new JArray(entry.RequiredParams);
            if (entry.OptionalParams != null && entry.OptionalParams.Length > 0)
                op["optionalParams"] = new JArray(entry.OptionalParams);

            if (includeSchema && !string.IsNullOrWhiteSpace(entry.SchemaJson))
            {
                try { op["inputSchema"] = JObject.Parse(entry.SchemaJson); }
                catch { op["inputSchema"] = entry.SchemaJson; }
            }

            operations.Add(op);
        }

        return new JObject
        {
            ["success"] = true,
            ["operationCount"] = operations.Count,
            ["totalCapabilities"] = _index.Count,
            ["operations"] = operations,
            ["usage_hint"] = "Use launch_slack with the method_name from an operation above. Pass required parameters in the body object."
        };
    }
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 4: MCP FRAMEWORK                                                  ║
// ║                                                                            ║
// ║  McpRequestHandler (MCP 2025-11-25), schema builder, error handling.       ║
// ║  Do not modify unless extending the framework itself.                      ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

// ── Configuration Types ──────────────────────────────────────────────────────

public class McpServerInfo
{
    public string Name { get; set; } = "mcp-server";
    public string Version { get; set; } = "1.0.0";
    public string Title { get; set; }
    public string Description { get; set; }
}

public class McpCapabilities
{
    public bool Tools { get; set; } = true;
    public bool Resources { get; set; }
    public bool Prompts { get; set; }
    public bool Logging { get; set; }
    public bool Completions { get; set; }
}

public class McpServerOptions
{
    public McpServerInfo ServerInfo { get; set; } = new McpServerInfo();
    public string ProtocolVersion { get; set; } = "2025-11-25";
    public McpCapabilities Capabilities { get; set; } = new McpCapabilities();
    public string Instructions { get; set; }
}

// ── Mission Control Configuration ────────────────────────────────────────────

public enum DiscoveryMode { Static, Hybrid, McpChain }
public enum BatchMode { Sequential, BatchEndpoint }

public class MissionControlOptions
{
    public string ServiceName { get; set; } = "api";
    public DiscoveryMode DiscoveryMode { get; set; } = DiscoveryMode.Static;
    public string BaseApiUrl { get; set; }
    public string DefaultApiVersion { get; set; }
    public BatchMode BatchMode { get; set; } = BatchMode.Sequential;
    public string BatchEndpointPath { get; set; } = "/$batch";
    public int MaxBatchSize { get; set; } = 20;
    public int DefaultPageSize { get; set; } = 25;
    public int CacheExpiryMinutes { get; set; } = 10;
    public int DescribeCacheTTL { get; set; } = 30;
    public int MaxDiscoverResults { get; set; } = 3;
    public bool SummarizeResponses { get; set; } = true;
    public int MaxBodyLength { get; set; } = 500;
    public int MaxTextLength { get; set; } = 1000;
    public string DescribeEndpointPattern { get; set; }
    public string McpChainEndpoint { get; set; }
    public string McpChainToolName { get; set; }
    public string McpChainQueryPrefix { get; set; }
    public Dictionary<string, Action<string, JObject>> SmartDefaults { get; set; }
}

// ── Capability Entry ─────────────────────────────────────────────────────────

public class CapabilityEntry
{
    public string Cid { get; set; }
    public string Endpoint { get; set; }
    public string Method { get; set; }
    public string Outcome { get; set; }
    public string Domain { get; set; }
    public string[] RequiredParams { get; set; }
    public string[] OptionalParams { get; set; }
    public string SchemaJson { get; set; }
}

// ── Capability Index ─────────────────────────────────────────────────────────

public class CapabilityIndex
{
    private readonly List<CapabilityEntry> _entries;

    public CapabilityIndex(string indexJson)
    {
        _entries = new List<CapabilityEntry>();
        if (string.IsNullOrWhiteSpace(indexJson)) return;

        var array = JArray.Parse(indexJson);
        foreach (var item in array)
        {
            _entries.Add(new CapabilityEntry
            {
                Cid = item.Value<string>("cid") ?? "",
                Endpoint = item.Value<string>("endpoint") ?? "",
                Method = (item.Value<string>("method") ?? "POST").ToUpperInvariant(),
                Outcome = item.Value<string>("outcome") ?? "",
                Domain = item.Value<string>("domain") ?? "",
                RequiredParams = item["requiredParams"]?.ToObject<string[]>() ?? Array.Empty<string>(),
                OptionalParams = item["optionalParams"]?.ToObject<string[]>() ?? Array.Empty<string>(),
                SchemaJson = item.Value<string>("schemaJson")
            });
        }
    }

    public List<CapabilityEntry> Search(string query, string domain = null, int maxResults = 3)
    {
        if (string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(domain))
            return _entries.Take(maxResults).ToList();

        var queryLower = (query ?? "").ToLowerInvariant();
        var queryWords = queryLower.Split(new[] { ' ', '_', '-', '/', '.' }, StringSplitOptions.RemoveEmptyEntries);
        var domainLower = (domain ?? "").ToLowerInvariant();

        var scored = new List<KeyValuePair<CapabilityEntry, int>>();

        foreach (var entry in _entries)
        {
            int score = 0;
            var cidLower = entry.Cid.ToLowerInvariant();
            var outcomeLower = entry.Outcome.ToLowerInvariant();
            var entryDomainLower = entry.Domain.ToLowerInvariant();
            var endpointLower = entry.Endpoint.ToLowerInvariant();

            if (cidLower == queryLower) score += 100;
            else if (cidLower.Contains(queryLower) || queryLower.Contains(cidLower)) score += 60;

            if (!string.IsNullOrWhiteSpace(domainLower) && entryDomainLower == domainLower) score += 50;
            else if (!string.IsNullOrWhiteSpace(domainLower) && entryDomainLower != domainLower) score -= 20;

            foreach (var word in queryWords)
            {
                if (word.Length < 2) continue;
                if (outcomeLower.Contains(word)) score += 10;
                if (cidLower.Contains(word)) score += 15;
                if (endpointLower.Contains(word)) score += 8;
            }

            if (score > 0) scored.Add(new KeyValuePair<CapabilityEntry, int>(entry, score));
        }

        return scored
            .OrderByDescending(kv => kv.Value)
            .Take(maxResults)
            .Select(kv => kv.Key)
            .ToList();
    }

    public CapabilityEntry Get(string cid)
    {
        return _entries.FirstOrDefault(e =>
            string.Equals(e.Cid, cid, StringComparison.OrdinalIgnoreCase));
    }

    public List<CapabilityEntry> GetAll() => new List<CapabilityEntry>(_entries);
    public int Count => _entries.Count;
}

// ── Error Handling ───────────────────────────────────────────────────────────

public enum McpErrorCode
{
    RequestTimeout = -32000,
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603
}

public class McpException : Exception
{
    public McpErrorCode Code { get; }
    public McpException(McpErrorCode code, string message) : base(message) => Code = code;
}

// ── Schema Builder ───────────────────────────────────────────────────────────

public class McpSchemaBuilder
{
    private readonly JObject _properties = new JObject();
    private readonly JArray _required = new JArray();

    public McpSchemaBuilder String(string name, string description, bool required = false, string format = null, string[] enumValues = null)
    {
        var prop = new JObject { ["type"] = "string", ["description"] = description };
        if (format != null) prop["format"] = format;
        if (enumValues != null) prop["enum"] = new JArray(enumValues);
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Integer(string name, string description, bool required = false, int? defaultValue = null)
    {
        var prop = new JObject { ["type"] = "integer", ["description"] = description };
        if (defaultValue.HasValue) prop["default"] = defaultValue.Value;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Number(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "number", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Boolean(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "boolean", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Array(string name, string description, JObject itemSchema, bool required = false)
    {
        _properties[name] = new JObject
        {
            ["type"] = "array",
            ["description"] = description,
            ["items"] = itemSchema
        };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Object(string name, string description, Action<McpSchemaBuilder> nestedConfig, bool required = false)
    {
        var nested = new McpSchemaBuilder();
        nestedConfig?.Invoke(nested);
        var obj = nested.Build();
        obj["description"] = description;
        _properties[name] = obj;
        if (required) _required.Add(name);
        return this;
    }

    public JObject Build()
    {
        var schema = new JObject { ["type"] = "object", ["properties"] = _properties };
        if (_required.Count > 0) schema["required"] = _required;
        return schema;
    }
}

// ── Internal Registration Classes ────────────────────────────────────────────

internal class McpToolDefinition
{
    public string Name { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public JObject InputSchema { get; set; }
    public JObject OutputSchema { get; set; }
    public JObject Annotations { get; set; }
    public Func<JObject, CancellationToken, Task<object>> Handler { get; set; }
}

internal class McpResourceDefinition
{
    public string Uri { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public JObject Annotations { get; set; }
    public Func<CancellationToken, Task<JArray>> Handler { get; set; }
}

internal class McpResourceTemplateDefinition
{
    public string UriTemplate { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public JObject Annotations { get; set; }
    public Func<string, CancellationToken, Task<JArray>> Handler { get; set; }
}

public class McpPromptArgument
{
    public string Name { get; set; }
    public string Description { get; set; }
    public bool Required { get; set; }
}

internal class McpPromptDefinition
{
    public string Name { get; set; }
    public string Description { get; set; }
    public List<McpPromptArgument> Arguments { get; set; } = new List<McpPromptArgument>();
    public Func<JObject, CancellationToken, Task<JArray>> Handler { get; set; }
}

// ── McpRequestHandler ────────────────────────────────────────────────────────

public class McpRequestHandler
{
    private readonly McpServerOptions _options;
    private readonly Dictionary<string, McpToolDefinition> _tools;
    private readonly Dictionary<string, McpResourceDefinition> _resources;
    private readonly List<McpResourceTemplateDefinition> _resourceTemplates;
    private readonly Dictionary<string, McpPromptDefinition> _prompts;

    public Action<string, object> OnLog { get; set; }

    public McpRequestHandler(McpServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = new Dictionary<string, McpToolDefinition>(StringComparer.OrdinalIgnoreCase);
        _resources = new Dictionary<string, McpResourceDefinition>(StringComparer.OrdinalIgnoreCase);
        _resourceTemplates = new List<McpResourceTemplateDefinition>();
        _prompts = new Dictionary<string, McpPromptDefinition>(StringComparer.OrdinalIgnoreCase);
    }

    // ── Tool Registration ────────────────────────────────────────────────

    public McpRequestHandler AddTool(
        string name, string description,
        Action<McpSchemaBuilder> schema,
        Func<JObject, CancellationToken, Task<JObject>> handler,
        Action<JObject> annotations = null,
        string title = null,
        Action<McpSchemaBuilder> outputSchemaConfig = null)
    {
        var builder = new McpSchemaBuilder();
        schema?.Invoke(builder);

        JObject annot = null;
        if (annotations != null) { annot = new JObject(); annotations(annot); }

        JObject outputSchema = null;
        if (outputSchemaConfig != null) { var ob = new McpSchemaBuilder(); outputSchemaConfig(ob); outputSchema = ob.Build(); }

        _tools[name] = new McpToolDefinition
        {
            Name = name,
            Title = title,
            Description = description,
            InputSchema = builder.Build(),
            OutputSchema = outputSchema,
            Annotations = annot,
            Handler = async (args, ct) => await handler(args, ct).ConfigureAwait(false)
        };
        return this;
    }

    // ── Resource Registration ─────────────────────────────────────────────

    public McpRequestHandler AddResource(
        string uri, string name, string description,
        Func<CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotations = null)
    {
        JObject annot = null;
        if (annotations != null) { annot = new JObject(); annotations(annot); }

        _resources[uri] = new McpResourceDefinition
        {
            Uri = uri, Name = name, Description = description,
            MimeType = mimeType, Annotations = annot, Handler = handler
        };
        return this;
    }

    public McpRequestHandler AddResourceTemplate(
        string uriTemplate, string name, string description,
        Func<string, CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotations = null)
    {
        JObject annot = null;
        if (annotations != null) { annot = new JObject(); annotations(annot); }

        _resourceTemplates.Add(new McpResourceTemplateDefinition
        {
            UriTemplate = uriTemplate, Name = name, Description = description,
            MimeType = mimeType, Annotations = annot, Handler = handler
        });
        return this;
    }

    // ── Prompt Registration ──────────────────────────────────────────────

    public McpRequestHandler AddPrompt(
        string name, string description,
        List<McpPromptArgument> arguments,
        Func<JObject, CancellationToken, Task<JArray>> handler)
    {
        _prompts[name] = new McpPromptDefinition
        {
            Name = name, Description = description,
            Arguments = arguments ?? new List<McpPromptArgument>(),
            Handler = handler
        };
        return this;
    }

    // ── Main Handler ─────────────────────────────────────────────────────

    public async Task<string> HandleAsync(string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body))
            return SerializeError(null, McpErrorCode.InvalidRequest, "Empty request body");

        JObject request;
        try { request = JObject.Parse(body); }
        catch (JsonException) { return SerializeError(null, McpErrorCode.ParseError, "Invalid JSON"); }

        var method = request.Value<string>("method") ?? "";
        var id = request["id"];

        Log("McpRequestReceived", new { Method = method, HasId = id != null });

        try
        {
            switch (method)
            {
                case "initialize": return HandleInitialize(id, request);

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "notifications/roots/list_changed":
                    return SerializeSuccess(id, new JObject());

                case "ping": return SerializeSuccess(id, new JObject());

                case "tools/list": return HandleToolsList(id);
                case "tools/call": return await HandleToolsCallAsync(id, request, cancellationToken).ConfigureAwait(false);

                case "resources/list": return HandleResourcesList(id);
                case "resources/templates/list": return HandleResourceTemplatesList(id);
                case "resources/read": return await HandleResourcesReadAsync(id, request, cancellationToken).ConfigureAwait(false);
                case "resources/subscribe":
                case "resources/unsubscribe": return SerializeSuccess(id, new JObject());

                case "prompts/list": return HandlePromptsList(id);
                case "prompts/get": return await HandlePromptsGetAsync(id, request, cancellationToken).ConfigureAwait(false);

                case "completion/complete":
                    return SerializeSuccess(id, new JObject
                    {
                        ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false }
                    });

                case "logging/setLevel": return SerializeSuccess(id, new JObject());

                default:
                    Log("McpMethodNotFound", new { Method = method });
                    return SerializeError(id, McpErrorCode.MethodNotFound, "Method not found", method);
            }
        }
        catch (McpException ex) { return SerializeError(id, ex.Code, ex.Message); }
        catch (Exception ex) { return SerializeError(id, McpErrorCode.InternalError, ex.Message); }
    }

    // ── Protocol Handlers ────────────────────────────────────────────────

    private string HandleInitialize(JToken id, JObject request)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString() ?? _options.ProtocolVersion;

        var capabilities = new JObject();
        if (_options.Capabilities.Tools) capabilities["tools"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Resources) capabilities["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false };
        if (_options.Capabilities.Prompts) capabilities["prompts"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Logging) capabilities["logging"] = new JObject();
        if (_options.Capabilities.Completions) capabilities["completions"] = new JObject();

        var serverInfo = new JObject { ["name"] = _options.ServerInfo.Name, ["version"] = _options.ServerInfo.Version };
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Title)) serverInfo["title"] = _options.ServerInfo.Title;
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Description)) serverInfo["description"] = _options.ServerInfo.Description;

        var result = new JObject { ["protocolVersion"] = clientProtocolVersion, ["capabilities"] = capabilities, ["serverInfo"] = serverInfo };
        if (!string.IsNullOrWhiteSpace(_options.Instructions)) result["instructions"] = _options.Instructions;

        Log("McpInitialized", new { Server = _options.ServerInfo.Name, Version = _options.ServerInfo.Version });
        return SerializeSuccess(id, result);
    }

    private string HandleToolsList(JToken id)
    {
        var toolsArray = new JArray();
        foreach (var tool in _tools.Values)
        {
            var toolObj = new JObject { ["name"] = tool.Name, ["description"] = tool.Description, ["inputSchema"] = tool.InputSchema };
            if (!string.IsNullOrWhiteSpace(tool.Title)) toolObj["title"] = tool.Title;
            if (tool.OutputSchema != null) toolObj["outputSchema"] = tool.OutputSchema;
            if (tool.Annotations != null && tool.Annotations.Count > 0) toolObj["annotations"] = tool.Annotations;
            toolsArray.Add(toolObj);
        }
        Log("McpToolsListed", new { Count = _tools.Count });
        return SerializeSuccess(id, new JObject { ["tools"] = toolsArray });
    }

    private string HandleResourcesList(JToken id)
    {
        var arr = new JArray();
        foreach (var r in _resources.Values)
        {
            var o = new JObject { ["uri"] = r.Uri, ["name"] = r.Name };
            if (!string.IsNullOrWhiteSpace(r.Description)) o["description"] = r.Description;
            if (!string.IsNullOrWhiteSpace(r.MimeType)) o["mimeType"] = r.MimeType;
            if (r.Annotations != null && r.Annotations.Count > 0) o["annotations"] = r.Annotations;
            arr.Add(o);
        }
        return SerializeSuccess(id, new JObject { ["resources"] = arr });
    }

    private string HandleResourceTemplatesList(JToken id)
    {
        var arr = new JArray();
        foreach (var t in _resourceTemplates)
        {
            var o = new JObject { ["uriTemplate"] = t.UriTemplate, ["name"] = t.Name };
            if (!string.IsNullOrWhiteSpace(t.Description)) o["description"] = t.Description;
            if (!string.IsNullOrWhiteSpace(t.MimeType)) o["mimeType"] = t.MimeType;
            if (t.Annotations != null && t.Annotations.Count > 0) o["annotations"] = t.Annotations;
            arr.Add(o);
        }
        return SerializeSuccess(id, new JObject { ["resourceTemplates"] = arr });
    }

    private async Task<string> HandleResourcesReadAsync(JToken id, JObject request, CancellationToken ct)
    {
        var uri = (request["params"] as JObject)?.Value<string>("uri");
        if (string.IsNullOrWhiteSpace(uri))
            return SerializeError(id, McpErrorCode.InvalidParams, "Resource URI is required");

        if (_resources.TryGetValue(uri, out var resource))
        {
            try
            {
                var contents = await resource.Handler(ct).ConfigureAwait(false);
                return SerializeSuccess(id, new JObject { ["contents"] = contents });
            }
            catch (Exception ex) { return SerializeError(id, McpErrorCode.InternalError, ex.Message); }
        }

        foreach (var tmpl in _resourceTemplates)
        {
            if (MatchesUriTemplate(tmpl.UriTemplate, uri))
            {
                try
                {
                    var contents = await tmpl.Handler(uri, ct).ConfigureAwait(false);
                    return SerializeSuccess(id, new JObject { ["contents"] = contents });
                }
                catch (Exception ex) { return SerializeError(id, McpErrorCode.InternalError, ex.Message); }
            }
        }

        return SerializeError(id, McpErrorCode.InvalidParams, $"Resource not found: {uri}");
    }

    private static bool MatchesUriTemplate(string template, string uri)
    {
        var tp = template.Split('/');
        var up = uri.Split('/');
        if (tp.Length != up.Length) return false;
        for (int i = 0; i < tp.Length; i++)
        {
            if (tp[i].StartsWith("{") && tp[i].EndsWith("}")) continue;
            if (!string.Equals(tp[i], up[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private string HandlePromptsList(JToken id)
    {
        var arr = new JArray();
        foreach (var p in _prompts.Values)
        {
            var o = new JObject { ["name"] = p.Name };
            if (!string.IsNullOrWhiteSpace(p.Description)) o["description"] = p.Description;
            if (p.Arguments.Count > 0)
            {
                var args = new JArray();
                foreach (var a in p.Arguments)
                {
                    var ao = new JObject { ["name"] = a.Name };
                    if (!string.IsNullOrWhiteSpace(a.Description)) ao["description"] = a.Description;
                    if (a.Required) ao["required"] = true;
                    args.Add(ao);
                }
                o["arguments"] = args;
            }
            arr.Add(o);
        }
        return SerializeSuccess(id, new JObject { ["prompts"] = arr });
    }

    private async Task<string> HandlePromptsGetAsync(JToken id, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var name = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(name))
            return SerializeError(id, McpErrorCode.InvalidParams, "Prompt name is required");
        if (!_prompts.TryGetValue(name, out var prompt))
            return SerializeError(id, McpErrorCode.InvalidParams, $"Prompt not found: {name}");

        try
        {
            var messages = await prompt.Handler(arguments, ct).ConfigureAwait(false);
            var result = new JObject { ["messages"] = messages };
            if (!string.IsNullOrWhiteSpace(prompt.Description)) result["description"] = prompt.Description;
            return SerializeSuccess(id, result);
        }
        catch (Exception ex) { return SerializeError(id, McpErrorCode.InternalError, ex.Message); }
    }

    private async Task<string> HandleToolsCallAsync(JToken id, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return SerializeError(id, McpErrorCode.InvalidParams, "Tool name is required");
        if (!_tools.TryGetValue(toolName, out var tool))
            return SerializeError(id, McpErrorCode.InvalidParams, $"Unknown tool: {toolName}");

        Log("McpToolCallStarted", new { Tool = toolName });

        try
        {
            var result = await tool.Handler(arguments, ct).ConfigureAwait(false);

            JObject callResult;
            if (result is JObject jobj && jobj["content"] is JArray contentArray
                && contentArray.Count > 0 && contentArray[0]?["type"] != null)
            {
                callResult = new JObject { ["content"] = contentArray, ["isError"] = jobj.Value<bool?>("isError") ?? false };
                if (jobj["structuredContent"] is JObject structured) callResult["structuredContent"] = structured;
            }
            else
            {
                string text;
                if (result is JObject po) text = po.ToString(Newtonsoft.Json.Formatting.Indented);
                else if (result is string s) text = s;
                else text = result == null ? "{}" : JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);

                callResult = new JObject
                {
                    ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
                    ["isError"] = false
                };
            }

            Log("McpToolCallCompleted", new { Tool = toolName });
            return SerializeSuccess(id, callResult);
        }
        catch (ArgumentException ex)
        {
            return SerializeSuccess(id, new JObject
            { ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" } }, ["isError"] = true });
        }
        catch (McpException ex)
        {
            return SerializeSuccess(id, new JObject
            { ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" } }, ["isError"] = true });
        }
        catch (Exception ex)
        {
            Log("McpToolCallError", new { Tool = toolName, Error = ex.Message });
            return SerializeSuccess(id, new JObject
            { ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" } }, ["isError"] = true });
        }
    }

    // ── JSON-RPC Serialization ───────────────────────────────────────────

    private string SerializeSuccess(JToken id, JObject result) =>
        new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToString(Newtonsoft.Json.Formatting.None);

    private string SerializeError(JToken id, McpErrorCode code, string message, string data = null) =>
        SerializeError(id, (int)code, message, data);

    private string SerializeError(JToken id, int code, string message, string data = null)
    {
        var error = new JObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrWhiteSpace(data)) error["data"] = data;
        return new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = error }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private void Log(string eventName, object data) => OnLog?.Invoke(eventName, data);
}
