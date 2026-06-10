using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharePointTransferMcp.Auth;
using SharePointTransferMcp.Graph;
using SharePointTransferMcp.Tools;

namespace SharePointTransferMcp.Endpoints;

public static class McpEndpoints
{
	private sealed record AuthChallenge(string Code, string Description);

	private const string AuthChallengeKey = "mcp.authChallenge";

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
	{
		WriteIndented = false
	};

	public static IEndpointRouteBuilder MapMcpRoute(this IEndpointRouteBuilder app, string path)
	{
		app.MapPost(path, (Func<HttpContext, Task<IResult>>)async delegate(HttpContext ctx)
		{
			ToolRegistry registry = ctx.RequestServices.GetRequiredService<ToolRegistry>();
			ILogger logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Mcp");
			IBearerTokenAccessor tokens = ctx.RequestServices.GetRequiredService<IBearerTokenAccessor>();
			TokenDiagnostic diag = tokens.Diagnose();
			logger.LogInformation("mcp request route={Route} token_present={Present} source={Source} shape={Shape} len={Len} exp_in_s={Remaining}", path, diag.Present, diag.Source, diag.Shape ?? "n/a", diag.Length, diag.SecondsUntilExpiry?.ToString() ?? "n/a");
			JsonNode body;
			try
			{
				body = await JsonNode.ParseAsync(ctx.Request.Body);
			}
			catch (JsonException ex)
			{
				return WriteError(ctx, null, -32700, "parse error: " + ex.Message);
			}
			if (body == null)
			{
				return WriteError(ctx, null, -32600, "invalid request");
			}
			JsonNode responsePayload;
			if (body is JsonArray arr)
			{
				JsonArray results = new JsonArray();
				foreach (JsonNode item in arr)
				{
					JsonObject r = await HandleAsync(ctx, item, registry);
					if (r != null)
					{
						results.Add(r);
					}
				}
				responsePayload = results;
			}
			else
			{
				JsonObject resp = await HandleAsync(ctx, body, registry);
				if (resp == null)
				{
					ctx.Response.StatusCode = 204;
					return Results.Empty;
				}
				responsePayload = resp;
			}
			AuthChallenge challenge = default(AuthChallenge);
			int num;
			if (ctx.Items.TryGetValue("mcp.authChallenge", out object challengeObj))
			{
				challenge = challengeObj as AuthChallenge;
				num = (((object)challenge != null) ? 1 : 0);
			}
			else
			{
				num = 0;
			}
			if (num != 0)
			{
				ctx.Response.StatusCode = 401;
				ctx.Response.Headers.Append("WWW-Authenticate", "Bearer error=\"invalid_token\", error_description=\"" + EscapeHeader(challenge.Description) + "\"");
				logger.LogWarning("mcp auth challenge issued route={Route} code={Code} desc={Desc}", path, challenge.Code, challenge.Description);
			}
			ctx.Response.ContentType = "application/json";
			await ctx.Response.WriteAsync(responsePayload.ToJsonString(Json));
			return Results.Empty;
		});
		return app;
	}

	private static string EscapeHeader(string s)
	{
		return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
	}

	private static async Task<JsonObject?> HandleAsync(HttpContext ctx, JsonNode? message, ToolRegistry registry)
	{
		if (!(message is JsonObject msg))
		{
			return Err(null, -32600, "invalid request");
		}
		JsonNode id = msg["id"]?.DeepClone();
		string method = msg["method"]?.GetValue<string>();
		if (id == null && method != null && method.StartsWith("notifications/", StringComparison.Ordinal))
		{
			return null;
		}
		if (string.IsNullOrEmpty(method))
		{
			return Err(id, -32600, "missing method");
		}
		try
		{
			if (1 == 0)
			{
			}
			JsonObject result = method switch
			{
				"initialize" => HandleInitialize(id, msg), 
				"tools/list" => HandleToolsList(id, registry), 
				"tools/call" => await HandleToolsCall(ctx, id, msg, registry), 
				"ping" => Ok(id, new JsonObject()), 
				_ => Err(id, -32601, "method not found: " + method), 
			};
			if (1 == 0)
			{
			}
			return result;
		}
		catch (UnauthorizedGraphException ex)
		{
			ctx.Items["mcp.authChallenge"] = new AuthChallenge(ex.ErrorCode, ex.Message);
			return Err(id, -32001, ex.Message, new JsonObject
			{
				["error_code"] = ex.ErrorCode,
				["action"] = "reauthorize_sharepoint_file_transfer_connector"
			});
		}
		catch (UploadSessionExpiredException ex2)
		{
			return Err(id, -32011, ex2.Message, new JsonObject
			{
				["error_code"] = "upload_session_expired",
				["upload_url"] = ex2.UploadUrl,
				["action"] = "call start_upload_session again and resume from byte 0"
			});
		}
		catch (GraphApiException ex3)
		{
			return Err(id, -32010, ex3.Message, ex3.ToData());
		}
		catch (ArgumentException ex4)
		{
			return Err(id, -32602, ex4.Message);
		}
		catch (Exception ex5)
		{
			return Err(id, -32000, "internal error: " + ex5.Message);
		}
	}

	private static JsonObject HandleInitialize(JsonNode? id, JsonObject msg)
	{
		string text = msg["params"]?["protocolVersion"]?.GetValue<string>() ?? "2024-11-05";
		JsonObject result = new JsonObject
		{
			["protocolVersion"] = text,
			["serverInfo"] = new JsonObject
			{
				["name"] = "sharepoint-file-transfer-mcp",
				["version"] = "0.1.0"
			},
			["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } }
		};
		return Ok(id, result);
	}

	private static JsonObject HandleToolsList(JsonNode? id, ToolRegistry registry)
	{
		JsonArray jsonArray = new JsonArray();
		foreach (ToolDescriptor tool in registry.Tools)
		{
			jsonArray.Add(tool.ToToolDescriptor());
		}
		return Ok(id, new JsonObject { ["tools"] = jsonArray });
	}

	private static async Task<JsonObject> HandleToolsCall(HttpContext ctx, JsonNode? id, JsonObject msg, ToolRegistry registry)
	{
		string name = msg["params"]?["name"]?.GetValue<string>();
		if (string.IsNullOrEmpty(name))
		{
			return Err(id, -32602, "missing tool name");
		}
		if (!registry.TryGet(name, out ToolDescriptor tool))
		{
			return Err(id, -32601, "unknown tool: " + name);
		}
		JsonObject args = (msg["params"]?["arguments"] as JsonObject) ?? new JsonObject();
		return Ok(id, await tool.InvokeAsync(ctx.RequestServices, args, ctx.RequestAborted));
	}

	private static JsonObject Ok(JsonNode? id, JsonNode result)
	{
		return new JsonObject
		{
			["jsonrpc"] = "2.0",
			["id"] = id,
			["result"] = result
		};
	}

	private static JsonObject Err(JsonNode? id, int code, string message, JsonNode? data = null)
	{
		JsonObject jsonObject = new JsonObject
		{
			["code"] = code,
			["message"] = message
		};
		if (data != null)
		{
			jsonObject["data"] = data;
		}
		return new JsonObject
		{
			["jsonrpc"] = "2.0",
			["id"] = id,
			["error"] = jsonObject
		};
	}

	private static IResult WriteError(HttpContext ctx, JsonNode? id, int code, string message)
	{
		JsonObject jsonObject = Err(id, code, message);
		ctx.Response.ContentType = "application/json";
		ctx.Response.WriteAsync(jsonObject.ToJsonString(Json)).GetAwaiter().GetResult();
		return Results.Empty;
	}
}
