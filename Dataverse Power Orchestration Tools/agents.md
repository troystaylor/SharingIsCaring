# AGENTS.MD - Dataverse Power Orchestration Tools

## About This Connector

You are a Dataverse Power Orchestration Tools server with specialized tools for interacting with Microsoft Dataverse environments. Your goal is to help users query, create, update, and manage Dataverse data efficiently.

Use `discover_functions` to find relevant tools for a task, then `invoke_tool` to execute them.

## Tool Selection Guide

Match user intent to category, then search for specific tools:

| Intent | Category | Example Tools |
|--------|----------|---------------|
| show/list/get/find/query | READ | list_rows, get_row, query_expand, fetchxml |
| create/add/new/insert | WRITE | create_row, upsert |
| update/change/modify/edit | WRITE | update_row, upsert |
| delete/remove | WRITE | delete_row |
| insert many/bulk create | BULK | create_multiple, batch |
| update many/bulk update | BULK | update_multiple, batch |
| link/connect/relate | RELATIONSHIPS | associate |
| unlink/disconnect | RELATIONSHIPS | disassociate |
| what tables/columns/schema | METADATA | get_entity_metadata, get_attribute_metadata |
| who owns/share/permissions | SECURITY | whoami, assign, share, retrieve_principal_access |
| activate/deactivate/status | RECORD_MGMT | set_state |
| combine duplicates | RECORD_MGMT | merge |
| upload/download file | ATTACHMENTS | upload_attachment, download_attachment |
| sync changes | CHANGE_TRACKING | track_changes |
| job status | ASYNC | get_async_operation, list_async_operations |

## Common Patterns

### Table Name Pluralization
- `account` → `accounts`, `contact` → `contacts`, `opportunity` → `opportunities`
- For FetchXML entity attribute: Use singular (`account`, `contact`)

### OData Filter Syntax
| Operation | Syntax | Example |
|-----------|--------|--------|
| Equals | `eq` | `name eq 'Contoso'` |
| Not Equals | `ne` | `statecode ne 1` |
| Greater Than | `gt` | `revenue gt 100000` |
| Contains | `contains()` | `contains(name,'Cont')` |
| And/Or | `and`/`or` | `statecode eq 0 and revenue gt 0` |

### Lookup Binding (Write Operations)
- Format: `"navprop@odata.bind": "/entitysetname(guid)"`

## Organization Context

<!-- Customize for your environment -->
| Business Term | Table | Entity Set |
|---------------|-------|------------|
| Customer | account | accounts |
| Person | contact | contacts |
| Deal | opportunity | opportunities |

---

## TOOLS

