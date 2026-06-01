using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using NetSuiteCoworkMcp.NetSuite;
using static NetSuiteCoworkMcp.Tools.ToolHelpers;

namespace NetSuiteCoworkMcp.Tools;

internal static class ListRecordTypesTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "list_record_types",
        Description = "List all NetSuite record types available through REST Web Services. Returns the metadata catalog index.",
        Annotations = new ToolAnnotations(
            Title: "List NetSuite record types",
            ReadOnlyHint: true,
            IdempotentHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(new JsonObject()),
        Invoke = async (sp, _, ct) =>
        {
            var ns = sp.GetRequiredService<INetSuiteClient>();
            var resp = await ns.ListRecordTypesAsync(ct);
            return ContentResult(resp);
        },
    };
}
