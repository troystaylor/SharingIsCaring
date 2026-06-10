using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SharePointTransferMcp.Auth;

namespace SharePointTransferMcp.Graph;

public sealed class GraphClient : IGraphClient
{
	private readonly HttpClient _http;

	private readonly IBearerTokenAccessor _token;

	private readonly IHttpClientFactory _factory;

	private readonly UploadSessionRunner _runner;

	private static readonly JsonSerializerOptions _json = new JsonSerializerOptions
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	public GraphClient(HttpClient http, IBearerTokenAccessor token, IHttpClientFactory factory, UploadSessionRunner runner)
	{
		_http = http;
		_token = token;
		_factory = factory;
		_runner = runner;
	}

	public async Task<JsonObject> SearchSitesAsync(string? query, int? top, CancellationToken ct)
	{
		string q = (string.IsNullOrWhiteSpace(query) ? "*" : query);
		string url = "sites?search=" + Uri.EscapeDataString(q);
		if (top.HasValue && top.GetValueOrDefault() > 0)
		{
			url += $"&$top={top.Value}";
		}
		return await SendJsonAsync(HttpMethod.Get, url, null, ct);
	}

	public async Task<JsonObject> ListDrivesAsync(string siteId, CancellationToken ct)
	{
		return await SendJsonAsync(HttpMethod.Get, "sites/" + Uri.EscapeDataString(siteId) + "/drives", null, ct);
	}

	public async Task<JsonObject> ListFolderAsync(string driveId, string? itemPath, string? itemId, int? top, string? skipToken, CancellationToken ct)
	{
		string url = BuildItemPath(driveId, itemPath, itemId, "/children");
		List<string> qs = new List<string>();
		if (top.HasValue && top.GetValueOrDefault() > 0)
		{
			qs.Add($"$top={top.Value}");
		}
		if (!string.IsNullOrWhiteSpace(skipToken))
		{
			qs.Add("$skiptoken=" + Uri.EscapeDataString(skipToken));
		}
		if (qs.Count > 0)
		{
			url = url + "?" + string.Join("&", qs);
		}
		return await SendJsonAsync(HttpMethod.Get, url, null, ct);
	}

	public async Task<JsonObject> GetItemAsync(string driveId, string? itemPath, string? itemId, CancellationToken ct)
	{
		return await SendJsonAsync(relativeUrl: BuildItemPath(driveId, itemPath, itemId, ""), method: HttpMethod.Get, body: null, ct: ct);
	}

	public async Task<JsonObject> CreateFolderAsync(string driveId, string parentPath, string name, string conflictBehavior, CancellationToken ct)
	{
		return await SendJsonAsync(relativeUrl: BuildItemPath(driveId, parentPath, null, "/children"), body: new JsonObject
		{
			["name"] = name,
			["folder"] = new JsonObject(),
			["@microsoft.graph.conflictBehavior"] = conflictBehavior
		}, method: HttpMethod.Post, ct: ct);
	}

	public async Task<JsonObject> MoveItemAsync(string driveId, string itemId, string? newParentId, string? newName, CancellationToken ct)
	{
		string url = "drives/" + Uri.EscapeDataString(driveId) + "/items/" + Uri.EscapeDataString(itemId);
		JsonObject body = new JsonObject();
		if (!string.IsNullOrWhiteSpace(newParentId))
		{
			body["parentReference"] = new JsonObject { ["id"] = newParentId };
		}
		if (!string.IsNullOrWhiteSpace(newName))
		{
			body["name"] = newName;
		}
		return await SendJsonAsync(HttpMethod.Patch, url, body, ct);
	}

	public async Task<JsonObject> SetListItemFieldsAsync(string driveId, string itemId, JsonObject fields, CancellationToken ct)
	{
		return await SendJsonAsync(relativeUrl: $"drives/{Uri.EscapeDataString(driveId)}/items/{Uri.EscapeDataString(itemId)}/listItem/fields", method: HttpMethod.Patch, body: fields, ct: ct);
	}

