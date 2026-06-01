using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace SalesforceCoworkMcp.Tools;

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
        "search_accounts",
        "get_account",
        "search_opportunities",
        "get_opportunity",
        "list_recent_activities",
        "search_contacts",
        "get_contact",
        "list_tasks",
        "get_task",
    };

    public void RegisterAll(IServiceProvider _)
    {
        Add(SearchAccountsTool.Build());
        Add(GetAccountTool.Build());
        Add(SearchOpportunitiesTool.Build());
        Add(GetOpportunityTool.Build());
        Add(ListRecentActivitiesTool.Build());
        Add(UpdateOpportunityTool.Build());
        Add(CreateTaskTool.Build());

        Add(CreateOpportunityTool.Build());
        Add(SearchContactsTool.Build());
        Add(GetContactTool.Build());
        Add(CreateContactTool.Build());
        Add(UpdateAccountTool.Build());
        Add(CreateAccountTool.Build());
        Add(UpdateContactTool.Build());
        Add(ListTasksTool.Build());
        Add(GetTaskTool.Build());
    }
}
