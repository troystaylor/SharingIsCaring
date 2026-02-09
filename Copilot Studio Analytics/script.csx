using System.Net;
using System.Net.Http;
using System.Text;
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Copilot Studio Analytics Connector Script
/// Provides operations for querying Copilot Studio analytics from Dataverse
/// </summary>
public class Script : ScriptBase
{
    // Set your Application Insights connection string here
    private static readonly string AppInsightsConnectionString = "";
    
    // Application Insights endpoint for telemetry
    private static readonly string AppInsightsEndpoint = "https://dc.services.visualstudio.com/v2/track";
    
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        try
        {
            // Log the incoming request
            await LogTelemetryAsync("CopilotStudioAnalytics_Request", new Dictionary<string, string>
            {
                { "OperationId", Context.OperationId },
                { "RequestUri", Context.Request.RequestUri?.ToString() ?? "unknown" }
            });

            // Route to appropriate handler based on operation
            switch (Context.OperationId)
            {
                // Transcript operations
                case "ListConversationTranscripts":
                    return await HandleListTranscriptsAsync();
                    
                case "GetConversationTranscript":
                    return await HandleGetTranscriptAsync();
                
                // Custom analytics operations
                case "GetBotConversationAnalytics":
                    return await HandleBotConversationAnalyticsAsync();
                    
                case "GetTopicAnalytics":
                    return await HandleTopicAnalyticsAsync();
                    
                case "ParseTranscriptContent":
                    return await HandleParseTranscriptContentAsync();
                
                // Session operations
                case "GetSessionAnalytics":
                    return await HandleSessionAnalyticsAsync();
                    
                // Feedback operations
                case "GetCSATAnalytics":
                    return await HandleCSATAnalyticsAsync();
                
                // MCP Protocol
                case "InvokeMCP":
                    return await HandleMcpRequestAsync();
                    
                default:
                    // Pass through for standard Dataverse operations
                    return await HandlePassthroughAsync();
            }
        }
        catch (Exception ex)
        {
            await LogTelemetryAsync("CopilotStudioAnalytics_Error", new Dictionary<string, string>
            {
                { "OperationId", Context.OperationId },
                { "ErrorMessage", ex.Message },
                { "ErrorType", ex.GetType().Name }
            });
            
            return CreateErrorResponse(HttpStatusCode.InternalServerError, $"Error processing request: {ex.Message}");
        }
    }

    // ========================================
    // TRANSCRIPT OPERATIONS
    // ========================================

    /// <summary>
    /// Handle ListConversationTranscripts with optional content parsing
    /// </summary>
    private async Task<HttpResponseMessage> HandleListTranscriptsAsync()
    {
        var response = await Context.SendAsync(Context.Request, CancellationToken);
        
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var data = JObject.Parse(content);
            
            // Parse transcript content from JSON strings to objects
            if (data["value"] is JArray transcripts)
            {
                foreach (var transcript in transcripts)
                {
                    EnrichTranscriptData(transcript);
                }
            }
            
            response.Content = new StringContent(
                data.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            );
        }
        
        return response;
    }

    /// <summary>
    /// Handle GetConversationTranscript with content parsing
    /// </summary>
    private async Task<HttpResponseMessage> HandleGetTranscriptAsync()
    {
        var response = await Context.SendAsync(Context.Request, CancellationToken);
        
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var transcript = JObject.Parse(content);
            
            EnrichTranscriptData(transcript);
            
            response.Content = new StringContent(
                transcript.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            );
        }
        
        return response;
    }

    /// <summary>
    /// Enrich transcript data with parsed content and metrics
    /// </summary>
    private void EnrichTranscriptData(JToken transcript)
    {
        try
        {
            var contentStr = transcript["content"]?.ToString();
            if (string.IsNullOrEmpty(contentStr)) return;

            var parsedContent = JToken.Parse(contentStr);
            transcript["contentParsed"] = parsedContent;

            if (parsedContent is JObject contentObj)
            {
                var activities = contentObj["activities"] as JArray;
                if (activities != null)
                {
                    transcript["activityCount"] = activities.Count;
                    
                    int userMessages = 0, botMessages = 0;
                    var topicsTriggered = new HashSet<string>();
                    bool wasEscalated = false;
                    DateTime? firstTime = null, lastTime = null;

                    foreach (var activity in activities)
                    {
                        var role = activity["from"]?["role"]?.ToString();
                        if (role == "user") userMessages++;
                        else if (role == "bot") botMessages++;

                        // Extract topic info
                        var topicName = activity["channelData"]?["topicName"]?.ToString();
                        if (!string.IsNullOrEmpty(topicName))
                            topicsTriggered.Add(topicName);

                        // Check for escalation
                        var activityType = activity["type"]?.ToString();
                        if (activityType == "handoff" || 
                            activity["channelData"]?["escalated"]?.Value<bool>() == true)
                            wasEscalated = true;

                        // Track timestamps
                        var timestamp = activity["timestamp"]?.ToString();
                        if (!string.IsNullOrEmpty(timestamp) && DateTime.TryParse(timestamp, out var ts))
                        {
                            if (firstTime == null || ts < firstTime) firstTime = ts;
                            if (lastTime == null || ts > lastTime) lastTime = ts;
                        }
                    }

                    transcript["userMessageCount"] = userMessages;
                    transcript["botMessageCount"] = botMessages;
                    transcript["topicsTriggered"] = JArray.FromObject(topicsTriggered);
                    transcript["wasEscalated"] = wasEscalated;
                    
                    if (firstTime.HasValue && lastTime.HasValue)
                    {
                        transcript["durationSeconds"] = (lastTime.Value - firstTime.Value).TotalSeconds;
                    }
                }
            }
        }
        catch
        {
            // If parsing fails, leave content as-is
        }
    }

    // ========================================
    // BOT CONVERSATION ANALYTICS
    // ========================================

    /// <summary>
    /// Handle GetBotConversationAnalytics - aggregates conversation metrics for a bot
    /// </summary>
    private async Task<HttpResponseMessage> HandleBotConversationAnalyticsAsync()
    {
        var bodyContent = await Context.Request.Content.ReadAsStringAsync();
        var body = JObject.Parse(bodyContent);

        var botId = body.Value<string>("botId");
        var startDate = body.Value<DateTime?>("startDate") ?? DateTime.UtcNow.AddDays(-30);
        var endDate = body.Value<DateTime?>("endDate") ?? DateTime.UtcNow;

        if (string.IsNullOrEmpty(botId))
        {
            return CreateErrorResponse(HttpStatusCode.BadRequest, "botId is required");
        }

        // Query transcripts for the bot within date range
        var filter = $"_bot_conversationtranscriptid_value eq '{botId}' and createdon ge {startDate:yyyy-MM-ddTHH:mm:ssZ} and createdon le {endDate:yyyy-MM-ddTHH:mm:ssZ}";
        var queryUrl = $"{GetBaseUrl()}/conversationtranscripts?$filter={Uri.EscapeDataString(filter)}&$select=conversationtranscriptid,name,createdon,content&$orderby=createdon desc";

        var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
        CopyAuthHeaders(request);

        var response = await Context.SendAsync(request, CancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return response;
        }

        var data = JObject.Parse(await response.Content.ReadAsStringAsync());
        var transcripts = data["value"] as JArray ?? new JArray();

        // Also get the bot name
        var botName = await GetBotNameAsync(botId);

        // Calculate analytics
        var analytics = CalculateConversationAnalytics(transcripts, botId, botName, startDate, endDate);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                analytics.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            )
        };
    }

    private JObject CalculateConversationAnalytics(JArray transcripts, string botId, string botName, DateTime startDate, DateTime endDate)
    {
        int totalConversations = transcripts.Count;
        int totalMessages = 0, totalUserMessages = 0, totalBotMessages = 0;
        int escalatedCount = 0;
        double totalDuration = 0;
        var conversationsByDay = new Dictionary<string, int>();

        foreach (var transcript in transcripts)
        {
            EnrichTranscriptData(transcript);

            totalUserMessages += transcript["userMessageCount"]?.Value<int>() ?? 0;
            totalBotMessages += transcript["botMessageCount"]?.Value<int>() ?? 0;
            totalMessages += transcript["activityCount"]?.Value<int>() ?? 0;
            totalDuration += transcript["durationSeconds"]?.Value<double>() ?? 0;

            if (transcript["wasEscalated"]?.Value<bool>() == true)
                escalatedCount++;

            var createdOn = transcript["createdon"]?.ToString();
            if (!string.IsNullOrEmpty(createdOn) && DateTime.TryParse(createdOn, out var date))
            {
                var dateKey = date.ToString("yyyy-MM-dd");
                if (!conversationsByDay.ContainsKey(dateKey))
                    conversationsByDay[dateKey] = 0;
                conversationsByDay[dateKey]++;
            }
        }

        var conversationsByDayArray = new JArray();
        foreach (var kvp in conversationsByDay.OrderBy(k => k.Key))
        {
            conversationsByDayArray.Add(new JObject
            {
                ["date"] = kvp.Key,
                ["count"] = kvp.Value
            });
        }

        return new JObject
        {
            ["botId"] = botId,
            ["botName"] = botName,
            ["startDate"] = startDate.ToString("o"),
            ["endDate"] = endDate.ToString("o"),
            ["totalConversations"] = totalConversations,
            ["averageMessagesPerConversation"] = totalConversations > 0 ? Math.Round((double)totalMessages / totalConversations, 2) : 0,
            ["averageUserMessagesPerConversation"] = totalConversations > 0 ? Math.Round((double)totalUserMessages / totalConversations, 2) : 0,
            ["averageDurationSeconds"] = totalConversations > 0 ? Math.Round(totalDuration / totalConversations, 2) : 0,
            ["escalationRate"] = totalConversations > 0 ? Math.Round((double)escalatedCount / totalConversations * 100, 2) : 0,
            ["escalatedCount"] = escalatedCount,
            ["conversationsByDay"] = conversationsByDayArray
        };
    }

    // ========================================
    // TOPIC ANALYTICS
    // ========================================

    /// <summary>
    /// Handle GetTopicAnalytics - aggregates topic-level metrics
    /// </summary>
    private async Task<HttpResponseMessage> HandleTopicAnalyticsAsync()
    {
        var bodyContent = await Context.Request.Content.ReadAsStringAsync();
        var body = JObject.Parse(bodyContent);

        var botId = body.Value<string>("botId");
        var startDate = body.Value<DateTime?>("startDate") ?? DateTime.UtcNow.AddDays(-30);
        var endDate = body.Value<DateTime?>("endDate") ?? DateTime.UtcNow;
        var topN = body.Value<int?>("top") ?? 10;

        if (string.IsNullOrEmpty(botId))
        {
            return CreateErrorResponse(HttpStatusCode.BadRequest, "botId is required");
        }

        // Query transcripts
        var filter = $"_bot_conversationtranscriptid_value eq '{botId}' and createdon ge {startDate:yyyy-MM-ddTHH:mm:ssZ} and createdon le {endDate:yyyy-MM-ddTHH:mm:ssZ}";
        var queryUrl = $"{GetBaseUrl()}/conversationtranscripts?$filter={Uri.EscapeDataString(filter)}&$select=conversationtranscriptid,content&$top=5000";

        var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
        CopyAuthHeaders(request);

        var response = await Context.SendAsync(request, CancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return response;
        }

        var data = JObject.Parse(await response.Content.ReadAsStringAsync());
        var transcripts = data["value"] as JArray ?? new JArray();

        // Analyze topics
        var topicAnalytics = CalculateTopicAnalytics(transcripts, botId, startDate, endDate, topN);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                topicAnalytics.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            )
        };
    }

    private JObject CalculateTopicAnalytics(JArray transcripts, string botId, DateTime startDate, DateTime endDate, int topN)
    {
        var topicStats = new Dictionary<string, TopicMetrics>();

        foreach (var transcript in transcripts)
        {
            try
            {
                var contentStr = transcript["content"]?.ToString();
                if (string.IsNullOrEmpty(contentStr)) continue;

                var content = JObject.Parse(contentStr);
                var activities = content["activities"] as JArray;
                if (activities == null) continue;

                var sessionTopics = new HashSet<string>();
                bool sessionEscalated = false;
                bool sessionCompleted = true;

                foreach (var activity in activities)
                {
                    var topicName = activity["channelData"]?["topicName"]?.ToString();
                    if (!string.IsNullOrEmpty(topicName))
                    {
                        sessionTopics.Add(topicName);
                    }

                    var activityType = activity["type"]?.ToString();
                    if (activityType == "handoff")
                        sessionEscalated = true;
                }

                // Update stats for each topic seen
                foreach (var topic in sessionTopics)
                {
                    if (!topicStats.ContainsKey(topic))
                        topicStats[topic] = new TopicMetrics();

                    topicStats[topic].TriggerCount++;
                    if (sessionEscalated) topicStats[topic].EscalatedCount++;
                    if (sessionCompleted) topicStats[topic].CompletedCount++;
                }
            }
            catch { }
        }

        var topicsArray = new JArray();
        foreach (var kvp in topicStats.OrderByDescending(k => k.Value.TriggerCount).Take(topN))
        {
            var completionRate = kvp.Value.TriggerCount > 0 
                ? Math.Round((double)kvp.Value.CompletedCount / kvp.Value.TriggerCount * 100, 2) 
                : 0;
            var escalationRate = kvp.Value.TriggerCount > 0 
                ? Math.Round((double)kvp.Value.EscalatedCount / kvp.Value.TriggerCount * 100, 2) 
                : 0;

            topicsArray.Add(new JObject
            {
                ["topicName"] = kvp.Key,
                ["triggerCount"] = kvp.Value.TriggerCount,
                ["completionRate"] = completionRate,
                ["escalationRate"] = escalationRate
            });
        }

        return new JObject
        {
            ["botId"] = botId,
            ["startDate"] = startDate.ToString("o"),
            ["endDate"] = endDate.ToString("o"),
            ["totalTopicsAnalyzed"] = topicStats.Count,
            ["topics"] = topicsArray
        };
    }

    private class TopicMetrics
    {
        public int TriggerCount { get; set; }
        public int CompletedCount { get; set; }
        public int EscalatedCount { get; set; }
    }

    // ========================================
    // PARSE TRANSCRIPT CONTENT
    // ========================================

    /// <summary>
    /// Handle ParseTranscriptContent - parses a single transcript into structured format
    /// </summary>
    private async Task<HttpResponseMessage> HandleParseTranscriptContentAsync()
    {
        var bodyContent = await Context.Request.Content.ReadAsStringAsync();
        var body = JObject.Parse(bodyContent);

        var transcriptId = body.Value<string>("transcriptId");
        if (string.IsNullOrEmpty(transcriptId))
        {
            return CreateErrorResponse(HttpStatusCode.BadRequest, "transcriptId is required");
        }

        // Fetch the transcript
        var queryUrl = $"{GetBaseUrl()}/conversationtranscripts({transcriptId})";
        var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
        CopyAuthHeaders(request);

        var response = await Context.SendAsync(request, CancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return response;
        }

        var transcript = JObject.Parse(await response.Content.ReadAsStringAsync());
        var parsed = ParseTranscriptToStructured(transcript);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                parsed.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            )
        };
    }

    private JObject ParseTranscriptToStructured(JObject transcript)
    {
        var result = new JObject
        {
            ["transcriptId"] = transcript["conversationtranscriptid"],
            ["botId"] = transcript["_bot_conversationtranscriptid_value"]
        };

        var contentStr = transcript["content"]?.ToString();
        if (string.IsNullOrEmpty(contentStr))
        {
            result["error"] = "No content available";
            return result;
        }

        try
        {
            var content = JObject.Parse(contentStr);
            var activities = content["activities"] as JArray;

            if (activities == null || activities.Count == 0)
            {
                result["messageCount"] = 0;
                result["messages"] = new JArray();
                return result;
            }

            var messages = new JArray();
            var topicsTriggered = new HashSet<string>();
            int userMsgCount = 0, botMsgCount = 0;
            DateTime? startTime = null, endTime = null;
            bool wasEscalated = false;
            string sessionOutcome = "completed";

            foreach (var activity in activities)
            {
                var role = activity["from"]?["role"]?.ToString() ?? "unknown";
                var text = activity["text"]?.ToString();
                var timestamp = activity["timestamp"]?.ToString();
                var topicName = activity["channelData"]?["topicName"]?.ToString();
                var activityType = activity["type"]?.ToString();

                if (role == "user") userMsgCount++;
                else if (role == "bot") botMsgCount++;

                if (!string.IsNullOrEmpty(topicName))
                    topicsTriggered.Add(topicName);

                if (activityType == "handoff")
                {
                    wasEscalated = true;
                    sessionOutcome = "escalated";
                }

                if (!string.IsNullOrEmpty(timestamp) && DateTime.TryParse(timestamp, out var ts))
                {
                    if (startTime == null || ts < startTime) startTime = ts;
                    if (endTime == null || ts > endTime) endTime = ts;
                }

                // Only include message activities with text
                if (!string.IsNullOrEmpty(text) && (activityType == "message" || string.IsNullOrEmpty(activityType)))
                {
                    messages.Add(new JObject
                    {
                        ["timestamp"] = timestamp,
                        ["role"] = role,
                        ["text"] = text,
                        ["topicName"] = topicName ?? ""
                    });
                }
            }

            result["startTime"] = startTime?.ToString("o");
            result["endTime"] = endTime?.ToString("o");
            result["messageCount"] = messages.Count;
            result["userMessageCount"] = userMsgCount;
            result["botMessageCount"] = botMsgCount;
            result["topicsTriggered"] = JArray.FromObject(topicsTriggered);
            result["messages"] = messages;
            result["wasEscalated"] = wasEscalated;
            result["sessionOutcome"] = sessionOutcome;
        }
        catch (Exception ex)
        {
            result["error"] = $"Failed to parse content: {ex.Message}";
        }

        return result;
    }

    // ========================================
    // SESSION ANALYTICS
    // ========================================

    /// <summary>
    /// Handle GetSessionAnalytics - aggregates session-level metrics
    /// </summary>
    private async Task<HttpResponseMessage> HandleSessionAnalyticsAsync()
    {
        var bodyContent = await Context.Request.Content.ReadAsStringAsync();
        var body = JObject.Parse(bodyContent);

        var botId = body.Value<string>("botId");
        var startDate = body.Value<DateTime?>("startDate") ?? DateTime.UtcNow.AddDays(-30);
        var endDate = body.Value<DateTime?>("endDate") ?? DateTime.UtcNow;

        if (string.IsNullOrEmpty(botId))
        {
            return CreateErrorResponse(HttpStatusCode.BadRequest, "botId is required");
        }

        // Query bot sessions
        var filter = $"_msdyn_botid_value eq '{botId}' and createdon ge {startDate:yyyy-MM-ddTHH:mm:ssZ} and createdon le {endDate:yyyy-MM-ddTHH:mm:ssZ}";
        var queryUrl = $"{GetBaseUrl()}/msdyn_botsessions?$filter={Uri.EscapeDataString(filter)}&$select=msdyn_botsessionid,msdyn_sessionoutcome,createdon,msdyn_starttime,msdyn_endtime";

        var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
        CopyAuthHeaders(request);

        var response = await Context.SendAsync(request, CancellationToken);
        
        // If sessions table doesn't exist or is empty, return empty analytics
        if (!response.IsSuccessStatusCode)
        {
            var fallbackAnalytics = new JObject
            {
                ["botId"] = botId,
                ["startDate"] = startDate.ToString("o"),
                ["endDate"] = endDate.ToString("o"),
                ["totalSessions"] = 0,
                ["note"] = "Session data not available. Use GetBotConversationAnalytics for transcript-based analytics."
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(fallbackAnalytics.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
            };
        }

        var data = JObject.Parse(await response.Content.ReadAsStringAsync());
        var sessions = data["value"] as JArray ?? new JArray();

        var analytics = CalculateSessionAnalytics(sessions, botId, startDate, endDate);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                analytics.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            )
        };
    }

    private JObject CalculateSessionAnalytics(JArray sessions, string botId, DateTime startDate, DateTime endDate)
    {
        int totalSessions = sessions.Count;
        var outcomes = new Dictionary<string, int>();
        double totalDuration = 0;
        int sessionsWithDuration = 0;
        var sessionsByDay = new Dictionary<string, int>();

        foreach (var session in sessions)
        {
            // Count by outcome
            var outcome = session["msdyn_sessionoutcome"]?.ToString() ?? "unknown";
            if (!outcomes.ContainsKey(outcome))
                outcomes[outcome] = 0;
            outcomes[outcome]++;

            // Calculate duration
            var startTimeStr = session["msdyn_starttime"]?.ToString();
            var endTimeStr = session["msdyn_endtime"]?.ToString();
            if (!string.IsNullOrEmpty(startTimeStr) && !string.IsNullOrEmpty(endTimeStr))
            {
                if (DateTime.TryParse(startTimeStr, out var start) && DateTime.TryParse(endTimeStr, out var end))
                {
                    totalDuration += (end - start).TotalSeconds;
                    sessionsWithDuration++;
                }
            }

            // Count by day
            var createdOn = session["createdon"]?.ToString();
            if (!string.IsNullOrEmpty(createdOn) && DateTime.TryParse(createdOn, out var date))
            {
                var dateKey = date.ToString("yyyy-MM-dd");
                if (!sessionsByDay.ContainsKey(dateKey))
                    sessionsByDay[dateKey] = 0;
                sessionsByDay[dateKey]++;
            }
        }

        var outcomeBreakdown = new JArray();
        foreach (var kvp in outcomes.OrderByDescending(k => k.Value))
        {
            outcomeBreakdown.Add(new JObject
            {
                ["outcome"] = kvp.Key,
                ["count"] = kvp.Value,
                ["percentage"] = totalSessions > 0 ? Math.Round((double)kvp.Value / totalSessions * 100, 2) : 0
            });
        }

        var sessionsByDayArray = new JArray();
        foreach (var kvp in sessionsByDay.OrderBy(k => k.Key))
        {
            sessionsByDayArray.Add(new JObject
            {
                ["date"] = kvp.Key,
                ["count"] = kvp.Value
            });
        }

        return new JObject
        {
            ["botId"] = botId,
            ["startDate"] = startDate.ToString("o"),
            ["endDate"] = endDate.ToString("o"),
            ["totalSessions"] = totalSessions,
            ["averageSessionDurationSeconds"] = sessionsWithDuration > 0 ? Math.Round(totalDuration / sessionsWithDuration, 2) : 0,
            ["outcomeBreakdown"] = outcomeBreakdown,
            ["sessionsByDay"] = sessionsByDayArray
        };
    }

    // ========================================
    // CSAT ANALYTICS
    // ========================================

    /// <summary>
    /// Handle GetCSATAnalytics - aggregates customer satisfaction feedback
    /// </summary>
    private async Task<HttpResponseMessage> HandleCSATAnalyticsAsync()
    {
        var bodyContent = await Context.Request.Content.ReadAsStringAsync();
        var body = JObject.Parse(bodyContent);

        var botId = body.Value<string>("botId");
        var startDate = body.Value<DateTime?>("startDate") ?? DateTime.UtcNow.AddDays(-30);
        var endDate = body.Value<DateTime?>("endDate") ?? DateTime.UtcNow;

        if (string.IsNullOrEmpty(botId))
        {
            return CreateErrorResponse(HttpStatusCode.BadRequest, "botId is required");
        }

        // Query conversation transcripts and extract CSAT from content
        var filter = $"_bot_conversationtranscriptid_value eq '{botId}' and createdon ge {startDate:yyyy-MM-ddTHH:mm:ssZ} and createdon le {endDate:yyyy-MM-ddTHH:mm:ssZ}";
        var queryUrl = $"{GetBaseUrl()}/conversationtranscripts?$filter={Uri.EscapeDataString(filter)}&$select=conversationtranscriptid,content,metadata&$top=5000";

        var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
        CopyAuthHeaders(request);

        var response = await Context.SendAsync(request, CancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return response;
        }

        var data = JObject.Parse(await response.Content.ReadAsStringAsync());
        var transcripts = data["value"] as JArray ?? new JArray();

        var analytics = CalculateCSATAnalytics(transcripts, botId, startDate, endDate);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                analytics.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            )
        };
    }

    private JObject CalculateCSATAnalytics(JArray transcripts, string botId, DateTime startDate, DateTime endDate)
    {
        var ratings = new List<int>();
        var feedbackComments = new JArray();
        var ratingDistribution = new Dictionary<int, int> { {1,0}, {2,0}, {3,0}, {4,0}, {5,0} };

        foreach (var transcript in transcripts)
        {
            try
            {
                // Check metadata for CSAT
                var metadataStr = transcript["metadata"]?.ToString();
                if (!string.IsNullOrEmpty(metadataStr))
                {
                    var metadata = JObject.Parse(metadataStr);
                    var csatRating = metadata["csatRating"]?.Value<int>();
                    if (csatRating.HasValue && csatRating >= 1 && csatRating <= 5)
                    {
                        ratings.Add(csatRating.Value);
                        ratingDistribution[csatRating.Value]++;
                    }
                    
                    var feedback = metadata["csatFeedback"]?.ToString();
                    if (!string.IsNullOrEmpty(feedback))
                    {
                        feedbackComments.Add(new JObject
                        {
                            ["transcriptId"] = transcript["conversationtranscriptid"],
                            ["rating"] = csatRating,
                            ["comment"] = feedback
                        });
                    }
                }

                // Also check content for CSAT activities
                var contentStr = transcript["content"]?.ToString();
                if (!string.IsNullOrEmpty(contentStr))
                {
                    var content = JObject.Parse(contentStr);
                    var activities = content["activities"] as JArray;
                    if (activities != null)
                    {
                        foreach (var activity in activities)
                        {
                            var channelData = activity["channelData"] as JObject;
                            if (channelData != null)
                            {
                                var csatScore = channelData["csatScore"]?.Value<int>() ?? 
                                               channelData["satisfaction"]?.Value<int>();
                                if (csatScore.HasValue && csatScore >= 1 && csatScore <= 5)
                                {
                                    if (!ratings.Contains(csatScore.Value)) // Avoid duplicates
                                    {
                                        ratings.Add(csatScore.Value);
                                        ratingDistribution[csatScore.Value]++;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        var ratingDist = new JArray();
        for (int i = 1; i <= 5; i++)
        {
            ratingDist.Add(new JObject
            {
                ["rating"] = i,
                ["count"] = ratingDistribution[i],
                ["percentage"] = ratings.Count > 0 ? Math.Round((double)ratingDistribution[i] / ratings.Count * 100, 2) : 0
            });
        }

        return new JObject
        {
            ["botId"] = botId,
            ["startDate"] = startDate.ToString("o"),
            ["endDate"] = endDate.ToString("o"),
            ["totalResponses"] = ratings.Count,
            ["averageRating"] = ratings.Count > 0 ? Math.Round(ratings.Average(), 2) : 0,
            ["csatScore"] = ratings.Count > 0 ? Math.Round((double)ratings.Count(r => r >= 4) / ratings.Count * 100, 2) : 0,
            ["ratingDistribution"] = ratingDist,
            ["recentFeedback"] = new JArray(feedbackComments.Take(10))
        };
    }

    // ========================================
    // MCP PROTOCOL HANDLERS
    // ========================================

    private const string ServerName = "copilot-studio-analytics";
    private const string ServerVersion = "1.1.0";
    private const string ProtocolVersion = "2025-11-25";

    /// <summary>
    /// Handle MCP (Model Context Protocol) requests for Copilot Studio integration
    /// </summary>
    private async Task<HttpResponseMessage> HandleMcpRequestAsync()
    {
        string body = null;
        JObject request = null;
        string method = null;
        JToken requestId = null;

        try
        {
            body = await Context.Request.Content.ReadAsStringAsync();
            
            if (string.IsNullOrWhiteSpace(body))
            {
                return CreateJsonRpcErrorResponse(null, -32600, "Invalid Request", "Empty request body");
            }

            try
            {
                request = JObject.Parse(body);
            }
            catch (JsonException)
            {
                return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Invalid JSON");
            }

            method = request.Value<string>("method") ?? string.Empty;
            requestId = request["id"];

            await LogTelemetryAsync("MCP_Request", new Dictionary<string, string>
            {
                { "Method", method }
            });

            // Route to MCP method handlers
            switch (method)
            {
                case "initialize":
                    return HandleMcpInitialize(request, requestId);

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "ping":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());

                case "tools/list":
                    return HandleMcpToolsList(requestId);

                case "tools/call":
                    return await HandleMcpToolsCallAsync(request, requestId);

                case "resources/list":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });

                case "resources/templates/list":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resourceTemplates"] = new JArray() });

                case "prompts/list":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = new JArray() });

                case "completion/complete":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject 
                    { 
                        ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false } 
                    });

                case "logging/setLevel":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());

                default:
                    return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
            }
        }
        catch (Exception ex)
        {
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
    }

    /// <summary>
    /// Handle MCP initialize request
    /// </summary>
    private HttpResponseMessage HandleMcpInitialize(JObject request, JToken requestId)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString() ?? ProtocolVersion;

        var result = new JObject
        {
            ["protocolVersion"] = clientProtocolVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false },
                ["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false },
                ["prompts"] = new JObject { ["listChanged"] = false },
                ["logging"] = new JObject(),
                ["completions"] = new JObject()
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion,
                ["title"] = "Copilot Studio Analytics",
                ["description"] = "Analytics tools for Copilot Studio bots - conversation metrics, topic analysis, CSAT tracking"
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    /// <summary>
    /// Handle MCP tools/list request
    /// </summary>
    private HttpResponseMessage HandleMcpToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            new JObject
            {
                ["name"] = "list_bots",
                ["description"] = "List all Copilot Studio bots in the environment. Returns bot IDs, names, and status.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["top"] = new JObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Maximum number of bots to return"
                        }
                    }
                }
            },
            new JObject
            {
                ["name"] = "get_conversation_analytics",
                ["description"] = "Get aggregated conversation analytics for a bot including total conversations, message counts, escalation rates, and daily trends.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["bot_id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The bot ID to analyze"
                        },
                        ["start_date"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Start date (ISO 8601 format). Defaults to 30 days ago."
                        },
                        ["end_date"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "End date (ISO 8601 format). Defaults to now."
                        }
                    },
                    ["required"] = new JArray { "bot_id" }
                }
            },
            new JObject
            {
                ["name"] = "get_topic_analytics",
                ["description"] = "Get topic-level analytics including trigger counts, completion rates, and escalation rates for top topics.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["bot_id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The bot ID to analyze"
                        },
                        ["start_date"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Start date (ISO 8601 format)"
                        },
                        ["end_date"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "End date (ISO 8601 format)"
                        },
                        ["top"] = new JObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Number of top topics to return (default: 10)"
                        }
                    },
                    ["required"] = new JArray { "bot_id" }
                }
            },
            new JObject
            {
                ["name"] = "get_csat_analytics",
                ["description"] = "Get customer satisfaction analytics including average rating, CSAT score percentage, rating distribution, and recent feedback comments.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["bot_id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The bot ID to analyze"
                        },
                        ["start_date"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Start date (ISO 8601 format)"
                        },
                        ["end_date"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "End date (ISO 8601 format)"
                        }
                    },
                    ["required"] = new JArray { "bot_id" }
                }
            },
            new JObject
            {
                ["name"] = "get_session_analytics",
                ["description"] = "Get session-level analytics including total sessions, average duration, outcome breakdown, and daily trends.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["bot_id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The bot ID to analyze"
                        },
                        ["start_date"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Start date (ISO 8601 format)"
                        },
                        ["end_date"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "End date (ISO 8601 format)"
                        }
                    },
                    ["required"] = new JArray { "bot_id" }
                }
            },
            new JObject
            {
                ["name"] = "get_recent_transcripts",
                ["description"] = "Get recent conversation transcripts with parsed message content, topics triggered, and escalation status.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["bot_id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The bot ID to get transcripts for"
                        },
                        ["top"] = new JObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Number of transcripts to return (default: 10)"
                        }
                    },
                    ["required"] = new JArray { "bot_id" }
                }
            },
            new JObject
            {
                ["name"] = "parse_transcript",
                ["description"] = "Parse a specific conversation transcript into structured messages with timestamps, roles, and topic information.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["transcript_id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The transcript ID to parse"
                        }
                    },
                    ["required"] = new JArray { "transcript_id" }
                }
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    /// <summary>
    /// Handle MCP tools/call request
    /// </summary>
    private async Task<HttpResponseMessage> HandleMcpToolsCallAsync(JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        if (paramsObj == null)
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "params object is required");
        }

        var toolName = paramsObj.Value<string>("name");
        var arguments = paramsObj["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "Tool name is required");
        }

        await LogTelemetryAsync("MCP_ToolCall", new Dictionary<string, string>
        {
            { "ToolName", toolName }
        });

        try
        {
            JObject toolResult;
            
            switch (toolName.ToLowerInvariant())
            {
                case "list_bots":
                    toolResult = await ExecuteListBotsToolAsync(arguments);
                    break;

                case "get_conversation_analytics":
                    toolResult = await ExecuteConversationAnalyticsToolAsync(arguments);
                    break;

                case "get_topic_analytics":
                    toolResult = await ExecuteTopicAnalyticsToolAsync(arguments);
                    break;

                case "get_csat_analytics":
                    toolResult = await ExecuteCSATAnalyticsToolAsync(arguments);
                    break;

                case "get_session_analytics":
                    toolResult = await ExecuteSessionAnalyticsToolAsync(arguments);
                    break;

                case "get_recent_transcripts":
                    toolResult = await ExecuteRecentTranscriptsToolAsync(arguments);
                    break;

                case "parse_transcript":
                    toolResult = await ExecuteParseTranscriptToolAsync(arguments);
                    break;

                default:
                    throw new ArgumentException($"Unknown tool: {toolName}");
            }

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = toolResult.ToString(Newtonsoft.Json.Formatting.Indented)
                    }
                },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = $"Tool execution failed: {ex.Message}"
                    }
                },
                ["isError"] = true
            });
        }
    }

    // ========================================
    // MCP TOOL IMPLEMENTATIONS
    // ========================================

    private async Task<JObject> ExecuteListBotsToolAsync(JObject arguments)
    {
        var top = arguments.Value<int?>("top") ?? 50;
        var queryUrl = $"{GetBaseUrl()}/bots?$select=botid,name,statecode,createdon&$top={top}&$orderby=name";

        var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
        CopyAuthHeaders(request);

        var response = await Context.SendAsync(request, CancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            await LogTelemetryAsync("MCP_DataverseError", new Dictionary<string, string>
            {
                { "Operation", "list_bots" },
                { "StatusCode", response.StatusCode.ToString() },
                { "ErrorBody", errorBody?.Length > 1000 ? errorBody.Substring(0, 1000) : errorBody }
            });
            throw new Exception($"Failed to list bots: {response.StatusCode} - {errorBody}");
        }

        var data = JObject.Parse(await response.Content.ReadAsStringAsync());
        var bots = data["value"] as JArray ?? new JArray();

        var result = new JArray();
        foreach (var bot in bots)
        {
            result.Add(new JObject
            {
                ["id"] = bot["botid"],
                ["name"] = bot["name"],
                ["state"] = bot["statecode"]?.Value<int>() == 0 ? "Active" : "Inactive",
                ["created"] = bot["createdon"]
            });
        }

        return new JObject
        {
            ["botCount"] = result.Count,
            ["bots"] = result
        };
    }

    private async Task<JObject> ExecuteConversationAnalyticsToolAsync(JObject arguments)
    {
        var botId = arguments.Value<string>("bot_id");
        if (string.IsNullOrEmpty(botId))
        {
            throw new ArgumentException("bot_id is required");
        }

        var startDate = DateTime.UtcNow.AddDays(-30);
        var endDate = DateTime.UtcNow;

        var startDateStr = arguments.Value<string>("start_date");
        var endDateStr = arguments.Value<string>("end_date");
        if (!string.IsNullOrEmpty(startDateStr) && DateTime.TryParse(startDateStr, out var sd)) startDate = sd;
        if (!string.IsNullOrEmpty(endDateStr) && DateTime.TryParse(endDateStr, out var ed)) endDate = ed;

        var filter = $"_bot_conversationtranscriptid_value eq '{botId}' and createdon ge {startDate:yyyy-MM-ddTHH:mm:ssZ} and createdon le {endDate:yyyy-MM-ddTHH:mm:ssZ}";
        var queryUrl = $"{GetBaseUrl()}/conversationtranscripts?$filter={Uri.EscapeDataString(filter)}&$select=conversationtranscriptid,createdon,content&$orderby=createdon desc";

        var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
        CopyAuthHeaders(request);

        var response = await Context.SendAsync(request, CancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            await LogTelemetryAsync("MCP_DataverseError", new Dictionary<string, string>
            {
                { "Operation", "get_conversation_analytics" },
                { "StatusCode", response.StatusCode.ToString() },
                { "Filter", filter },
                { "ErrorBody", errorBody?.Length > 1000 ? errorBody.Substring(0, 1000) : errorBody }
            });
            throw new Exception($"Failed to get transcripts: {response.StatusCode} - {errorBody}");
        }

        var data = JObject.Parse(await response.Content.ReadAsStringAsync());
        var transcripts = data["value"] as JArray ?? new JArray();
        var botName = await GetBotNameAsync(botId);

        return CalculateConversationAnalytics(transcripts, botId, botName, startDate, endDate);
    }

    private async Task<JObject> ExecuteTopicAnalyticsToolAsync(JObject arguments)
    {
        var botId = arguments.Value<string>("bot_id");
        if (string.IsNullOrEmpty(botId))
        {
            throw new ArgumentException("bot_id is required");
        }

        var startDate = DateTime.UtcNow.AddDays(-30);
        var endDate = DateTime.UtcNow;
        var topN = arguments.Value<int?>("top") ?? 10;

        var startDateStr = arguments.Value<string>("start_date");
        var endDateStr = arguments.Value<string>("end_date");
        if (!string.IsNullOrEmpty(startDateStr) && DateTime.TryParse(startDateStr, out var sd)) startDate = sd;
        if (!string.IsNullOrEmpty(endDateStr) && DateTime.TryParse(endDateStr, out var ed)) endDate = ed;

        var filter = $"_bot_conversationtranscriptid_value eq '{botId}' and createdon ge {startDate:yyyy-MM-ddTHH:mm:ssZ} and createdon le {endDate:yyyy-MM-ddTHH:mm:ssZ}";
        var queryUrl = $"{GetBaseUrl()}/conversationtranscripts?$filter={Uri.EscapeDataString(filter)}&$select=conversationtranscriptid,content&$top=5000";

        var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
        CopyAuthHeaders(request);

        var response = await Context.SendAsync(request, CancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to get transcripts: {response.StatusCode}");
        }

        var data = JObject.Parse(await response.Content.ReadAsStringAsync());
        var transcripts = data["value"] as JArray ?? new JArray();

        return CalculateTopicAnalytics(transcripts, botId, startDate, endDate, topN);
    }

    private async Task<JObject> ExecuteCSATAnalyticsToolAsync(JObject arguments)
    {
        var botId = arguments.Value<string>("bot_id");
        if (string.IsNullOrEmpty(botId))
        {
            throw new ArgumentException("bot_id is required");
        }

        var startDate = DateTime.UtcNow.AddDays(-30);
        var endDate = DateTime.UtcNow;

        var startDateStr = arguments.Value<string>("start_date");
        var endDateStr = arguments.Value<string>("end_date");
        if (!string.IsNullOrEmpty(startDateStr) && DateTime.TryParse(startDateStr, out var sd)) startDate = sd;
        if (!string.IsNullOrEmpty(endDateStr) && DateTime.TryParse(endDateStr, out var ed)) endDate = ed;

        var filter = $"_bot_conversationtranscriptid_value eq '{botId}' and createdon ge {startDate:yyyy-MM-ddTHH:mm:ssZ} and createdon le {endDate:yyyy-MM-ddTHH:mm:ssZ}";
        var queryUrl = $"{GetBaseUrl()}/conversationtranscripts?$filter={Uri.EscapeDataString(filter)}&$select=conversationtranscriptid,content,metadata&$top=5000";

        var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
        CopyAuthHeaders(request);

        var response = await Context.SendAsync(request, CancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to get transcripts: {response.StatusCode}");
        }

        var data = JObject.Parse(await response.Content.ReadAsStringAsync());
        var transcripts = data["value"] as JArray ?? new JArray();

        return CalculateCSATAnalytics(transcripts, botId, startDate, endDate);
    }

    private async Task<JObject> ExecuteSessionAnalyticsToolAsync(JObject arguments)
    {
        var botId = arguments.Value<string>("bot_id");
        if (string.IsNullOrEmpty(botId))
        {
            throw new ArgumentException("bot_id is required");
        }

        var startDate = DateTime.UtcNow.AddDays(-30);
        var endDate = DateTime.UtcNow;

        var startDateStr = arguments.Value<string>("start_date");
        var endDateStr = arguments.Value<string>("end_date");
        if (!string.IsNullOrEmpty(startDateStr) && DateTime.TryParse(startDateStr, out var sd)) startDate = sd;
        if (!string.IsNullOrEmpty(endDateStr) && DateTime.TryParse(endDateStr, out var ed)) endDate = ed;

        var filter = $"_msdyn_botid_value eq '{botId}' and createdon ge {startDate:yyyy-MM-ddTHH:mm:ssZ} and createdon le {endDate:yyyy-MM-ddTHH:mm:ssZ}";
        var queryUrl = $"{GetBaseUrl()}/msdyn_botsessions?$filter={Uri.EscapeDataString(filter)}&$select=msdyn_botsessionid,msdyn_sessionoutcome,createdon,msdyn_starttime,msdyn_endtime";

        var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
        CopyAuthHeaders(request);

        var response = await Context.SendAsync(request, CancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            return new JObject
            {
                ["botId"] = botId,
                ["startDate"] = startDate.ToString("o"),
                ["endDate"] = endDate.ToString("o"),
                ["totalSessions"] = 0,
                ["note"] = "Session data not available. Use get_conversation_analytics instead."
            };
        }

        var data = JObject.Parse(await response.Content.ReadAsStringAsync());
        var sessions = data["value"] as JArray ?? new JArray();

        return CalculateSessionAnalytics(sessions, botId, startDate, endDate);
    }

    private async Task<JObject> ExecuteRecentTranscriptsToolAsync(JObject arguments)
    {
        var botId = arguments.Value<string>("bot_id");
        if (string.IsNullOrEmpty(botId))
        {
            throw new ArgumentException("bot_id is required");
        }

        var top = arguments.Value<int?>("top") ?? 10;
        var filter = $"_bot_conversationtranscriptid_value eq '{botId}'";
        var queryUrl = $"{GetBaseUrl()}/conversationtranscripts?$filter={Uri.EscapeDataString(filter)}&$select=conversationtranscriptid,name,createdon,content&$top={top}&$orderby=createdon desc";

        var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
        CopyAuthHeaders(request);

        var response = await Context.SendAsync(request, CancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to get transcripts: {response.StatusCode}");
        }

        var data = JObject.Parse(await response.Content.ReadAsStringAsync());
        var transcripts = data["value"] as JArray ?? new JArray();

        var result = new JArray();
        foreach (var transcript in transcripts)
        {
            EnrichTranscriptData(transcript);
            result.Add(new JObject
            {
                ["transcriptId"] = transcript["conversationtranscriptid"],
                ["name"] = transcript["name"],
                ["createdOn"] = transcript["createdon"],
                ["messageCount"] = transcript["activityCount"],
                ["userMessages"] = transcript["userMessageCount"],
                ["botMessages"] = transcript["botMessageCount"],
                ["topicsTriggered"] = transcript["topicsTriggered"],
                ["wasEscalated"] = transcript["wasEscalated"]
            });
        }

        return new JObject
        {
            ["botId"] = botId,
            ["transcriptCount"] = result.Count,
            ["transcripts"] = result
        };
    }

    private async Task<JObject> ExecuteParseTranscriptToolAsync(JObject arguments)
    {
        var transcriptId = arguments.Value<string>("transcript_id");
        if (string.IsNullOrEmpty(transcriptId))
        {
            throw new ArgumentException("transcript_id is required");
        }

        var queryUrl = $"{GetBaseUrl()}/conversationtranscripts({transcriptId})";
        var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
        CopyAuthHeaders(request);

        var response = await Context.SendAsync(request, CancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to get transcript: {response.StatusCode}");
        }

        var transcript = JObject.Parse(await response.Content.ReadAsStringAsync());
        return ParseTranscriptToStructured(transcript);
    }

    // ========================================
    // JSON-RPC HELPERS
    // ========================================

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var responseObj = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseObj.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrWhiteSpace(data))
        {
            error["data"] = data;
        }

        var responseObj = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseObj.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    // ========================================
    // HELPER METHODS
    // ========================================

    /// <summary>
    /// Simple passthrough for operations that don't need transformation
    /// </summary>
    private async Task<HttpResponseMessage> HandlePassthroughAsync()
    {
        var response = await Context.SendAsync(Context.Request, CancellationToken);
        
        await LogTelemetryAsync("CopilotStudioAnalytics_Response", new Dictionary<string, string>
        {
            { "OperationId", Context.OperationId },
            { "StatusCode", ((int)response.StatusCode).ToString() },
            { "IsSuccess", response.IsSuccessStatusCode.ToString() }
        });
        
        return response;
    }

    /// <summary>
    /// Get the base URL for Dataverse API calls
    /// </summary>
    private string GetBaseUrl()
    {
        var uri = Context.Request.RequestUri;
        return $"{uri.Scheme}://{uri.Host}/api/data/v9.2";
    }

    /// <summary>
    /// Copy authorization headers from original request
    /// </summary>
    private void CopyAuthHeaders(HttpRequestMessage request)
    {
        if (Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = Context.Request.Headers.Authorization;
        }
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Get bot name by ID
    /// </summary>
    private async Task<string> GetBotNameAsync(string botId)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{GetBaseUrl()}/bots({botId})?$select=name");
            CopyAuthHeaders(request);
            var response = await Context.SendAsync(request, CancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var bot = JObject.Parse(await response.Content.ReadAsStringAsync());
                return bot["name"]?.ToString() ?? "Unknown Bot";
            }
        }
        catch { }
        return "Unknown Bot";
    }

    /// <summary>
    /// Create an error response
    /// </summary>
    private HttpResponseMessage CreateErrorResponse(HttpStatusCode statusCode, string message)
    {
        var error = new JObject
        {
            ["error"] = new JObject
            {
                ["code"] = statusCode.ToString(),
                ["message"] = message
            }
        };

        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                error.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            )
        };
    }

    /// <summary>
    /// Log telemetry to Application Insights using Context.SendAsync
    /// </summary>
    private async Task LogTelemetryAsync(string eventName, Dictionary<string, string> properties)
    {
        // Skip if no connection string configured
        if (string.IsNullOrEmpty(AppInsightsConnectionString))
            return;

        try
        {
            // Extract instrumentation key from connection string
            var instrumentationKey = "";
            foreach (var part in AppInsightsConnectionString.Split(';'))
            {
                if (part.StartsWith("InstrumentationKey="))
                {
                    instrumentationKey = part.Substring("InstrumentationKey=".Length);
                    break;
                }
            }

            if (string.IsNullOrEmpty(instrumentationKey))
                return;

            var telemetry = new JArray
            {
                new JObject
                {
                    ["name"] = "Microsoft.ApplicationInsights.Event",
                    ["time"] = DateTime.UtcNow.ToString("o"),
                    ["iKey"] = instrumentationKey,
                    ["data"] = new JObject
                    {
                        ["baseType"] = "EventData",
                        ["baseData"] = new JObject
                        {
                            ["name"] = eventName,
                            ["properties"] = JObject.FromObject(properties)
                        }
                    }
                }
            };

            // Create telemetry request using Context
            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, AppInsightsEndpoint);
            telemetryRequest.Content = new StringContent(
                telemetry.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            );
            
            // Send via Context - fire and forget
            await Context.SendAsync(telemetryRequest, CancellationToken);
        }
        catch
        {
            // Silently ignore telemetry errors
        }
    }
}
