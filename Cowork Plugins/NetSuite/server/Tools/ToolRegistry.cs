using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace NetSuiteCoworkMcp.Tools;

public sealed class ToolRegistry
{
    private readonly ConcurrentDictionary<string, ToolDescriptor> _tools = new(StringComparer.Ordinal);

    public IReadOnlyCollection<ToolDescriptor> Tools => _tools.Values.OrderBy(t => t.Name).ToList();

    public void Add(ToolDescriptor tool)
    {
        if (!_tools.TryAdd(tool.Name, tool))
            throw new InvalidOperationException($"duplicate tool: {tool.Name}");
    }

    public bool TryGet(string name, [NotNullWhen(true)] out ToolDescriptor? tool)
        => _tools.TryGetValue(name, out tool);

    public static readonly IReadOnlySet<string> FederatedReadOnlyTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "run_suiteql",
        "list_records",
        "get_record",
        "list_record_types",
        "get_record_metadata",
        "get_sublist",
        "search_customers",
        "search_vendors",
        "get_open_sales_orders",
        "get_open_invoices",
    };

    public void RegisterAll(IServiceProvider _)
    {
        // Reads
        Add(RunSuiteQLTool.Build());
        Add(ListRecordsTool.Build());
        Add(GetRecordTool.Build());
        Add(ListRecordTypesTool.Build());
        Add(GetRecordMetadataTool.Build());
        Add(GetSublistTool.Build());
        Add(SearchCustomersTool.Build());
        Add(SearchVendorsTool.Build());
        Add(GetOpenSalesOrdersTool.Build());
        Add(GetOpenInvoicesTool.Build());

        // Writes
        Add(CreateRecordTool.Build());
        Add(UpdateRecordTool.Build());
        Add(DeleteRecordTool.Build());
        Add(AddSublistLineTool.Build());
        Add(UpdateSublistLineTool.Build());
        Add(DeleteSublistLineTool.Build());
    }
}
