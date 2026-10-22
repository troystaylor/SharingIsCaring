using System.Text.RegularExpressions;
using ModelContextProtocol;

namespace SalesforceMcp.Salesforce;

/// <summary>
/// Validates every value that is placed into a Salesforce URL path. Tool arguments are model output, so
/// each one is checked against the narrowest shape Salesforce allows before it is used.
/// </summary>
public static partial class SalesforceNames
{
    public const int MaxFieldCount = 200;

    public static string SObject(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !NamePattern().IsMatch(value.Trim()))
        {
            throw new McpException(
                $"'{Shorten(value)}' is not a valid sObject API name. Use letters, digits, and underscores, such as Account or Invoice__c. Call list_objects to find the right name.");
        }

        return value.Trim();
    }

    public static string Field(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !NamePattern().IsMatch(value.Trim()))
        {
            throw new McpException(
                $"'{Shorten(value)}' is not a valid field API name. Use letters, digits, and underscores, such as Name or Region__c. Call describe_object to see the fields.");
        }

        return value.Trim();
    }

    public static string RecordId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !IdPattern().IsMatch(value.Trim()))
        {
            throw new McpException(
                $"'{Shorten(value)}' is not a valid Salesforce record ID. IDs are 15 or 18 letters and digits.");
        }

        return value.Trim();
    }

    /// <summary>
    /// A continuation URL returned by a previous query. Only that exact shape is accepted, so the argument
    /// cannot be used to make the server request any other Salesforce path.
    /// </summary>
    public static string NextRecordsUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !NextRecordsPattern().IsMatch(value.Trim()))
        {
            throw new McpException(
                "nextRecordsUrl must be the value returned by a previous query, such as /services/data/v66.0/query/01gxx0000000001AAA-2000.");
        }

        return value.Trim();
    }

    public static string Soql(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !SoqlStart().IsMatch(value))
        {
            throw new McpException("query must be a SOQL statement that starts with SELECT.");
        }

        return value.Trim();
    }

    public static string Sosl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !SoslStart().IsMatch(value))
        {
            throw new McpException("search must be a SOSL statement that starts with FIND, such as FIND {Acme} IN ALL FIELDS RETURNING Account(Id, Name).");
        }

        return value.Trim();
    }

    private static string Shorten(string? value) =>
        value is null ? string.Empty : value.Length <= 40 ? value : value[..40] + "...";

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]{0,79}$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9]{15}([A-Za-z0-9]{3})?$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^/services/data/v\d{2,3}\.\d/query/[A-Za-z0-9]{15,18}-\d+$")]
    private static partial Regex NextRecordsPattern();

    [GeneratedRegex(@"^\s*SELECT\b", RegexOptions.IgnoreCase)]
    private static partial Regex SoqlStart();

    [GeneratedRegex(@"^\s*FIND\b", RegexOptions.IgnoreCase)]
    private static partial Regex SoslStart();
}
