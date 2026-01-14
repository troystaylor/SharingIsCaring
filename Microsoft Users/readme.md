# Microsoft Users Connector

Power Platform custom connector for Microsoft Graph Users API with MCP integration for Copilot Studio agents. An enhanced alternative to Microsoft's first-party User Profile MCP Server.

## Overview

This connector provides comprehensive access to user profiles, organizational hierarchy, presence status, and people discovery via Microsoft Graph:

- **User Profiles** - Get user details by ID or UPN, list/search users
- **Self-Knowledge** - Access signed-in user's profile, manager, direct reports
- **Organizational Hierarchy** - Navigate manager/report relationships
- **Presence** - Real-time availability and activity status
- **People Discovery** - Relevance-ranked people search
- **Photos** - User profile photos
- **Schedule/Availability** - Free/busy calendar information
- **Group Memberships** - Groups, Teams, and directory roles
- **Licenses** - Microsoft 365 license assignments
- **Mail Tips** - Out of office status, mailbox info
- **Authentication Methods** - Registered auth methods
- **Owned Objects** - Apps and groups owned by users
- **Profile API (Beta)** - Skills, projects, certifications, awards, languages, positions, education, interests, web accounts

## Comparison to Microsoft 1P Server

| Feature | Microsoft 1P Server | This Connector |
|---------|---------------------|----------------|
| Get my profile | ✅ | ✅ |
| Get my manager | ✅ | ✅ |
| Get user profile | ✅ | ✅ |
| Get user's manager | ✅ | ✅ |
| Get direct reports | ✅ | ✅ + My direct reports |
| List/search users | ✅ | ✅ |
| **Get presence** | ❌ | ✅ My + user + batch |
| **Search people** | ❌ | ✅ Relevance-ranked |
| **Get photos** | ❌ | ✅ My + user photos |
| **Schedule availability** | ❌ | ✅ Free/busy lookup |
| **Group memberships** | ❌ | ✅ Groups, Teams, roles |
| **Joined Teams** | ❌ | ✅ |
| **Licenses** | ❌ | ✅ My + user licenses |
| **Mail tips / OOO** | ❌ | ✅ |
| **Auth methods** | ❌ | ✅ |
| **Owned objects** | ❌ | ✅ |
| **Skills** | ❌ | ✅ Beta Profile API |
| **Projects** | ❌ | ✅ Beta Profile API |
| **Certifications** | ❌ | ✅ Beta Profile API |
| **Awards** | ❌ | ✅ Beta Profile API |
| **Languages** | ❌ | ✅ Beta Profile API |
| **Positions/Jobs** | ❌ | ✅ Beta Profile API |
| **Education** | ❌ | ✅ Beta Profile API |
| **Interests** | ❌ | ✅ Beta Profile API |
| **Web Accounts** | ❌ | ✅ Beta Profile API |
| **Addresses** | ❌ | ✅ Beta Profile API |
| **Websites** | ❌ | ✅ Beta Profile API |
| **Anniversaries** | ❌ | ✅ Beta Profile API |
| **Notes** | ❌ | ✅ Beta Profile API |
| MCP tools | 6 | **51** |

## Prerequisites

- Microsoft 365 tenant
- Azure AD app registration with appropriate permissions
- (Optional) Application Insights resource for telemetry

## Operations

### My Profile (Signed-in User)

| Operation | Method | Description |
|-----------|--------|-------------|
| GetMyProfile | GET | Get signed-in user's profile |
| GetMyManager | GET | Get signed-in user's manager |
| GetMyDirectReports | GET | Get signed-in user's direct reports |
| GetMyPhoto | GET | Get signed-in user's profile photo |
| GetMyPresence | GET | Get signed-in user's presence/availability |
| GetMyMemberships | GET | Get groups/roles the signed-in user belongs to |
| GetMyLicenses | GET | Get signed-in user's license details |
| GetMyJoinedTeams | GET | Get Teams the signed-in user is a member of |
| GetMyAuthMethods | GET | Get signed-in user's authentication methods |
| GetMyOwnedObjects | GET | Get objects owned by signed-in user |

### User Profiles