```json
[
  {
    "name": "dataverse_list_rows",
    "category": "READ",
    "description": "List Dataverse rows from a table with optional $select, $filter, $orderby, $top, and pagination",
    "keywords": ["query", "list", "search", "find", "get all", "records"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Plural name of the table, e.g., accounts, contacts"},
        "select": {"type": "string", "description": "Comma-separated columns to select"},
        "filter": {"type": "string", "description": "OData $filter expression"},
        "orderby": {"type": "string", "description": "OData $orderby expression (e.g., 'createdon desc')"},
        "top": {"type": "integer", "description": "Max rows per page (default 5, max 50)"},
        "includeFormatted": {"type": "boolean", "description": "Include formatted values for lookups/optionsets"},
        "nextLink": {"type": "string", "description": "Pagination URL from previous response"},
        "impersonateUserId": {"type": "string", "description": "User GUID to impersonate"}
      },
      "required": ["table"]
    }
  },
  {
    "name": "dataverse_get_row",
    "category": "READ",
    "description": "Get a single Dataverse row by table and GUID id with optional $select",
    "keywords": ["get", "retrieve", "fetch", "single", "by id", "record"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Plural name of the table"},
        "id": {"type": "string", "description": "Row GUID"},
        "select": {"type": "string", "description": "Comma-separated columns"},
        "includeFormatted": {"type": "boolean", "description": "Include formatted values"},
        "impersonateUserId": {"type": "string", "description": "User GUID to impersonate"}
      },
      "required": ["table", "id"]
    }
  },
  {
    "name": "dataverse_query_expand",
    "category": "READ",
    "description": "Query Dataverse rows with $expand to retrieve related records in a single request",
    "keywords": ["expand", "related", "join", "navigation", "lookup"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Plural name of the table"},
        "select": {"type": "string", "description": "Comma-separated columns to select"},
        "expand": {"type": "string", "description": "Navigation properties to expand (e.g., 'primarycontactid($select=fullname)')"},
        "filter": {"type": "string", "description": "OData $filter expression"},
        "orderby": {"type": "string", "description": "OData $orderby expression"},
        "top": {"type": "integer", "description": "Max rows per page (default 5, max 50)"},
        "includeFormatted": {"type": "boolean", "description": "Include formatted values"},
        "nextLink": {"type": "string", "description": "Pagination URL"}
      },
      "required": ["table", "expand"]
    }
  },
  {
    "name": "dataverse_fetchxml",
    "category": "READ",
    "description": "Execute FetchXML against a Dataverse table for complex queries and aggregations",
    "keywords": ["fetchxml", "aggregate", "sum", "count", "avg", "complex", "linked"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Plural table name"},
        "fetchXml": {"type": "string", "description": "FetchXML query"}
      },
      "required": ["table", "fetchXml"]
    }
  },
  {
    "name": "dataverse_count_rows",
    "category": "READ",
    "description": "Get count of rows matching filter criteria without retrieving data",
    "keywords": ["count", "total", "how many", "number of"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Plural name of the table"},
        "filter": {"type": "string", "description": "OData $filter expression (optional)"}
      },
      "required": ["table"]
    }
  },
  {
    "name": "dataverse_aggregate",
    "category": "READ",
    "description": "Retrieve aggregate values (sum, avg, min, max, count) using FetchXML",
    "keywords": ["sum", "average", "min", "max", "aggregate", "total", "calculate"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Logical name of the table"},
        "aggregateAttribute": {"type": "string", "description": "Attribute to aggregate"},
        "aggregateFunction": {"type": "string", "enum": ["sum", "avg", "min", "max", "count", "countcolumn"], "description": "Aggregation function"},
        "groupBy": {"type": "string", "description": "Optional attribute to group by"},
        "filter": {"type": "string", "description": "Optional filter attribute name"},
        "filterOperator": {"type": "string", "description": "Filter operator (eq, ne, gt, lt)"},
        "filterValue": {"type": "string", "description": "Filter value"}
      },
      "required": ["table", "aggregateAttribute", "aggregateFunction"]
    }
  },
  {
    "name": "dataverse_execute_saved_query",
    "category": "READ",
    "description": "Execute a saved query (system view or user view) by name",
    "keywords": ["view", "saved query", "system view", "user view"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Logical name of the table"},
        "viewName": {"type": "string", "description": "Name of the saved view"},
        "top": {"type": "integer", "description": "Max rows (default 5, max 50)"}
      },
      "required": ["table", "viewName"]
    }
  },
  {
    "name": "dataverse_create_row",
    "category": "WRITE",
    "description": "Create a new Dataverse row with provided fields",
    "keywords": ["create", "insert", "add", "new", "record"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Plural name of the table"},
        "record": {"type": "object", "description": "JSON body of the record"},
        "impersonateUserId": {"type": "string", "description": "User GUID to impersonate"}
      },
      "required": ["table", "record"]
    }
  },
  {
    "name": "dataverse_update_row",
    "category": "WRITE",
    "description": "Update a Dataverse row by GUID or alternate key with partial record body",
    "keywords": ["update", "modify", "change", "edit", "patch"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Plural name of the table"},
        "id": {"type": "string", "description": "Row GUID or omit if using alternateKey"},
        "alternateKey": {"type": "string", "description": "Alternate key in format: key1=value1,key2=value2"},
        "record": {"type": "object", "description": "Partial fields to update"},
        "impersonateUserId": {"type": "string", "description": "User GUID to impersonate"}
      },
      "required": ["table", "record"]
    }
  },
  {
    "name": "dataverse_delete_row",
    "category": "WRITE",
    "description": "Delete a Dataverse row by GUID or alternate key",
    "keywords": ["delete", "remove", "destroy"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Plural name of the table"},
        "id": {"type": "string", "description": "Row GUID or omit if using alternateKey"},
        "alternateKey": {"type": "string", "description": "Alternate key in format: key1=value1,key2=value2"},
        "impersonateUserId": {"type": "string", "description": "User GUID to impersonate"}
      },
      "required": ["table"]
    }
  },
  {
    "name": "dataverse_upsert",
    "category": "WRITE",
    "description": "Upsert (create or update) a Dataverse row using alternate keys",
    "keywords": ["upsert", "create or update", "alternate key"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Plural table name"},
        "keys": {"type": "object", "description": "Alternate key name-value pairs"},
        "record": {"type": "object", "description": "Record data to upsert"}
      },
      "required": ["table", "keys", "record"]
    }
  },
  {
    "name": "dataverse_create_multiple",
    "category": "BULK",
    "description": "Create multiple Dataverse rows in a single optimized request",
    "keywords": ["bulk", "batch", "multiple", "create many", "insert many"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Plural table name"},
        "records": {"type": "array", "description": "Array of record objects to create", "items": {"type": "object"}}
      },
      "required": ["table", "records"]
    }
  },
  {
    "name": "dataverse_update_multiple",
    "category": "BULK",
    "description": "Update multiple Dataverse rows in a single optimized request",
    "keywords": ["bulk", "batch", "multiple", "update many", "modify many"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Plural table name"},
        "records": {"type": "array", "description": "Array of record objects with ID and fields to update", "items": {"type": "object"}}
      },
      "required": ["table", "records"]
    }
  },
  {
    "name": "dataverse_upsert_multiple",
    "category": "BULK",
    "description": "Upsert multiple Dataverse rows in a single optimized request using alternate keys",
    "keywords": ["bulk", "batch", "multiple", "upsert many"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Plural table name"},
        "records": {"type": "array", "description": "Array of record objects to upsert", "items": {"type": "object"}}
      },
      "required": ["table", "records"]
    }
  },
  {
    "name": "dataverse_batch",
    "category": "BULK",
    "description": "Execute multiple Dataverse operations in a single atomic batch request",
    "keywords": ["batch", "transaction", "atomic", "multiple operations"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "requests": {"type": "array", "description": "Array of request objects with method, url, headers, body", "items": {"type": "object"}}
      },
      "required": ["requests"]
    }
  },
  {
    "name": "dataverse_associate",
    "category": "RELATIONSHIPS",
    "description": "Associate two Dataverse records via a relationship",
    "keywords": ["associate", "link", "connect", "relationship", "relate"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Primary table name"},
        "id": {"type": "string", "description": "Primary record GUID"},
        "navigationProperty": {"type": "string", "description": "Relationship navigation property name"},
        "relatedTable": {"type": "string", "description": "Related table name"},
        "relatedId": {"type": "string", "description": "Related record GUID"}
      },
      "required": ["table", "id", "navigationProperty", "relatedTable", "relatedId"]
    }
  },
  {
    "name": "dataverse_disassociate",
    "category": "RELATIONSHIPS",
    "description": "Disassociate two Dataverse records by removing a relationship",
    "keywords": ["disassociate", "unlink", "disconnect", "remove relationship"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Primary table name"},
        "id": {"type": "string", "description": "Primary record GUID"},
        "navigationProperty": {"type": "string", "description": "Relationship navigation property name"},
        "relatedId": {"type": "string", "description": "Related record GUID (optional for single-valued)"}
      },
      "required": ["table", "id", "navigationProperty"]
    }
  },
  {
    "name": "dataverse_get_entity_metadata",
    "category": "METADATA",
    "description": "Retrieve table (entity) metadata including schema name, columns, keys",
    "keywords": ["metadata", "schema", "entity", "table definition", "columns"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Logical name of the table (e.g., account, contact)"}
      },
      "required": ["table"]
    }
  },
  {
    "name": "dataverse_get_attribute_metadata",
    "category": "METADATA",
    "description": "Retrieve column (attribute) metadata for a table including type, format, options",
    "keywords": ["attribute", "column", "field", "metadata", "type"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Logical name of the table"},
        "attribute": {"type": "string", "description": "Logical name of the attribute/column (optional - returns all if omitted)"}
      },
      "required": ["table"]
    }
  },
  {
    "name": "dataverse_get_relationships",
    "category": "METADATA",
    "description": "Retrieve relationship metadata for a table",
    "keywords": ["relationships", "navigation", "foreign key", "lookup"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Logical name of the table"}
      },
      "required": ["table"]
    }
  },
  {
    "name": "dataverse_get_global_optionsets",
    "category": "METADATA",
    "description": "Retrieve global option set (picklist) definitions with label/value pairs",
    "keywords": ["optionset", "picklist", "choice", "dropdown", "values"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "optionSetName": {"type": "string", "description": "Name of the global option set (optional - returns all if omitted)"}
      },
      "required": []
    }
  },
  {
    "name": "dataverse_get_business_rules",
    "category": "METADATA",
    "description": "Retrieve business rules (validation/automation) for a table",
    "keywords": ["business rules", "validation", "automation"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Logical name of the table"}
      },
      "required": ["table"]
    }
  },
  {
    "name": "dataverse_get_security_roles",
    "category": "METADATA",
    "description": "List security roles with privileges",
    "keywords": ["security", "roles", "privileges", "permissions"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "roleName": {"type": "string", "description": "Name of specific role (optional - returns all if omitted)"}
      },
      "required": []
    }
  },
  {
    "name": "dataverse_whoami",
    "category": "SECURITY",
    "description": "Get current user context (userId, businessUnitId, organizationId)",
    "keywords": ["whoami", "current user", "me", "identity"],
    "inputSchema": {
      "type": "object",
      "properties": {},
      "required": []
    }
  },
  {
    "name": "dataverse_assign",
    "category": "SECURITY",
    "description": "Assign a record to a different user or team (change owner)",
    "keywords": ["assign", "owner", "change owner", "transfer"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Logical name of the table"},
        "recordId": {"type": "string", "description": "GUID of the record to assign"},
        "assigneeId": {"type": "string", "description": "GUID of the user or team to assign to"},
        "assigneeType": {"type": "string", "description": "Type of assignee: systemuser or team"}
      },
      "required": ["table", "recordId", "assigneeId", "assigneeType"]
    }
  },
  {
    "name": "dataverse_share",
    "category": "SECURITY",
    "description": "Grant access to a record for a specific user or team",
    "keywords": ["share", "grant", "access", "permission"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Logical name of the table"},
        "recordId": {"type": "string", "description": "GUID of the record to share"},
        "principalId": {"type": "string", "description": "GUID of the user or team"},
        "principalType": {"type": "string", "description": "Type: systemuser or team"},
        "accessMask": {"type": "string", "description": "Access rights: Read, Write, Delete, Append, AppendTo, Assign, Share (comma-separated)"}
      },
      "required": ["table", "recordId", "principalId", "principalType", "accessMask"]
    }
  },
  {
    "name": "dataverse_unshare",
    "category": "SECURITY",
    "description": "Revoke access to a record for a specific user or team",
    "keywords": ["unshare", "revoke", "remove access"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Logical name of the table"},
        "recordId": {"type": "string", "description": "GUID of the record"},
        "principalId": {"type": "string", "description": "GUID of the user or team"}
      },
      "required": ["table", "recordId", "principalId"]
    }
  },
  {
    "name": "dataverse_modify_access",
    "category": "SECURITY",
    "description": "Modify existing access rights for a user or team on a record",
    "keywords": ["modify", "change", "access", "permissions"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Logical name of the table"},
        "recordId": {"type": "string", "description": "GUID of the record"},
        "principalId": {"type": "string", "description": "GUID of the user or team"},
        "accessMask": {"type": "string", "description": "New access rights (comma-separated)"}
      },
      "required": ["table", "recordId", "principalId", "accessMask"]
    }
  },
  {
    "name": "dataverse_retrieve_principal_access",
    "category": "SECURITY",
    "description": "Check what access rights a user or team has to a specific record",
    "keywords": ["check", "access", "permissions", "can user"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Logical name of the table"},
        "recordId": {"type": "string", "description": "GUID of the record"},
        "principalId": {"type": "string", "description": "GUID of the user or team"}
      },
      "required": ["table", "recordId", "principalId"]
    }
  },
  {
    "name": "dataverse_add_team_members",
    "category": "SECURITY",
    "description": "Add users to a team",
    "keywords": ["team", "add member", "user"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "teamId": {"type": "string", "description": "GUID of the team"},
        "memberIds": {"type": "array", "description": "Array of user GUIDs to add", "items": {"type": "string"}}
      },
      "required": ["teamId", "memberIds"]
    }
  },
  {
    "name": "dataverse_remove_team_members",
    "category": "SECURITY",
    "description": "Remove users from a team",
    "keywords": ["team", "remove member", "user"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "teamId": {"type": "string", "description": "GUID of the team"},
        "memberIds": {"type": "array", "description": "Array of user GUIDs to remove", "items": {"type": "string"}}
      },
      "required": ["teamId", "memberIds"]
    }
  },
  {
    "name": "dataverse_set_state",
    "category": "RECORD_MGMT",
    "description": "Change record state and status (activate/deactivate or custom states)",
    "keywords": ["state", "status", "activate", "deactivate", "close"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Logical name of the table"},
        "recordId": {"type": "string", "description": "GUID of the record"},
        "state": {"type": "integer", "description": "State code (e.g., 0=Active, 1=Inactive)"},
        "status": {"type": "integer", "description": "Status code (depends on table)"}
      },
      "required": ["table", "recordId", "state", "status"]
    }
  },
  {
    "name": "dataverse_merge",
    "category": "RECORD_MGMT",
    "description": "Merge two records (subordinate merged into target, subordinate deleted)",
    "keywords": ["merge", "duplicate", "combine", "consolidate"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Logical name of the table"},
        "targetId": {"type": "string", "description": "GUID of the target record (kept)"},
        "subordinateId": {"type": "string", "description": "GUID of the subordinate record (deleted)"},
        "updateContent": {"type": "object", "description": "Optional data to update on target after merge"}
      },
      "required": ["table", "targetId", "subordinateId"]
    }
  },
  {
    "name": "dataverse_initialize_from",
    "category": "RECORD_MGMT",
    "description": "Create a new record initialized with values from an existing record (template pattern)",
    "keywords": ["initialize", "template", "copy", "clone"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "sourceTable": {"type": "string", "description": "Logical name of the source table"},
        "sourceId": {"type": "string", "description": "GUID of the source record"},
        "targetTable": {"type": "string", "description": "Logical name of the target table to create"}
      },
      "required": ["sourceTable", "sourceId", "targetTable"]
    }
  },
  {
    "name": "dataverse_calculate_rollup",
    "category": "RECORD_MGMT",
    "description": "Force recalculation of a rollup field on a record",
    "keywords": ["rollup", "calculate", "recalculate", "sum"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Logical name of the table"},
        "recordId": {"type": "string", "description": "GUID of the record"},
        "fieldName": {"type": "string", "description": "Logical name of the rollup field"}
      },
      "required": ["table", "recordId", "fieldName"]
    }
  },
  {
    "name": "dataverse_upload_attachment",
    "category": "ATTACHMENTS",
    "description": "Upload a file attachment to a note (annotation) or email",
    "keywords": ["upload", "file", "attachment", "document"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "regarding": {"type": "string", "description": "GUID of the record to attach to"},
        "regardingType": {"type": "string", "description": "Logical name of the regarding record's table"},
        "fileName": {"type": "string", "description": "Name of the file"},
        "mimeType": {"type": "string", "description": "MIME type (e.g., application/pdf)"},
        "content": {"type": "string", "description": "Base64-encoded file content"},
        "subject": {"type": "string", "description": "Note subject/title (optional)"}
      },
      "required": ["regarding", "regardingType", "fileName", "mimeType", "content"]
    }
  },
  {
    "name": "dataverse_download_attachment",
    "category": "ATTACHMENTS",
    "description": "Download a file attachment from a note (annotation) by ID",
    "keywords": ["download", "file", "attachment", "get file"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "annotationId": {"type": "string", "description": "GUID of the annotation record"}
      },
      "required": ["annotationId"]
    }
  },
  {
    "name": "dataverse_track_changes",
    "category": "CHANGE_TRACKING",
    "description": "Retrieve changes to records since last sync using delta token",
    "keywords": ["track", "changes", "delta", "sync", "modified"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Plural name of the table (must have change tracking enabled)"},
        "select": {"type": "string", "description": "Comma-separated columns to select"},
        "deltaToken": {"type": "string", "description": "Delta token from previous response (omit for initial sync)"}
      },
      "required": ["table"]
    }
  },
  {
    "name": "dataverse_get_async_operation",
    "category": "ASYNC",
    "description": "Get status of an asynchronous operation (background job)",
    "keywords": ["async", "job", "status", "background"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "asyncOperationId": {"type": "string", "description": "GUID of the async operation"}
      },
      "required": ["asyncOperationId"]
    }
  },
  {
    "name": "dataverse_list_async_operations",
    "category": "ASYNC",
    "description": "List recent asynchronous operations with optional filter by status",
    "keywords": ["async", "jobs", "list", "background", "operations"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "status": {"type": "string", "description": "Filter by status: InProgress, Succeeded, Failed, Canceled"},
        "top": {"type": "integer", "description": "Max rows (default 10, max 50)"}
      },
      "required": []
    }
  },
  {
    "name": "dataverse_execute_action",
    "category": "ADVANCED",
    "description": "Execute a Dataverse action (bound or unbound) with parameters",
    "keywords": ["action", "execute", "custom", "workflow"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "action": {"type": "string", "description": "Action name (e.g., WinOpportunity, CloseIncident)"},
        "table": {"type": "string", "description": "Table name for bound actions (optional)"},
        "id": {"type": "string", "description": "Record GUID for bound actions (optional)"},
        "parameters": {"type": "object", "description": "Action input parameters as JSON object"}
      },
      "required": ["action"]
    }
  },
  {
    "name": "dataverse_execute_function",
    "category": "ADVANCED",
    "description": "Execute a Dataverse function (bound or unbound) with parameters",
    "keywords": ["function", "execute", "custom", "calculate"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "function": {"type": "string", "description": "Function name (e.g., WhoAmI, RetrieveVersion)"},
        "table": {"type": "string", "description": "Table name for bound functions (optional)"},
        "id": {"type": "string", "description": "Record GUID for bound functions (optional)"},
        "parameters": {"type": "object", "description": "Function parameters as query string key-value pairs"}
      },
      "required": ["function"]
    }
  },
  {
    "name": "dataverse_detect_duplicates",
    "category": "ADVANCED",
    "description": "Detect duplicate records based on duplicate detection rules",
    "keywords": ["duplicate", "detect", "find duplicates", "similar"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Logical name of the table"},
        "record": {"type": "object", "description": "Record data to check for duplicates"}
      },
      "required": ["table", "record"]
    }
  },
  {
    "name": "dataverse_get_audit_history",
    "category": "ADVANCED",
    "description": "Retrieve audit history (change log) for a record",
    "keywords": ["audit", "history", "changes", "log", "who changed"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "table": {"type": "string", "description": "Logical name of the table"},
        "recordId": {"type": "string", "description": "GUID of the record"},
        "top": {"type": "integer", "description": "Max audit records (default 10, max 50)"}
      },
      "required": ["table", "recordId"]
    }
  },
  {
    "name": "dataverse_get_plugin_traces",
    "category": "ADVANCED",
    "description": "Retrieve plugin trace logs for diagnostics",
    "keywords": ["plugin", "trace", "logs", "debug", "troubleshoot"],
    "inputSchema": {
      "type": "object",
      "properties": {
        "correlationId": {"type": "string", "description": "Correlation ID to filter traces (optional)"},
        "top": {"type": "integer", "description": "Max trace records (default 10, max 50)"}
      },
      "required": []
    }
  }
]
```

---
*Version: 2.0*
