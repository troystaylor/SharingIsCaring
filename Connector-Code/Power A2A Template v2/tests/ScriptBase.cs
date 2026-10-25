using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// A local stand-in for the connector runtime, so script.csx compiles unchanged.
// It mirrors https://learn.microsoft.com/connectors/custom-connectors/write-code
public interface ILogger
{
    void LogInformation(string message);
    void LogError(string message);
}

public interface IScriptContext
{
    string CorrelationId { get; }
    string OperationId { get; }
    HttpRequestMessage Request { get; }
    ILogger Logger { get; }
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}

public abstract class ScriptBase
{
    public IScriptContext Context { get; set; }
    public CancellationToken CancellationToken { get; set; }

    public static StringContent CreateJsonContent(string serializedJson)
        => new StringContent(serializedJson, System.Text.Encoding.UTF8, "application/json");

    public abstract Task<HttpResponseMessage> ExecuteAsync();
}