	public async Task<JsonObject> CreateShareLinkAsync(string driveId, string itemId, string type, string scope, string? password, DateTimeOffset? expirationDateTime, CancellationToken ct)
	{
		string url = $"drives/{Uri.EscapeDataString(driveId)}/items/{Uri.EscapeDataString(itemId)}/createLink";
		JsonObject body = new JsonObject
		{
			["type"] = type,
			["scope"] = scope
		};
		if (!string.IsNullOrEmpty(password))
		{
			body["password"] = password;
		}
		if (expirationDateTime.HasValue)
		{
			body["expirationDateTime"] = expirationDateTime.Value.UtcDateTime.ToString("O");
		}
		return await SendJsonAsync(HttpMethod.Post, url, body, ct);
	}

	public async Task<JsonObject> CreateUploadSessionAsync(string driveId, string itemPath, string conflictBehavior, string? description, CancellationToken ct)
	{
		string encodedPath = EncodePath(itemPath);
		string url = $"drives/{Uri.EscapeDataString(driveId)}/root:/{encodedPath}:/createUploadSession";
		JsonObject item = new JsonObject { ["@microsoft.graph.conflictBehavior"] = conflictBehavior };
		if (!string.IsNullOrWhiteSpace(description))
		{
			item["description"] = description;
		}
		return await SendJsonAsync(body: new JsonObject
		{
			["item"] = item,
			["deferCommit"] = false
		}, method: HttpMethod.Post, relativeUrl: url, ct: ct);
	}

