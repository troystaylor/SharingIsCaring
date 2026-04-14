using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Identity;

namespace LLMbook.Api.Services;

/// <summary>
/// Creates or updates the Azure AI Search index schema for book pages.
/// Called during Function App startup to ensure the index exists.
/// </summary>
public static class SearchIndexSetup
{
    public static async Task EnsureIndexExistsAsync(Uri searchEndpoint, string indexName)
    {
        var credential = new DefaultAzureCredential();
        var adminClient = new SearchIndexClient(searchEndpoint, credential);

        var index = new SearchIndex(indexName)
        {
            Fields =
            {
                new SimpleField("pageId", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
                new SearchableField("title") { IsFilterable = true, IsSortable = true },
                new SearchableField("content"),
                new SimpleField("category", SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField("scope", SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField("userId", SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField("updatedBy", SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField("lastUpdated", SearchFieldDataType.String) { IsFilterable = true, IsSortable = true },
                new SearchableField("crossReferences"),
                new SimpleField("sourceCount", SearchFieldDataType.Int32) { IsFilterable = true },
                new SimpleField("linkCount", SearchFieldDataType.Int32) { IsFilterable = true }
            }
        };

        await adminClient.CreateOrUpdateIndexAsync(index);
    }
}
