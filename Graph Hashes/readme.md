# Graph Hashes Connector

File hash computation and verification for Microsoft Graph files. Supports **QuickXorHash**, **SHA1**, and **CRC32** algorithms for integrity verification, change detection, and deduplication.

## Overview

This connector enables:
- **Hash computation** for files in Power Automate flows
- **File integrity verification** against Microsoft Graph stored hashes
- **Change detection** by comparing hash values
- **Deduplication** by identifying identical files
- **Natural language file verification** via Copilot Studio agents

## Authentication

**OAuth 2.0** with Microsoft Entra ID (Azure AD):
- **Scopes**: `Files.Read`, `Files.Read.All`
- **Identity Provider**: Azure Active Directory
- **Redirect URL**: Global consent redirect

## Use Cases

### Power Automate Scenarios

1. **Upload Verification**
   - Compute hash before upload → Upload file → Verify hash matches
   
2. **Change Detection**
   - Store QuickXorHash in variable → Compute hash on next run → Compare
   
3. **File Deduplication**
   - Compute hashes for multiple files → Compare to identify duplicates
   
4. **Integrity Monitoring**
   - Schedule flow to verify critical files haven't been corrupted/tampered

### Copilot Studio Scenarios

**User**: *"Has the Q4 financial report changed since last week?"*  
**Agent**: Retrieves current file hash, compares to stored value

**User**: *"Verify the uploaded contract matches the original"*  
**Agent**: Computes hash, compares to expected value

**User**: *"Are these two invoices identical?"*  
**Agent**: Computes hashes for both files, compares

## Operations

### Direct API Operations (Power Automate)

| Operation | Description | Input | Output |
|-----------|-------------|-------|--------|
| **Compute QuickXorHash** | Calculate QuickXorHash (OneDrive/SharePoint standard) | File content (base64) | Hash (base64), file size |
| **Compute SHA1 Hash** | Calculate SHA1 hash | File content (base64) | Hash (hex), file size |
| **Compute CRC32 Hash** | Calculate CRC32 hash | File content (base64) | Hash (hex), file size |
| **Compare Hashes** | Compare two hash values | hash1, hash2, algorithm | Boolean match + details |
| **Verify File Integrity** | Compare computed vs Graph stored hash | File content, driveId, itemId | Verified boolean + hashes |
| **Get File Hashes** | Retrieve stored hashes from Graph | driveId, itemId | File metadata with hashes |
| **Download File Content** | Download file for hash computation | driveId, itemId | File binary content |

### MCP Tools (Copilot Studio)

| Tool | Description | Natural Language Examples |
|------|-------------|--------------------------|
| `compute_file_hash` | Compute hash for a file | "Calculate the QuickXorHash for this file" |
| `get_graph_file_hashes` | Retrieve stored hashes from Graph | "What are the hashes for the Q4 report?" |
| `verify_file_integrity` | Verify file against Graph hash | "Verify this file hasn't been corrupted" |
| `compare_hashes` | Compare two hash values | "Do these hashes match?" |

## Hash Algorithms

### QuickXorHash
- **Format**: Base64
- **Use**: OneDrive/SharePoint standard hash
- **Guaranteed**: Available for all OneDrive for Business and OneDrive for home files
- **Speed**: Fast, optimized for cloud storage

### SHA1 Hash
- **Format**: Hexadecimal (lowercase)
- **Use**: Standard cryptographic hash
- **Availability**: May not be available for all files in Graph API

### CRC32 Hash
- **Format**: Hexadecimal (lowercase, 8 characters)
- **Use**: Fast integrity check
- **Availability**: May not be available for all files in Graph API

## Power Automate Examples

### Example 1: Verify Upload Integrity

```
1. [OneDrive] When a file is created → Get file content
2. [Graph Hashes] Compute QuickXorHash → Input: File Content
3. [Delay] Wait 5 seconds (allow OneDrive to process)
4. [Graph Hashes] Get File Hashes → Input: Drive ID, Item ID
5. [Condition] If computed hash equals Graph hash → Success
```

