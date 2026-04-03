# Seismic Library Content Management

Manage files, folders, and items in Seismic Library teamsites. Supports file upload, download, metadata management, folder operations, item queries, and content deletion. Works as both a REST connector for Power Automate/Power Apps and an MCP server for Copilot Studio agents.

## Publisher: Troy Taylor

## Prerequisites

- A Seismic account with access to the Library Content Management API
- An OAuth2 application registered in Seismic with `seismic.library.view` and `seismic.library.manage` scopes

## Obtaining Credentials

1. Log in to your Seismic tenant and navigate to **Settings > Integration > API Token Management**
2. Register an OAuth2 application with the authorization code flow
3. Set the redirect URI to `https://global.consent.azure-apim.net/redirect`
4. Note the **Client ID** and **Client Secret** for connector configuration

## Supported Operations

### Invoke Seismic Library Content Management MCP
Model Context Protocol endpoint for Copilot Studio agents. Exposes library content management operations via JSON-RPC 2.0 including tools for listing teamsites, browsing folders, querying items, downloading files, creating folders, updating metadata, and deleting items.

### List Teamsites
Get the full list of teamsites in the tenant.

### Get Teamsite
Get the name and details of a specific teamsite.

### Add a File
Upload a new file to a teamsite library folder. Provide the file content as a base64-encoded string. Max file size is 2 GB.

### Add a New File Version
Upload a new version of an existing file. Provide the file content as a base64-encoded string.

### Get File Information
Get the metadata and properties for a specific file in a teamsite library.

### Update File Information
Update a file's name, parent folder, owner, description, expiration, or custom properties.

### Download a File
Get the download URL and base64-encoded file content for a file.

### Download a File Version
Get the download URL and base64-encoded file content for a specific historical version of a file.

### Add a Folder
Create a new folder inside a teamsite.

### Get Folder Information
Get the metadata for a specific folder in a teamsite library.

### Update Folder Information
Rename or move a folder by updating its name or parent folder.

### List Items in a Folder
Get the list of files, folders, and URLs in a given folder with pagination support.

### Get Item Information
Get information and properties for any item type (file, folder, or URL).

### Delete an Item
Delete any item type (file, folder, or URL) from the library.

### Query Items
Search for items in the library using query parameters such as name, type, or modification date.

### Get Item Versions
Get all versions for an item in the library.

### Copy an Item
Copy a file, folder, or URL to a target folder within the same teamsite.

### Copy a File
Copy a file to a target folder within the same teamsite.

### Copy a URL
Copy a URL item to a target folder within the same teamsite.

### Copy a Folder
Copy a folder and its contents to a target folder within the same teamsite.

### Add a URL
Add a URL item to the library in a specified folder.

### Get URL Information
Get the metadata and properties for a URL item in the library.

### Update URL Information
Update a URL item's name, parent folder, URL value, or properties.

### Get or Create Folder by Path
Get an existing folder or create it if it does not exist, using a path string.

## API Documentation

[Seismic Library Content Management API](https://developer.seismic.com/seismicsoftware/reference/seismiclibrarycontentmanagementaddafile)

## Known Issues and Limitations

- File upload operations (Add a File, Add a New File Version) accept file content as a base64-encoded string and the connector's custom code transforms the request into the `multipart/form-data` format required by the Seismic API. Extremely large files may exceed Power Platform's request size limits.
- The Download a File and Download a File Version actions return both the download URL and the base64-encoded file content. Very large files may exceed Power Platform's response size limits.
- Rate limiting is 600 requests per 60 seconds for most endpoints, and 60 requests per 60 seconds for the List Items in a Folder endpoint.
- Maximum of 1,000 items returned per List Items request.
