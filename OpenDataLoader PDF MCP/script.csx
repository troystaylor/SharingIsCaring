using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private string _correlationId;

    // ========================================
    // APPLICATION INSIGHTS CONFIGURATION
    // ========================================
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";
    private const string ConnectorName = "OpenDataLoader PDF MCP";

    // ========================================
    // MCP PROTOCOL
    // ========================================
    private const string ProtocolVersion = "2025-03-26";
    private const string ServerVersion = "1.0.0";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        _correlationId = Guid.NewGuid().ToString();

        await LogToAppInsights("RequestReceived", new Dictionary<string, string>
        {
            ["CorrelationId"] = _correlationId,
            ["OperationId"] = this.Context.OperationId
        });

        try
        {
            HttpResponseMessage result;
            switch (this.Context.OperationId)
            {
                case "InvokeMCP":
                    result = await HandleMcpRequest().ConfigureAwait(false);
                    break;

                case "ConvertPdf":
                    result = await ForwardToService("/api/convert").ConfigureAwait(false);
                    break;

                case "ExtractTables":
                    result = await ForwardToService("/api/tables").ConfigureAwait(false);
                    break;

                case "GetPageElements":
                    result = await ForwardToService("/api/elements").ConfigureAwait(false);
                    break;

                case "CheckAccessibility":
                    result = await ForwardToService("/api/accessibility").ConfigureAwait(false);
                    break;

                case "GetServerInfo":
                    result = await ForwardToService("/api/info").ConfigureAwait(false);
                    break;

                default:
                    result = new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent(
                            JObject.FromObject(new { error = "Unknown operation: " + this.Context.OperationId }).ToString(),
                            Encoding.UTF8,
                            "application/json")
                    };
                    break;
            }

            await LogToAppInsights("RequestCompleted", new Dictionary<string, string>
            {
                ["CorrelationId"] = _correlationId,
                ["OperationId"] = this.Context.OperationId,
                ["StatusCode"] = ((int)result.StatusCode).ToString()
            });

            return result;
        }
        catch (Exception ex)
        {
            await LogExceptionToAppInsights(ex, new Dictionary<string, string>
            {
                ["CorrelationId"] = _correlationId,
                ["OperationId"] = this.Context.OperationId
            });
            throw;
        }
    }

    // ========================================
    // MCP PROTOCOL HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpRequest()
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch (Exception)
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Invalid JSON");
        }

        var method = request["method"]?.ToString();
        var requestId = request["id"];
        var @params = request["params"] as JObject ?? new JObject();

        await LogToAppInsights("McpMethod", new Dictionary<string, string>
        {
            ["CorrelationId"] = _correlationId,
            ["Method"] = method ?? "(null)"
        });

        switch (method)
        {
            case "initialize":
                return HandleInitialize(requestId);

            case "initialized":
            case "notifications/initialized":
            case "notifications/cancelled":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            case "tools/list":
                return HandleToolsList(requestId);

            case "tools/call":
                return await HandleToolsCall(@params, requestId).ConfigureAwait(false);

            case "resources/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });

            case "ping":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            default:
                return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
        }
    }

    private HttpResponseMessage HandleInitialize(JToken requestId)
    {
        var result = new JObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = "opendataloader-pdf-mcp",
                ["version"] = ServerVersion
            }
        };
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            new JObject
            {
                ["name"] = "convert_pdf",
                ["description"] = "Convert a PDF document to Markdown, JSON (with bounding boxes), HTML, or plain text. Use when the user wants to read, analyze, or extract content from a PDF. Supports hybrid mode for complex tables, scanned PDFs, and formulas. The #1-ranked open-source PDF parser with 0.907 overall accuracy.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["source"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "URL of the PDF to convert, or base64-encoded PDF content."
                        },
                        ["source_type"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Whether the source is a 'url' or 'base64'. Defaults to 'url'.",
                            ["enum"] = new JArray { "url", "base64" },
                            ["default"] = "url"
                        },
                        ["format"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Output format: 'markdown' for clean text (best for LLM context), 'json' for structured data with bounding boxes, 'html' for web display, 'text' for plain text. Defaults to 'markdown'.",
                            ["enum"] = new JArray { "markdown", "json", "html", "text" },
                            ["default"] = "markdown"
                        },
                        ["pages"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Page range to convert (e.g., '1-5', '1,3,5'). Leave empty for all pages."
                        },
                        ["use_struct_tree"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Use native PDF structure tags when available. Set to true for tagged PDFs for best accuracy.",
                            ["default"] = false
                        },
                        ["hybrid"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Enable hybrid mode for higher accuracy on complex/borderless tables, scanned PDFs, and formulas. Slower but significantly more accurate.",
                            ["default"] = false
                        }
                    },
                    ["required"] = new JArray { "source" }
                }
            },
            new JObject
            {
                ["name"] = "extract_tables",
                ["description"] = "Extract tables from a PDF document. Returns structured table data with rows, columns, cell content, and bounding boxes. Use hybrid mode for complex or borderless tables (improves table accuracy from 0.489 to 0.928 TEDS score).",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["source"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "URL of the PDF, or base64-encoded PDF content."
                        },
                        ["source_type"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Whether the source is a 'url' or 'base64'. Defaults to 'url'.",
                            ["enum"] = new JArray { "url", "base64" },
                            ["default"] = "url"
                        },
                        ["pages"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Page range to extract tables from (e.g., '1-5'). Leave empty for all pages."
                        },
                        ["hybrid"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Enable hybrid mode for complex/borderless tables. Strongly recommended for accurate table extraction.",
                            ["default"] = false
                        }
                    },
                    ["required"] = new JArray { "source" }
                }
            },
            new JObject
            {
                ["name"] = "get_page_elements",
                ["description"] = "Get structured elements from PDF pages with bounding boxes and semantic types. Returns headings, paragraphs, tables, lists, images, captions, and formulas with their exact coordinates. Use when you need element-level control for RAG source citations or document analysis.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["source"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "URL of the PDF, or base64-encoded PDF content."
                        },
                        ["source_type"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Whether the source is a 'url' or 'base64'. Defaults to 'url'.",
                            ["enum"] = new JArray { "url", "base64" },
                            ["default"] = "url"
                        },
                        ["pages"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Page range (e.g., '1-3'). Leave empty for all pages."
                        },
                        ["element_types"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Comma-separated element types to include: heading, paragraph, table, list, image, caption, formula. Leave empty for all types."
                        }
                    },
                    ["required"] = new JArray { "source" }
                }
            },
            new JObject
            {
                ["name"] = "check_accessibility",
                ["description"] = "Check if a PDF has structure tags and assess its accessibility status. Reports whether the PDF is tagged, has a title and language set, the tag tree structure, and any accessibility issues. Useful for EAA, ADA, and Section 508 compliance assessment.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["source"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "URL of the PDF, or base64-encoded PDF content."
                        },
                        ["source_type"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Whether the source is a 'url' or 'base64'. Defaults to 'url'.",
                            ["enum"] = new JArray { "url", "base64" },
                            ["default"] = "url"
                        }
                    },
                    ["required"] = new JArray { "source" }
                }
            },
            new JObject
            {
                ["name"] = "get_server_info",
                ["description"] = "Get the OpenDataLoader PDF service configuration, version, and available capabilities. Shows whether hybrid mode and OCR are available, supported output formats, and supported OCR languages.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject()
                }
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private async Task<HttpResponseMessage> HandleToolsCall(JObject @params, JToken requestId)
    {
        var toolName = @params["name"]?.ToString();
        var arguments = @params["arguments"] as JObject ?? new JObject();

        await LogToAppInsights("ToolCall", new Dictionary<string, string>
        {
            ["CorrelationId"] = _correlationId,
            ["ToolName"] = toolName ?? "(null)"
        });

        try
        {
            string resultText;
            switch (toolName)
            {
                case "convert_pdf":
                    resultText = await CallServiceTool("/api/convert", BuildConvertBody(arguments)).ConfigureAwait(false);
                    break;

                case "extract_tables":
                    resultText = await CallServiceTool("/api/tables", BuildTablesBody(arguments)).ConfigureAwait(false);
                    break;

                case "get_page_elements":
                    resultText = await CallServiceTool("/api/elements", BuildElementsBody(arguments)).ConfigureAwait(false);
                    break;

                case "check_accessibility":
                    resultText = await CallServiceTool("/api/accessibility", BuildAccessibilityBody(arguments)).ConfigureAwait(false);
                    break;

                case "get_server_info":
                    resultText = await CallServiceTool("/api/info", null, "GET").ConfigureAwait(false);
                    break;

                default:
                    return CreateJsonRpcSuccessResponse(requestId, new JObject
                    {
                        ["content"] = new JArray
                        {
                            new JObject
                            {
                                ["type"] = "text",
                                ["text"] = "Unknown tool: " + toolName
                            }
                        },
                        ["isError"] = true
                    });
            }

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = resultText
                    }
                },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            await LogExceptionToAppInsights(ex, new Dictionary<string, string>
            {
                ["CorrelationId"] = _correlationId,
                ["ToolName"] = toolName ?? "(null)"
            });

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = "Tool execution failed: " + ex.Message
                    }
                },
                ["isError"] = true
            });
        }
    }

    // ========================================
    // SERVICE COMMUNICATION
    // ========================================

    private async Task<HttpResponseMessage> ForwardToService(string path)
    {
        var originalRequest = this.Context.Request;
        var serviceUrl = originalRequest.RequestUri.GetLeftPart(UriPartial.Authority) + path;

        var newRequest = new HttpRequestMessage(originalRequest.Method, serviceUrl);

        if (originalRequest.Content != null)
        {
            var content = await originalRequest.Content.ReadAsStringAsync().ConfigureAwait(false);
            newRequest.Content = new StringContent(content, Encoding.UTF8, "application/json");
        }

        foreach (var header in originalRequest.Headers)
        {
            if (!header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                newRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return await this.Context.SendAsync(newRequest, this.CancellationToken).ConfigureAwait(false);
    }

    private async Task<string> CallServiceTool(string path, JObject body, string method = "POST")
    {
        var originalRequest = this.Context.Request;
        var serviceUrl = originalRequest.RequestUri.GetLeftPart(UriPartial.Authority) + path;

        var request = new HttpRequestMessage(
            method == "GET" ? HttpMethod.Get : HttpMethod.Post,
            serviceUrl);

        if (body != null && method != "GET")
        {
            request.Content = new StringContent(
                body.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json");
        }

        foreach (var header in originalRequest.Headers)
        {
            if (!header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) &&
                !header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) &&
                !header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Service returned {(int)response.StatusCode}: {responseBody}");
        }

        return responseBody;
    }

    // ========================================
    // REQUEST BODY BUILDERS
    // ========================================

    private JObject BuildConvertBody(JObject arguments)
    {
        var body = new JObject
        {
            ["source"] = arguments["source"]?.ToString()
        };

        var sourceType = arguments["source_type"]?.ToString();
        if (!string.IsNullOrEmpty(sourceType))
            body["sourceType"] = sourceType;

        var format = arguments["format"]?.ToString();
        if (!string.IsNullOrEmpty(format))
            body["format"] = format;

        var pages = arguments["pages"]?.ToString();
        if (!string.IsNullOrEmpty(pages))
            body["pages"] = pages;

        if (arguments["use_struct_tree"] != null)
            body["useStructTree"] = arguments["use_struct_tree"].Value<bool>();

        if (arguments["hybrid"] != null)
            body["hybrid"] = arguments["hybrid"].Value<bool>();

        return body;
    }

    private JObject BuildTablesBody(JObject arguments)
    {
        var body = new JObject
        {
            ["source"] = arguments["source"]?.ToString()
        };

        var sourceType = arguments["source_type"]?.ToString();
        if (!string.IsNullOrEmpty(sourceType))
            body["sourceType"] = sourceType;

        var pages = arguments["pages"]?.ToString();
        if (!string.IsNullOrEmpty(pages))
            body["pages"] = pages;

        if (arguments["hybrid"] != null)
            body["hybrid"] = arguments["hybrid"].Value<bool>();

        return body;
    }

    private JObject BuildElementsBody(JObject arguments)
    {
        var body = new JObject
        {
            ["source"] = arguments["source"]?.ToString()
        };

        var sourceType = arguments["source_type"]?.ToString();
        if (!string.IsNullOrEmpty(sourceType))
            body["sourceType"] = sourceType;

        var pages = arguments["pages"]?.ToString();
        if (!string.IsNullOrEmpty(pages))
            body["pages"] = pages;

        var elementTypes = arguments["element_types"]?.ToString();
        if (!string.IsNullOrEmpty(elementTypes))
            body["elementTypes"] = elementTypes;

        return body;
    }

    private JObject BuildAccessibilityBody(JObject arguments)
    {
        var body = new JObject
        {
            ["source"] = arguments["source"]?.ToString()
        };

        var sourceType = arguments["source_type"]?.ToString();
        if (!string.IsNullOrEmpty(sourceType))
            body["sourceType"] = sourceType;

        return body;
    }

    // ========================================
    // JSON-RPC HELPERS
    // ========================================

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result,
            ["id"] = id
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                response.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (data != null) error["data"] = data;

        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = error,
            ["id"] = id
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                response.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    // ========================================
    // APPLICATION INSIGHTS LOGGING
    // ========================================

    private async Task LogToAppInsights(string eventName, IDictionary<string, string> properties = null)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.IndexOf("INSERT_YOUR", StringComparison.OrdinalIgnoreCase) >= 0)
            return;

        try
        {
            var props = properties ?? new Dictionary<string, string>();
            props["ConnectorName"] = ConnectorName;

            var telemetryData = new JObject
            {
                ["name"] = "Microsoft.ApplicationInsights.Event",
                ["time"] = DateTime.UtcNow.ToString("O"),
                ["iKey"] = APP_INSIGHTS_KEY,
                ["data"] = new JObject
                {
                    ["baseType"] = "EventData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["name"] = eventName,
                        ["properties"] = JObject.FromObject(props)
                    }
                }
            };

            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT)
            {
                Content = new StringContent(
                    telemetryData.ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8,
                    "application/json")
            };
            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Silent fail for telemetry */ }
    }

    private async Task LogExceptionToAppInsights(Exception ex, IDictionary<string, string> properties = null)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.IndexOf("INSERT_YOUR", StringComparison.OrdinalIgnoreCase) >= 0)
            return;

        try
        {
            var props = properties ?? new Dictionary<string, string>();
            props["ConnectorName"] = ConnectorName;
            props["ExceptionMessage"] = ex.Message;
            props["ExceptionType"] = ex.GetType().Name;

            var telemetryData = new JObject
            {
                ["name"] = "Microsoft.ApplicationInsights.Exception",
                ["time"] = DateTime.UtcNow.ToString("O"),
                ["iKey"] = APP_INSIGHTS_KEY,
                ["data"] = new JObject
                {
                    ["baseType"] = "ExceptionData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["exceptions"] = new JArray
                        {
                            new JObject
                            {
                                ["typeName"] = ex.GetType().Name,
                                ["message"] = ex.Message,
                                ["hasFullStack"] = ex.StackTrace != null
                            }
                        },
                        ["properties"] = JObject.FromObject(props)
                    }
                }
            };

            var exceptionRequest = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT)
            {
                Content = new StringContent(
                    telemetryData.ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8,
                    "application/json")
            };
            await this.Context.SendAsync(exceptionRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Silent fail for telemetry */ }
    }
}
