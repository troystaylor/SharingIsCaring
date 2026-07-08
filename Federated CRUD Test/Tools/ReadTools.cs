using System.ComponentModel;
using System.Text.Json;

namespace FederatedCrudTest.Tools;

public static class ReadTools
{
    public static string ListTasks()
    {
        var items = DataStore.GetAll();
        return JsonSerializer.Serialize(items, JsonOptions.Default);
    }

    public static string GetTask(int id)
    {
        var item = DataStore.GetById(id);
        if (item is null) return JsonSerializer.Serialize(new { error = "Task not found" });
        return JsonSerializer.Serialize(item, JsonOptions.Default);
    }

    public static string SearchTasks(string query)
    {
        var results = DataStore.Search(query);
        return JsonSerializer.Serialize(results, JsonOptions.Default);
    }
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
}
