using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SharePointTransferMcp.Graph;

/// <summary>
/// Drives sequential chunked PUTs into a Graph upload session URL.
/// - Chunks are sized at 10 MiB and aligned to 320 KiB (Graph requirement).
/// - No Authorization header is sent: upload URLs are pre-authenticated, and adding one returns 401.
/// - Retries 5xx with exponential backoff, honours Retry-After on 429, restarts the caller on 404 (session expired).
/// </summary>
public sealed class UploadSessionRunner
{
	private const int ChunkAlignment = 327680;

	private const int DefaultChunkSize = 10485760;

	private const int MaxAttemptsPerChunk = 3;

	private readonly IHttpClientFactory _factory;

	private readonly ILogger<UploadSessionRunner> _log;

	public UploadSessionRunner(IHttpClientFactory factory, ILogger<UploadSessionRunner> log)
	{
		_factory = factory;
		_log = log;
	}

	public Task<JsonObject> RunFromStreamAsync(Stream source, long totalBytes, string uploadUrl, CancellationToken ct)
	{
		return RunFromStreamAsync(source, totalBytes, uploadUrl, 0L, null, ct);
	}

	public async Task<JsonObject> RunFromStreamAsync(Stream source, long totalBytes, string uploadUrl, long startOffset, IProgress<long>? progress, CancellationToken ct)
	{
		if (totalBytes <= 0)
		{
			throw new ArgumentOutOfRangeException("totalBytes");
		}
		if (startOffset < 0 || startOffset >= totalBytes)
		{
			throw new ArgumentOutOfRangeException("startOffset");
		}
		if (string.IsNullOrWhiteSpace(uploadUrl))
		{
			throw new ArgumentException("uploadUrl required", "uploadUrl");
		}
		int chunkSize = AlignChunk(10485760);
		byte[] buffer = new byte[chunkSize];
		long offset = startOffset;
		JsonObject finalResult = null;
		using HttpClient client = _factory.CreateClient("graph-upload");
		while (offset < totalBytes)
		{
			int toRead = (int)Math.Min(chunkSize, totalBytes - offset);
			int read = await ReadFullAsync(source, buffer, toRead, ct);
			if (read == 0)
			{
				throw new GraphApiException(uploadUrl, 0, $"source stream ended at offset {offset} but Content-Length was {totalBytes}");
			}
			long rangeStart = offset;
			long rangeEnd = offset + read - 1;
			JsonObject response = await PutChunkAsync(client, uploadUrl, buffer, read, rangeStart, rangeEnd, totalBytes, ct);
			offset += read;
			try
			{
				progress?.Report(offset);
			}
			catch
			{
			}
			if (offset >= totalBytes)
			{
				finalResult = response;
			}
		}
		return finalResult ?? new JsonObject
		{
			["status"] = "completed",
			["uploadedBytes"] = totalBytes
		};
	}

	private async Task<JsonObject?> PutChunkAsync(HttpClient client, string uploadUrl, byte[] buffer, int length, long rangeStart, long rangeEnd, long totalBytes, CancellationToken ct)
	{
		for (int attempt = 1; attempt <= 3; attempt++)
		{
			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
			ByteArrayContent content = new ByteArrayContent(buffer, 0, length);
			content.Headers.ContentLength = length;
			content.Headers.ContentRange = new ContentRangeHeaderValue(rangeStart, rangeEnd, totalBytes);
			content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
			req.Content = content;
			HttpResponseMessage resp;
			try
			{
				resp = await client.SendAsync(req, ct);
			}
			catch (HttpRequestException exception) when (attempt < 3)
			{
				_log.LogWarning(exception, "transient network error on chunk {Range} attempt {Attempt}", $"{rangeStart}-{rangeEnd}", attempt);
				await Task.Delay(BackoffDelay(attempt), ct);
				goto end_IL_0069;
			}
			int status = (int)resp.StatusCode;
			string body = await resp.Content.ReadAsStringAsync(ct);
			resp.Dispose();
			if ((uint)(status - 200) <= 1u)
			{
				return ParseObject(body);
			}
			int num;
			switch (status)
			{
			case 202:
				return null;
			case 404:
				throw new UploadSessionExpiredException(uploadUrl, "upload session no longer exists (HTTP 404); start a new session and resume from byte 0");
			case 429:
				num = ((attempt < 3) ? 1 : 0);
				break;
			default:
				num = 0;
				break;
			}
			if (num != 0)
			{
				TimeSpan retry = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2L);
				_log.LogWarning("graph throttled chunk {Range} (429); waiting {RetrySeconds}s", $"{rangeStart}-{rangeEnd}", retry.TotalSeconds);
				await Task.Delay(retry, ct);
				continue;
			}
			if (status >= 500 && attempt < 3)
			{
				_log.LogWarning("graph 5xx ({Status}) on chunk {Range} attempt {Attempt}", status, $"{rangeStart}-{rangeEnd}", attempt);
				await Task.Delay(BackoffDelay(attempt), ct);
				continue;
			}
			throw new GraphApiException(uploadUrl, status, body);
			end_IL_0069:;
		}
		throw new GraphApiException(uploadUrl, 0, $"chunk {rangeStart}-{rangeEnd} failed after {3} attempts");
	}

	private static int AlignChunk(int desired)
	{
		int num = desired / 327680 * 327680;
		return (num <= 0) ? 327680 : num;
	}

	private static async Task<int> ReadFullAsync(Stream source, byte[] buffer, int count, CancellationToken ct)
	{
		int total;
		int n;
		for (total = 0; total < count; total += n)
		{
			n = await source.ReadAsync(buffer.AsMemory(total, count - total), ct);
			if (n == 0)
			{
				break;
			}
		}
		return total;
	}

	private static TimeSpan BackoffDelay(int attempt)
	{
		return TimeSpan.FromMilliseconds(500.0 * Math.Pow(2.0, attempt - 1));
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
