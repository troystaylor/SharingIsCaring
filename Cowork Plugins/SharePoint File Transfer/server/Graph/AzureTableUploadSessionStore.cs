using System;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace SharePointTransferMcp.Graph;

public sealed class AzureTableUploadSessionStore : IUploadSessionStore
{
	private const string PartitionKey = "session";

	private readonly TableClient? _table;

	private readonly ILogger<AzureTableUploadSessionStore> _log;

	public AzureTableUploadSessionStore(TableClient? table, ILogger<AzureTableUploadSessionStore> log)
	{
		_table = table;
		_log = log;
	}

	public async Task SaveAsync(UploadSessionRecord record, CancellationToken ct)
	{
		if (_table == null)
		{
			_log.LogDebug("upload session store disabled; SaveAsync is a no-op for {Token}", record.SessionToken);
			return;
		}
		TableEntity entity = new TableEntity("session", record.SessionToken)
		{
			["UploadUrl"] = record.UploadUrl,
			["DriveId"] = record.DriveId,
			["ItemPath"] = record.ItemPath,
			["SizeBytes"] = record.SizeBytes,
			["ExpirationDateTime"] = record.ExpirationDateTime,
			["CreatedAt"] = record.CreatedAt,
			["Status"] = record.Status,
			["UploadedBytes"] = record.UploadedBytes,
			["SourceUrl"] = record.SourceUrl,
			["ContentType"] = record.ContentType,
			["LastError"] = record.LastError,
			["DriveItemJson"] = record.DriveItemJson,
			["CompletedAt"] = record.CompletedAt
		};
		await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
	}

	public async Task<UploadSessionRecord?> GetAsync(string sessionToken, CancellationToken ct)
	{
		if (_table == null)
		{
			return null;
		}
		try
		{
			TableEntity e = (await _table.GetEntityAsync<TableEntity>("session", sessionToken, null, ct)).Value;
			return new UploadSessionRecord(sessionToken, e.GetString("UploadUrl") ?? string.Empty, e.GetString("DriveId") ?? string.Empty, e.GetString("ItemPath") ?? string.Empty, e.GetInt64("SizeBytes").GetValueOrDefault(), e.GetDateTimeOffset("ExpirationDateTime") ?? DateTimeOffset.MinValue, e.GetDateTimeOffset("CreatedAt") ?? DateTimeOffset.MinValue, e.GetString("Status") ?? "Created", e.GetInt64("UploadedBytes").GetValueOrDefault(), e.GetString("SourceUrl"), e.GetString("ContentType"), e.GetString("LastError"), e.GetString("DriveItemJson"), e.GetDateTimeOffset("CompletedAt"));
		}
		catch (RequestFailedException ex) when (ex.Status == 404)
		{
			return null;
		}
	}

	public async Task DeleteAsync(string sessionToken, CancellationToken ct)
	{
		if (_table == null)
		{
			return;
		}
		try
		{
			await _table.DeleteEntityAsync("session", sessionToken, ETag.All, ct);
		}
		catch (RequestFailedException ex) when (ex.Status == 404)
		{
		}
	}

	public async Task UpdateProgressAsync(string sessionToken, long uploadedBytes, CancellationToken ct)
	{
		if (_table == null)
		{
			return;
		}
		try
		{
			TableEntity entity = new TableEntity("session", sessionToken)
			{
				["UploadedBytes"] = uploadedBytes,
				["Status"] = "Running"
			};
			await _table.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Merge, ct);
		}
		catch (RequestFailedException exception)
		{
			_log.LogDebug(exception, "progress update failed for {Token} at {Bytes}", sessionToken, uploadedBytes);
		}
	}

	public async Task UpdateStatusAsync(string sessionToken, string status, string? lastError, string? driveItemJson, CancellationToken ct)
	{
		if (_table == null)
		{
			return;
		}
		try
		{
			TableEntity entity = new TableEntity("session", sessionToken)
			{
				["Status"] = status,
				["LastError"] = lastError,
				["DriveItemJson"] = driveItemJson,
				["CompletedAt"] = DateTimeOffset.UtcNow
			};
			await _table.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Merge, ct);
		}
		catch (RequestFailedException exception)
		{
			_log.LogWarning(exception, "status update failed for {Token} → {Status}", sessionToken, status);
		}
	}
}