### Example 2: Detect File Changes

```
1. [Schedule] Recurrence - Daily at 9 AM
2. [Graph Hashes] Get File Hashes → Input: Critical file ID
3. [Variables] Get previous hash from variable
4. [Graph Hashes] Compare Hashes → Input: Current vs Previous
5. [Condition] If no match → Send alert email
6. [Variables] Update variable with new hash
```

### Example 3: Deduplication

```
1. [SharePoint] Get files from folder
2. [Apply to each] File in folder
   2a. [Graph Hashes] Compute QuickXorHash → Input: File Content
   2b. [Append to array] Hash + File Name
3. [Apply to each] Compare all hashes
4. [Condition] If duplicate found → Move to archive
```

## Copilot Studio Integration

### Setup Instructions

1. **Add connector** to your Copilot Studio environment
2. **Create connection** with appropriate Microsoft Graph permissions
3. **Enable actions** in your agent's knowledge sources
4. **Test** with natural language queries about file verification

### Example Agent Instructions

```markdown
You are a file integrity specialist. When users ask about file verification:

1. Use `get_graph_file_hashes` to retrieve stored hashes
2. Use `compute_file_hash` to calculate hashes for comparison
3. Use `verify_file_integrity` to confirm files haven't changed
4. Use `compare_hashes` to check if two files are identical

Always explain what QuickXorHash is when first mentioned.
```

## Technical Details

### QuickXorHash Algorithm

The connector implements the official Microsoft QuickXorHash algorithm:
- **Width**: 160 bits (20 bytes)
- **Method**: Circular-shifting XOR with file length
- **Performance**: Optimized for large files
- **Reference**: [Microsoft QuickXorHash Documentation](https://learn.microsoft.com/onedrive/developer/code-snippets/quickxorhash)

### File Size Limits

- **Power Platform**: Maximum 50 MB file content in base64 (connector execution timeout)
- **Graph API**: Downloads up to 250 MB
- **Recommendation**: For files >10 MB, use direct Graph API download within connector

### Application Insights

Optional telemetry tracking:
- Hash computation events
- File integrity verification results
- MCP tool invocations
- Performance metrics

To enable: Update `APP_INSIGHTS_CONNECTION_STRING` in script.csx

## Error Handling

| Error | Cause | Solution |
|-------|-------|----------|
| `Invalid base64` | File content not properly encoded | Ensure file content is base64-encoded |
| `Graph API error: 404` | File not found | Verify driveId and itemId are correct |
| `Graph API error: 401` | Insufficient permissions | Grant `Files.Read.All` scope |
| `Hash not available` | Graph doesn't have hash for file | File may be processing; retry after delay |

## Best Practices

1. **Use QuickXorHash for OneDrive/SharePoint** - Guaranteed to be available
2. **Cache hashes** - Store in variables to avoid repeated computation
3. **Add delays after upload** - Allow time for Graph API to process files
4. **Handle missing hashes** - Some files may not have SHA1/CRC32
5. **Batch operations** - Use parallel branches for multiple files

## Limitations

- **Read-only**: Cannot write hashes to Graph API (hashes are computed by service)
- **No streaming**: Files must be loaded entirely into memory
- **Timeout constraints**: Very large files may hit Power Platform execution limits
- **Hash availability**: SHA1/CRC32 not guaranteed for all files

## References

- [Microsoft Graph Hashes Resource](https://learn.microsoft.com/graph/api/resources/hashes)
- [QuickXorHash Algorithm](https://learn.microsoft.com/onedrive/developer/code-snippets/quickxorhash)
- [File System Hash Comparison](https://learn.microsoft.com/graph/api/driveitem-get)

## Support

**Developer**: Troy Taylor  
**Contact**: troytaylor@microsoft.com  
**Version**: 1.0.0  
**Last Updated**: January 13, 2026
