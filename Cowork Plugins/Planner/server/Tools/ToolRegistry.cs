using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace PlannerCoworkMcp.Tools;

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
        "list_group_plans",
        "list_plan_tasks",
        "list_plan_buckets",
        "list_my_tasks",
        "list_my_private_tasks",
        "list_my_personal_tasks",
        "my_personal_tasks_weekly_delta",
        "list_user_tasks",
        "get_task",
        "get_task_details",
        "plan_health_summary",
    };

    public void RegisterAll(IServiceProvider _)
    {
        Add(ListGroupPlansTool.Build());
        Add(ListPlanTasksTool.Build());
        Add(ListPlanBucketsTool.Build());
        Add(ListMyTasksTool.Build());
        Add(ListMyPrivateTasksTool.Build());
        Add(ListMyPersonalTasksTool.Build());
        Add(MyPersonalTasksWeeklyDeltaTool.Build());
        Add(ListUserTasksTool.Build());
        Add(GetTaskTool.Build());
        Add(GetTaskDetailsTool.Build());
        Add(CreateTaskTool.Build());
        Add(UpdateTaskTool.Build());
        Add(UpdateTaskDetailsTool.Build());
        Add(PlanHealthSummaryTool.Build());
    }
}