	public async Task<JsonObject> GetUploadStatusFromUrlAsync(string uploadUrl, CancellationToken ct)
	{
		using HttpClient client = _factory.CreateClient("graph-upload");
		using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, uploadUrl);
		using HttpResponseMessage resp = await client.SendAsync(req, ct);
		string body = await resp.Content.ReadAsStringAsync(ct);
		if (!resp.IsSuccessStatusCode)
		{
			throw new GraphApiException(uploadUrl, (int)resp.StatusCode, body);
		}
		return ParseObject(body);
	}

	public async Task CancelUploadAtUrlAsync(string uploadUrl, CancellationToken ct)
	{
		using HttpClient client = _factory.CreateClient("graph-upload");
		using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Delete, uploadUrl);
		using HttpResponseMessage resp = await client.SendAsync(req, ct);
		if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.NotFound)
		{
			throw new GraphApiException(responseBody: await resp.Content.ReadAsStringAsync(ct), endpoint: uploadUrl, statusCode: (int)resp.StatusCode);
		}
	}

	public async Task<JsonObject> UploadFromUrlAsync(string driveId, string itemPath, string sourceUrl, string conflictBehavior, CancellationToken ct)
	{
		string uploadUrl = (await CreateUploadSessionAsync(driveId, itemPath, conflictBehavior, null, ct))["uploadUrl"]?.GetValue<string>() ?? throw new GraphApiException("createUploadSession", 0, "missing uploadUrl in response");
		SourceUrlProbe probe = await ProbeSourceUrlAsync(sourceUrl, ct);
		if (probe.ContentLength <= 0)
		{
			try
			{
				await CancelUploadAtUrlAsync(uploadUrl, CancellationToken.None);
			}
			catch
			{
			}
			throw new GraphApiException(sourceUrl, 0, "source URL did not return Content-Length; cannot upload to Graph upload session");
		}
		try
		{
			return await RunSessionFromUrlAsync(uploadUrl, sourceUrl, probe.ContentLength, 0L, null, ct);
		}
		catch
		{
			try
			{
				await CancelUploadAtUrlAsync(uploadUrl, CancellationToken.None);
			}
			catch
			{
			}
			throw;
		}
	}

	public async Task<SourceUrlProbe> ProbeSourceUrlAsync(string sourceUrl, CancellationToken ct)
	{
		using HttpClient ingest = _factory.CreateClient("graph-ingest");
		using (HttpRequestMessage headReq = new HttpRequestMessage(HttpMethod.Head, sourceUrl))
		{
			using HttpResponseMessage headResp = await ingest.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead, ct);
			if (headResp.IsSuccessStatusCode)
			{
				long len = headResp.Content.Headers.ContentLength.GetValueOrDefault();
				string type = headResp.Content.Headers.ContentType?.MediaType;
				bool supportsRange = headResp.Headers.AcceptRanges.Any((string v) => string.Equals(v, "bytes", StringComparison.OrdinalIgnoreCase));
				if (len > 0)
				{
					return new SourceUrlProbe(len, type, supportsRange);
				}
			}
		}
		using HttpRequestMessage rangeReq = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
		rangeReq.Headers.Range = new RangeHeaderValue(0L, 0L);
		using HttpResponseMessage rangeResp = await ingest.SendAsync(rangeReq, HttpCompletionOption.ResponseHeadersRead, ct);
		if (rangeResp.StatusCode == HttpStatusCode.PartialContent)
		{
			long total = (rangeResp.Content.Headers.ContentRange?.Length).GetValueOrDefault();
			string type2 = rangeResp.Content.Headers.ContentType?.MediaType;
			if (total > 0)
			{
				return new SourceUrlProbe(total, type2, SupportsRange: true);
			}
		}
		if (rangeResp.IsSuccessStatusCode)
		{
			long len2 = rangeResp.Content.Headers.ContentLength.GetValueOrDefault();
			string type3 = rangeResp.Content.Headers.ContentType?.MediaType;
			if (len2 > 0)
			{
				return new SourceUrlProbe(len2, type3, SupportsRange: false);
			}
		}
		throw new GraphApiException(responseBody: await rangeResp.Content.ReadAsStringAsync(ct), endpoint: sourceUrl, statusCode: (int)rangeResp.StatusCode);
	}

	public async Task<JsonObject> RunSessionFromUrlAsync(string uploadUrl, string sourceUrl, long totalBytes, long startOffset, IProgress<long>? progress, CancellationToken ct)
	{
		if (totalBytes <= 0)
		{
			throw new ArgumentOutOfRangeException("totalBytes");
		}
		if (startOffset < 0 || startOffset >= totalBytes)
		{
			throw new ArgumentOutOfRangeException("startOffset");
		}
		using HttpClient ingest = _factory.CreateClient("graph-ingest");
		using HttpRequestMessage srcReq = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
		if (startOffset > 0)
		{
			srcReq.Headers.Range = new RangeHeaderValue(startOffset, null);
		}
		using HttpResponseMessage srcResp = await ingest.SendAsync(srcReq, HttpCompletionOption.ResponseHeadersRead, ct);
		if (!srcResp.IsSuccessStatusCode)
		{
			throw new GraphApiException(responseBody: await srcResp.Content.ReadAsStringAsync(ct), endpoint: sourceUrl, statusCode: (int)srcResp.StatusCode);
		}
		if (startOffset > 0 && srcResp.StatusCode != HttpStatusCode.PartialContent)
		{
			throw new GraphApiException(sourceUrl, (int)srcResp.StatusCode, $"source URL ignored Range header (returned {(int)srcResp.StatusCode}); cannot resume from offset {startOffset}");
		}
		JsonObject result;
		await using (Stream srcStream = await srcResp.Content.ReadAsStreamAsync(ct))
		{
			result = await _runner.RunFromStreamAsync(srcStream, totalBytes, uploadUrl, startOffset, progress, ct);
		}
		return result;
	}

	public async Task<string> CopyItemAsync(string driveId, string itemId, string destDriveId, string destFolderId, string? newName, string conflictBehavior, CancellationToken ct)
	{
		string url = $"drives/{Uri.EscapeDataString(driveId)}/items/{Uri.EscapeDataString(itemId)}/copy";
		if (!string.Equals(conflictBehavior, "rename", StringComparison.OrdinalIgnoreCase))
		{
			url += "?@microsoft.graph.conflictBehavior=" + Uri.EscapeDataString(conflictBehavior);
		}

		JsonObject body = new JsonObject
		{
			["parentReference"] = new JsonObject
			{
				["driveId"] = destDriveId,
				["id"] = destFolderId
			}
		};
		if (!string.IsNullOrWhiteSpace(newName))
		{
			body["name"] = newName;
		}

		using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url);
		req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _token.RequireGraphTokenAsync());
		req.Headers.Accept.Clear();
		req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		req.Content = new StringContent(body.ToJsonString(_json), Encoding.UTF8, "application/json");

		using HttpResponseMessage resp = await _http.SendAsync(req, ct);
		if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
		{
			throw new UnauthorizedGraphException(
				resp.StatusCode == HttpStatusCode.Unauthorized ? "unauthorized" : "forbidden",
				$"graph returned {(int)resp.StatusCode} for POST {url}");
		}
		if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.Accepted)
		{
			string errorBody = await resp.Content.ReadAsStringAsync(ct);
			throw new GraphApiException(url, (int)resp.StatusCode, errorBody);
		}

		string? monitorUrl = resp.Headers.Location?.ToString();
		if (string.IsNullOrEmpty(monitorUrl))
		{
			throw new GraphApiException(url, (int)resp.StatusCode, "copy accepted but no Location header returned");
		}
		return monitorUrl;
	}

	public async Task<JsonObject> CheckCopyStatusAsync(string monitorUrl, CancellationToken ct)
	{
		using HttpClient client = _factory.CreateClient("graph-upload");
		using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, monitorUrl);
		using HttpResponseMessage resp = await client.SendAsync(req, ct);
		string body = await resp.Content.ReadAsStringAsync(ct);
		if (!resp.IsSuccessStatusCode)
		{
			throw new GraphApiException(monitorUrl, (int)resp.StatusCode, body);
		}
		return ParseObject(body);
	}

	private static string BuildItemPath(string driveId, string? itemPath, string? itemId, string suffix)
	{
		if (!string.IsNullOrWhiteSpace(itemId))
		{
			return $"drives/{Uri.EscapeDataString(driveId)}/items/{Uri.EscapeDataString(itemId)}{suffix}";
		}
		string value = EncodePath(itemPath ?? string.Empty);
		if (string.IsNullOrEmpty(value))
		{
			return suffix.StartsWith('/') ? ("drives/" + Uri.EscapeDataString(driveId) + "/root" + suffix) : ("drives/" + Uri.EscapeDataString(driveId) + "/root");
		}
		return $"drives/{Uri.EscapeDataString(driveId)}/root:/{value}:{suffix}";
	}

	private static string EncodePath(string path)
	{
		string text = path.Trim('/');
		if (string.IsNullOrEmpty(text))
		{
			return string.Empty;
		}
		string[] array = text.Split('/', StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = Uri.EscapeDataString(array[i]);
		}
		return string.Join('/', array);
	}

	private async Task<JsonObject> SendJsonAsync(HttpMethod method, string relativeUrl, JsonNode? body, CancellationToken ct)
	{
		using HttpRequestMessage req = new HttpRequestMessage(method, relativeUrl);
		req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _token.RequireGraphTokenAsync());
		req.Headers.Accept.Clear();
		req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		if (body != null)
		{
			req.Content = new StringContent(body.ToJsonString(_json), Encoding.UTF8, "application/json");
		}
		using HttpResponseMessage resp = await _http.SendAsync(req, ct);
		string text = await resp.Content.ReadAsStringAsync(ct);
		if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
		{
			throw new UnauthorizedGraphException((resp.StatusCode == HttpStatusCode.Unauthorized) ? "unauthorized" : "forbidden", $"graph returned {(int)resp.StatusCode} for {method} {relativeUrl}");
		}
		if (!resp.IsSuccessStatusCode)
		{
			throw new GraphApiException(relativeUrl, (int)resp.StatusCode, text);
		}
		return ParseObject(text);
	}

	private static JsonObject ParseObject(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return new JsonObject();
		}
		JsonNode jsonNode = JsonNode.Parse(text);
		if (jsonNode is JsonObject result)
		{
			return result;
		}
		return new JsonObject { ["value"] = jsonNode };
	}
}
