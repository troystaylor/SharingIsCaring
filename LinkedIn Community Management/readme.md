# LinkedIn Community Management

## Overview
A Power Platform custom connector for the LinkedIn Community Management API. Manage LinkedIn organization pages including posts, comments, reactions, media uploads, organization administration, and engagement analytics — all from Power Automate, Power Apps, or Copilot Studio.

Includes a Model Context Protocol (MCP) endpoint with 28 tools for use with Copilot Studio agents.

## Prerequisites
- A LinkedIn account with admin access to at least one LinkedIn Page (organization)
- A LinkedIn Developer App with the following products enabled:
  - **Community Management** (provides the required API permissions)
- OAuth 2.0 credentials (Client ID and Client Secret) from your LinkedIn Developer App
- Power Platform environment with custom connector permissions

## LinkedIn Developer App Setup
1. Go to [LinkedIn Developer Portal](https://developer.linkedin.com/) and sign in
2. Create a new app or select an existing one
3. Under **Products**, request access to **Community Management**
4. Under **Auth**, note your **Client ID** and **Client Secret**
5. Add the Power Platform redirect URL to **Authorized redirect URLs for your app**:
   ```
   https://global.consent.azure-apim.net/redirect
   ```

### Required OAuth Scopes
| Scope | Description |
|-------|-------------|
| `w_member_social` | Create and manage posts as a member |
| `w_organization_social` | Create and manage posts as an organization |
| `r_organization_social` | Read organization posts and social actions |
| `rw_organization_admin` | Read/write organization page administration |
| `r_organization_admin` | Read organization page details |

## Connector Deployment
1. Install the [Power Platform CLI](https://learn.microsoft.com/en-us/power-platform/developer/cli/introduction)
2. Authenticate: `pac auth create --environment <your-environment-url>`
3. Navigate to the `LinkedIn Community Management` folder
4. Validate: `paconn validate --api-def apiDefinition.swagger.json --api-prop apiProperties.json --script script.csx`
5. Create: `paconn create --api-def apiDefinition.swagger.json --api-prop apiProperties.json --script script.csx`
6. In the Power Platform portal, edit the connector to add your LinkedIn Client ID and Client Secret

## Operations

### Posts (5 operations)
| Operation | Method | Description |
|-----------|--------|-------------|
| CreatePost | POST | Create a text, image, video, article, document, or reshare post |
| GetPost | GET | Retrieve a post by its URN |
| FindPostsByAuthor | GET | Find posts by a specific author (member or organization) |
| UpdatePost | POST | Update post commentary or reshare settings (partial update) |
| DeletePost | DELETE | Delete a post |

### Comments (4 operations)
| Operation | Method | Description |
|-----------|--------|-------------|
| GetComments | GET | List comments on a post |
| CreateComment | POST | Add a comment to a post |
| GetComment | GET | Get a specific comment by ID |
| DeleteComment | DELETE | Remove a comment |

### Reactions (3 operations)
| Operation | Method | Description |
|-----------|--------|-------------|
| GetReactions | GET | List reactions on a post |
| CreateReaction | POST | React to a post (LIKE, PRAISE, EMPATHY, etc.) |
| DeleteReaction | DELETE | Remove a reaction |

### Organizations (4 operations)
| Operation | Method | Description |
|-----------|--------|-------------|
| GetOrganization | GET | Get organization details by numeric ID |
| FindOrgByVanityName | GET | Look up an organization by its URL vanity name |
| GetFollowerCount | GET | Get total follower count for an organization |
| FindMemberOrgAccess | GET | Find organizations the member administers, or admins of an organization |

### Social Metadata (2 operations)
| Operation | Method | Description |
|-----------|--------|-------------|
| GetSocialMetadata | GET | Get engagement stats (likes, comments, shares) for a post |
| ToggleComments | POST | Enable or disable comments on a post |

### Media (7 operations)
| Operation | Method | Description |
|-----------|--------|-------------|
| InitializeImageUpload | POST | Get an upload URL for an image |
| GetImage | GET | Get image details by URN |
| InitializeVideoUpload | POST | Initialize a video upload (returns chunk upload URLs) |
| FinalizeVideoUpload | POST | Finalize a video upload after all chunks are uploaded |
| GetVideo | GET | Get video details and processing status |
| InitializeDocumentUpload | POST | Get an upload URL for a document (PDF, PPTX, DOCX) |
| GetDocument | GET | Get document details by URN |

### Statistics (3 operations)
| Operation | Method | Description |
|-----------|--------|-------------|
| GetShareStatistics | GET | Post/share engagement analytics for an organization |
| GetPageStatistics | GET | Page view analytics segmented by demographics |
| GetFollowerStatistics | GET | Follower growth and demographic analytics |

### MCP (1 operation)
| Operation | Method | Description |
|-----------|--------|-------------|
| InvokeMCP | POST | Model Context Protocol endpoint with 28 tools for Copilot Studio |

**Total: 28 REST operations + 1 MCP endpoint (28 tools)**

## MCP Tools
The MCP endpoint exposes all 28 REST operations as tools for use by Copilot Studio agents:

`create_post` · `get_post` · `find_posts_by_author` · `update_post` · `delete_post` · `get_comments` · `create_comment` · `get_comment` · `delete_comment` · `get_reactions` · `create_reaction` · `delete_reaction` · `get_organization` · `find_org_by_vanity_name` · `get_follower_count` · `find_member_org_access` · `get_social_metadata` · `toggle_comments` · `initialize_image_upload` · `get_image` · `initialize_video_upload` · `finalize_video_upload` · `get_video` · `initialize_document_upload` · `get_document` · `get_share_statistics` · `get_page_statistics` · `get_follower_statistics`

## LinkedIn API Notes

### URN Encoding
LinkedIn uses URN identifiers (e.g., `urn:li:organization:123`, `urn:li:ugcPost:456`). The connector automatically URL-encodes URNs in API paths. Pass raw URN values in all parameters.

### Post Creation
When creating a post, the LinkedIn API returns the new post ID in the `x-restli-id` response header. The connector extracts this and returns it in the response body as `{ "id": "urn:li:ugcPost:..." }`.

### Partial Updates
UpdatePost and ToggleComments use LinkedIn's REST.li partial update format. The connector automatically wraps your changes in the required `{"patch":{"$set":{...}}}` envelope.

### Reactions Compound Keys
Delete reaction uses LinkedIn's REST.li compound key format: `/reactions/(actor:{urn},entity:{urn})`. The connector constructs this path from the individual parameters.

### Video Uploads
Video files must be uploaded in 4MB chunks:
1. Call **InitializeVideoUpload** with the file size — returns chunk upload URLs and a video URN
2. PUT each 4MB chunk to its corresponding upload URL — save the ETag from each response
3. Call **FinalizeVideoUpload** with the video URN, upload token, and array of ETags

### Statistics Time Range
Statistics operations support optional time-based filtering. When using MCP tools, provide `start_time` and `end_time` as epoch milliseconds with an optional `granularity` (DAY, WEEK, MONTH). Data is available for the past 12 months.

### API Versioning
All requests include the `Linkedin-Version: 202602` and `X-Restli-Protocol-Version: 2.0.0` headers automatically.

## Application Insights
This connector includes Application Insights telemetry. To enable logging:
1. Create an Application Insights resource in Azure
2. Copy the connection string
3. Set the `APP_INSIGHTS_CONNECTION_STRING` constant in `script.csx`

Events tracked: `RequestReceived`, `RequestCompleted`, `RequestError`, `MCPRequest`, `MCPToolCall`, `MCPToolError`, `LinkedInAPIError`

## Author
- **Name**: Troy Taylor
- **Email**: troy@troystaylor.com
- **GitHub**: https://github.com/troystaylor