| Operation | Method | Description |
|-----------|--------|-------------|
| ListUsers | GET | List/search users in the organization |
| GetUserProfile | GET | Get any user's profile by ID or UPN |
| GetUsersManager | GET | Get any user's manager |
| GetDirectReports | GET | Get any user's direct reports |
| GetUserPhoto | GET | Get any user's profile photo |
| GetUserPresence | GET | Get any user's presence |
| GetUserMemberships | GET | Get groups/roles a user belongs to |
| GetUserLicenses | GET | Get a user's license details |
| GetUserOwnedObjects | GET | Get objects owned by a user |

### People & Presence

| Operation | Method | Description |
|-----------|--------|-------------|
| SearchPeople | GET | Search people relevant to signed-in user |
| GetBatchPresence | POST | Get presence for multiple users at once |

### Schedule & Mail

| Operation | Method | Description |
|-----------|--------|-------------|
| GetScheduleAvailability | POST | Get free/busy availability for users |
| GetMailTips | POST | Get mail tips (OOO, mailbox status) |

### Profile API (Beta)

> ⚠️ **Note**: These operations use the Microsoft Graph **beta** endpoint. Beta APIs are subject to change and may not be available in all tenants.

| Operation | Method | Description |
|-----------|--------|-------------|
| GetMyFullProfile | GET | Get signed-in user's complete profile (Beta) |
| GetUserFullProfile | GET | Get any user's complete profile (Beta) |
| GetMySkills | GET | Get signed-in user's skills (Beta) |
| GetUserSkills | GET | Get any user's skills (Beta) |
| GetMyProjects | GET | Get signed-in user's projects (Beta) |
| GetUserProjects | GET | Get any user's projects (Beta) |
| GetMyCertifications | GET | Get signed-in user's certifications (Beta) |
| GetUserCertifications | GET | Get any user's certifications (Beta) |
| GetMyAwards | GET | Get signed-in user's awards (Beta) |
| GetUserAwards | GET | Get any user's awards (Beta) |
| GetMyLanguages | GET | Get signed-in user's languages (Beta) |
| GetUserLanguages | GET | Get any user's languages (Beta) |
| GetMyPositions | GET | Get signed-in user's job positions (Beta) |
| GetUserPositions | GET | Get any user's job positions (Beta) |
| GetMyEducation | GET | Get signed-in user's education (Beta) |
| GetUserEducation | GET | Get any user's education (Beta) |
| GetMyInterests | GET | Get signed-in user's interests (Beta) |
| GetUserInterests | GET | Get any user's interests (Beta) |
| GetMyWebAccounts | GET | Get signed-in user's web accounts (Beta) |
| GetUserWebAccounts | GET | Get any user's web accounts (Beta) |
| GetMyAddresses | GET | Get signed-in user's addresses (Beta) |
| GetUserAddresses | GET | Get any user's addresses (Beta) |
| GetMyWebsites | GET | Get signed-in user's websites (Beta) |
| GetUserWebsites | GET | Get any user's websites (Beta) |
| GetMyAnniversaries | GET | Get signed-in user's anniversaries (Beta) |
| GetUserAnniversaries | GET | Get any user's anniversaries (Beta) |
| GetMyNotes | GET | Get signed-in user's notes (Beta) |
| GetUserNotes | GET | Get any user's notes (Beta) |

### MCP Protocol

| Operation | Description |
|-----------|-------------|
| MCP | Model Context Protocol endpoint for Copilot Studio |

## MCP Tools (for Copilot Studio)

When used with Copilot Studio agents, the connector exposes **51 tools**:

### Core Profile Tools (matches Microsoft 1P + enhanced)

| Tool | Description |
|------|-------------|
| get_my_profile | Get signed-in user's profile |
| get_my_manager | Get signed-in user's manager |
| get_my_direct_reports | Get signed-in user's direct reports |
| get_user_profile | Get any user's profile by ID or UPN |
| get_users_manager | Get any user's manager |
| get_direct_reports | Get any user's direct reports |
| list_users | List/search users with OData support |

### Presence Tools

| Tool | Description |
|------|-------------|
| get_my_presence | Get signed-in user's availability |
| get_user_presence | Get any user's availability |
| get_batch_presence | Get presence for multiple users (up to 650) |

### People & Photo Tools

| Tool | Description |
|------|-------------|
| search_people | Search relevant people (ranked by collaboration) |
| get_my_photo_url | Get signed-in user's photo URL |
| get_user_photo_url | Get any user's photo URL |

### Schedule & Availability Tools

