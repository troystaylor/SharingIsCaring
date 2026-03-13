# Microsoft Cloud Licensing

Manage Microsoft Cloud Licensing through the Microsoft Graph Cloud Licensing API (preview). This MCP connector exposes allotments, assignments, usage rights, assignment errors, and waiting members as tools for Copilot Studio agents.

> **Note:** All endpoints use the Microsoft Graph **beta** API. There are no v1.0 endpoints available for the cloudLicensing namespace at this time.

## Publisher: Troy Taylor

## Prerequisites

- A Microsoft 365 tenant with cloud licensing enabled.
- An Azure AD app registration with the following **delegated** permissions granted:
  - `CloudLicensing.ReadWrite.All`
  - `User.Read.All`
  - `Group.Read.All`
  - `Directory.Read.All`
- Admin consent granted for the above permissions.

## Obtaining Credentials

1. Go to the [Azure portal](https://portal.azure.com) > **Azure Active Directory** > **App registrations**.
2. Register a new application or select an existing one.
3. Under **API permissions**, add the delegated permissions listed above and grant admin consent.
4. Under **Authentication**, add `https://global.consent.azure-apim.net/redirect` as a redirect URI.
5. Copy the **Application (client) ID** into the connector's `apiProperties.json` `clientId` field.

## Supported Operations

### Allotments

| Tool | Description |
|------|-------------|
| `list_allotments` | List all license allotments in the organization |
| `get_allotment` | Get details of a specific license allotment |

### Allotment Assignments

| Tool | Description |
|------|-------------|
| `list_allotment_assignments` | List assignments consuming from an allotment |
| `create_allotment_assignment` | Create a new license assignment for an allotment |

### Allotment Waiting Members

| Tool | Description |
|------|-------------|
| `list_allotment_waiting_members` | List over-assigned users in the waiting room |
| `get_allotment_waiting_member` | Get details of a specific waiting member |

### Organization Assignments

| Tool | Description |
|------|-------------|
| `list_assignments` | List all license assignments in the organization |
| `get_assignment` | Get details of a specific assignment |
| `update_assignment` | Update an assignment to enable or disable services |
| `delete_assignment` | Delete a license assignment |

### Assignment Errors

| Tool | Description |
|------|-------------|
| `list_assignment_errors` | List assignment synchronization errors (org-level) |
| `get_assignment_error` | Get details of a specific assignment error |

### User-Scoped

| Tool | Description |
|------|-------------|
| `list_user_usage_rights` | List usage rights for a user |
| `get_user_usage_right` | Get a specific usage right for a user |
| `list_user_assignments` | List license assignments for a user |
| `list_user_assignment_errors` | List assignment errors for a user |
| `list_user_waiting_members` | List allotments a user is waiting for |
| `reprocess_user_assignments` | Reprocess assignments to fix sync issues |

### Group-Scoped

| Tool | Description |
|------|-------------|
| `list_group_usage_rights` | List usage rights for a group |
| `get_group_usage_right` | Get a specific usage right for a group |
| `list_group_assignments` | List license assignments for a group |

### Signed-In User (Me)

| Tool | Description |
|------|-------------|
| `list_my_usage_rights` | List the signed-in user's usage rights |
| `list_my_assignments` | List the signed-in user's assignments |
| `list_my_waiting_members` | List allotments the signed-in user is waiting for |
| `list_my_assignment_errors` | List assignment errors for the signed-in user |

## 28 MCP Tools Total

## Key Concepts

### Allotments
A license allotment represents a pool of licenses for a specific SKU (product). Each allotment shows how many units are allotted vs. consumed, what services are included, and which subscriptions back it.

### Assignments
An assignment links a user or group to an allotment. Assignments can optionally disable specific service plans within the SKU. The `assignedTo@odata.bind` pattern is used when creating assignments.

### Usage Rights
Usage rights represent the effective licenses a user or group has after all assignments are processed. They show which SKUs and services the entity has access to.

### Assignment Errors
When license synchronization fails (e.g., conflicting assignments, insufficient licenses), assignment errors are created with error codes and messages.

### Waiting Members
When an allotment is over-assigned (more assignments than available licenses), excess users are placed in a waiting room. The `waitingSinceDateTime` property tracks when they started waiting.

## API Reference

- [Cloud Licensing API overview](https://learn.microsoft.com/en-us/graph/api/resources/cloud-licensing-api-overview?view=graph-rest-beta)
- [Allotment resource](https://learn.microsoft.com/en-us/graph/api/resources/cloudlicensing-allotment?view=graph-rest-beta)
- [Assignment resource](https://learn.microsoft.com/en-us/graph/api/resources/cloudlicensing-assignment?view=graph-rest-beta)
- [UsageRight resource](https://learn.microsoft.com/en-us/graph/api/resources/cloudlicensing-usageright?view=graph-rest-beta)
- [AssignmentError resource](https://learn.microsoft.com/en-us/graph/api/resources/cloudlicensing-assignmenterror?view=graph-rest-beta)
- [WaitingMember resource](https://learn.microsoft.com/en-us/graph/api/resources/cloudlicensing-waitingmember?view=graph-rest-beta)

## Known Limitations

- All endpoints are **beta only** — no v1.0 endpoints exist for cloudLicensing.
- Some list operations may require `$top` parameter for large tenants.
- The `reprocessAssignments` action returns 204 No Content on success.
- Assignment creation uses `@odata.bind` for the `assignedTo` relationship.
