# Canvas LMS MCP

MCP-enabled custom connector for Canvas LMS (Instructure). Exposes teacher-focused operations as MCP tools for Microsoft Copilot Studio.

## Prerequisites

- A Canvas LMS instance (e.g., `canvas.instructure.com` or your institution's domain)
- A Canvas **Developer Key** (OAuth2 client credentials) — obtained from your Canvas admin
- Power Platform environment with custom connector support

## Setup

### 1. Obtain a Canvas Developer Key

1. Ask your Canvas admin to create a Developer Key at **Admin > Developer Keys > + Developer Key > API Key**
2. Set the redirect URI to: `https://global.consent.azure-apim.net/redirect/canvaslmsmcp`
3. Note the **Client ID** (numeric) and **Client Secret**

### 2. Update Configuration

In `apiProperties.json`, replace `[YOUR_CANVAS_DEVELOPER_KEY_ID]` with your Client ID.

If your Canvas instance is **not** `canvas.instructure.com`, update these files:

- `apiDefinition.swagger.json` — Change `host` and the OAuth URLs in `securityDefinitions`
- `apiProperties.json` — Change all URLs in `customParameters` (authorizationUrlTemplate, tokenUrlTemplate, refreshUrlTemplate)
- `script.csx` — Change `CANVAS_API_BASE` constant

### 3. Deploy the Connector

```bash
pac connector create --settings-file apiProperties.json --api-definition-file apiDefinition.swagger.json --script-file script.csx
```

### 4. Create a Connection

1. Go to the connector in Power Platform
2. Click **+ New connection**
3. Authenticate with your Canvas credentials
4. The OAuth flow will redirect you to Canvas for authorization

### 5. (Optional) Application Insights

Edit `script.csx` and replace the `APP_INSIGHTS_KEY` placeholder with your instrumentation key.

## MCP Tools (37 Total)

### Core (Read)

| Tool | Description |
|------|-------------|
| `list_courses` | List user's courses with filters for enrollment type and state |
| `get_course` | Get detailed course info including term, teachers, and grading needs |
| `get_my_profile` | Get current user's profile |
| `list_upcoming_events` | List upcoming assignments and calendar events |
| `get_needs_grading` | Get the teacher's TODO list — items needing grading |

### Courses & Users

| Tool | Description |
|------|-------------|
| `list_course_users` | List enrolled users filtered by role (student, teacher, TA, etc.) |
| `list_enrollments` | List enrollments with grades and last activity timestamps |
| `list_sections` | List sections with student counts |
| `list_groups` | List student groups for group work |
| `update_course` | Update course name, dates, syllabus, default view, and time zone |

### Assignments

| Tool | Description |
|------|-------------|
| `list_assignments` | List assignments with filters for bucket (overdue, ungraded, upcoming, etc.) |
| `get_assignment` | Get full assignment details with rubric, dates, and score statistics |
| `create_assignment` | Create a new assignment with name, due date, points, and submission types |
| `update_assignment` | Edit assignment name, due date, points, description, and grading type |
| `publish_assignment` | Publish or unpublish an assignment |
| `list_assignment_groups` | List categories (Homework, Exams) with weight toward final grade |

### Submissions & Grading

| Tool | Description |
|------|-------------|
| `list_submissions` | List all submissions for an assignment with student info and comments |
| `get_submission` | Get a single student's submission with rubric assessment and history |
| `grade_submission` | Grade a submission — set score/grade and add a comment |
| `post_comment` | Add feedback comment without changing the grade |
| `list_missing_submissions` | List past-due assignments a student hasn't submitted |

### Content & Modules

| Tool | Description |
|------|-------------|
| `list_modules` | List course modules with items and content details |
| `list_module_items` | List items within a specific module |
| `create_module` | Create a new module to organize content |
| `list_pages` | List wiki pages in a course |
| `list_files` | List course files with name, size, and download URL |
| `list_rubrics` | List rubrics with criteria, ratings, and point values |

### Communication

| Tool | Description |
|------|-------------|
| `list_announcements` | List announcements for a course with date range filtering |
| `create_announcement` | Post an announcement visible to all enrolled users |
| `list_discussions` | List discussion topics for a course |
| `create_discussion` | Create a new discussion topic |
| `send_message` | Send a conversation to one or more users |

### Analytics & Calendar

| Tool | Description |
|------|-------------|
| `get_course_analytics` | Get assignment statistics or student analytics for a course |
| `get_student_summary` | Get per-student scores and performance across all assignments |
| `list_quizzes` | List quizzes in a course |
| `list_calendar_events` | List calendar events within arbitrary date ranges |

## Example Prompts

- "What assignments need grading in my courses?"
- "Show me the submissions for Assignment 5 in Intro to Biology"
- "Give student 12345 a score of 85 on Assignment 3 with the comment 'Great work!'"
- "Create a new homework assignment due next Friday worth 50 points"
- "Post an announcement to my Chemistry class about the lab schedule change"
- "Which students in Course 789 are missing submissions?"
- "Send a message to all students in my Physics course about the upcoming exam"
- "List all students in my Fall 2026 Physics course"
- "Show me the analytics for my Chemistry course — how are students doing?"
- "What events are on the calendar for my courses next week?"

## Canvas API Notes

- **Authentication**: OAuth 2.0 authorization code flow. Tokens expire in 1 hour; refresh tokens are used automatically.
- **IDs**: Canvas uses 64-bit integer IDs passed as strings. Use `self` for the current user's ID.
- **Pagination**: Tools accept `per_page` (1-100). Canvas uses Link-header pagination; this connector returns one page per call.
- **Rate Limiting**: Canvas applies per-user rate limits. The connector surfaces rate limit errors when they occur.
- **Scoped Keys**: If your Developer Key uses scopes, ensure the required scopes are enabled for each tool's endpoint.

## API Reference

- [Canvas REST API Documentation](https://developerdocs.instructure.com/services/canvas)
- [OAuth2 Guide](https://canvas.instructure.com/doc/api/file.oauth.html)
- [Developer Keys](https://canvas.instructure.com/doc/api/file.developer_keys.html)