| Tool | Description |
|------|-------------|
| get_schedule_availability | Get free/busy times for users (find meeting times) |
| get_mail_tips | Get out-of-office status, mailbox info for recipients |

### Group & Team Tools

| Tool | Description |
|------|-------------|
| get_my_memberships | Get groups, Teams, roles signed-in user belongs to |
| get_user_memberships | Get groups, Teams, roles a user belongs to |
| get_my_joined_teams | Get Microsoft Teams the signed-in user is in |

### License Tools

| Tool | Description |
|------|-------------|
| get_my_licenses | Get signed-in user's M365 license details |
| get_user_licenses | Get any user's license details |

### Security & Auth Tools

| Tool | Description |
|------|-------------|
| get_my_auth_methods | Get signed-in user's registered auth methods |

### Ownership Tools

| Tool | Description |
|------|-------------|
| get_my_owned_objects | Get apps/groups owned by signed-in user |
| get_user_owned_objects | Get apps/groups owned by a user |

### Profile API Tools (Beta)

> ⚠️ **Note**: These tools use the Microsoft Graph **beta** endpoint. Beta APIs are subject to change and may not be available in all tenants.

| Tool | Description |
|------|-------------|
| get_my_full_profile | Get signed-in user's complete profile (Beta) |
| get_user_full_profile | Get any user's complete profile (Beta) |
| get_my_skills | Get signed-in user's professional skills (Beta) |
| get_user_skills | Get any user's skills - find expertise (Beta) |
| get_my_projects | Get projects signed-in user worked on (Beta) |
| get_user_projects | Get projects a user worked on (Beta) |
| get_my_certifications | Get signed-in user's certifications (Beta) |
| get_user_certifications | Get user's certifications - verify qualifications (Beta) |
| get_my_awards | Get signed-in user's awards and honors (Beta) |
| get_user_awards | Get any user's awards (Beta) |
| get_my_languages | Get languages signed-in user speaks (Beta) |
| get_user_languages | Get languages a user speaks - find multilingual staff (Beta) |
| get_my_positions | Get signed-in user's job history (Beta) |
| get_user_positions | Get any user's job history (Beta) |
| get_my_education | Get signed-in user's education history (Beta) |
| get_user_education | Get any user's education (Beta) |
| get_my_interests | Get signed-in user's interests (Beta) |
| get_user_interests | Get user's interests - find shared interests (Beta) |
| get_my_web_accounts | Get signed-in user's web accounts - GitHub, LinkedIn (Beta) |
| get_user_web_accounts | Get any user's web accounts (Beta) |
| get_my_addresses | Get signed-in user's physical addresses (Beta) |
| get_user_addresses | Get any user's addresses (Beta) |
| get_my_websites | Get signed-in user's websites (Beta) |
| get_user_websites | Get any user's websites (Beta) |
| get_my_anniversaries | Get signed-in user's anniversaries - birthday, work (Beta) |
| get_user_anniversaries | Get any user's anniversaries - recognize milestones (Beta) |
| get_my_notes | Get signed-in user's profile notes (Beta) |
| get_user_notes | Get any user's profile notes (Beta) |

## Setup

### 1. Create Azure AD App Registration

1. Go to Azure Portal → Microsoft Entra ID → App registrations
2. Click "New registration"
3. Name: `Microsoft Users Connector`
4. Supported account types: Accounts in this organizational directory only
5. Redirect URI: `https://global.consent.azure-apim.net/redirect`

### 2. Configure API Permissions

Add these Microsoft Graph permissions (Delegated):

| Permission | Required For |
|------------|--------------|
| `User.Read` | Read signed-in user's profile |
| `User.ReadBasic.All` | Read basic profiles of all users |
| `User.Read.All` | Read full profiles of all users |
| `People.Read` | Search people |
| `Presence.Read` | Read own presence |
| `Presence.Read.All` | Read all users' presence |
| `Calendars.Read` | Read own calendar |
| `Calendars.Read.Shared` | Read shared calendars for availability |
| `MailboxSettings.Read` | Read mailbox settings |
| `Group.Read.All` | Read group memberships |
| `Directory.Read.All` | Read directory objects |
| `Team.ReadBasic.All` | Read joined Teams |
| `UserAuthenticationMethod.Read.All` | Read auth methods |

Grant admin consent for your organization.

### 3. Create Client Secret

