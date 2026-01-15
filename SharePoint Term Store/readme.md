# SharePoint Term Store Connector

Microsoft Graph termStore API connector for SharePoint taxonomy and managed metadata management, including term groups, term sets, and hierarchical terms.

## Publisher

Troy Taylor

## Prerequisites

- SharePoint site with term store enabled
- Microsoft 365 account with appropriate permissions
- Required Graph API permissions:
  - `TermStore.Read.All` - Read term store data
  - `TermStore.ReadWrite.All` - Read and write term store data
  - `Sites.Read.All` - Read SharePoint sites
  - `Sites.ReadWrite.All` - Read and write SharePoint sites

## Obtaining Credentials

This connector uses OAuth 2.0 authentication with Azure Active Directory. When you create a connection, you'll be prompted to sign in with your Microsoft 365 account and grant the necessary permissions.

## Supported Operations

### Term Store Management

#### Get Term Store
Get the default term store for a SharePoint site, including available languages and configuration.

#### List Term Groups
Retrieve all term groups in a site's term store. Term groups organize term sets by business area or function (e.g., "Departments", "Locations", "Product Categories").

#### Create Term Group
Create a new term group to organize related term sets.

#### Get Term Group
Get details of a specific term group by ID.

#### Update Term Group
Update a term group's display name or description.

#### Delete Term Group
Remove a term group and all its term sets from the term store.

### Term Set Management

#### List Term Sets
Get all term sets within a term group. Term sets are collections of related managed metadata terms.

#### Create Term Set
Create a new term set with localized names for categorization and tagging.

#### Get Term Set
Retrieve details of a specific term set.

#### Update Term Set
Modify a term set's properties, names, or descriptions.

#### Delete Term Set
Remove a term set and all its terms from the term store.

### Term Management

#### List Terms
Get all root-level terms in a term set.

#### Create Term
Add a new term to a term set with labels in one or more languages.

#### Get Term
Retrieve details of a specific term including labels, descriptions, and properties.

#### Update Term
Modify a term's labels, descriptions, or custom properties. Supports multilingual updates.

#### Delete Term
Remove a term from the term store (will also delete all child terms).

#### List Child Terms
Get all child terms under a parent term for navigating hierarchical taxonomies.

#### Create Child Term
Add a child term under a parent term to build hierarchical taxonomy structures.

### Term Relations

#### List Term Relations
Get all relations (pins or reuses) for a term.

#### Create Term Relation
Create a relation between terms for pinning or reusing terms across term sets.

#### Delete Term Relation
Remove a term relation.

## MCP Protocol Support

This connector includes MCP (Model Context Protocol) support for Copilot Studio agent integration. The `/mcp` endpoint exposes natural language tools for:

- `get_term_store` - Get term store configuration
- `list_term_groups` - List taxonomy groups
- `create_term_group` - Create new term group
- `list_term_sets` - List term sets in a group
- `create_term_set` - Create new term set
- `list_terms` - List terms in a set
- `create_term` - Create new term
- `list_child_terms` - Navigate term hierarchies
- `create_child_term` - Build nested taxonomies
- `update_term` - Modify existing terms
- `delete_term` - Remove terms

## Common Use Cases

### Building Content Taxonomies
Create hierarchical term structures for document classification:
1. Create a term group for "Document Types"
2. Add term sets like "Contracts", "Reports", "Proposals"
3. Build nested terms (e.g., Contracts > Legal Contracts > NDAs)

### Multilingual Metadata
Support global organizations with multilingual terms:
1. Create terms with labels in multiple languages
2. Set default labels per language
3. Users see terms in their preferred language

### Metadata-Driven Search
Enable faceted search and filtering:
1. Apply terms to documents and list items
2. Users filter by term sets (Department, Location, Status)
3. Search results grouped by taxonomy

### Governance & Consistency
Centrally manage organizational vocabulary:
1. Define approved terms in the term store
2. Prevent ad-hoc metadata creation
3. Maintain consistent tagging across sites

## Known Limitations

- Term store operations require elevated permissions
- Deleting parent terms cascades to all children
- Maximum term hierarchy depth: 7 levels
- Site-scoped term groups only accessible within that site collection
- Global term groups require tenant admin permissions

## API Documentation

[Microsoft Graph termStore API Reference](https://learn.microsoft.com/graph/api/resources/termstore-store)

## Deployment Notes

This connector uses custom code (`script.csx`) and must be imported as a custom connector in Power Platform. The connector cannot be shared as a certified connector due to the scriptOperations requirement.

### Validation

```powershell
# Validate the connector definition
paconn validate --api-def apiDefinition.swagger.json
```

### Import to Power Platform

1. Navigate to Power Platform maker portal
2. Go to Custom Connectors
3. Select "Import OpenAPI from file"
4. Upload `apiDefinition.swagger.json`
5. Enable custom code in the "Code" tab
6. Paste contents of `script.csx`
7. Create connection and test operations
