using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Dataverse SQL: dual-purpose connector over the Web API "sql" query option.
/// Exposes an MCP endpoint for Copilot Studio plus typed operations for Power Automate,
/// both backed by one engine that validates the statement against the supported T-SQL
/// subset, resolves the entity set from the FROM clause, and pages with odata.maxpagesize.
/// </summary>
public class Script : ScriptBase
{
    private const bool APP_INSIGHTS_ENABLED = false;
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    private const string ApiPath = "/api/data/v9.2/";
    private const string ProtocolVersion = "2024-11-05";

    private const int DefaultPageSize = 100;
    private const int MaxPageSize = 5000;
    private const int DefaultTableLimit = 1000;
    private const int MaxTableLimit = 5000;
    private const int MaxColumnsReturned = 250;
    private const int MaxSuggestions = 10;
    private const int AggregateRecordLimit = 50000;
    private const int MaxRowBudget = 100000;
    private const int MaxThrottleRetries = 2;

    // The connector runtime kills a script at two minutes. Paging and throttle waits stop short of
    // that so a long run returns the rows collected so far with a usable next link, rather than
    // losing the whole call.
    private static readonly TimeSpan ScriptBudget = TimeSpan.FromSeconds(100);

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private TimeSpan Remaining
    {
        get
        {
            var left = ScriptBudget - _clock.Elapsed;
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }
    }

