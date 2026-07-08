namespace FederatedCrudTest.Tools;

/// <summary>
/// In-memory data store for testing CRUD via federated connector.
/// Thread-safe for concurrent tool invocations.
/// </summary>
public static class DataStore
{
    private static readonly Lock _lock = new();
    private static int _nextId = 7;

    private static readonly List<TaskItem> _items =
    [
        new(1, "Deploy MCP server", "Infrastructure", "open", "Set up container app hosting"),
        new(2, "Configure Entra auth", "Security", "open", "Register app and expose scope"),
        new(3, "Write read tools", "Development", "done", "Implement list/get/search"),
        new(4, "Write write tools", "Development", "open", "Implement create/update/delete"),
        new(5, "Register federated connector", "Testing", "open", "Connect in M365 admin center"),
        new(6, "Test CRUD from Copilot", "Testing", "open", "Verify if write tools are callable"),
    ];

    public static List<TaskItem> GetAll()
    {
        lock (_lock) return [.. _items];
    }

    public static TaskItem? GetById(int id)
    {
        lock (_lock) return _items.FirstOrDefault(t => t.Id == id);
    }

    public static List<TaskItem> Search(string query)
    {
        lock (_lock)
            return _items.Where(t =>
                t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static TaskItem Create(string title, string category, string description)
    {
        lock (_lock)
        {
            var item = new TaskItem(_nextId++, title, category, "open", description);
            _items.Add(item);
            return item;
        }
    }

    public static TaskItem? Update(int id, string? title, string? category, string? status, string? description)
    {
        lock (_lock)
        {
            var idx = _items.FindIndex(t => t.Id == id);
            if (idx < 0) return null;
            var existing = _items[idx];
            var updated = existing with
            {
                Title = title ?? existing.Title,
                Category = category ?? existing.Category,
                Status = status ?? existing.Status,
                Description = description ?? existing.Description,
            };
            _items[idx] = updated;
            return updated;
        }
    }

    public static bool Delete(int id)
    {
        lock (_lock) return _items.RemoveAll(t => t.Id == id) > 0;
    }
}

public record TaskItem(int Id, string Title, string Category, string Status, string Description);
