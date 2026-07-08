using System.Text.Json;

namespace FederatedCrudTest.Tools;

public static class WriteTools
{
    public static string CreateTask(string title, string category, string description)
    {
        var item = DataStore.Create(title, category, description);
        return JsonSerializer.Serialize(new { success = true, task = item }, JsonOptions.Default);
    }

    public static string UpdateTask(int id, string? title = null, string? category = null, string? status = null, string? description = null)
    {
        var item = DataStore.Update(id, title, category, status, description);
        if (item is null) return JsonSerializer.Serialize(new { error = "Task not found" });
        return JsonSerializer.Serialize(new { success = true, task = item }, JsonOptions.Default);
    }

    public static string DeleteTask(int id)
    {
        var deleted = DataStore.Delete(id);
        return JsonSerializer.Serialize(new { success = deleted, message = deleted ? "Task deleted" : "Task not found" });
    }
}