    private static readonly HashSet<string> KnownOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        "InvokeMCP", "ExecuteSql", "GetNextPage", "CountRows", "ValidateSql", "ListTables", "ListColumns",
        "GetRowSchema"
    };

    /// <summary>Everything Dataverse will evaluate inside a SELECT. Anything else is rejected before the call.</summary>
    private static readonly HashSet<string> AllowedFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "SUM", "AVG", "MIN", "MAX", "DATEADD", "GETUTCDATE"
    };

    /// <summary>Keywords that can be followed by "(" without being a function call.</summary>
    private static readonly HashSet<string> NonFunctionKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "IN", "AND", "OR", "NOT", "ON", "WHERE", "BY", "FROM", "SELECT", "JOIN", "HAVING", "UNION",
        "BETWEEN", "LIKE", "AS", "VALUES", "EXCEPT", "INTERSECT", "ALL", "ANY", "DISTINCT", "OFFSET",
        "FETCH", "ROWS", "ROW", "NEXT", "ONLY", "ASC", "DESC", "GROUP", "ORDER", "INNER", "LEFT",
        "RIGHT", "FULL", "OUTER", "CROSS", "TOP", "IS", "NULL", "EXISTS", "CASE", "WHEN", "THEN",
        "ELSE", "END", "APPLY"
    };

    /// <summary>Reserved words that can follow a table name and must not be mistaken for its alias.</summary>
    private static readonly HashSet<string> NotAnAlias = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AS", "ON", "WHERE", "GROUP", "ORDER", "OFFSET", "INNER", "LEFT", "RIGHT", "FULL", "CROSS",
        "JOIN", "OUTER", "APPLY", "UNION", "EXCEPT", "INTERSECT", "HAVING"
    };

    private Dictionary<string, TableSummary> _tables;

    private readonly Dictionary<string, Dictionary<string, string>> _columnTypes =
        new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- models

    private class TableSummary
    {
        public string LogicalName;
        public string EntitySetName;
        public string DisplayName;
        public string PrimaryId;
        public string PrimaryName;
        public bool IsCustom;
    }

    private class Validation
    {
        public string Statement;
        public string Probe;
        public string BaseTable;
        public JArray Errors = new JArray();
        public JArray Warnings = new JArray();

        public bool IsValid
        {
            get { return Errors.Count == 0; }
        }
    }

    private class DataverseSqlException : Exception
    {
        public DataverseSqlException(string code, string message) : base(message)
        {
            this.Code = code;
        }

        public string Code;
        public string Hint;
        public string ProvidedValue;
        public JArray ValidValues;
        public JArray Issues;
        public int RetryAfterSeconds;

        public JObject ToJson()
        {
            return new JObject
            {
                ["error"] = Code,
                ["message"] = Message,
                ["providedValue"] = ProvidedValue ?? string.Empty,
                ["validValues"] = ValidValues ?? new JArray(),
                ["issues"] = Issues ?? new JArray(),
                ["retryAfterSeconds"] = RetryAfterSeconds,
                ["hint"] = Hint ?? string.Empty
            };
        }
    }

    // --------------------------------------------------------------- routing

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var operation = ResolveOperation();

        try
        {
            switch (operation)
            {
                case "InvokeMCP":
                    return await HandleMcpAsync().ConfigureAwait(false);
                case "ExecuteSql":
                    return JsonResponse(HttpStatusCode.OK,
                        await OpRunSqlAsync(await ReadArgsAsync().ConfigureAwait(false)).ConfigureAwait(false));
                case "GetNextPage":
                    return JsonResponse(HttpStatusCode.OK,
                        await OpNextPageAsync(await ReadArgsAsync().ConfigureAwait(false)).ConfigureAwait(false));
                case "CountRows":
                    return JsonResponse(HttpStatusCode.OK,
                        await OpCountRowsAsync(await ReadArgsAsync().ConfigureAwait(false)).ConfigureAwait(false));
                case "ValidateSql":
                    return JsonResponse(HttpStatusCode.OK,
                        await OpValidateSqlAsync(await ReadArgsAsync().ConfigureAwait(false)).ConfigureAwait(false));
                case "ListTables":
                    return JsonResponse(HttpStatusCode.OK,
                        await OpListTablesAsync(await ReadArgsAsync().ConfigureAwait(false)).ConfigureAwait(false));
                case "ListColumns":
                    return JsonResponse(HttpStatusCode.OK,
                        await OpDescribeTableAsync(await ReadArgsAsync().ConfigureAwait(false)).ConfigureAwait(false));
                case "GetRowSchema":
                    return JsonResponse(HttpStatusCode.OK,
                        await OpRowSchemaAsync(await ReadArgsAsync().ConfigureAwait(false)).ConfigureAwait(false));
                default:
                    var unknown = new DataverseSqlException("unknown_operation",
                        "Operation '" + (operation ?? "(none)") + "' is not implemented by this connector.");
                    unknown.ValidValues = new JArray(KnownOperations.Select(o => new JObject { ["value"] = o }));
                    return JsonResponse(HttpStatusCode.BadRequest, unknown.ToJson());
            }
        }
        catch (DataverseSqlException sx)
        {
            await LogToAppInsightsAsync("SqlError", new Dictionary<string, string>
            {
                ["operation"] = operation ?? string.Empty,
                ["code"] = sx.Code,
                ["message"] = sx.Message
            }).ConfigureAwait(false);

            return JsonResponse(HttpStatusCode.BadRequest, sx.ToJson());
        }
        catch (Exception ex)
        {
            await LogToAppInsightsAsync("UnhandledError", new Dictionary<string, string>
            {
                ["operation"] = operation ?? string.Empty,
                ["message"] = ex.Message,
                ["stack"] = ex.StackTrace ?? string.Empty
            }).ConfigureAwait(false);

            return JsonResponse(HttpStatusCode.InternalServerError, new JObject
            {
                ["error"] = "unhandled_exception",
                ["message"] = ex.Message
            });
        }
    }

    /// <summary>OperationId arrives base64 encoded in some regions; fall back to the request path.</summary>
    private string ResolveOperation()
    {
        var operationId = this.Context.OperationId;

        if (!string.IsNullOrEmpty(operationId))
        {
            if (KnownOperations.Contains(operationId))
            {
                return operationId;
            }

            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(operationId));

                if (KnownOperations.Contains(decoded))
                {
                    return decoded;
                }
            }
            catch
            {
                // Not base64; fall through to path routing.
            }
        }

        var path = (this.Context.Request.RequestUri.AbsolutePath ?? string.Empty).TrimEnd('/');

        if (path.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase) || path.Length == 0) return "InvokeMCP";
        if (path.EndsWith("/sql/query", StringComparison.OrdinalIgnoreCase)) return "ExecuteSql";
        if (path.EndsWith("/sql/next", StringComparison.OrdinalIgnoreCase)) return "GetNextPage";
        if (path.EndsWith("/sql/count", StringComparison.OrdinalIgnoreCase)) return "CountRows";
        if (path.EndsWith("/sql/validate", StringComparison.OrdinalIgnoreCase)) return "ValidateSql";
        if (path.EndsWith("/metadata/tables", StringComparison.OrdinalIgnoreCase)) return "ListTables";
        if (path.EndsWith("/metadata/columns", StringComparison.OrdinalIgnoreCase)) return "ListColumns";
        if (path.EndsWith("/sql/schema", StringComparison.OrdinalIgnoreCase)) return "GetRowSchema";

        return operationId;
    }

    /// <summary>Typed operations arrive as either a JSON body or a query string; both feed the same op methods.</summary>
    private async Task<JObject> ReadArgsAsync()
    {
        var args = new JObject();
        var query = this.Context.Request.RequestUri.Query;

        if (!string.IsNullOrEmpty(query))
        {
            foreach (var pair in query.TrimStart('?').Split('&'))
            {
                if (pair.Length == 0)
                {
                    continue;
                }

                var separator = pair.IndexOf('=');
                var key = separator < 0 ? pair : pair.Substring(0, separator);
                var value = separator < 0 ? string.Empty : pair.Substring(separator + 1);

                args[Uri.UnescapeDataString(key.Replace("+", " "))] = Uri.UnescapeDataString(value.Replace("+", " "));
            }
        }

        if (this.Context.Request.Content == null)
        {
            return args;
        }

        var raw = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return args;
        }

        JObject body;

        try
        {
            body = JObject.Parse(raw);
        }
        catch (JsonException)
        {
            throw new DataverseSqlException("invalid_json", "The request body is not valid JSON.");
        }

        foreach (var property in body.Properties())
        {
            args[property.Name] = property.Value;
        }

        return args;
    }

    private HttpResponseMessage JsonResponse(HttpStatusCode statusCode, JToken payload)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Content = CreateJsonContent(payload.ToString(Newtonsoft.Json.Formatting.None));
        return response;
    }

    // ----------------------------------------------------- Dataverse access

    private async Task<JObject> SendDataverseAsync(string url, string prefer)
    {
        for (var attempt = 0; ; attempt++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (this.Context.Request.Headers.Authorization != null)
            {
                request.Headers.Authorization = this.Context.Request.Headers.Authorization;
            }

            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
            request.Headers.TryAddWithoutValidation("OData-Version", "4.0");

            if (!string.IsNullOrEmpty(prefer))
            {
                request.Headers.TryAddWithoutValidation("Prefer", prefer);
            }

            var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
            var raw = response.Content == null
                ? string.Empty
                : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if ((int)response.StatusCode == 429)
            {
                var wait = RetryAfterOf(response);

                // Dataverse sizes Retry-After by how demanding the last five minutes were, so it can
                // exceed what is left of the script budget. Waiting past that would be killed mid-wait
                // and lose the error detail, so the wait is handed to the caller instead.
                if (attempt >= MaxThrottleRetries || wait >= Remaining)
                {
                    throw BuildThrottleException(wait);
                }

                await Task.Delay(wait, this.CancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw BuildDataverseException(response.StatusCode, raw);
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                return new JObject();
            }

            return JObject.Parse(raw);
        }
    }

    /// <summary>Retry-After arrives as a delta in seconds, but the header also allows an HTTP date.</summary>
    private static TimeSpan RetryAfterOf(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter != null)
        {
            if (retryAfter.Delta.HasValue)
            {
                return retryAfter.Delta.Value;
            }

            if (retryAfter.Date.HasValue)
            {
                var until = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                return until > TimeSpan.Zero ? until : TimeSpan.Zero;
            }
        }

        return TimeSpan.FromSeconds(5);
    }

    private DataverseSqlException BuildThrottleException(TimeSpan wait)
    {
        var seconds = (int)Math.Ceiling(wait.TotalSeconds);
        var throttled = new DataverseSqlException("throttled",
            "Dataverse service protection limits are in effect. Retry after " + seconds + " seconds.");
        throttled.RetryAfterSeconds = seconds;
        throttled.Hint = "The connector retries a short wait automatically. This one is longer than the script may run, "
            + "so wait " + seconds + " seconds before calling again -- in a flow, a Delay action followed by a retry.";

        return throttled;
    }

    private async Task<JObject> GetJsonAsync(string relativeUrl, string prefer = null)
    {
        var baseUrl = this.Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);
        return await SendDataverseAsync(baseUrl + ApiPath + relativeUrl.TrimStart('/'), prefer).ConfigureAwait(false);
    }

    private DataverseSqlException BuildDataverseException(HttpStatusCode statusCode, string raw)
    {
        var message = raw;

        try
        {
            var error = JObject.Parse(raw)["error"] as JObject;

            if (error != null && error["message"] != null)
            {
                message = error["message"].ToString();
            }
        }
        catch
        {
            // Leave the raw payload as the message.
        }

        var exception = new DataverseSqlException("dataverse_error", message);

        if (message.IndexOf("AggregateQueryRecordLimit", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("8004E023", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            exception.Code = "aggregate_limit_exceeded";
            exception.Hint = "An aggregate query may evaluate at most " + AggregateRecordLimit
                + " records. Narrow the WHERE clause, for example to a date range or a subset of a choice column, "
                + "then run the query several times and combine the results.";
        }
        else if (message.IndexOf("Could not find a property named", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            exception.Code = "unknown_column";
            exception.Hint = "Call describe_table or List columns to confirm the column logical names before querying.";
        }
        else if (statusCode == HttpStatusCode.NotFound)
        {
            exception.Code = "not_found";
            exception.Hint = "Confirm the table exists in this environment with list_tables.";
        }
        else
        {
            exception.Hint = "Dataverse rejected the statement. Check it against the supported SQL subset with validate_sql.";
        }

        return exception;
    }

    private static string EscapeOData(string value)
    {
        return (value ?? string.Empty).Replace("'", "''");
    }

    private static string SanitizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().Trim('[', ']');

        if (trimmed.Length == 0)
        {
            return null;
        }

        foreach (var character in trimmed)
        {
            var isValid = (character >= 'a' && character <= 'z')
                || (character >= 'A' && character <= 'Z')
                || (character >= '0' && character <= '9')
                || character == '_';

            if (!isValid)
            {
                return null;
            }
        }

        return trimmed.ToLowerInvariant();
    }

    private static string LabelOf(JToken label)
    {
        if (label == null || label.Type != JTokenType.Object)
        {
            return null;
        }

        var userLocalized = label["UserLocalizedLabel"];

        if (userLocalized != null && userLocalized.Type == JTokenType.Object && userLocalized["Label"] != null)
        {
            return userLocalized["Label"].ToString();
        }

        var localized = label["LocalizedLabels"] as JArray;

        if (localized != null && localized.Count > 0
            && localized[0].Type == JTokenType.Object && localized[0]["Label"] != null)
        {
            return localized[0]["Label"].ToString();
        }

        return null;
    }

    // ------------------------------------------------------------- metadata

    private async Task<Dictionary<string, TableSummary>> GetAllTablesAsync()
    {
        if (_tables != null)
        {
            return _tables;
        }

        var url = "EntityDefinitions"
            + "?$select=LogicalName,EntitySetName,DisplayName,PrimaryIdAttribute,PrimaryNameAttribute,IsCustomEntity";

        var response = await GetJsonAsync(url).ConfigureAwait(false);
        var records = response["value"] as JArray;

        _tables = new Dictionary<string, TableSummary>(StringComparer.OrdinalIgnoreCase);

        if (records == null)
        {
            return _tables;
        }

        foreach (JObject record in records.OfType<JObject>())
        {
            var logicalName = record.Value<string>("LogicalName");
            var entitySetName = record.Value<string>("EntitySetName");

            // A table with no entity set has no Web API resource to hang ?sql= on.
            if (string.IsNullOrEmpty(logicalName) || string.IsNullOrEmpty(entitySetName))
            {
                continue;
            }

            _tables[logicalName] = new TableSummary
            {
                LogicalName = logicalName,
                EntitySetName = entitySetName,
                DisplayName = LabelOf(record["DisplayName"]) ?? logicalName,
                PrimaryId = record.Value<string>("PrimaryIdAttribute"),
                PrimaryName = record.Value<string>("PrimaryNameAttribute"),
                IsCustom = record.Value<bool?>("IsCustomEntity") ?? false
            };
        }

        return _tables;
    }

    /// <summary>
    /// The entity set in the request URL has to match the base table of the query, and the two names
    /// differ (account / accounts, msdyn_botsession / msdyn_botsessions), so the FROM clause is
    /// resolved through metadata rather than pluralized by rule.
    /// </summary>
    private async Task<TableSummary> ResolveBaseTableAsync(string name)
    {
        var identifier = SanitizeIdentifier(name);

        if (identifier == null)
        {
            var invalid = new DataverseSqlException("invalid_table",
                "'" + (name ?? "(none)") + "' is not a valid table name.");
            invalid.ProvidedValue = name;
            invalid.Hint = "Table names contain only letters, digits and underscores.";
            throw invalid;
        }

        var tables = await GetAllTablesAsync().ConfigureAwait(false);
        TableSummary summary;

        if (tables.TryGetValue(identifier, out summary))
        {
            return summary;
        }

        var bySet = tables.Values.FirstOrDefault(t =>
            string.Equals(t.EntitySetName, identifier, StringComparison.OrdinalIgnoreCase));

        if (bySet != null)
        {
            var wrongName = new DataverseSqlException("entity_set_in_from",
                "'" + identifier + "' is an entity set name. The query must name the table itself, '" + bySet.LogicalName + "'.");
            wrongName.ProvidedValue = identifier;
            wrongName.ValidValues = new JArray(new JObject
            {
                ["value"] = bySet.LogicalName,
                ["title"] = bySet.DisplayName
            });
            wrongName.Hint = "Use FROM " + bySet.LogicalName
                + ". The connector adds the entity set to the request URL for you.";
            throw wrongName;
        }

        var notFound = new DataverseSqlException("unknown_table",
            "No table named '" + identifier + "' exists in this environment.");
        notFound.ProvidedValue = identifier;
        notFound.ValidValues = new JArray(Suggest(tables, identifier).Select(t => new JObject
        {
            ["value"] = t.LogicalName,
            ["title"] = t.DisplayName
        }));
        notFound.Hint = "Call list_tables with a search term to find the real logical name.";
        throw notFound;
    }

    private static IEnumerable<TableSummary> Suggest(Dictionary<string, TableSummary> tables, string term)
    {
        return tables.Values
            .Where(t => t.LogicalName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                || (t.DisplayName ?? string.Empty).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(t => t.LogicalName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSuggestions);
    }

    private async Task<JArray> GetColumnsAsync(TableSummary table, string search)
    {
        var url = "EntityDefinitions(LogicalName='" + EscapeOData(table.LogicalName) + "')/Attributes"
            + "?$select=LogicalName,DisplayName,AttributeType,AttributeOf,IsPrimaryId,IsPrimaryName";

        var response = await GetJsonAsync(url).ConfigureAwait(false);
        var records = response["value"] as JArray;

        if (records == null)
        {
            return new JArray();
        }

        var columns = new List<JObject>();

        foreach (JObject record in records.OfType<JObject>())
        {
            var logicalName = record.Value<string>("LogicalName");

            // AttributeOf marks derived columns -- lookup name shadows, base currency amounts --
            // that cannot be named in a SELECT list.
            if (string.IsNullOrEmpty(logicalName) || !string.IsNullOrEmpty(record.Value<string>("AttributeOf")))
            {
                continue;
            }

            var displayName = LabelOf(record["DisplayName"]) ?? logicalName;

            if (!string.IsNullOrWhiteSpace(search)
                && logicalName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                && displayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            columns.Add(new JObject
            {
                ["logicalName"] = logicalName,
                ["displayName"] = displayName,
                ["type"] = record.Value<string>("AttributeType") ?? string.Empty,
                ["isPrimaryId"] = record.Value<bool?>("IsPrimaryId") ?? false,
                ["isPrimaryName"] = record.Value<bool?>("IsPrimaryName") ?? false
            });
        }

        return new JArray(columns.OrderBy(c => c.Value<string>("logicalName"), StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Column logical name to Dataverse attribute type, cached because one statement may join several tables.</summary>
    private async Task<Dictionary<string, string>> GetColumnTypesAsync(string logicalName)
    {
        Dictionary<string, string> cached;

        if (_columnTypes.TryGetValue(logicalName, out cached))
        {
            return cached;
        }

        var url = "EntityDefinitions(LogicalName='" + EscapeOData(logicalName) + "')/Attributes"
            + "?$select=LogicalName,AttributeType";

        var response = await GetJsonAsync(url).ConfigureAwait(false);
        var records = response["value"] as JArray;
        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (records != null)
        {
            foreach (JObject record in records.OfType<JObject>())
            {
                var column = record.Value<string>("LogicalName");

                if (!string.IsNullOrEmpty(column))
                {
                    types[column] = record.Value<string>("AttributeType") ?? string.Empty;
                }
            }
        }

        _columnTypes[logicalName] = types;
        return types;
    }

    // ----------------------------------------------------------- SQL parsing

    /// <summary>
    /// Strips comments and masks string literals in a single pass. The masked copy keeps the
    /// original offsets and whitespace, so clause detection never trips over a keyword that
    /// only appears inside a quoted value.
    /// </summary>
    private static void Normalize(string sql, out string statement, out string probe)
    {
        var text = new StringBuilder();
        var mask = new StringBuilder();
        var inLiteral = false;
        var index = 0;

        while (index < sql.Length)
        {
            var character = sql[index];

            if (!inLiteral && character == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            {
                while (index < sql.Length && sql[index] != '\n')
                {
                    index++;
                }

                text.Append(' ');
                mask.Append(' ');
                continue;
            }

            if (!inLiteral && character == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                index += 2;

                while (index + 1 < sql.Length && !(sql[index] == '*' && sql[index + 1] == '/'))
                {
                    index++;
                }

                index = Math.Min(sql.Length, index + 2);
                text.Append(' ');
                mask.Append(' ');
                continue;
            }

            if (character == '\'')
            {
                inLiteral = !inLiteral;
                text.Append(character);
                mask.Append(character);
                index++;
                continue;
            }

            text.Append(character);
            mask.Append(inLiteral && !char.IsWhiteSpace(character) ? 'x' : character);
            index++;
        }

        statement = text.ToString().Trim();
        probe = mask.ToString().Trim();
    }

    private static string ClauseText(string probe, string clause, params string[] terminators)
    {
        int start;
        int length;

        return ClauseRange(probe, clause, terminators, out start, out length)
            ? probe.Substring(start, length)
            : null;
    }

    /// <summary>
    /// Locates a clause body by offset. Offsets are shared between the masked probe and the
    /// statement, so a range found on one can be read from the other.
    /// </summary>
    private static bool ClauseRange(string probe, string clause, string[] terminators, out int start, out int length)
    {
        start = 0;
        length = 0;

        var match = Regex.Match(probe, @"\b" + clause.Replace(" ", @"\s+") + @"\b", RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return false;
        }

        start = match.Index + match.Length;
        var remainder = probe.Substring(start);
        var end = remainder.Length;

        foreach (var terminator in terminators ?? new string[0])
        {
            var stop = Regex.Match(remainder, @"\b" + terminator.Replace(" ", @"\s+") + @"\b", RegexOptions.IgnoreCase);

            if (stop.Success && stop.Index < end)
            {
                end = stop.Index;
            }
        }

        length = end;
        return true;
    }

    /// <summary>Splits on commas that sit outside any parentheses, so COUNT(*) stays in one piece.</summary>
    private static List<string> SplitTopLevel(string probe, string statement, int start, int length)
    {
        var items = new List<string>();
        var depth = 0;
        var itemStart = start;

        for (var index = start; index < start + length; index++)
        {
            var character = probe[index];

            if (character == '(')
            {
                depth++;
            }
            else if (character == ')')
            {
                depth--;
            }
            else if (character == ',' && depth == 0)
            {
                items.Add(statement.Substring(itemStart, index - itemStart));
                itemStart = index + 1;
            }
        }

        items.Add(statement.Substring(itemStart, start + length - itemStart));

        return items.Select(i => i.Trim()).Where(i => i.Length > 0).ToList();
    }

    /// <summary>Maps each table alias in the FROM and JOIN clauses back to its table logical name.</summary>
    private static Dictionary<string, string> BuildAliasMap(string probe)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in Regex.Matches(probe,
            @"\b(?:FROM|JOIN)\s+\[?([A-Za-z_][A-Za-z0-9_]*)\]?(?:\s+(?:AS\s+)?\[?([A-Za-z_][A-Za-z0-9_]*)\]?)?",
            RegexOptions.IgnoreCase))
        {
            var table = match.Groups[1].Value;
            aliases[table] = table;

            var alias = match.Groups[2].Success ? match.Groups[2].Value : null;

            if (!string.IsNullOrEmpty(alias) && !NotAnAlias.Contains(alias))
            {
                aliases[alias] = table;
            }
        }

        return aliases;
    }

    private static JObject JsonType(string attributeType)
    {
        switch (attributeType ?? string.Empty)
        {
            case "Integer":
            case "BigInt":
            case "Picklist":
            case "State":
            case "Status":
                return new JObject { ["type"] = "integer" };
            case "Decimal":
            case "Double":
            case "Money":
                return new JObject { ["type"] = "number" };
            case "Boolean":
                return new JObject { ["type"] = "boolean" };
            case "DateTime":
                return new JObject { ["type"] = "string", ["format"] = "date-time" };
            default:
                return new JObject { ["type"] = "string" };
        }
    }

    private static void AddIssue(JArray target, string rule, string message)
    {
        target.Add(new JObject
        {
            ["rule"] = rule,
            ["message"] = message
        });
    }

    /// <summary>
    /// Checks a statement against the read-only T-SQL subset the Web API accepts. Every rule here
    /// fails locally with a named issue instead of surfacing a raw Dataverse fault.
    /// </summary>
    private static Validation Validate(string sql)
    {
        var result = new Validation();
        result.Statement = string.Empty;
        result.Probe = string.Empty;

        if (string.IsNullOrWhiteSpace(sql))
        {
            AddIssue(result.Errors, "empty_statement", "No SQL statement was supplied.");
            return result;
        }

        string statement;
        string probe;
        Normalize(sql, out statement, out probe);

        while (statement.EndsWith(";", StringComparison.Ordinal))
        {
            statement = statement.Substring(0, statement.Length - 1).TrimEnd();
            probe = probe.Substring(0, probe.Length - 1).TrimEnd();
        }

        result.Statement = statement;
        result.Probe = probe;

        if (statement.Length == 0)
        {
            AddIssue(result.Errors, "empty_statement", "The statement is empty once comments are removed.");
            return result;
        }

        if (probe.IndexOf(';') >= 0)
        {
            AddIssue(result.Errors, "multiple_statements",
                "Dataverse runs one SELECT per request and cannot return multiple result sets. Remove the extra statement.");
        }

        // A second statement does not need a semicolon to be a second statement, so the write
        // keywords are rejected wherever they appear outside a quoted value.
        var write = Regex.Match(probe,
            @"\b(INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|TRUNCATE|GRANT|REVOKE|DECLARE|EXEC|EXECUTE)\b",
            RegexOptions.IgnoreCase);

        if (write.Success)
        {
            AddIssue(result.Errors, "write_statement",
                write.Value.ToUpperInvariant() + " is not allowed. This connector is read-only and Dataverse accepts SELECT only.");
        }

        if (!Regex.IsMatch(probe, @"^\s*SELECT\b", RegexOptions.IgnoreCase))
        {
            AddIssue(result.Errors, "not_a_select",
                "Only SELECT is supported. INSERT, UPDATE, DELETE, MERGE, DECLARE, WITH and ALTER are all rejected.");
            return result;
        }

        if (Regex.IsMatch(probe, @"^\s*SELECT\s+(DISTINCT\s+)?(TOP\s+\d+\s+)?\*", RegexOptions.IgnoreCase)
            || Regex.IsMatch(probe, @"\b[A-Za-z_][A-Za-z0-9_]*\s*\.\s*\*"))
        {
            AddIssue(result.Errors, "select_star", "SELECT * is not supported. Name each column explicitly.");
        }

        if (!Regex.IsMatch(probe, @"\bFROM\b", RegexOptions.IgnoreCase))
        {
            AddIssue(result.Errors, "missing_from", "The statement has no FROM clause, so no base table can be resolved.");
        }

        if (Regex.IsMatch(probe, @"\(\s*SELECT\b", RegexOptions.IgnoreCase))
        {
            AddIssue(result.Errors, "subquery", "Subqueries and common table expressions are not supported.");
        }

        if (Regex.IsMatch(probe, @"\bHAVING\b", RegexOptions.IgnoreCase))
        {
            AddIssue(result.Errors, "having", "HAVING is not supported. Filter with WHERE before aggregating.");
        }

        if (Regex.IsMatch(probe, @"\b(UNION|EXCEPT|INTERSECT)\b", RegexOptions.IgnoreCase))
        {
            AddIssue(result.Errors, "set_operator", "UNION, EXCEPT and INTERSECT are not supported.");
        }

        var badJoin = Regex.Match(probe, @"\b(RIGHT|FULL|CROSS)\s+(OUTER\s+)?(JOIN|APPLY)\b", RegexOptions.IgnoreCase);

        if (badJoin.Success)
        {
            AddIssue(result.Errors, "unsupported_join",
                Regex.Replace(badJoin.Value, @"\s+", " ").ToUpperInvariant()
                + " is not supported. Only INNER JOIN and LEFT JOIN are available.");
        }

        if (Regex.IsMatch(probe, @"\bCASE\b", RegexOptions.IgnoreCase))
        {
            AddIssue(result.Errors, "case_expression", "CASE expressions are not supported.");
        }

        if (Regex.IsMatch(probe, @"\bEXISTS\b", RegexOptions.IgnoreCase))
        {
            AddIssue(result.Errors, "exists", "EXISTS and NOT EXISTS are not supported.");
        }

        if (Regex.IsMatch(probe, @"\bOVER\s*\(", RegexOptions.IgnoreCase))
        {
            AddIssue(result.Errors, "window_function", "Window functions are not supported.");
        }

        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in Regex.Matches(probe, @"\b([A-Za-z_][A-Za-z0-9_]*)\s*\("))
        {
            var name = match.Groups[1].Value;

            if (NonFunctionKeywords.Contains(name) || AllowedFunctions.Contains(name) || !reported.Add(name))
            {
                continue;
            }

            AddIssue(result.Errors, "unsupported_function",
                name.ToUpperInvariant() + "() is not supported. The only functions Dataverse evaluates are "
                + "COUNT, SUM, AVG, MIN, MAX, DATEADD and GETUTCDATE.");
        }

        var groupBy = ClauseText(probe, "GROUP BY", "ORDER BY", "OFFSET");

        if (groupBy != null && groupBy.IndexOf('(') >= 0)
        {
            AddIssue(result.Errors, "group_by_expression",
                "GROUP BY can only list columns. Grouping by a function, including date parts such as MONTH(createdon), is not supported.");
        }

        var orderBy = ClauseText(probe, "ORDER BY", "OFFSET");

        if (orderBy != null && orderBy.IndexOf('(') >= 0)
        {
            AddIssue(result.Errors, "order_by_expression",
                "ORDER BY can only reference columns, not expressions or aggregates.");
        }

        if (Regex.IsMatch(probe, @"\bTOP\s+(?!\d)", RegexOptions.IgnoreCase))
        {
            AddIssue(result.Errors, "top_requires_literal", "TOP only accepts an integer literal.");
        }

        var where = ClauseText(probe, "WHERE", "GROUP BY", "ORDER BY", "OFFSET");

        if (where != null)
        {
            if (Regex.IsMatch(where, @"(?<![A-Za-z0-9_.])\d+(\.\d+)?\s*(<=|>=|<>|!=|=|<|>)\s*\d+(\.\d+)?(?![A-Za-z0-9_.])"))
            {
                AddIssue(result.Errors, "literal_comparison",
                    "WHERE cannot compare two literals, such as WHERE 1=1. Every comparison needs a column on one side.");
            }

            if (Regex.IsMatch(where, @"(?<![A-Za-z0-9_.])([A-Za-z_][A-Za-z0-9_]*\.)?[A-Za-z_][A-Za-z0-9_]*\s*(<=|>=|<>|!=|=|<|>)\s*[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*"))
            {
                AddIssue(result.Errors, "column_comparison",
                    "WHERE cannot compare one column to another. Compare a column to a constant, and put column-to-column "
                    + "conditions in an ON clause.");
            }
        }

        // The reference lists TOP and OFFSET/FETCH under supported syntax, while the paging section
        // states they are not honored, so they are surfaced as warnings rather than blocked.
        if (Regex.IsMatch(probe, @"\bTOP\b", RegexOptions.IgnoreCase))
        {
            AddIssue(result.Warnings, "top_paging",
                "TOP may be ignored. Limit the result with the page size on this operation, which sets Prefer: odata.maxpagesize.");
        }

        if (Regex.IsMatch(probe, @"\bOFFSET\b", RegexOptions.IgnoreCase))
        {
            AddIssue(result.Warnings, "offset_paging",
                "OFFSET ... FETCH may be ignored. Page with the returned nextLink, or filter on the last id you saw.");
        }

        var from = Regex.Match(probe, @"\bFROM\s+\[?([A-Za-z_][A-Za-z0-9_]*)\]?", RegexOptions.IgnoreCase);

        if (from.Success)
        {
            result.BaseTable = from.Groups[1].Value;
        }

        return result;
    }

    private static void ThrowIfInvalid(Validation validation)
    {
        if (validation.IsValid)
        {
            return;
        }

        var first = validation.Errors[0] as JObject;
        var exception = new DataverseSqlException("invalid_sql",
            "The statement is outside the SQL subset Dataverse supports: "
            + (first == null ? string.Empty : first.Value<string>("message")));

        exception.Issues = validation.Errors;
        exception.ProvidedValue = validation.Statement;
        exception.Hint = "Dataverse supports SELECT, SELECT DISTINCT, INNER and LEFT JOIN, WHERE, GROUP BY with "
            + "COUNT/SUM/AVG/MIN/MAX, and ORDER BY over plain columns. Fix every issue listed and try again.";

        throw exception;
    }

    // ------------------------------------------------------------ operations

    /// <summary>
    /// Builds the design-time schema for Execute SQL so Power Automate offers real dynamic content
    /// instead of an untyped array. Column types come from the metadata of whichever table each
    /// alias refers to, so a joined column is typed from its own table.
    /// </summary>
    private async Task<JObject> OpRowSchemaAsync(JObject args)
    {
        var validation = Validate(Str(args, "sql"));
        ThrowIfInvalid(validation);

        var table = await ResolveBaseTableAsync(validation.BaseTable).ConfigureAwait(false);
        var aliases = BuildAliasMap(validation.Probe);
        var rowProperties = new JObject();
        var columns = new JArray();

        int start;
        int length;

        if (ClauseRange(validation.Probe, "SELECT", new[] { "FROM" }, out start, out length))
        {
            foreach (var item in SplitTopLevel(validation.Probe, validation.Statement, start, length))
            {
                var expression = item;
                var name = (string)null;

                var alias = Regex.Match(expression, @"\s+AS\s+\[?([A-Za-z_][A-Za-z0-9_]*)\]?\s*$", RegexOptions.IgnoreCase);

                if (alias.Success)
                {
                    name = alias.Groups[1].Value;
                    expression = expression.Substring(0, alias.Index).Trim();
                }

                var source = expression;
                JObject type = null;

                var aggregate = Regex.Match(expression,
                    @"^(COUNT|SUM|AVG|MIN|MAX)\s*\((.*)\)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);

                if (aggregate.Success)
                {
                    var function = aggregate.Groups[1].Value.ToUpperInvariant();
                    var inner = aggregate.Groups[2].Value.Trim();

                    // Dataverse names an aggregate column after its alias, so one without an alias
                    // cannot be described and is left out rather than guessed at.
                    if (name == null)
                    {
                        continue;
                    }

                    if (function == "COUNT")
                    {
                        type = new JObject { ["type"] = "integer" };
                    }
                    else if (function == "MIN" || function == "MAX")
                    {
                        type = await TypeOfColumnAsync(inner, aliases, table).ConfigureAwait(false);
                    }
                    else
                    {
                        type = new JObject { ["type"] = "number" };
                    }
                }
                else
                {
                    var column = Regex.Match(expression,
                        @"^(?:\[?([A-Za-z_][A-Za-z0-9_]*)\]?\s*\.\s*)?\[?([A-Za-z_][A-Za-z0-9_]*)\]?$");

                    if (!column.Success)
                    {
                        continue;
                    }

                    if (name == null)
                    {
                        name = column.Groups[2].Value;
                    }

                    type = await TypeOfColumnAsync(expression, aliases, table).ConfigureAwait(false);
                }

                if (name == null || type == null || rowProperties[name] != null)
                {
                    continue;
                }

                rowProperties[name] = type;
                columns.Add(new JObject
                {
                    ["name"] = name,
                    ["source"] = source,
                    ["type"] = type.Value<string>("type")
                });
            }
        }

        // Dataverse returns the base table primary key and the row etag whether or not they were
        // selected, and on some tables more besides -- systemuser and team also return ownerid,
        // while queue and role do not. The rule is not derivable from metadata, so the statement is
        // sampled for one row and its real keys are used instead of being predicted.
        var sampled = await SampleRowAsync(table, validation.Statement).ConfigureAwait(false);

        if (sampled != null)
        {
            foreach (var property in sampled.Properties())
            {
                if (rowProperties[property.Name] != null)
                {
                    continue;
                }

                rowProperties[property.Name] = await TypeOfColumnAsync(property.Name, aliases, table).ConfigureAwait(false);
                columns.Add(new JObject
                {
                    ["name"] = property.Name,
                    ["source"] = "returned by Dataverse",
                    ["type"] = rowProperties[property.Name].Value<string>("type")
                });
            }
        }
        else
        {
            // Without a sample the primary key and etag are the safe assumption: they came back on
            // every table tested. An aggregate query returns neither -- SELECT COUNT(*) AS row_count
            // yields only row_count -- so they are never added once a real row has been seen.
            if (!string.IsNullOrEmpty(table.PrimaryId) && rowProperties[table.PrimaryId] == null)
            {
                rowProperties[table.PrimaryId] = new JObject { ["type"] = "string" };
                columns.Add(new JObject
                {
                    ["name"] = table.PrimaryId,
                    ["source"] = "assumed",
                    ["type"] = "string"
                });
            }

            if (rowProperties["@odata.etag"] == null)
            {
                rowProperties["@odata.etag"] = new JObject { ["type"] = "string" };
            }
        }

        return new JObject
        {
            ["table"] = table.LogicalName,
            ["entitySet"] = table.EntitySetName,
            ["sampled"] = sampled != null,
            ["columns"] = columns,
            ["schema"] = BuildResponseSchema(rowProperties)
        };
    }

    /// <summary>
    /// Runs the statement for a single row so the schema reflects what Dataverse actually returns.
    /// Returns null when the table is empty or the query cannot be sampled, in which case the
    /// schema falls back to what the SELECT list alone implies.
    /// </summary>
    private async Task<JObject> SampleRowAsync(TableSummary table, string statement)
    {
        try
        {
            var url = table.EntitySetName + "?sql=" + Uri.EscapeDataString(statement);
            var response = await GetJsonAsync(url, "odata.maxpagesize=1").ConfigureAwait(false);
            var rows = response["value"] as JArray;

            return rows != null && rows.Count > 0 ? rows[0] as JObject : null;
        }
        catch (DataverseSqlException)
        {
            // Sampling is an optimization. A throttled, empty or rejected sample must not stop the
            // designer from offering the columns that can be inferred.
            return null;
        }
    }

    private async Task<JObject> TypeOfColumnAsync(
        string expression, Dictionary<string, string> aliases, TableSummary baseTable)
    {
        var column = Regex.Match(expression,
            @"^(?:\[?([A-Za-z_][A-Za-z0-9_]*)\]?\s*\.\s*)?\[?([A-Za-z_][A-Za-z0-9_]*)\]?$");

        if (!column.Success)
        {
            return new JObject { ["type"] = "string" };
        }

        var qualifier = column.Groups[1].Success ? column.Groups[1].Value : null;
        var name = column.Groups[2].Value;
        var owner = baseTable.LogicalName;

        if (!string.IsNullOrEmpty(qualifier) && !aliases.TryGetValue(qualifier, out owner))
        {
            owner = baseTable.LogicalName;
        }

        try
        {
            var types = await GetColumnTypesAsync(owner).ConfigureAwait(false);
            string attributeType;

            return types.TryGetValue(name, out attributeType)
                ? JsonType(attributeType)
                : new JObject { ["type"] = "string" };
        }
        catch (DataverseSqlException)
        {
            // A table the caller cannot read should not break design-time schema resolution.
            return new JObject { ["type"] = "string" };
        }
    }

    private static JObject BuildResponseSchema(JObject rowProperties)
    {
        var issue = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["rule"] = new JObject { ["type"] = "string" },
                ["message"] = new JObject { ["type"] = "string" }
            }
        };

        return new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["sql"] = new JObject { ["type"] = "string" },
                ["table"] = new JObject { ["type"] = "string" },
                ["entitySet"] = new JObject { ["type"] = "string" },
                ["rowCount"] = new JObject { ["type"] = "integer" },
                ["rows"] = new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = rowProperties
                    }
                },
                ["hasMore"] = new JObject { ["type"] = "boolean" },
                ["nextLink"] = new JObject { ["type"] = "string" },
                ["pagesFetched"] = new JObject { ["type"] = "integer" },
                ["stoppedBecause"] = new JObject { ["type"] = "string" },
                ["warnings"] = new JObject { ["type"] = "array", ["items"] = issue }
            }
        };
    }

    private async Task<JObject> OpRunSqlAsync(JObject args)
    {
        var pageSize = Int(args, "maxPageSize", DefaultPageSize, 1, MaxPageSize);
        var maxRows = Int(args, "maxRows", 0, 0, MaxRowBudget);
        var includeFormattedValues = Bool(args, "includeFormattedValues", false);

        var validation = Validate(Str(args, "sql"));
        ThrowIfInvalid(validation);

        var table = await ResolveBaseTableAsync(validation.BaseTable).ConfigureAwait(false);
        var url = table.EntitySetName + "?sql=" + Uri.EscapeDataString(validation.Statement);
        var prefer = "odata.maxpagesize=" + pageSize;

        if (includeFormattedValues)
        {
            prefer += ",odata.include-annotations=\"*\"";
        }

        var response = await GetJsonAsync(url, prefer).ConfigureAwait(false);
        var rows = response["value"] as JArray ?? new JArray();
        var nextLink = response.Value<string>("@odata.nextLink") ?? string.Empty;
        var pages = 1;
        var stopped = nextLink.Length == 0 ? "complete" : "page";

        if (maxRows > 0)
        {
            while (nextLink.Length > 0)
            {
                if (rows.Count >= maxRows)
                {
                    stopped = "maxRows";
                    break;
                }

                // One more page has to fit in what is left of the script budget, or the run is
                // killed and every row already collected is lost.
                if (Remaining < TimeSpan.FromSeconds(15))
                {
                    stopped = "timeBudget";
                    break;
                }

                var page = await SendDataverseAsync(RequireSameEnvironment(nextLink), prefer).ConfigureAwait(false);
                pages++;

                foreach (var row in page["value"] as JArray ?? new JArray())
                {
                    rows.Add(row);
                }

                nextLink = page.Value<string>("@odata.nextLink") ?? string.Empty;
                stopped = nextLink.Length == 0 ? "complete" : "page";
            }
        }

        await LogToAppInsightsAsync("ExecuteSql", new Dictionary<string, string>
        {
            ["table"] = table.LogicalName,
            ["entitySet"] = table.EntitySetName,
            ["pages"] = pages.ToString(),
            ["stoppedBecause"] = stopped
        }).ConfigureAwait(false);

        var payload = BuildRowPayload(validation.Statement, table.LogicalName, table.EntitySetName,
            rows, nextLink, validation.Warnings);

        payload["pagesFetched"] = pages;
        payload["stoppedBecause"] = stopped;

        return payload;
    }

    private async Task<JObject> OpNextPageAsync(JObject args)
    {
        var nextLink = Str(args, "nextLink");

        if (string.IsNullOrWhiteSpace(nextLink))
        {
            var missing = new DataverseSqlException("next_link_required", "A nextLink value is required.");
            missing.Hint = "Pass the nextLink returned by the previous page. Never build one by hand.";
            throw missing;
        }

        var response = await SendDataverseAsync(RequireSameEnvironment(nextLink), null).ConfigureAwait(false);

        return BuildRowPayload(string.Empty, string.Empty, string.Empty,
            response["value"] as JArray,
            response.Value<string>("@odata.nextLink"),
            new JArray());
    }

    /// <summary>A nextLink is handed back to us by the caller, so it is re-checked before it is followed.</summary>
    private string RequireSameEnvironment(string nextLink)
    {
        Uri parsed;

        if (!Uri.TryCreate(nextLink.Trim(), UriKind.Absolute, out parsed)
            || !string.Equals(parsed.Scheme, "https", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parsed.Authority, this.Context.Request.RequestUri.Authority, StringComparison.OrdinalIgnoreCase)
            || !parsed.AbsolutePath.StartsWith("/api/data/", StringComparison.OrdinalIgnoreCase))
        {
            var invalid = new DataverseSqlException("invalid_next_link",
                "The nextLink does not point at the Web API of this Dataverse environment.");
            invalid.ProvidedValue = nextLink;
            invalid.Hint = "Use the nextLink exactly as it was returned by the previous page.";
            throw invalid;
        }

        return parsed.AbsoluteUri;
    }

    private async Task<JObject> OpCountRowsAsync(JObject args)
    {
        var tableName = SanitizeIdentifier(Str(args, "table"));

        if (tableName == null)
        {
            var invalid = new DataverseSqlException("table_required", "A table logical name is required.");
            invalid.ProvidedValue = Str(args, "table");
            invalid.Hint = "Pass a logical name such as account. Call list_tables to find one.";
            throw invalid;
        }

        var where = Str(args, "where");
        var sql = "SELECT COUNT(*) AS row_count FROM " + tableName;

        if (!string.IsNullOrWhiteSpace(where))
        {
            var clause = where.Trim();

            if (clause.StartsWith("WHERE", StringComparison.OrdinalIgnoreCase))
            {
                clause = clause.Substring(5).Trim();
            }

            sql += " WHERE " + clause;
        }

        // The assembled statement goes through the same validator, so a WHERE clause cannot
        // smuggle in a second statement or an unsupported construct.
        var validation = Validate(sql);
        ThrowIfInvalid(validation);

        var table = await ResolveBaseTableAsync(validation.BaseTable).ConfigureAwait(false);
        var url = table.EntitySetName + "?sql=" + Uri.EscapeDataString(validation.Statement);
        var response = await GetJsonAsync(url, "odata.maxpagesize=1").ConfigureAwait(false);

        long count = 0;
        var rows = response["value"] as JArray;

        if (rows != null && rows.Count > 0)
        {
            var first = rows[0] as JObject;

            if (first != null && first["row_count"] != null)
            {
                long.TryParse(first["row_count"].ToString(), out count);
            }
        }

        return new JObject
        {
            ["sql"] = validation.Statement,
            ["table"] = table.LogicalName,
            ["entitySet"] = table.EntitySetName,
            ["count"] = count,
            ["warnings"] = validation.Warnings
        };
    }

    private async Task<JObject> OpValidateSqlAsync(JObject args)
    {
        var validation = Validate(Str(args, "sql"));
        var payload = new JObject
        {
            ["valid"] = validation.IsValid,
            ["statement"] = validation.Statement ?? string.Empty,
            ["table"] = string.Empty,
            ["entitySet"] = string.Empty,
            ["errors"] = validation.Errors,
            ["warnings"] = validation.Warnings
        };

        if (!validation.IsValid || string.IsNullOrEmpty(validation.BaseTable))
        {
            return payload;
        }

        try
        {
            var table = await ResolveBaseTableAsync(validation.BaseTable).ConfigureAwait(false);
            payload["table"] = table.LogicalName;
            payload["entitySet"] = table.EntitySetName;
        }
        catch (DataverseSqlException sx)
        {
            // A table that cannot be resolved is part of the validation report, not a thrown error:
            // the caller asked whether the statement would run, and this is the answer.
            payload["valid"] = false;
            AddIssue(validation.Errors, sx.Code, sx.Message);
        }

        return payload;
    }

    private async Task<JObject> OpListTablesAsync(JObject args)
    {
        var search = Str(args, "search");
        var limit = Int(args, "limit", DefaultTableLimit, 1, MaxTableLimit);
        var customOnly = Bool(args, "customOnly", false);

        var tables = await GetAllTablesAsync().ConfigureAwait(false);
        var matches = tables.Values
            .Where(t => !customOnly || t.IsCustom)
            .Where(t => string.IsNullOrWhiteSpace(search)
                || t.LogicalName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                || (t.DisplayName ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var page = matches.Take(limit).Select(t => new JObject
        {
            ["logicalName"] = t.LogicalName,
            ["entitySetName"] = t.EntitySetName,
            ["displayName"] = t.DisplayName,
            ["primaryIdColumn"] = t.PrimaryId ?? string.Empty,
            ["primaryNameColumn"] = t.PrimaryName ?? string.Empty,
            ["isCustom"] = t.IsCustom
        });

        return new JObject
        {
            ["value"] = new JArray(page),
            ["totalCount"] = matches.Count,
            ["truncated"] = matches.Count > limit
        };
    }

    private async Task<JObject> OpDescribeTableAsync(JObject args)
    {
        var table = await ResolveBaseTableAsync(Str(args, "table")).ConfigureAwait(false);
        var columns = await GetColumnsAsync(table, Str(args, "search")).ConfigureAwait(false);
        var truncated = columns.Count > MaxColumnsReturned;

        return new JObject
        {
            ["table"] = table.LogicalName,
            ["entitySetName"] = table.EntitySetName,
            ["displayName"] = table.DisplayName,
            ["primaryIdColumn"] = table.PrimaryId ?? string.Empty,
            ["primaryNameColumn"] = table.PrimaryName ?? string.Empty,
            ["value"] = truncated ? new JArray(columns.Take(MaxColumnsReturned)) : columns,
            ["totalCount"] = columns.Count,
            ["truncated"] = truncated,
            ["sample"] = "SELECT " + (table.PrimaryName ?? table.PrimaryId ?? "name") + " FROM " + table.LogicalName
        };
    }

    private static JObject BuildRowPayload(string sql, string table, string entitySet,
        JArray rows, string nextLink, JArray warnings)
    {
        rows = rows ?? new JArray();
        nextLink = nextLink ?? string.Empty;

        return new JObject
        {
            ["sql"] = sql ?? string.Empty,
            ["table"] = table ?? string.Empty,
            ["entitySet"] = entitySet ?? string.Empty,
            ["rowCount"] = rows.Count,
            ["rows"] = rows,
            ["hasMore"] = nextLink.Length > 0,
            ["nextLink"] = nextLink,
            ["pagesFetched"] = 1,
            ["stoppedBecause"] = nextLink.Length > 0 ? "page" : "complete",
            ["warnings"] = warnings ?? new JArray()
        };
    }

    // --------------------------------------------------------------- helpers

    private static string Str(JObject args, string key)
    {
        if (args == null)
        {
            return null;
        }

        var token = args[key];

        if (token == null || token.Type == JTokenType.Null)
        {
            return null;
        }

        return token.ToString().Trim();
    }

    private static int Int(JObject args, string key, int fallback, int minimum, int maximum)
    {
        var raw = Str(args, key);
        int parsed;

        if (string.IsNullOrEmpty(raw) || !int.TryParse(raw, out parsed))
        {
            return fallback;
        }

        return Math.Min(maximum, Math.Max(minimum, parsed));
    }

    private static bool Bool(JObject args, string key, bool fallback)
    {
        var raw = Str(args, key);
        bool parsed;

        if (string.IsNullOrEmpty(raw) || !bool.TryParse(raw, out parsed))
        {
            return fallback;
        }

        return parsed;
    }

    // ------------------------------------------------------------------- MCP

    private async Task<HttpResponseMessage> HandleMcpAsync()
    {
        var body = await ReadArgsAsync().ConfigureAwait(false);
        var method = body.Value<string>("method") ?? string.Empty;
        var id = body["id"] ?? JValue.CreateNull();

        // Notifications still get a valid JSON-RPC envelope; Copilot Studio requires it.
        if (method.StartsWith("notifications/", StringComparison.OrdinalIgnoreCase))
        {
            return JsonRpcResult(id, new JObject());
        }

        switch (method)
        {
            case "initialize":
                var requested = body["params"] != null ? body["params"].Value<string>("protocolVersion") : null;

                return JsonRpcResult(id, new JObject
                {
                    ["protocolVersion"] = string.IsNullOrWhiteSpace(requested) ? ProtocolVersion : requested,
                    ["capabilities"] = new JObject
                    {
                        ["tools"] = new JObject { ["listChanged"] = false }
                    },
                    ["serverInfo"] = new JObject
                    {
                        ["name"] = "dataverse-sql",
                        ["version"] = "1.0.0"
                    }
                });

            case "ping":
                return JsonRpcResult(id, new JObject());

            case "tools/list":
                return JsonRpcResult(id, new JObject { ["tools"] = BuildToolList() });

            case "resources/list":
                return JsonRpcResult(id, new JObject { ["resources"] = new JArray() });

            case "prompts/list":
                return JsonRpcResult(id, new JObject { ["prompts"] = new JArray() });

            case "tools/call":
                return await HandleToolCallAsync(id, body["params"] as JObject).ConfigureAwait(false);

            default:
                return JsonRpcError(id, -32601, "Method '" + method + "' is not supported.");
        }
    }

    private async Task<HttpResponseMessage> HandleToolCallAsync(JToken id, JObject parameters)
    {
        var toolName = parameters != null ? parameters.Value<string>("name") : null;
        var arguments = (parameters != null ? parameters["arguments"] as JObject : null) ?? new JObject();

        await LogToAppInsightsAsync("McpToolCall", new Dictionary<string, string>
        {
            ["tool"] = toolName ?? string.Empty
        }).ConfigureAwait(false);

        try
        {
            JObject payload;

            switch (toolName)
            {
                case "list_tables":
                    payload = await OpListTablesAsync(arguments).ConfigureAwait(false);
                    break;
                case "describe_table":
                    payload = await OpDescribeTableAsync(arguments).ConfigureAwait(false);
                    break;
                case "validate_sql":
                    payload = await OpValidateSqlAsync(arguments).ConfigureAwait(false);
                    break;
                case "run_sql":
                    payload = await OpRunSqlAsync(arguments).ConfigureAwait(false);
                    break;
                case "next_page":
                    payload = await OpNextPageAsync(arguments).ConfigureAwait(false);
                    break;
                case "count_rows":
                    payload = await OpCountRowsAsync(arguments).ConfigureAwait(false);
                    break;
                default:
                    var unknown = new DataverseSqlException("unknown_tool",
                        "'" + (toolName ?? "(none)") + "' is not a tool on this server.");
                    unknown.ValidValues = new JArray(BuildToolList()
                        .OfType<JObject>()
                        .Select(t => new JObject { ["value"] = t["name"] }));
                    unknown.Hint = "Call tools/list to see the available tools.";
                    return JsonRpcResult(id, ToolResult(unknown.ToJson(), true));
            }

            return JsonRpcResult(id, ToolResult(payload, false));
        }
        catch (DataverseSqlException sx)
        {
            // Returned as a tool result rather than a protocol error so the model can read the
            // issues and validValues and retry with a real name instead of inventing one.
            return JsonRpcResult(id, ToolResult(sx.ToJson(), true));
        }
        catch (Exception ex)
        {
            await LogToAppInsightsAsync("McpToolError", new Dictionary<string, string>
            {
                ["tool"] = toolName ?? string.Empty,
                ["message"] = ex.Message,
                ["stack"] = ex.StackTrace ?? string.Empty
            }).ConfigureAwait(false);

            return JsonRpcResult(id, ToolResult(new JObject
            {
                ["error"] = "unhandled_exception",
                ["message"] = ex.Message
            }, true));
        }
    }

    private static JObject ToolResult(JObject payload, bool isError)
    {
        return new JObject
        {
            ["content"] = new JArray(new JObject
            {
                ["type"] = "text",
                ["text"] = payload.ToString(Newtonsoft.Json.Formatting.None)
            }),
            ["structuredContent"] = payload,
            ["isError"] = isError
        };
    }

    private HttpResponseMessage JsonRpcResult(JToken id, JObject result)
    {
        return JsonResponse(HttpStatusCode.OK, new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id ?? JValue.CreateNull(),
            ["result"] = result
        });
    }

    private HttpResponseMessage JsonRpcError(JToken id, int code, string message)
    {
        return JsonResponse(HttpStatusCode.OK, new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id ?? JValue.CreateNull(),
            ["error"] = new JObject
            {
                ["code"] = code,
                ["message"] = message
            }
        });
    }

    private static JObject StringProperty(string description)
    {
        return new JObject { ["type"] = "string", ["description"] = description };
    }

    private static JObject IntegerProperty(string description, int defaultValue)
    {
        return new JObject { ["type"] = "integer", ["description"] = description, ["default"] = defaultValue };
    }

    private static JObject BooleanProperty(string description, bool defaultValue)
    {
        return new JObject { ["type"] = "boolean", ["description"] = description, ["default"] = defaultValue };
    }

    private static JArray BuildToolList()
    {
        const string subset = "Dataverse accepts a read-only subset of T-SQL: SELECT, SELECT DISTINCT, INNER JOIN, LEFT JOIN, "
            + "WHERE with = != > < >= <= LIKE IN NOT IN IS NULL IS NOT NULL BETWEEN AND OR, GROUP BY with COUNT SUM AVG MIN MAX, "
            + "and ORDER BY over plain columns. SELECT * is rejected, so name every column. Subqueries, CTEs, HAVING, UNION, "
            + "RIGHT JOIN, FULL JOIN, CROSS JOIN, CASE, COALESCE, window functions and string, date or math functions are all "
            + "rejected; the only functions available are DATEADD and GETUTCDATE, and only inside WHERE or ON. "
            + "Never guess a table or column name: call list_tables and describe_table first and use the names they return.";

        var tools = new JArray();

        tools.Add(new JObject
        {
            ["name"] = "list_tables",
            ["description"] = "List the Dataverse tables in this environment with their logical name, entity set name and display "
                + "name. Call this first to find the exact logical name to put in a FROM clause. Filter with search when you already "
                + "know roughly what the table is called.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["search"] = StringProperty("Match against the logical name and display name, for example account or session."),
                    ["customOnly"] = BooleanProperty("Return only custom tables.", false),
                    ["limit"] = IntegerProperty("Maximum tables to return (1-5000).", DefaultTableLimit)
                }
            }
        });

        tools.Add(new JObject
        {
            ["name"] = "describe_table",
            ["description"] = "List the columns of one table with their logical name, display name and type, plus the primary id and "
                + "primary name columns. Call this before writing a SELECT so that every column you name really exists. Use search on "
                + "wide tables to narrow the list.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray("table"),
                ["properties"] = new JObject
                {
                    ["table"] = StringProperty("Table logical name from list_tables, for example account."),
                    ["search"] = StringProperty("Match against the column logical name and display name.")
                }
            }
        });

        tools.Add(new JObject
        {
            ["name"] = "validate_sql",
            ["description"] = "Check a SELECT statement against the supported subset and resolve its base table, without running it. "
                + "Use this when you are unsure whether a construct is allowed. " + subset,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray("sql"),
                ["properties"] = new JObject
                {
                    ["sql"] = StringProperty("The SELECT statement to check.")
                }
            }
        });

        tools.Add(new JObject
        {
            ["name"] = "run_sql",
            ["description"] = "Run a read-only SELECT against Dataverse and return the matching rows. The entity set is resolved from "
                + "the FROM clause, so write the query against the table logical name. When more rows are available, hasMore is true "
                + "and nextLink can be passed to next_page. " + subset,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray("sql"),
                ["properties"] = new JObject
                {
                    ["sql"] = StringProperty("The SELECT statement to run, for example SELECT name, telephone1 FROM account WHERE statecode = 0."),
                    ["maxPageSize"] = IntegerProperty("Rows per page (1-5000). Sent as Prefer: odata.maxpagesize.", DefaultPageSize),
                    ["maxRows"] = IntegerProperty(
                        "Collect up to this many rows by following next links automatically. 0 returns a single page. "
                        + "Paging stops early if the script runs out of time, and stoppedBecause says which limit was hit.", 0),
                    ["includeFormattedValues"] = BooleanProperty(
                        "Also return display text for choice, lookup and date columns as FormattedValue annotations.", false)
                }
            }
        });

        tools.Add(new JObject
        {
            ["name"] = "next_page",
            ["description"] = "Fetch the next page of a previous run_sql result. Pass the nextLink exactly as it was returned; never "
                + "construct one.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray("nextLink"),
                ["properties"] = new JObject
                {
                    ["nextLink"] = StringProperty("The nextLink value from the previous page.")
                }
            }
        });

        tools.Add(new JObject
        {
            ["name"] = "count_rows",
            ["description"] = "Count the rows in a table, optionally filtered. Cheaper than running a SELECT and counting the rows "
                + "yourself. An aggregate may evaluate at most " + AggregateRecordLimit + " records, so add a filter on large tables.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray("table"),
                ["properties"] = new JObject
                {
                    ["table"] = StringProperty("Table logical name from list_tables, for example contact."),
                    ["where"] = StringProperty("Optional WHERE condition without the WHERE keyword, for example statecode = 0.")
                }
            }
        });

        return tools;
    }

    // ------------------------------------------------------------- telemetry

    private async Task LogToAppInsightsAsync(string eventName, IDictionary<string, string> properties)
    {
        if (!APP_INSIGHTS_ENABLED
            || string.IsNullOrEmpty(APP_INSIGHTS_KEY)
            || APP_INSIGHTS_KEY.Contains("INSERT_YOUR"))
        {
            return;
        }

        try
        {
            var telemetry = new JObject
            {
                ["name"] = "Microsoft.ApplicationInsights.Event",
                ["time"] = DateTime.UtcNow.ToString("O"),
                ["iKey"] = APP_INSIGHTS_KEY,
                ["data"] = new JObject
                {
                    ["baseType"] = "EventData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["name"] = eventName,
                        ["properties"] = JObject.FromObject(properties ?? new Dictionary<string, string>())
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT)
            {
                Content = CreateJsonContent(telemetry.ToString(Newtonsoft.Json.Formatting.None))
            };

            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Telemetry must never fail the operation.
        }
    }
}
