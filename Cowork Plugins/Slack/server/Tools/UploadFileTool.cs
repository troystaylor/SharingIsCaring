using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SlackCoworkMcp.Slack;
using static SlackCoworkMcp.Tools.ToolHelpers;

namespace SlackCoworkMcp.Tools;

internal static class UploadFileTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "upload_file",
        Description = "Upload a file to one or more Slack channels using the v2 external upload flow (files.getUploadURLExternal -> PUT -> files.completeUploadExternal).",
        Annotations = new ToolAnnotations(
            Title: "Upload file to Slack",
            ReadOnlyHint: true,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["channels"] = Prop("string", "Comma-separated channel IDs to share the file with."),
                ["filename"] = Prop("string", "Target filename including extension."),
                ["content"] = Prop("string", "Inline UTF-8 text content. Provide either 'content' or 'file' (base64)."),
                ["file"] = Prop("string", "Base64-encoded binary content. Provide either 'content' or 'file'."),
                ["title"] = Prop("string", "Optional human title shown in Slack."),
                ["initial_comment"] = Prop("string", "Optional comment posted alongside the file."),
                ["thread_ts"] = Prop("string", "Optional parent thread ts to attach the share to."),
            },
            required: new[] { "channels", "filename" }),
        Invoke = async (sp, args, ct) =>
        {
            var slack = sp.GetRequiredService<ISlackClient>();
            var channels = RequireString(args, "channels");
            var filename = RequireString(args, "filename");
            var title = OptString(args, "title");
            var initialComment = OptString(args, "initial_comment");
            var threadTs = OptString(args, "thread_ts");

            byte[] bytes;
            if (OptString(args, "content") is { } text)
            {
                bytes = Encoding.UTF8.GetBytes(text);
            }
            else if (OptString(args, "file") is { } b64)
            {
                bytes = Convert.FromBase64String(b64);
            }
            else
            {
                throw new ArgumentException("upload_file requires either 'content' or 'file'");
            }

            // Step 1: get an upload URL.
            var step1 = await slack.PostFormAsync("files.getUploadURLExternal", new Dictionary<string, string?>
            {
                ["filename"] = filename,
                ["length"] = bytes.Length.ToString(),
            }, ct);
            var uploadUrl = step1["upload_url"]?.GetValue<string>()
                ?? throw new SlackApiException("files.getUploadURLExternal", "missing_upload_url", step1);
            var fileId = step1["file_id"]?.GetValue<string>()
                ?? throw new SlackApiException("files.getUploadURLExternal", "missing_file_id", step1);

            // Step 2: PUT raw bytes to the upload URL.
            var put = await slack.PutRawAsync(uploadUrl, bytes, "application/octet-stream", ct);
            if (!put.IsSuccessStatusCode)
            {
                var raw = await put.Content.ReadAsStringAsync(ct);
                throw new SlackApiException("upload_url_put", $"http_{(int)put.StatusCode}",
                    new JsonObject { ["body"] = raw });
            }

            // Step 3: complete the upload, sharing into channels.
            var fileEntry = new JsonObject { ["id"] = fileId };
            if (!string.IsNullOrEmpty(title)) fileEntry["title"] = title;
            var completeBody = new JsonObject
            {
                ["files"] = new JsonArray { fileEntry },
                ["channels"] = channels,
            };
            if (!string.IsNullOrEmpty(initialComment))
                completeBody["initial_comment"] = initialComment;
            if (!string.IsNullOrEmpty(threadTs))
                completeBody["thread_ts"] = threadTs;

            var step3 = await slack.PostJsonAsync("files.completeUploadExternal", completeBody, ct);
            return ContentResult(step3);
        },
    };
}
