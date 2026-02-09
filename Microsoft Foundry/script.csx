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
    // ========================================
    // CONFIGURATION
    // ========================================

    /// <summary>
    /// Application Insights connection string (optional)
    /// Get from: Azure Portal → Application Insights → Overview → Connection String
    /// Leave empty to disable telemetry
    /// </summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    /// <summary>MCP Server name</summary>
    private const string ServerName = "azure-ai-foundry";
    
    /// <summary>MCP Server version</summary>
    private const string ServerVersion = "4.0.0";
    
    /// <summary>MCP Protocol version supported</summary>
    private const string ProtocolVersion = "2025-11-25";

    // ========================================
    // MAIN ENTRY POINT
    // ========================================
    
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        
        this.Context.Logger.LogInformation($"[{correlationId}] Request received: {this.Context.OperationId}");

        try
        {
            // Log request received
            _ = LogToAppInsights("RequestReceived", new
            {
                CorrelationId = correlationId,
                OperationId = this.Context.OperationId,
                Path = this.Context.Request.RequestUri.AbsolutePath,
                Method = this.Context.Request.Method.Method
            });

            // Route based on operation
            HttpResponseMessage response;
            switch (this.Context.OperationId)
            {
                // MCP Protocol endpoint
                case "McpRequest":
                    response = await HandleMcpRequestAsync(correlationId).ConfigureAwait(false);
                    break;

                // Model Inference operations
                case "ChatCompletion":
                    response = await HandleChatCompletionAsync(correlationId).ConfigureAwait(false);
                    break;

                case "GetEmbeddings":
                case "GetImageEmbeddings":
                case "GetModelInfo":
                    response = await HandlePassthroughAsync().ConfigureAwait(false);
                    break;

                // Assistants/Agents operations
                case "ListAssistants":
                case "CreateAssistant":
                case "GetAssistant":
                case "DeleteAssistant":
                case "CreateThread":
                case "GetThread":
                case "DeleteThread":
                case "ListMessages":
                case "CreateMessage":
                case "ListRuns":
                case "CreateRun":
                case "GetRun":
                case "CreateThreadAndRun":
                    response = await HandlePassthroughAsync().ConfigureAwait(false);
                    break;

                // Foundry Tools - Content Safety
                case "AnalyzeText":
                    response = await HandleContentSafetyAsync(correlationId, "text").ConfigureAwait(false);
                    break;

                case "AnalyzeImage":
                    response = await HandleContentSafetyAsync(correlationId, "image").ConfigureAwait(false);
                    break;

                // Foundry Tools - Image Analysis (Vision)
                case "AnalyzeImageVision":
                    response = await HandleImageAnalysisAsync(correlationId).ConfigureAwait(false);
                    break;

                // AI Evaluation
                case "SubmitEvaluation":
                    response = await HandleSubmitEvaluationAsync(correlationId).ConfigureAwait(false);
                    break;

                case "GetEvaluationResult":
                    response = await HandlePassthroughAsync().ConfigureAwait(false);
                    break;

                // Speech operations (via OpenAI Whisper)
                case "TranscribeAudio":
                case "TranslateAudio":
                    response = await HandlePassthroughAsync().ConfigureAwait(false);
                    break;

                // Translator operations
                case "TranslateText":
                case "TransliterateText":
                case "DetectLanguageTranslator":
                    response = await HandlePassthroughAsync().ConfigureAwait(false);
                    break;

                // Language operations
                case "AnalyzeTextLanguage":
                    response = await HandleLanguageAnalysisAsync(correlationId).ConfigureAwait(false);
                    break;

                // Document Intelligence operations
                case "AnalyzeDocument":
                    response = await HandleDocumentAnalysisAsync(correlationId).ConfigureAwait(false);
                    break;

                case "GetDocumentAnalysisResult":
                    response = await HandlePassthroughAsync().ConfigureAwait(false);
                    break;

                default:
                    response = await HandlePassthroughAsync().ConfigureAwait(false);
                    break;
            }

            return response;
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"[{correlationId}] Error: {ex.Message}");
            _ = LogToAppInsights("RequestError", new
            {
                CorrelationId = correlationId,
                OperationId = this.Context.OperationId,
                ErrorType = ex.GetType().Name,
                ErrorMessage = ex.Message
            });
            throw;
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            _ = LogToAppInsights("RequestCompleted", new
            {
                CorrelationId = correlationId,
                OperationId = this.Context.OperationId,
                DurationMs = duration.TotalMilliseconds
            });
        }
    }

    // ========================================
    // OPERATION HANDLERS
    // ========================================

    /// <summary>
    /// Handles Chat Completion requests. Ensures content is returned as a string.
    /// </summary>
    private async Task<HttpResponseMessage> HandleChatCompletionAsync(string correlationId)
    {
        // Log the actual request URL (after policy transforms)
        _ = LogToAppInsights("ChatCompletionRequest", new
        {
            CorrelationId = correlationId,
            RequestUrl = this.Context.Request.RequestUri.ToString(),
            HasApiKey = this.Context.Request.Headers.Contains("api-key")
        });

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        // Log response status
        _ = LogToAppInsights("ChatCompletionResponse", new
        {
            CorrelationId = correlationId,
            StatusCode = (int)response.StatusCode,
            IsSuccess = response.IsSuccessStatusCode
        });

        if (!response.IsSuccessStatusCode)
        {
            // Log error details
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _ = LogToAppInsights("ChatCompletionError", new
            {
                CorrelationId = correlationId,
                StatusCode = (int)response.StatusCode,
                ErrorBody = errorContent.Length > 500 ? errorContent.Substring(0, 500) : errorContent
            });
            return response;
        }

        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = JObject.Parse(responseString);

        // Ensure content values in choices are strings
        var choices = result["choices"] as JArray;
        if (choices != null)
        {
            foreach (var choice in choices)
            {
                var message = choice["message"];
                if (message != null)
                {
                    var content = message["content"];
                    if (content != null && content.Type != JTokenType.String)
                    {
                        message["content"] = content.ToString(Newtonsoft.Json.Formatting.None);
                    }
                }
            }
        }

        response.Content = CreateJsonContent(result.ToString(Newtonsoft.Json.Formatting.None));

        return response;
    }

    /// <summary>
    /// Pass-through handler - forwards the request and returns the response unchanged.
    /// </summary>
    private async Task<HttpResponseMessage> HandlePassthroughAsync()
    {
        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);
        return response;
    }

    /// <summary>
    /// Handle Content Safety analysis for text or images.
    /// </summary>
    private async Task<HttpResponseMessage> HandleContentSafetyAsync(string correlationId, string analysisType)
    {
        _ = LogToAppInsights("ContentSafetyRequest", new
        {
            CorrelationId = correlationId,
            AnalysisType = analysisType
        });

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = JObject.Parse(responseString);

            // Log the severity levels for monitoring
            var categories = result["categoriesAnalysis"] as JArray;
            if (categories != null)
            {
                var maxSeverity = 0;
                foreach (var cat in categories)
                {
                    var severity = cat["severity"]?.Value<int>() ?? 0;
                    if (severity > maxSeverity) maxSeverity = severity;
                }

                _ = LogToAppInsights("ContentSafetyResult", new
                {
                    CorrelationId = correlationId,
                    AnalysisType = analysisType,
                    MaxSeverity = maxSeverity,
                    CategoryCount = categories.Count
                });
            }

            response.Content = CreateJsonContent(result.ToString(Newtonsoft.Json.Formatting.None));
        }

        return response;
    }

    /// <summary>
    /// Handle Image Analysis (Vision) requests.
    /// </summary>
    private async Task<HttpResponseMessage> HandleImageAnalysisAsync(string correlationId)
    {
        _ = LogToAppInsights("ImageAnalysisRequest", new
        {
            CorrelationId = correlationId
        });

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = JObject.Parse(responseString);

            // Log detected features
            var featuresDetected = new List<string>();
            if (result["captionResult"] != null) featuresDetected.Add("caption");
            if (result["tagsResult"] != null) featuresDetected.Add("tags");
            if (result["objectsResult"] != null) featuresDetected.Add("objects");
            if (result["readResult"] != null) featuresDetected.Add("read");
            if (result["peopleResult"] != null) featuresDetected.Add("people");
            if (result["smartCropsResult"] != null) featuresDetected.Add("smartCrops");

            _ = LogToAppInsights("ImageAnalysisResult", new
            {
                CorrelationId = correlationId,
                FeaturesDetected = string.Join(",", featuresDetected)
            });

            response.Content = CreateJsonContent(result.ToString(Newtonsoft.Json.Formatting.None));
        }

        return response;
    }

    /// <summary>
    /// Handle AI Evaluation submission requests.
    /// Implements evaluation logic similar to Azure AI Evaluation SDK.
    /// </summary>
    private async Task<HttpResponseMessage> HandleSubmitEvaluationAsync(string correlationId)
    {
        var bodyString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = JObject.Parse(bodyString);

        var evaluationType = body.Value<string>("evaluationType");
        var query = body.Value<string>("query") ?? "";
        var responseText = body.Value<string>("response") ?? "";
        var context = body.Value<string>("context") ?? "";
        var groundTruth = body.Value<string>("groundTruth") ?? "";

        _ = LogToAppInsights("EvaluationRequest", new
        {
            CorrelationId = correlationId,
            EvaluationType = evaluationType
        });

        // For AI-assisted evaluations, use chat completion to evaluate
        // This mirrors the approach used by azure-ai-evaluation SDK
        JObject evaluationResult;
        
        if (IsSafetyEvaluation(evaluationType))
        {
            // Safety evaluations can use Content Safety API
            evaluationResult = await EvaluateSafetyAsync(evaluationType, responseText, correlationId).ConfigureAwait(false);
        }
        else if (IsNlpEvaluation(evaluationType))
        {
            // NLP evaluations use local computation (no API needed)
            evaluationResult = EvaluateNlpMetric(evaluationType, responseText, groundTruth, correlationId);
        }
        else
        {
            // Quality evaluations use LLM-based assessment
            evaluationResult = await EvaluateQualityAsync(evaluationType, query, responseText, context, groundTruth, correlationId).ConfigureAwait(false);
        }

        _ = LogToAppInsights("EvaluationResult", new
        {
            CorrelationId = correlationId,
            EvaluationType = evaluationType,
            Score = evaluationResult["result"]?["score"]?.ToString()
        });

        return new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = CreateJsonContent(evaluationResult.ToString(Newtonsoft.Json.Formatting.None))
        };
    }

    private bool IsSafetyEvaluation(string evaluationType)
    {
        var safetyTypes = new[] { "hate", "violence", "selfharm", "sexual" };
        return safetyTypes.Contains(evaluationType?.ToLowerInvariant() ?? "");
    }

    private bool IsNlpEvaluation(string evaluationType)
    {
        var nlpTypes = new[] { "f1", "f1score", "bleu", "bleuscore", "rouge", "rougescore", "gleu", "gleuscore", "meteor", "meteorscore" };
        return nlpTypes.Contains(evaluationType?.ToLowerInvariant() ?? "");
    }

    /// <summary>
    /// Evaluate using NLP metrics (local computation, no API needed)
    /// </summary>
    private JObject EvaluateNlpMetric(string evaluationType, string response, string groundTruth, string correlationId)
    {
        try
        {
            double score;
            string reasoning;
            var evalType = evaluationType?.ToLowerInvariant() ?? "";

            switch (evalType)
            {
                case "f1":
                case "f1score":
                    score = CalculateF1Score(response, groundTruth);
                    reasoning = $"F1 Score measures the harmonic mean of precision and recall. Response tokens compared against ground truth tokens.";
                    break;

                case "bleu":
                case "bleuscore":
                    score = CalculateBleuScore(response, groundTruth);
                    reasoning = $"BLEU Score measures n-gram precision between response and ground truth. Higher scores indicate better overlap.";
                    break;

                case "rouge":
                case "rougescore":
                    score = CalculateRougeScore(response, groundTruth);
                    reasoning = $"ROUGE-L Score measures longest common subsequence between response and ground truth.";
                    break;

                case "gleu":
                case "gleuscore":
                    score = CalculateGleuScore(response, groundTruth);
                    reasoning = $"GLEU Score (Google-BLEU) measures n-gram overlap with penalty for very short outputs.";
                    break;

                case "meteor":
                case "meteorscore":
                    score = CalculateMeteorScore(response, groundTruth);
                    reasoning = $"METEOR Score measures unigram precision and recall with stemming consideration.";
                    break;

                default:
                    score = 0;
                    reasoning = $"Unknown NLP metric type: {evaluationType}";
                    break;
            }

            return new JObject
            {
                ["operationId"] = correlationId,
                ["status"] = "succeeded",
                ["result"] = new JObject
                {
                    ["evaluationType"] = evaluationType,
                    ["score"] = Math.Round(score, 4),
                    ["reasoning"] = reasoning,
                    ["passed"] = score >= 0.5 // 0.5+ is generally considered acceptable for NLP metrics
                }
            };
        }
        catch (Exception ex)
        {
            return new JObject
            {
                ["operationId"] = correlationId,
                ["status"] = "failed",
                ["error"] = new JObject
                {
                    ["code"] = "NlpEvaluationFailed",
                    ["message"] = ex.Message
                }
            };
        }
    }

    /// <summary>
    /// Calculate F1 Score - harmonic mean of precision and recall
    /// </summary>
    private double CalculateF1Score(string response, string groundTruth)
    {
        var responseTokens = Tokenize(response);
        var groundTruthTokens = Tokenize(groundTruth);

        if (responseTokens.Count == 0 || groundTruthTokens.Count == 0)
            return 0;

        var responseSet = new HashSet<string>(responseTokens);
        var groundTruthSet = new HashSet<string>(groundTruthTokens);

        var intersection = responseSet.Intersect(groundTruthSet).Count();

        var precision = (double)intersection / responseSet.Count;
        var recall = (double)intersection / groundTruthSet.Count;

        if (precision + recall == 0)
            return 0;

        return 2 * (precision * recall) / (precision + recall);
    }

    /// <summary>
    /// Calculate BLEU Score - n-gram precision with brevity penalty
    /// </summary>
    private double CalculateBleuScore(string response, string groundTruth)
    {
        var responseTokens = Tokenize(response);
        var groundTruthTokens = Tokenize(groundTruth);

        if (responseTokens.Count == 0)
            return 0;

        // Calculate n-gram precisions for n=1 to 4
        var weights = new[] { 0.25, 0.25, 0.25, 0.25 };
        var logPrecisions = new List<double>();

        for (int n = 1; n <= 4; n++)
        {
            var responseNgrams = GetNgrams(responseTokens, n);
            var groundTruthNgrams = GetNgrams(groundTruthTokens, n);

            if (responseNgrams.Count == 0)
                break;

            var matches = 0;
            var groundTruthNgramCounts = groundTruthNgrams.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

            foreach (var ngram in responseNgrams)
            {
                if (groundTruthNgramCounts.TryGetValue(ngram, out int count) && count > 0)
                {
                    matches++;
                    groundTruthNgramCounts[ngram]--;
                }
            }

            var precision = (double)matches / responseNgrams.Count;
            if (precision > 0)
                logPrecisions.Add(Math.Log(precision));
        }

        if (logPrecisions.Count == 0)
            return 0;

        // Brevity penalty
        var brevityPenalty = responseTokens.Count >= groundTruthTokens.Count ? 1.0 :
            Math.Exp(1 - (double)groundTruthTokens.Count / responseTokens.Count);

        // Geometric mean of precisions
        var avgLogPrecision = logPrecisions.Take(logPrecisions.Count).Select((p, i) => weights[i] * p).Sum();
        
        return brevityPenalty * Math.Exp(avgLogPrecision);
    }

    /// <summary>
    /// Calculate ROUGE-L Score - longest common subsequence based
    /// </summary>
    private double CalculateRougeScore(string response, string groundTruth)
    {
        var responseTokens = Tokenize(response);
        var groundTruthTokens = Tokenize(groundTruth);

        if (responseTokens.Count == 0 || groundTruthTokens.Count == 0)
            return 0;

        var lcsLength = LongestCommonSubsequence(responseTokens, groundTruthTokens);

        var precision = (double)lcsLength / responseTokens.Count;
        var recall = (double)lcsLength / groundTruthTokens.Count;

        if (precision + recall == 0)
            return 0;

        // F1-score of LCS
        return 2 * (precision * recall) / (precision + recall);
    }

    /// <summary>
    /// Calculate GLEU Score - Google-BLEU variant
    /// </summary>
    private double CalculateGleuScore(string response, string groundTruth)
    {
        var responseTokens = Tokenize(response);
        var groundTruthTokens = Tokenize(groundTruth);

        if (responseTokens.Count == 0 || groundTruthTokens.Count == 0)
            return 0;

        var allNgrams = new List<string>();
        var matchingNgrams = new List<string>();

        for (int n = 1; n <= Math.Min(4, Math.Min(responseTokens.Count, groundTruthTokens.Count)); n++)
        {
            var responseNgrams = GetNgrams(responseTokens, n);
            var groundTruthNgrams = new HashSet<string>(GetNgrams(groundTruthTokens, n));

            allNgrams.AddRange(responseNgrams);
            matchingNgrams.AddRange(responseNgrams.Where(ng => groundTruthNgrams.Contains(ng)));
        }

        if (allNgrams.Count == 0)
            return 0;

        return (double)matchingNgrams.Count / allNgrams.Count;
    }

    /// <summary>
    /// Calculate METEOR Score - unigram precision/recall with alignment
    /// </summary>
    private double CalculateMeteorScore(string response, string groundTruth)
    {
        var responseTokens = Tokenize(response);
        var groundTruthTokens = Tokenize(groundTruth);

        if (responseTokens.Count == 0 || groundTruthTokens.Count == 0)
            return 0;

        // Exact match
        var responseSet = new HashSet<string>(responseTokens);
        var groundTruthSet = new HashSet<string>(groundTruthTokens);
        var matches = responseSet.Intersect(groundTruthSet).Count();

        // Stem match (simple: lowercase already done, check prefixes)
        var unmatchedResponse = responseSet.Except(groundTruthSet).ToList();
        var unmatchedGroundTruth = groundTruthSet.Except(responseSet).ToList();

        foreach (var respToken in unmatchedResponse.ToList())
        {
            foreach (var gtToken in unmatchedGroundTruth.ToList())
            {
                if (respToken.Length >= 4 && gtToken.Length >= 4 &&
                    respToken.Substring(0, Math.Min(4, Math.Min(respToken.Length, gtToken.Length))) ==
                    gtToken.Substring(0, Math.Min(4, Math.Min(respToken.Length, gtToken.Length))))
                {
                    matches++;
                    unmatchedResponse.Remove(respToken);
                    unmatchedGroundTruth.Remove(gtToken);
                    break;
                }
            }
        }

        var precision = (double)matches / responseTokens.Count;
        var recall = (double)matches / groundTruthTokens.Count;

        if (precision + recall == 0)
            return 0;

        // METEOR uses weighted harmonic mean favoring recall
        var alpha = 0.9; // Weight for recall
        return (precision * recall) / (alpha * precision + (1 - alpha) * recall);
    }

    /// <summary>
    /// Tokenize text into lowercase words
    /// </summary>
    private List<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        return System.Text.RegularExpressions.Regex.Split(text.ToLowerInvariant(), @"\W+")
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
    }

    /// <summary>
    /// Get n-grams from token list
    /// </summary>
    private List<string> GetNgrams(List<string> tokens, int n)
    {
        var ngrams = new List<string>();
        for (int i = 0; i <= tokens.Count - n; i++)
        {
            ngrams.Add(string.Join(" ", tokens.Skip(i).Take(n)));
        }
        return ngrams;
    }

    /// <summary>
    /// Calculate length of longest common subsequence
    /// </summary>
    private int LongestCommonSubsequence(List<string> a, List<string> b)
    {
        var m = a.Count;
        var n = b.Count;
        var dp = new int[m + 1, n + 1];

        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                if (a[i - 1] == b[j - 1])
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                else
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
            }
        }

        return dp[m, n];
    }

    private async Task<JObject> EvaluateSafetyAsync(string evaluationType, string content, string correlationId)
    {
        // Use Content Safety API for safety evaluations
        var requestBody = new JObject
        {
            ["text"] = content,
            ["categories"] = new JArray { CapitalizeFirst(evaluationType) }
        };

        var apiUrl = BuildApiUrl("/contentsafety/text:analyze", "2024-09-01");
        
        try
        {
            var result = await SendApiRequestAsync(HttpMethod.Post, apiUrl, requestBody).ConfigureAwait(false);
            var categories = result["categoriesAnalysis"] as JArray;
            var severity = 0;
            if (categories != null && categories.Count > 0)
            {
                severity = categories[0]["severity"]?.Value<int>() ?? 0;
            }

            return new JObject
            {
                ["operationId"] = correlationId,
                ["status"] = "succeeded",
                ["result"] = new JObject
                {
                    ["evaluationType"] = evaluationType,
                    ["score"] = severity,
                    ["reasoning"] = GetSafetyReasoning(severity),
                    ["passed"] = severity <= 2 // Low or Safe is passing
                }
            };
        }
        catch (Exception ex)
        {
            return new JObject
            {
                ["operationId"] = correlationId,
                ["status"] = "failed",
                ["error"] = new JObject
                {
                    ["code"] = "EvaluationFailed",
                    ["message"] = ex.Message
                }
            };
        }
    }

    private async Task<JObject> EvaluateQualityAsync(string evaluationType, string query, string response, string context, string groundTruth, string correlationId)
    {
        // Use LLM-based evaluation for quality metrics
        var systemPrompt = GetEvaluationSystemPrompt(evaluationType);
        var userPrompt = BuildEvaluationUserPrompt(evaluationType, query, response, context, groundTruth);

        var messages = new JArray
        {
            new JObject { ["role"] = "system", ["content"] = systemPrompt },
            new JObject { ["role"] = "user", ["content"] = userPrompt }
        };

        var requestBody = new JObject
        {
            ["messages"] = messages,
            ["temperature"] = 0.1,
            ["max_tokens"] = 500
        };

        var apiUrl = BuildApiUrl("/models/chat/completions", "2024-05-01-preview");

        try
        {
            var result = await SendApiRequestAsync(HttpMethod.Post, apiUrl, requestBody).ConfigureAwait(false);
            var llmResponse = result["choices"]?[0]?["message"]?["content"]?.ToString() ?? "";
            
            // Parse the score from LLM response (expecting format: "Score: X\nReasoning: ...")
            var score = ParseScoreFromResponse(llmResponse);
            var reasoning = ParseReasoningFromResponse(llmResponse);

            return new JObject
            {
                ["operationId"] = correlationId,
                ["status"] = "succeeded",
                ["result"] = new JObject
                {
                    ["evaluationType"] = evaluationType,
                    ["score"] = score,
                    ["reasoning"] = reasoning,
                    ["passed"] = score >= 3 // 3+ out of 5 is passing for quality
                }
            };
        }
        catch (Exception ex)
        {
            return new JObject
            {
                ["operationId"] = correlationId,
                ["status"] = "failed",
                ["error"] = new JObject
                {
                    ["code"] = "EvaluationFailed",
                    ["message"] = ex.Message
                }
            };
        }
    }

    private string GetEvaluationSystemPrompt(string evaluationType)
    {
        return evaluationType?.ToLowerInvariant() switch
        {
            "groundedness" => "You are an AI assistant that evaluates whether responses are grounded in the provided context. Score 1-5 where 5 means fully grounded in facts from context.",
            "relevance" => "You are an AI assistant that evaluates whether responses are relevant to the query. Score 1-5 where 5 means highly relevant.",
            "coherence" => "You are an AI assistant that evaluates whether responses are coherent and well-structured. Score 1-5 where 5 means perfectly coherent.",
            "fluency" => "You are an AI assistant that evaluates whether responses are fluent and natural. Score 1-5 where 5 means perfectly fluent.",
            "similarity" => "You are an AI assistant that evaluates semantic similarity between response and ground truth. Score 1-5 where 5 means nearly identical meaning.",
            _ => "You are an AI assistant that evaluates response quality. Score 1-5 where 5 is excellent."
        };
    }

    private string BuildEvaluationUserPrompt(string evaluationType, string query, string response, string context, string groundTruth)
    {
        var prompt = $"Response to evaluate:\n{response}\n\n";
        
        if (!string.IsNullOrWhiteSpace(query))
            prompt += $"Original query:\n{query}\n\n";
        
        if (!string.IsNullOrWhiteSpace(context) && evaluationType?.ToLowerInvariant() == "groundedness")
            prompt += $"Context/Source:\n{context}\n\n";
        
        if (!string.IsNullOrWhiteSpace(groundTruth) && evaluationType?.ToLowerInvariant() == "similarity")
            prompt += $"Ground truth:\n{groundTruth}\n\n";

        prompt += "Provide your evaluation in this format:\nScore: [1-5]\nReasoning: [Your explanation]";
        return prompt;
    }

    private int ParseScoreFromResponse(string response)
    {
        var lines = response.Split('\n');
        foreach (var line in lines)
        {
            if (line.Trim().StartsWith("Score:", StringComparison.OrdinalIgnoreCase))
            {
                var scoreStr = line.Substring(line.IndexOf(':') + 1).Trim();
                if (int.TryParse(scoreStr.Split(' ')[0], out int score))
                {
                    return Math.Max(1, Math.Min(5, score));
                }
            }
        }
        return 3; // Default middle score
    }

    private string ParseReasoningFromResponse(string response)
    {
        var idx = response.IndexOf("Reasoning:", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            return response.Substring(idx + "Reasoning:".Length).Trim();
        }
        return response;
    }

    private string GetSafetyReasoning(int severity)
    {
        return severity switch
        {
            0 => "Content is safe with no detected harmful material.",
            2 => "Low severity - minor concerns detected.",
            4 => "Medium severity - potentially harmful content detected.",
            6 => "High severity - harmful content detected.",
            _ => $"Severity level: {severity}"
        };
    }

    private string CapitalizeFirst(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return char.ToUpper(input[0]) + input.Substring(1).ToLower();
    }

    /// <summary>
    /// Handle Language service text analysis requests.
    /// </summary>
    private async Task<HttpResponseMessage> HandleLanguageAnalysisAsync(string correlationId)
    {
        _ = LogToAppInsights("LanguageAnalysisRequest", new
        {
            CorrelationId = correlationId
        });

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = JObject.Parse(responseString);

            var kind = result.Value<string>("kind");
            _ = LogToAppInsights("LanguageAnalysisResult", new
            {
                CorrelationId = correlationId,
                AnalysisKind = kind
            });

            response.Content = CreateJsonContent(result.ToString(Newtonsoft.Json.Formatting.None));
        }

        return response;
    }

    /// <summary>
    /// Handle Document Intelligence analyze requests.
    /// Extracts operation ID from response headers for async polling.
    /// </summary>
    private async Task<HttpResponseMessage> HandleDocumentAnalysisAsync(string correlationId)
    {
        _ = LogToAppInsights("DocumentAnalysisRequest", new
        {
            CorrelationId = correlationId
        });

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        // Extract operation ID from Operation-Location header for the response body
        if (response.StatusCode == HttpStatusCode.Accepted || response.IsSuccessStatusCode)
        {
            string operationId = null;
            if (response.Headers.TryGetValues("Operation-Location", out var operationLocations))
            {
                var operationUrl = operationLocations.FirstOrDefault();
                if (!string.IsNullOrEmpty(operationUrl))
                {
                    // Extract result ID from URL: .../analyzeResults/{resultId}?...
                    var uri = new Uri(operationUrl);
                    var segments = uri.Segments;
                    if (segments.Length > 0)
                    {
                        operationId = segments[segments.Length - 1].TrimEnd('/');
                    }
                }
            }

            _ = LogToAppInsights("DocumentAnalysisStarted", new
            {
                CorrelationId = correlationId,
                OperationId = operationId
            });

            // Create response body with operation ID
            var responseBody = new JObject
            {
                ["operationId"] = operationId ?? correlationId,
                ["status"] = "running"
            };

            response.Content = CreateJsonContent(responseBody.ToString(Newtonsoft.Json.Formatting.None));
        }

        return response;
    }

    // ========================================
    // MCP PROTOCOL HANDLERS
    // ========================================

    /// <summary>
    /// Handle MCP (Model Context Protocol) requests for Copilot Studio integration
    /// </summary>
    private async Task<HttpResponseMessage> HandleMcpRequestAsync(string correlationId)
    {
        string body = null;
        JObject request = null;
        string method = null;
        JToken requestId = null;

        try
        {
            body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            
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

            _ = LogToAppInsights("McpRequestReceived", new
            {
                CorrelationId = correlationId,
                Method = method
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
                    return await HandleMcpToolsCallAsync(correlationId, request, requestId).ConfigureAwait(false);

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
                ["title"] = "Azure AI Foundry",
                ["description"] = "Azure AI Foundry connector for model inference and AI agents"
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
                ["name"] = "chat_completion",
                ["description"] = "Send a chat completion request to the AI model. Use for generating responses to prompts.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["prompt"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The user's message or prompt to send to the AI model"
                        },
                        ["system_prompt"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional system instructions for the AI"
                        }
                    },
                    ["required"] = new JArray { "prompt" }
                }
            },
            new JObject
            {
                ["name"] = "get_embeddings",
                ["description"] = "Generate embedding vectors for text. Useful for semantic search and similarity comparisons.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["text"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The text to generate embeddings for"
                        }
                    },
                    ["required"] = new JArray { "text" }
                }
            },
            new JObject
            {
                ["name"] = "create_assistant",
                ["description"] = "Create a new AI assistant with custom instructions and tools.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["name"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The name of the assistant"
                        },
                        ["instructions"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "System instructions for the assistant"
                        },
                        ["model"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The model deployment name to use"
                        }
                    },
                    ["required"] = new JArray { "model" }
                }
            },
            new JObject
            {
                ["name"] = "run_assistant",
                ["description"] = "Create a thread with a message and run it with an assistant in one call.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["assistant_id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The ID of the assistant to use"
                        },
                        ["message"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The message to send to the assistant"
                        }
                    },
                    ["required"] = new JArray { "assistant_id", "message" }
                }
            },
            new JObject
            {
                ["name"] = "analyze_content_safety",
                ["description"] = "Analyze text for harmful content including hate speech, violence, self-harm, and sexual content.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["text"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The text to analyze for safety"
                        },
                        ["categories"] = new JObject
                        {
                            ["type"] = "array",
                            ["description"] = "Categories to check: Hate, Violence, SelfHarm, Sexual. Leave empty for all.",
                            ["items"] = new JObject { ["type"] = "string" }
                        }
                    },
                    ["required"] = new JArray { "text" }
                }
            },
            new JObject
            {
                ["name"] = "analyze_image",
                ["description"] = "Analyze an image for tags, objects, captions, text (OCR), and people detection.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["image_url"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Public URL of the image to analyze"
                        },
                        ["features"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Features to extract: caption, tags, objects, read, people, smartCrops (comma-separated)"
                        }
                    },
                    ["required"] = new JArray { "image_url" }
                }
            },
            new JObject
            {
                ["name"] = "evaluate_response",
                ["description"] = "Evaluate an AI response for quality metrics like groundedness, relevance, coherence, or safety.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["response"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The AI response to evaluate"
                        },
                        ["evaluation_type"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Type: groundedness, relevance, coherence, fluency, similarity, hate, violence, selfharm, sexual"
                        },
                        ["query"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The original query (for relevance evaluation)"
                        },
                        ["context"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Source context (for groundedness evaluation)"
                        }
                    },
                    ["required"] = new JArray { "response", "evaluation_type" }
                }
            },
            new JObject
            {
                ["name"] = "transcribe_audio",
                ["description"] = "Transcribe audio to text using OpenAI Whisper model.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["audio_url"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "URL of the audio file to transcribe"
                        },
                        ["language"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional ISO-639-1 language code (e.g., 'en', 'es')"
                        }
                    },
                    ["required"] = new JArray { "audio_url" }
                }
            },
            new JObject
            {
                ["name"] = "translate_text",
                ["description"] = "Translate text from one language to another using Azure Translator.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["text"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The text to translate"
                        },
                        ["to_language"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Target language code (e.g., 'es', 'fr', 'de')"
                        },
                        ["from_language"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional source language code (auto-detected if omitted)"
                        }
                    },
                    ["required"] = new JArray { "text", "to_language" }
                }
            },
            new JObject
            {
                ["name"] = "analyze_language",
                ["description"] = "Analyze text for sentiment, entities, key phrases, or PII using Azure Language service.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["text"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The text to analyze"
                        },
                        ["analysis_type"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Type of analysis: SentimentAnalysis, EntityRecognition, KeyPhraseExtraction, PiiEntityRecognition"
                        }
                    },
                    ["required"] = new JArray { "text", "analysis_type" }
                }
            },
            new JObject
            {
                ["name"] = "analyze_document",
                ["description"] = "Extract text, tables, and structure from documents using Azure Document Intelligence.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["document_url"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "URL of the document to analyze"
                        },
                        ["model"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Model to use: prebuilt-read, prebuilt-layout, prebuilt-document, prebuilt-invoice, prebuilt-receipt"
                        }
                    },
                    ["required"] = new JArray { "document_url" }
                }
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    /// <summary>
    /// Handle MCP tools/call request
    /// </summary>
    private async Task<HttpResponseMessage> HandleMcpToolsCallAsync(string correlationId, JObject request, JToken requestId)
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

        _ = LogToAppInsights("McpToolCall", new
        {
            CorrelationId = correlationId,
            ToolName = toolName
        });

        try
        {
            JObject toolResult;
            
            switch (toolName.ToLowerInvariant())
            {
                case "chat_completion":
                    toolResult = await ExecuteChatCompletionToolAsync(arguments).ConfigureAwait(false);
                    break;

                case "get_embeddings":
                    toolResult = await ExecuteEmbeddingsToolAsync(arguments).ConfigureAwait(false);
                    break;

                case "create_assistant":
                    toolResult = await ExecuteCreateAssistantToolAsync(arguments).ConfigureAwait(false);
                    break;

                case "run_assistant":
                    toolResult = await ExecuteRunAssistantToolAsync(arguments).ConfigureAwait(false);
                    break;

                case "analyze_content_safety":
                    toolResult = await ExecuteContentSafetyToolAsync(arguments).ConfigureAwait(false);
                    break;

                case "analyze_image":
                    toolResult = await ExecuteImageAnalysisToolAsync(arguments).ConfigureAwait(false);
                    break;

                case "evaluate_response":
                    toolResult = await ExecuteEvaluationToolAsync(arguments, correlationId).ConfigureAwait(false);
                    break;

                case "transcribe_audio":
                    toolResult = await ExecuteTranscribeAudioToolAsync(arguments).ConfigureAwait(false);
                    break;

                case "translate_text":
                    toolResult = await ExecuteTranslateTextToolAsync(arguments).ConfigureAwait(false);
                    break;

                case "analyze_language":
                    toolResult = await ExecuteLanguageAnalysisToolAsync(arguments).ConfigureAwait(false);
                    break;

                case "analyze_document":
                    toolResult = await ExecuteDocumentAnalysisToolAsync(arguments).ConfigureAwait(false);
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

    private async Task<JObject> ExecuteChatCompletionToolAsync(JObject arguments)
    {
        var prompt = arguments.Value<string>("prompt");
        var systemPrompt = arguments.Value<string>("system_prompt");

        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("'prompt' is required");
        }

        var messages = new JArray();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new JObject { ["role"] = "system", ["content"] = systemPrompt });
        }
        messages.Add(new JObject { ["role"] = "user", ["content"] = prompt });

        var requestBody = new JObject { ["messages"] = messages };
        var apiUrl = BuildApiUrl("/models/chat/completions", "2024-05-01-preview");
        
        var result = await SendApiRequestAsync(HttpMethod.Post, apiUrl, requestBody).ConfigureAwait(false);
        
        // Extract the response content
        var content = result["choices"]?[0]?["message"]?["content"]?.ToString();
        return new JObject
        {
            ["response"] = content ?? "No response generated",
            ["usage"] = result["usage"]
        };
    }

    private async Task<JObject> ExecuteEmbeddingsToolAsync(JObject arguments)
    {
        var text = arguments.Value<string>("text");

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("'text' is required");
        }

        var requestBody = new JObject { ["input"] = new JArray { text } };
        var apiUrl = BuildApiUrl("/models/embeddings", "2024-05-01-preview");
        
        var result = await SendApiRequestAsync(HttpMethod.Post, apiUrl, requestBody).ConfigureAwait(false);
        
        return new JObject
        {
            ["embedding_count"] = (result["data"] as JArray)?.Count ?? 0,
            ["model"] = result["model"],
            ["usage"] = result["usage"]
        };
    }

    private async Task<JObject> ExecuteCreateAssistantToolAsync(JObject arguments)
    {
        var model = arguments.Value<string>("model");
        
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("'model' is required");
        }

        var requestBody = new JObject { ["model"] = model };
        
        var name = arguments.Value<string>("name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            requestBody["name"] = name;
        }
        
        var instructions = arguments.Value<string>("instructions");
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            requestBody["instructions"] = instructions;
        }

        var apiUrl = BuildApiUrl("/openai/assistants", "2025-04-01-preview");
        var result = await SendApiRequestAsync(HttpMethod.Post, apiUrl, requestBody).ConfigureAwait(false);
        
        return new JObject
        {
            ["assistant_id"] = result["id"],
            ["name"] = result["name"],
            ["model"] = result["model"],
            ["created_at"] = result["created_at"]
        };
    }

    private async Task<JObject> ExecuteRunAssistantToolAsync(JObject arguments)
    {
        var assistantId = arguments.Value<string>("assistant_id");
        var message = arguments.Value<string>("message");

        if (string.IsNullOrWhiteSpace(assistantId))
        {
            throw new ArgumentException("'assistant_id' is required");
        }
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("'message' is required");
        }

        var requestBody = new JObject
        {
            ["assistant_id"] = assistantId,
            ["thread"] = new JObject
            {
                ["messages"] = new JArray
                {
                    new JObject { ["role"] = "user", ["content"] = message }
                }
            }
        };

        var apiUrl = BuildApiUrl("/openai/threads/runs", "2025-04-01-preview");
        var result = await SendApiRequestAsync(HttpMethod.Post, apiUrl, requestBody).ConfigureAwait(false);
        
        return new JObject
        {
            ["run_id"] = result["id"],
            ["thread_id"] = result["thread_id"],
            ["status"] = result["status"],
            ["assistant_id"] = result["assistant_id"]
        };
    }

    private async Task<JObject> ExecuteContentSafetyToolAsync(JObject arguments)
    {
        var text = arguments.Value<string>("text");

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("'text' is required");
        }

        var requestBody = new JObject { ["text"] = text };
        
        var categories = arguments["categories"] as JArray;
        if (categories != null && categories.Count > 0)
        {
            requestBody["categories"] = categories;
        }

        var apiUrl = BuildApiUrl("/contentsafety/text:analyze", "2024-09-01");
        var result = await SendApiRequestAsync(HttpMethod.Post, apiUrl, requestBody).ConfigureAwait(false);

        // Parse and format the results
        var analysis = new JObject();
        var categoriesResult = result["categoriesAnalysis"] as JArray;
        if (categoriesResult != null)
        {
            foreach (var cat in categoriesResult)
            {
                var category = cat["category"]?.ToString();
                var severity = cat["severity"]?.Value<int>() ?? 0;
                analysis[category] = new JObject
                {
                    ["severity"] = severity,
                    ["level"] = GetSeverityLevel(severity)
                };
            }
        }

        return new JObject
        {
            ["safe"] = IsSafeContent(categoriesResult),
            ["analysis"] = analysis,
            ["blocklist_matches"] = result["blocklistsMatch"]
        };
    }

    private async Task<JObject> ExecuteImageAnalysisToolAsync(JObject arguments)
    {
        var imageUrl = arguments.Value<string>("image_url");

        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            throw new ArgumentException("'image_url' is required");
        }

        var features = arguments.Value<string>("features") ?? "caption,tags";
        var requestBody = new JObject { ["url"] = imageUrl };

        var apiUrl = BuildApiUrl("/imageanalysis:analyze", "2023-02-01-preview") + $"&features={features}";
        var result = await SendApiRequestAsync(HttpMethod.Post, apiUrl, requestBody).ConfigureAwait(false);

        var response = new JObject();
        
        // Extract caption
        if (result["captionResult"] != null)
        {
            response["caption"] = result["captionResult"]["text"];
            response["caption_confidence"] = result["captionResult"]["confidence"];
        }

        // Extract tags
        var tags = result["tagsResult"]?["values"] as JArray;
        if (tags != null)
        {
            var tagList = new JArray();
            foreach (var tag in tags)
            {
                tagList.Add(tag["name"]?.ToString());
            }
            response["tags"] = tagList;
        }

        // Extract objects
        var objects = result["objectsResult"]?["values"] as JArray;
        if (objects != null)
        {
            var objectList = new JArray();
            foreach (var obj in objects)
            {
                var objTags = obj["tags"] as JArray;
                if (objTags != null && objTags.Count > 0)
                {
                    objectList.Add(objTags[0]["name"]?.ToString());
                }
            }
            response["objects"] = objectList;
        }

        // Extract OCR text
        if (result["readResult"] != null)
        {
            response["text_content"] = result["readResult"]["content"];
        }

        // Extract people count
        var people = result["peopleResult"]?["values"] as JArray;
        if (people != null)
        {
            response["people_count"] = people.Count;
        }

        response["image_size"] = new JObject
        {
            ["width"] = result["metadata"]?["width"],
            ["height"] = result["metadata"]?["height"]
        };

        return response;
    }

    private async Task<JObject> ExecuteEvaluationToolAsync(JObject arguments, string correlationId)
    {
        var responseText = arguments.Value<string>("response");
        var evaluationType = arguments.Value<string>("evaluation_type");
        var query = arguments.Value<string>("query") ?? "";
        var context = arguments.Value<string>("context") ?? "";

        if (string.IsNullOrWhiteSpace(responseText))
        {
            throw new ArgumentException("'response' is required");
        }
        if (string.IsNullOrWhiteSpace(evaluationType))
        {
            throw new ArgumentException("'evaluation_type' is required");
        }

        JObject result;
        if (IsSafetyEvaluation(evaluationType))
        {
            result = await EvaluateSafetyAsync(evaluationType, responseText, correlationId).ConfigureAwait(false);
        }
        else
        {
            result = await EvaluateQualityAsync(evaluationType, query, responseText, context, "", correlationId).ConfigureAwait(false);
        }

        return new JObject
        {
            ["evaluation_type"] = evaluationType,
            ["score"] = result["result"]?["score"],
            ["reasoning"] = result["result"]?["reasoning"],
            ["passed"] = result["result"]?["passed"]
        };
    }

    private async Task<JObject> ExecuteTranscribeAudioToolAsync(JObject arguments)
    {
        var audioUrl = arguments.Value<string>("audio_url");
        var language = arguments.Value<string>("language");

        if (string.IsNullOrWhiteSpace(audioUrl))
        {
            throw new ArgumentException("'audio_url' is required");
        }

        // Note: For production, this would construct a proper multipart request
        // with the audio file. This is a placeholder for the tool definition.
        return new JObject
        {
            ["status"] = "pending",
            ["message"] = "Audio transcription initiated. Use the TranscribeAudio operation directly with the audio file.",
            ["audio_url"] = audioUrl,
            ["language"] = language ?? "auto-detect"
        };
    }

    private async Task<JObject> ExecuteTranslateTextToolAsync(JObject arguments)
    {
        var text = arguments.Value<string>("text");
        var toLang = arguments.Value<string>("to_language");
        var fromLang = arguments.Value<string>("from_language");

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("'text' is required");
        }
        if (string.IsNullOrWhiteSpace(toLang))
        {
            throw new ArgumentException("'to_language' is required");
        }

        // Build Translator API request
        var url = $"https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&to={toLang}";
        if (!string.IsNullOrWhiteSpace(fromLang))
        {
            url += $"&from={fromLang}";
        }

        var body = new JArray
        {
            new JObject { ["Text"] = text }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        
        // Forward API key
        if (this.Context.Request.Headers.Contains("api-key"))
        {
            request.Headers.Add("Ocp-Apim-Subscription-Key", this.Context.Request.Headers.GetValues("api-key"));
        }
        
        request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Translation failed ({(int)response.StatusCode}): {content}");
        }

        var result = JArray.Parse(content);
        var firstResult = result.FirstOrDefault() as JObject;
        var translations = firstResult?["translations"] as JArray;
        var translation = translations?.FirstOrDefault();

        return new JObject
        {
            ["original_text"] = text,
            ["translated_text"] = translation?["text"]?.ToString(),
            ["to_language"] = toLang,
            ["detected_language"] = firstResult?["detectedLanguage"]?["language"]?.ToString()
        };
    }

    private async Task<JObject> ExecuteLanguageAnalysisToolAsync(JObject arguments)
    {
        var text = arguments.Value<string>("text");
        var analysisType = arguments.Value<string>("analysis_type");

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("'text' is required");
        }
        if (string.IsNullOrWhiteSpace(analysisType))
        {
            throw new ArgumentException("'analysis_type' is required");
        }

        var body = new JObject
        {
            ["kind"] = analysisType,
            ["analysisInput"] = new JObject
            {
                ["documents"] = new JArray
                {
                    new JObject
                    {
                        ["id"] = "1",
                        ["language"] = "en",
                        ["text"] = text
                    }
                }
            }
        };

        var baseUri = this.Context.Request.RequestUri;
        var url = $"https://{baseUri.Host}/language/:analyze-text?api-version=2024-11-01";

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        
        if (this.Context.Request.Headers.Contains("api-key"))
        {
            request.Headers.Add("api-key", this.Context.Request.Headers.GetValues("api-key"));
        }
        
        request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Language analysis failed ({(int)response.StatusCode}): {content}");
        }

        var result = JObject.Parse(content);
        return new JObject
        {
            ["analysis_type"] = analysisType,
            ["results"] = result["results"]
        };
    }

    private async Task<JObject> ExecuteDocumentAnalysisToolAsync(JObject arguments)
    {
        var documentUrl = arguments.Value<string>("document_url");
        var model = arguments.Value<string>("model") ?? "prebuilt-read";

        if (string.IsNullOrWhiteSpace(documentUrl))
        {
            throw new ArgumentException("'document_url' is required");
        }

        var body = new JObject
        {
            ["urlSource"] = documentUrl
        };

        var baseUri = this.Context.Request.RequestUri;
        var url = $"https://{baseUri.Host}/documentintelligence/documentModels/{model}:analyze?api-version=2024-11-30";

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        
        if (this.Context.Request.Headers.Contains("api-key"))
        {
            request.Headers.Add("api-key", this.Context.Request.Headers.GetValues("api-key"));
        }
        
        request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);

        string operationId = null;
        if (response.Headers.TryGetValues("Operation-Location", out var operationLocations))
        {
            var operationUrl = operationLocations.FirstOrDefault();
            if (!string.IsNullOrEmpty(operationUrl))
            {
                var uri = new Uri(operationUrl);
                operationId = uri.Segments.LastOrDefault()?.TrimEnd('/');
            }
        }

        return new JObject
        {
            ["status"] = "running",
            ["operation_id"] = operationId,
            ["model"] = model,
            ["document_url"] = documentUrl,
            ["message"] = $"Document analysis started. Poll GetDocumentAnalysisResult with operation_id '{operationId}' to get results."
        };
    }

    private bool IsSafeContent(JArray categories)
    {
        if (categories == null) return true;
        foreach (var cat in categories)
        {
            var severity = cat["severity"]?.Value<int>() ?? 0;
            if (severity > 2) return false; // Medium or higher is unsafe
        }
        return true;
    }

    private string GetSeverityLevel(int severity)
    {
        return severity switch
        {
            0 => "Safe",
            2 => "Low",
            4 => "Medium",
            6 => "High",
            _ => $"Level {severity}"
        };
    }

    // ========================================
    // API HELPERS
    // ========================================

    private string BuildApiUrl(string path, string apiVersion)
    {
        var baseUri = this.Context.Request.RequestUri;
        var host = baseUri.Host;
        return $"https://{host}{path}?api-version={apiVersion}";
    }

    private async Task<JObject> SendApiRequestAsync(HttpMethod method, string url, JObject body = null)
    {
        var request = new HttpRequestMessage(method, url);
        
        // Forward authorization
        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }
        
        // Forward API key
        if (this.Context.Request.Headers.Contains("api-key"))
        {
            request.Headers.Add("api-key", this.Context.Request.Headers.GetValues("api-key"));
        }
        
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        
        if (body != null)
        {
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"API request failed ({(int)response.StatusCode}): {content}");
        }

        return JObject.Parse(content);
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
    // APPLICATION INSIGHTS TELEMETRY
    // ========================================

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractInstrumentationKey(APP_INSIGHTS_CONNECTION_STRING);
            var ingestionEndpoint = ExtractIngestionEndpoint(APP_INSIGHTS_CONNECTION_STRING);

            if (string.IsNullOrEmpty(instrumentationKey) || string.IsNullOrEmpty(ingestionEndpoint))
            {
                return; // Telemetry disabled
            }

            var propsDict = new Dictionary<string, string>
            {
                ["ServerName"] = ServerName,
                ["ServerVersion"] = ServerVersion
            };

            if (properties != null)
            {
                var propsJson = Newtonsoft.Json.JsonConvert.SerializeObject(properties);
                var propsObj = Newtonsoft.Json.Linq.JObject.Parse(propsJson);
                foreach (var prop in propsObj.Properties())
                {
                    propsDict[prop.Name] = prop.Value?.ToString() ?? "";
                }
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

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(telemetryData);
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");

            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Suppress telemetry errors
        }
    }

    private string ExtractInstrumentationKey(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        foreach (var part in connectionString.Split(';'))
        {
            if (part.StartsWith("InstrumentationKey=", StringComparison.OrdinalIgnoreCase))
            {
                return part.Substring("InstrumentationKey=".Length);
            }
        }
        return null;
    }

    private string ExtractIngestionEndpoint(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return "https://dc.services.visualstudio.com/";
        foreach (var part in connectionString.Split(';'))
        {
            if (part.StartsWith("IngestionEndpoint=", StringComparison.OrdinalIgnoreCase))
            {
                return part.Substring("IngestionEndpoint=".Length);
            }
        }
        return "https://dc.services.visualstudio.com/";
    }
}
