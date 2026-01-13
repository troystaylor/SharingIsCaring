using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    // Application Insights configuration
    private const string APP_INSIGHTS_CONNECTION_STRING = "";
    
    // MCP Protocol version
    private const string PROTOCOL_VERSION = "2024-11-05";
    
    // Define available MCP tools for file hash operations
    private static readonly JArray AVAILABLE_TOOLS = new JArray
    {
        new JObject
        {
            ["name"] = "compute_file_hash",
            ["description"] = "Compute hash for a file. Can calculate QuickXorHash, SHA1, or CRC32. Accepts file content directly or downloads from Graph API.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["algorithm"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Hash algorithm to use",
                        ["enum"] = new JArray { "quickXorHash", "sha1Hash", "crc32Hash" }
                    },
                    ["fileContent"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Base64-encoded file content (optional if driveId/itemId provided)"
                    },
                    ["driveId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Drive ID to download file from Graph (optional if fileContent provided)"
                    },
                    ["itemId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Item ID to download file from Graph (optional if fileContent provided)"
                    }
                },
                ["required"] = new JArray { "algorithm" }
            }
        },
        new JObject
        {
            ["name"] = "get_graph_file_hashes",
            ["description"] = "Retrieve stored hash values for a file from Microsoft Graph API. Returns QuickXorHash, SHA1, and CRC32 if available.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["driveId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Drive ID (use 'me' for current user's drive)"
                    },
                    ["itemId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Item ID of the file"
                    }
                },
                ["required"] = new JArray { "itemId" }
            }
        },
        new JObject
        {
            ["name"] = "verify_file_integrity",
            ["description"] = "Verify file integrity by comparing computed hash to Graph API stored hash. Useful for detecting file corruption or changes.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["fileContent"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Base64-encoded file content to verify"
                    },
                    ["driveId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Drive ID (use 'me' for current user's drive)"
                    },
                    ["itemId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Item ID of the file"
                    }
                },
                ["required"] = new JArray { "fileContent", "itemId" }
            }
        },
        new JObject
        {
            ["name"] = "compare_hashes",
            ["description"] = "Compare two hash values to determine if they match. Useful for detecting file changes or verifying duplicates.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["hash1"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "First hash value"
                    },
                    ["hash2"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Second hash value"
                    },
                    ["algorithm"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Hash algorithm for context",
                        ["enum"] = new JArray { "quickXorHash", "sha1Hash", "crc32Hash" }
                    }
                },
                ["required"] = new JArray { "hash1", "hash2" }
            }
        }
    };

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        this.Context.Logger.LogInformation($"Graph Hashes request. CorrelationId: {correlationId}");
        
        try
        {
            var requestPath = this.Context.Request.RequestUri.AbsolutePath;
            
            await LogToAppInsights("RequestReceived", new {
                CorrelationId = correlationId,
                Path = requestPath,
                Method = this.Context.Request.Method.Method,
                OperationId = this.Context.OperationId
            });
            
            // Route MCP requests
            if (requestPath.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
            {
                return await HandleMCPProtocolAsync(correlationId).ConfigureAwait(false);
            }
            
            // Route custom operations
            var operationId = this.Context.OperationId;
            switch (operationId)
            {
                case "ComputeQuickXorHash":
                    return await HandleComputeQuickXorHashAsync(correlationId).ConfigureAwait(false);
                case "ComputeSHA1Hash":
                    return await HandleComputeSHA1HashAsync(correlationId).ConfigureAwait(false);
                case "ComputeCRC32Hash":
                    return await HandleComputeCRC32HashAsync(correlationId).ConfigureAwait(false);
                case "CompareHashes":
                    return await HandleCompareHashesAsync(correlationId).ConfigureAwait(false);
                case "VerifyFileIntegrity":
                    return await HandleVerifyFileIntegrityAsync(correlationId).ConfigureAwait(false);
                default:
                    // Forward to Microsoft Graph
                    this.Context.Logger.LogInformation($"Forwarding to Graph API: {operationId}");
                    var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
                    
                    await LogToAppInsights("GraphAPIResponse", new {
                        CorrelationId = correlationId,
                        OperationId = operationId,
                        StatusCode = (int)response.StatusCode
                    });
                    
                    return response;
            }
        }
        catch (Exception ex)
        {
            var errorMessage = $"Unexpected error: {ex.Message}";
            this.Context.Logger.LogError($"CorrelationId: {correlationId}, Error: {errorMessage}");
            
            await LogToAppInsights("RequestError", new {
                CorrelationId = correlationId,
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name,
                OperationId = this.Context.OperationId
            });
            
            return CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            this.Context.Logger.LogInformation($"Request completed. CorrelationId: {correlationId}, Duration: {duration.TotalMilliseconds}ms");
            
            await LogToAppInsights("RequestCompleted", new {
                CorrelationId = correlationId,
                DurationMs = duration.TotalMilliseconds
            });
        }
    }

    #region MCP Protocol Handler

    private async Task<HttpResponseMessage> HandleMCPProtocolAsync(string correlationId)
    {
        var requestBody = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        
        await LogToAppInsights("MCPProtocolInvoked", new {
            CorrelationId = correlationId,
            BodyLength = requestBody?.Length ?? 0
        });
        
        if (string.IsNullOrWhiteSpace(requestBody))
        {
            return CreateMCPErrorResponse(-32600, "Invalid Request: Empty body", null);
        }

        JObject requestObj;
        try
        {
            requestObj = JObject.Parse(requestBody);
        }
        catch
        {
            return CreateMCPErrorResponse(-32700, "Parse error", null);
        }

        var method = requestObj["method"]?.ToString();
        var id = requestObj["id"];
        var paramsObj = requestObj["params"] as JObject;

        switch (method)
        {
            case "initialize":
                return HandleInitialize(id);
            case "initialized":
                return CreateMCPSuccessResponse(new JObject(), id);
            case "ping":
                return CreateMCPSuccessResponse(new JObject(), id);
            case "tools/list":
                return HandleToolsList(id);
            case "tools/call":
                return await HandleToolsCallAsync(paramsObj, id, correlationId).ConfigureAwait(false);
            case "logging/setLevel":
                return CreateMCPSuccessResponse(new JObject(), id);
            case "notifications/cancelled":
                return CreateMCPSuccessResponse(new JObject(), id);
            default:
                return CreateMCPErrorResponse(-32601, $"Method not found: {method}", id);
        }
    }

    private HttpResponseMessage HandleInitialize(JToken id)
    {
        var result = new JObject
        {
            ["protocolVersion"] = PROTOCOL_VERSION,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = "Graph Hashes",
                ["version"] = "1.0.0"
            }
        };
        return CreateMCPSuccessResponse(result, id);
    }

    private HttpResponseMessage HandleToolsList(JToken id)
    {
        var result = new JObject { ["tools"] = AVAILABLE_TOOLS };
        return CreateMCPSuccessResponse(result, id);
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject paramsObj, JToken id, string correlationId)
    {
        var toolName = paramsObj?["name"]?.ToString();
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();
        
        this.Context.Logger.LogInformation($"MCP tool invoked: {toolName}");
        
        await LogToAppInsights("MCPToolInvoked", new {
            CorrelationId = correlationId,
            ToolName = toolName,
            ArgumentCount = arguments.Count
        });

        try
        {
            switch (toolName)
            {
                case "compute_file_hash":
                    return await HandleMCPComputeFileHashAsync(arguments, id).ConfigureAwait(false);
                case "get_graph_file_hashes":
                    return await HandleMCPGetGraphFileHashesAsync(arguments, id).ConfigureAwait(false);
                case "verify_file_integrity":
                    return await HandleMCPVerifyFileIntegrityAsync(arguments, id).ConfigureAwait(false);
                case "compare_hashes":
                    return HandleMCPCompareHashes(arguments, id);
                default:
                    return CreateMCPErrorResponse(-32602, $"Unknown tool: {toolName}", id);
            }
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"MCP tool error: {ex.Message}");
            return CreateMCPErrorResponse(-32603, $"Tool execution failed: {ex.Message}", id);
        }
    }

    private async Task<HttpResponseMessage> HandleMCPComputeFileHashAsync(JObject arguments, JToken id)
    {
        var algorithm = arguments["algorithm"]?.ToString() ?? "quickXorHash";
        var fileContent = arguments["fileContent"]?.ToString();
        var driveId = arguments["driveId"]?.ToString();
        var itemId = arguments["itemId"]?.ToString();

        byte[] fileBytes;
        
        if (!string.IsNullOrEmpty(fileContent))
        {
            fileBytes = Convert.FromBase64String(fileContent);
        }
        else if (!string.IsNullOrEmpty(itemId))
        {
            fileBytes = await DownloadFileFromGraphAsync(driveId, itemId).ConfigureAwait(false);
        }
        else
        {
            return CreateMCPErrorResponse(-32602, "Either fileContent or itemId must be provided", id);
        }

        var hash = ComputeHash(fileBytes, algorithm);
        
        var content = new JArray
        {
            new JObject
            {
                ["type"] = "text",
                ["text"] = $"File hash computed successfully:\nAlgorithm: {algorithm}\nHash: {hash}\nFile size: {fileBytes.Length:N0} bytes"
            }
        };
        
        return CreateMCPSuccessResponse(new JObject { ["content"] = content }, id);
    }

    private async Task<HttpResponseMessage> HandleMCPGetGraphFileHashesAsync(JObject arguments, JToken id)
    {
        var driveId = arguments["driveId"]?.ToString() ?? "me";
        var itemId = arguments["itemId"]?.ToString();

        if (string.IsNullOrEmpty(itemId))
        {
            return CreateMCPErrorResponse(-32602, "itemId is required", id);
        }

        var graphUrl = driveId == "me" 
            ? $"/v1.0/me/drive/items/{itemId}?$select=id,name,size,file"
            : $"/v1.0/drives/{driveId}/items/{itemId}?$select=id,name,size,file";

        var request = new HttpRequestMessage(HttpMethod.Get, $"https://graph.microsoft.com{graphUrl}");
        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            return CreateMCPErrorResponse(-32603, $"Graph API error: {response.StatusCode}", id);
        }

        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var fileData = JObject.Parse(responseBody);
        var hashes = fileData["file"]?["hashes"];

        var text = new StringBuilder();
        text.AppendLine($"File: {fileData["name"]}");
        text.AppendLine($"Size: {fileData["size"]:N0} bytes");
        text.AppendLine($"\nStored hashes:");
        text.AppendLine($"QuickXorHash: {hashes?["quickXorHash"] ?? "Not available"}");
        text.AppendLine($"SHA1: {hashes?["sha1Hash"] ?? "Not available"}");
        text.AppendLine($"CRC32: {hashes?["crc32Hash"] ?? "Not available"}");
        
        var content = new JArray
        {
            new JObject
            {
                ["type"] = "text",
                ["text"] = text.ToString()
            }
        };
        
        return CreateMCPSuccessResponse(new JObject { ["content"] = content }, id);
    }

    private async Task<HttpResponseMessage> HandleMCPVerifyFileIntegrityAsync(JObject arguments, JToken id)
    {
        var fileContent = arguments["fileContent"]?.ToString();
        var driveId = arguments["driveId"]?.ToString() ?? "me";
        var itemId = arguments["itemId"]?.ToString();

        if (string.IsNullOrEmpty(fileContent) || string.IsNullOrEmpty(itemId))
        {
            return CreateMCPErrorResponse(-32602, "fileContent and itemId are required", id);
        }

        var fileBytes = Convert.FromBase64String(fileContent);
        var computedHash = ComputeQuickXorHash(fileBytes);

        // Get hash from Graph
        var graphUrl = driveId == "me" 
            ? $"/v1.0/me/drive/items/{itemId}?$select=name,size,file"
            : $"/v1.0/drives/{driveId}/items/{itemId}?$select=name,size,file";

        var request = new HttpRequestMessage(HttpMethod.Get, $"https://graph.microsoft.com{graphUrl}");
        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            return CreateMCPErrorResponse(-32603, $"Graph API error: {response.StatusCode}", id);
        }

        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var fileData = JObject.Parse(responseBody);
        var graphHash = fileData["file"]?["hashes"]?["quickXorHash"]?.ToString();

        var verified = !string.IsNullOrEmpty(graphHash) && 
                      string.Equals(computedHash, graphHash, StringComparison.OrdinalIgnoreCase);

        var text = new StringBuilder();
        text.AppendLine($"File: {fileData["name"]}");
        text.AppendLine($"Size: {fileBytes.Length:N0} bytes");
        text.AppendLine($"\nVerification result: {(verified ? "✓ VERIFIED" : "✗ MISMATCH")}");
        text.AppendLine($"\nComputed hash: {computedHash}");
        text.AppendLine($"Graph API hash: {graphHash ?? "Not available"}");
        
        var content = new JArray
        {
            new JObject
            {
                ["type"] = "text",
                ["text"] = text.ToString()
            }
        };
        
        return CreateMCPSuccessResponse(new JObject { ["content"] = content }, id);
    }

    private HttpResponseMessage HandleMCPCompareHashes(JObject arguments, JToken id)
    {
        var hash1 = arguments["hash1"]?.ToString();
        var hash2 = arguments["hash2"]?.ToString();
        var algorithm = arguments["algorithm"]?.ToString() ?? "unknown";

        if (string.IsNullOrEmpty(hash1) || string.IsNullOrEmpty(hash2))
        {
            return CreateMCPErrorResponse(-32602, "hash1 and hash2 are required", id);
        }

        var match = string.Equals(hash1, hash2, StringComparison.OrdinalIgnoreCase);

        var text = new StringBuilder();
        text.AppendLine($"Hash comparison ({algorithm}):");
        text.AppendLine($"\nHash 1: {hash1}");
        text.AppendLine($"Hash 2: {hash2}");
        text.AppendLine($"\nResult: {(match ? "✓ MATCH" : "✗ NO MATCH")}");
        
        var content = new JArray
        {
            new JObject
            {
                ["type"] = "text",
                ["text"] = text.ToString()
            }
        };
        
        return CreateMCPSuccessResponse(new JObject { ["content"] = content }, id);
    }

    #endregion

    #region Direct API Handlers

    private async Task<HttpResponseMessage> HandleComputeQuickXorHashAsync(string correlationId)
    {
        var requestBody = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var requestObj = JObject.Parse(requestBody);
        var fileContent = requestObj["fileContent"]?.ToString();

        if (string.IsNullOrEmpty(fileContent))
        {
            return CreateErrorResponse(HttpStatusCode.BadRequest, "fileContent is required");
        }

        var fileBytes = Convert.FromBase64String(fileContent);
        var hash = ComputeQuickXorHash(fileBytes);

        await LogToAppInsights("HashComputed", new {
            CorrelationId = correlationId,
            Algorithm = "QuickXorHash",
            FileSize = fileBytes.Length
        });

        var result = new JObject
        {
            ["algorithm"] = "quickXorHash",
            ["hash"] = hash,
            ["fileSize"] = fileBytes.Length
        };

        return CreateJsonResponse(HttpStatusCode.OK, result.ToString());
    }

    private async Task<HttpResponseMessage> HandleComputeSHA1HashAsync(string correlationId)
    {
        var requestBody = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var requestObj = JObject.Parse(requestBody);
        var fileContent = requestObj["fileContent"]?.ToString();

        if (string.IsNullOrEmpty(fileContent))
        {
            return CreateErrorResponse(HttpStatusCode.BadRequest, "fileContent is required");
        }

        var fileBytes = Convert.FromBase64String(fileContent);
        var hash = ComputeSHA1Hash(fileBytes);

        await LogToAppInsights("HashComputed", new {
            CorrelationId = correlationId,
            Algorithm = "SHA1",
            FileSize = fileBytes.Length
        });

        var result = new JObject
        {
            ["algorithm"] = "sha1Hash",
            ["hash"] = hash,
            ["fileSize"] = fileBytes.Length
        };

        return CreateJsonResponse(HttpStatusCode.OK, result.ToString());
    }

    private async Task<HttpResponseMessage> HandleComputeCRC32HashAsync(string correlationId)
    {
        var requestBody = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var requestObj = JObject.Parse(requestBody);
        var fileContent = requestObj["fileContent"]?.ToString();

        if (string.IsNullOrEmpty(fileContent))
        {
            return CreateErrorResponse(HttpStatusCode.BadRequest, "fileContent is required");
        }

        var fileBytes = Convert.FromBase64String(fileContent);
        var hash = ComputeCRC32Hash(fileBytes);

        await LogToAppInsights("HashComputed", new {
            CorrelationId = correlationId,
            Algorithm = "CRC32",
            FileSize = fileBytes.Length
        });

        var result = new JObject
        {
            ["algorithm"] = "crc32Hash",
            ["hash"] = hash,
            ["fileSize"] = fileBytes.Length
        };

        return CreateJsonResponse(HttpStatusCode.OK, result.ToString());
    }

    private async Task<HttpResponseMessage> HandleCompareHashesAsync(string correlationId)
    {
        var requestBody = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var requestObj = JObject.Parse(requestBody);
        var hash1 = requestObj["hash1"]?.ToString();
        var hash2 = requestObj["hash2"]?.ToString();
        var algorithm = requestObj["algorithm"]?.ToString() ?? "unknown";

        if (string.IsNullOrEmpty(hash1) || string.IsNullOrEmpty(hash2))
        {
            return CreateErrorResponse(HttpStatusCode.BadRequest, "hash1 and hash2 are required");
        }

        var match = string.Equals(hash1, hash2, StringComparison.OrdinalIgnoreCase);

        await LogToAppInsights("HashesCompared", new {
            CorrelationId = correlationId,
            Algorithm = algorithm,
            Match = match
        });

        var result = new JObject
        {
            ["match"] = match,
            ["hash1"] = hash1,
            ["hash2"] = hash2,
            ["algorithm"] = algorithm
        };

        return CreateJsonResponse(HttpStatusCode.OK, result.ToString());
    }

    private async Task<HttpResponseMessage> HandleVerifyFileIntegrityAsync(string correlationId)
    {
        var requestBody = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var requestObj = JObject.Parse(requestBody);
        var fileContent = requestObj["fileContent"]?.ToString();
        var driveId = requestObj["driveId"]?.ToString();
        var itemId = requestObj["itemId"]?.ToString();

        if (string.IsNullOrEmpty(fileContent) || string.IsNullOrEmpty(itemId))
        {
            return CreateErrorResponse(HttpStatusCode.BadRequest, "fileContent and itemId are required");
        }

        var fileBytes = Convert.FromBase64String(fileContent);
        var computedHash = ComputeQuickXorHash(fileBytes);

        // Get hash from Graph
        var graphUrl = string.IsNullOrEmpty(driveId) 
            ? $"/v1.0/me/drive/items/{itemId}?$select=name,size,file"
            : $"/v1.0/drives/{driveId}/items/{itemId}?$select=name,size,file";

        var request = new HttpRequestMessage(HttpMethod.Get, $"https://graph.microsoft.com{graphUrl}");
        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            return CreateErrorResponse(response.StatusCode, "Failed to retrieve file from Graph API");
        }

        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var fileData = JObject.Parse(responseBody);
        var graphHash = fileData["file"]?["hashes"]?["quickXorHash"]?.ToString();

        var verified = !string.IsNullOrEmpty(graphHash) && 
                      string.Equals(computedHash, graphHash, StringComparison.OrdinalIgnoreCase);

        await LogToAppInsights("FileIntegrityVerified", new {
            CorrelationId = correlationId,
            Verified = verified,
            FileSize = fileBytes.Length
        });

        var result = new JObject
        {
            ["verified"] = verified,
            ["computedHash"] = computedHash,
            ["graphHash"] = graphHash,
            ["fileName"] = fileData["name"],
            ["fileSize"] = fileBytes.Length
        };

        return CreateJsonResponse(HttpStatusCode.OK, result.ToString());
    }

    #endregion

    #region Hash Computation

    private string ComputeHash(byte[] data, string algorithm)
    {
        switch (algorithm.ToLowerInvariant())
        {
            case "quickxorhash":
                return ComputeQuickXorHash(data);
            case "sha1hash":
            case "sha1":
                return ComputeSHA1Hash(data);
            case "crc32hash":
            case "crc32":
                return ComputeCRC32Hash(data);
            default:
                return ComputeQuickXorHash(data);
        }
    }

    private string ComputeQuickXorHash(byte[] data)
    {
        using (var hasher = new QuickXorHash())
        {
            var hash = hasher.ComputeHash(data);
            return Convert.ToBase64String(hash);
        }
    }

    private string ComputeSHA1Hash(byte[] data)
    {
        using (var sha1 = SHA1.Create())
        {
            var hash = sha1.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    private string ComputeCRC32Hash(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        uint[] table = GenerateCRC32Table();

        foreach (byte b in data)
        {
            crc = (crc >> 8) ^ table[(crc ^ b) & 0xFF];
        }

        crc ^= 0xFFFFFFFF;
        return crc.ToString("x8");
    }

    private uint[] GenerateCRC32Table()
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 1) == 1)
                    crc = (crc >> 1) ^ 0xEDB88320;
                else
                    crc >>= 1;
            }
            table[i] = crc;
        }
        return table;
    }

    private async Task<byte[]> DownloadFileFromGraphAsync(string driveId, string itemId)
    {
        var graphUrl = string.IsNullOrEmpty(driveId) || driveId == "me"
            ? $"/v1.0/me/drive/items/{itemId}/content"
            : $"/v1.0/drives/{driveId}/items/{itemId}/content";

        var request = new HttpRequestMessage(HttpMethod.Get, $"https://graph.microsoft.com{graphUrl}");
        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to download file from Graph: {response.StatusCode}");
        }

        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    #endregion

    #region QuickXorHash Implementation

    private class QuickXorHash : HashAlgorithm
    {
        private const int BitsInLastCell = 32;
        private const byte Shift = 11;
        private const byte WidthInBits = 160;

        private UInt64[] _data;
        private Int64 _lengthSoFar;
        private int _shiftSoFar;

        public QuickXorHash()
        {
            this.Initialize();
        }

        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            unchecked
            {
                int currentShift = this._shiftSoFar;
                int vectorArrayIndex = currentShift / 64;
                int vectorOffset = currentShift % 64;
                int iterations = Math.Min(cbSize, WidthInBits);

                for (int i = 0; i < iterations; i++)
                {
                    bool isLastCell = vectorArrayIndex == this._data.Length - 1;
                    int bitsInVectorCell = isLastCell ? BitsInLastCell : 64;

                    if (vectorOffset <= bitsInVectorCell - 8)
                    {
                        for (int j = ibStart + i; j < cbSize + ibStart; j += WidthInBits)
                        {
                            this._data[vectorArrayIndex] ^= (ulong)array[j] << vectorOffset;
                        }
                    }
                    else
                    {
                        int index1 = vectorArrayIndex;
                        int index2 = isLastCell ? 0 : (vectorArrayIndex + 1);
                        byte low = (byte)(bitsInVectorCell - vectorOffset);

                        byte xoredByte = 0;
                        for (int j = ibStart + i; j < cbSize + ibStart; j += WidthInBits)
                        {
                            xoredByte ^= array[j];
                        }
                        this._data[index1] ^= (ulong)xoredByte << vectorOffset;
                        this._data[index2] ^= (ulong)xoredByte >> low;
                    }
                    vectorOffset += Shift;
                    while (vectorOffset >= bitsInVectorCell)
                    {
                        vectorArrayIndex = isLastCell ? 0 : vectorArrayIndex + 1;
                        vectorOffset -= bitsInVectorCell;
                    }
                }

                this._shiftSoFar = (this._shiftSoFar + Shift * (cbSize % WidthInBits)) % WidthInBits;
            }

            this._lengthSoFar += cbSize;
        }

        protected override byte[] HashFinal()
        {
            byte[] rgb = new byte[(WidthInBits - 1) / 8 + 1];

            for (Int32 i = 0; i < this._data.Length - 1; i++)
            {
                Buffer.BlockCopy(
                    BitConverter.GetBytes(this._data[i]), 0,
                    rgb, i * 8,
                    8);
            }

            Buffer.BlockCopy(
                BitConverter.GetBytes(this._data[this._data.Length - 1]), 0,
                rgb, (this._data.Length - 1) * 8,
                rgb.Length - (this._data.Length - 1) * 8);

            var lengthBytes = BitConverter.GetBytes(this._lengthSoFar);
            for (int i = 0; i < lengthBytes.Length; i++)
            {
                rgb[(WidthInBits / 8) - lengthBytes.Length + i] ^= lengthBytes[i];
            }

            return rgb;
        }

        public override sealed void Initialize()
        {
            this._data = new ulong[(WidthInBits - 1) / 64 + 1];
            this._shiftSoFar = 0;
            this._lengthSoFar = 0;
        }

        public override int HashSize
        {
            get { return WidthInBits; }
        }
    }

    #endregion

    #region Response Helpers

    private HttpResponseMessage CreateMCPSuccessResponse(JObject result, JToken id)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };
        return CreateJsonResponse(HttpStatusCode.OK, response.ToString());
    }

    private HttpResponseMessage CreateMCPErrorResponse(int code, string message, JToken id)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
        return CreateJsonResponse(HttpStatusCode.OK, response.ToString());
    }

    private HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string json)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return response;
    }

    private HttpResponseMessage CreateErrorResponse(HttpStatusCode statusCode, string message)
    {
        var error = new JObject
        {
            ["error"] = new JObject
            {
                ["message"] = message,
                ["statusCode"] = (int)statusCode
            }
        };
        return CreateJsonResponse(statusCode, error.ToString());
    }

    #endregion

    #region Application Insights Telemetry

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractInstrumentationKey(APP_INSIGHTS_CONNECTION_STRING);
            var ingestionEndpoint = ExtractIngestionEndpoint(APP_INSIGHTS_CONNECTION_STRING);
            
            if (string.IsNullOrEmpty(instrumentationKey) || string.IsNullOrEmpty(ingestionEndpoint))
            {
                return;
            }

            var propsDict = new Dictionary<string, string>();
            if (properties != null)
            {
                var propsJson = Newtonsoft.Json.JsonConvert.SerializeObject(properties);
                var propsObj = JObject.Parse(propsJson);
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
                    baseData = new
                    {
                        ver = 2,
                        name = eventName,
                        properties = propsDict
                    }
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
        catch (Exception ex)
        {
            this.Context.Logger.LogWarning($"Telemetry error: {ex.Message}");
        }
    }

    private string ExtractInstrumentationKey(string connectionString)
    {
        try
        {
            if (string.IsNullOrEmpty(connectionString)) return null;
            
            var parts = connectionString.Split(';');
            foreach (var part in parts)
            {
                if (part.StartsWith("InstrumentationKey=", StringComparison.OrdinalIgnoreCase))
                {
                    return part.Substring("InstrumentationKey=".Length);
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private string ExtractIngestionEndpoint(string connectionString)
    {
        try
        {
            if (string.IsNullOrEmpty(connectionString)) 
                return "https://dc.services.visualstudio.com/";
            
            var parts = connectionString.Split(';');
            foreach (var part in parts)
            {
                if (part.StartsWith("IngestionEndpoint=", StringComparison.OrdinalIgnoreCase))
                {
                    return part.Substring("IngestionEndpoint=".Length);
                }
            }
            return "https://dc.services.visualstudio.com/";
        }
        catch
        {
            return "https://dc.services.visualstudio.com/";
        }
    }

    #endregion
}