1. Go to Certificates & secrets
2. New client secret
3. Copy the secret value

### 4. Import Connector

1. Go to Power Platform maker portal
2. Navigate to Custom connectors
3. Import from OpenAPI file → select `apiDefinition.swagger.json`
4. On the "Code" tab, enable custom code and paste `script.csx`
5. Update the app ID in `apiProperties.json`
6. Create and test the connector

## Application Insights (Optional)

To enable telemetry:

1. Create an Application Insights resource in Azure
2. Copy the connection string
3. Update `APP_INSIGHTS_CONNECTION_STRING` in `script.csx`

## Usage Examples

### Get My Profile with Manager

```
Tool: get_my_profile
Arguments: { "expand": "manager" }
```

### Search for Users Named John

```
Tool: list_users
Arguments: { "search": "\"displayName:John\"", "top": 10 }
```

### Check Team Availability

```
Tool: get_batch_presence
Arguments: { "userIds": ["user-id-1", "user-id-2", "user-id-3"] }
```

### Find Relevant People in Engineering

```
Tool: search_people
Arguments: { "query": "Engineering", "top": 5 }
```

### Find Meeting Time for Team

```
Tool: get_schedule_availability
Arguments: { 
  "schedules": ["john@contoso.com", "jane@contoso.com"],
  "startDateTime": "2024-01-15T09:00:00",
  "endDateTime": "2024-01-15T17:00:00",
  "timeZone": "Pacific Standard Time"
}
```

### Check if Someone is Out of Office

```
Tool: get_mail_tips
Arguments: { 
  "emailAddresses": ["john@contoso.com"],
  "mailTipsOptions": "automaticReplies"
}
```

### Get User's Group Memberships

```
Tool: get_user_memberships
Arguments: { "userIdentifier": "john@contoso.com", "top": 20 }
```

### Check User's Licenses

```
Tool: get_user_licenses
Arguments: { "userIdentifier": "john@contoso.com" }
```

### Get User's Skills (Find Expertise)

```
Tool: get_user_skills
Arguments: { "userIdentifier": "john@contoso.com" }
```

### Check User's Certifications

```
Tool: get_user_certifications
Arguments: { "userIdentifier": "john@contoso.com" }
```

### Get Complete User Profile

```
Tool: get_user_full_profile
Arguments: { "userIdentifier": "john@contoso.com" }
```

### Find Someone's Project History

```
Tool: get_user_projects
Arguments: { "userIdentifier": "john@contoso.com" }
```

### Find Multilingual Team Members

```
Tool: get_user_languages
Arguments: { "userIdentifier": "john@contoso.com" }
```

## Notes

1. Use `get_my_*` tools for signed-in user, not `get_user_*` with 'me'
2. `userIdentifier` must be object ID (GUID) or userPrincipalName (UPN)
3. If only display name is available, use `list_users` to look up the user first
4. `$expand` can only expand one property per request (manager OR directReports)
5. `ConsistencyLevel: eventual` is automatically set for advanced user queries
6. Batch presence supports up to 650 user IDs per request
7. **Profile API tools use the beta endpoint** - data availability depends on profile completeness
8. Profile data is populated by People Data Connectors or manual entry by users

## References

- [User resource](https://learn.microsoft.com/graph/api/resources/user)
- [List users](https://learn.microsoft.com/graph/api/user-list)
- [Get presence](https://learn.microsoft.com/graph/api/presence-get)
- [People API](https://learn.microsoft.com/graph/api/user-list-people)
- [Get schedule](https://learn.microsoft.com/graph/api/calendar-getschedule)
- [Get mail tips](https://learn.microsoft.com/graph/api/user-getmailtips)
- [User memberOf](https://learn.microsoft.com/graph/api/user-list-memberof)
- [Joined teams](https://learn.microsoft.com/graph/api/user-list-joinedteams)
- [License details](https://learn.microsoft.com/graph/api/user-list-licensedetails)
- [Auth methods](https://learn.microsoft.com/graph/api/authentication-list-methods)
- [Profile API (Beta)](https://learn.microsoft.com/graph/api/resources/profile)
- [People Data Connectors](https://learn.microsoft.com/microsoft-365-copilot/extensibility/build-connectors-with-people-data)
- [Microsoft 1P User Profile MCP Server](https://learn.microsoft.com/microsoft-agent-365/mcp-server-reference/me)
