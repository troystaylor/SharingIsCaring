# Microsoft Planner

## Overview

A custom connector for the Microsoft Graph Planner API (beta) that exposes all Planner operations — plans, tasks, buckets, rosters, and business scenarios (preview) — as MCP tools for use in Copilot Studio agents.

## Prerequisites

- Power Platform environment with a custom connector license
- Microsoft 365 account with Planner access
- Azure AD app registration with the following permissions:
  - **Tasks.ReadWrite** — Read and write Planner tasks
  - **Group.ReadWrite.All** — Manage group plans
  - **BusinessScenarioConfig.ReadWrite.OwnedBy** — Configure business scenarios
  - **BusinessScenarioData.ReadWrite.OwnedBy** — Manage business scenario data
  - **offline_access** — Maintain refresh tokens

## Setup

1. Register an Azure AD application at [https://portal.azure.com](https://portal.azure.com)
2. Add the required API permissions (Microsoft Graph, delegated)
3. Grant admin consent if needed (especially for business scenario permissions)
4. Update `apiProperties.json` with your client ID
5. Deploy using PAC CLI:
   ```
   paconn create -s Planner --api-def apiDefinition.swagger.json --api-prop apiProperties.json --script script.csx
   ```

## Operations

### Plans
| Operation | Operation ID | Description |
|-----------|-------------|-------------|
| List Group Plans | ListGroupPlans | Get all plans for a Microsoft 365 group |
| Get Plan | GetPlan | Get a specific plan by ID |
| Create Plan | CreatePlan | Create a new plan in a group or roster |
| Update Plan | UpdatePlan | Update a plan title (requires ETag) |
| Delete Plan | DeletePlan | Delete a plan (requires ETag) |
| Get Plan Details | GetPlanDetails | Get category labels and shared-with info |
| Update Plan Details | UpdatePlanDetails | Update category labels (requires ETag) |
| List Plan Buckets | ListPlanBuckets | Get all buckets in a plan |
| List Plan Tasks | ListPlanTasks | Get all tasks in a plan |
| Archive Plan | ArchivePlan | Archive a plan to make it read-only (requires ETag) |
| Unarchive Plan | UnarchivePlan | Unarchive a previously archived plan (requires ETag) |
| Move Plan to Container | MovePlanToContainer | Move a plan to a different container (e.g., user to group) |
| List My Plans | ListMyPlans | Get all plans for the current user |
| List Favorite Plans | ListFavoritePlans | Get plans marked as favorites |
| List Recent Plans | ListRecentPlans | Get recently viewed plans |

### Tasks
| Operation | Operation ID | Description |
|-----------|-------------|-------------|
| Get Task | GetTask | Get a specific task by ID |
| Create Task | CreateTask | Create a new task in a plan |
| Update Task | UpdateTask | Update task properties (requires ETag) |
| Delete Task | DeleteTask | Delete a task (requires ETag) |
| Get Task Details | GetTaskDetails | Get task description, checklist, references |
| Update Task Details | UpdateTaskDetails | Update task description (requires ETag) |
| List My Tasks | ListMyTasks | Get tasks assigned to current user |
| List My Day Tasks | ListMyDayTasks | Get tasks in the current user's My Day view |
| Get Assigned-To Board Format | GetAssignedToTaskBoardFormat | Get task board format for Assigned To view |
| Update Assigned-To Board Format | UpdateAssignedToTaskBoardFormat | Update task board format for Assigned To view |
| Get Progress Board Format | GetProgressTaskBoardFormat | Get task board format for Progress view |
| Update Progress Board Format | UpdateProgressTaskBoardFormat | Update task board format for Progress view |
| Get Bucket Board Format | GetBucketTaskBoardFormat | Get task board format for Bucket view |
| Update Bucket Board Format | UpdateBucketTaskBoardFormat | Update task board format for Bucket view |
| Get Task Delta | GetTaskDelta | Get incremental task changes via delta query |

### Buckets
| Operation | Operation ID | Description |
|-----------|-------------|-------------|
| Get Bucket | GetBucket | Get a specific bucket by ID |
| Create Bucket | CreateBucket | Create a new bucket in a plan |
| Update Bucket | UpdateBucket | Update bucket name/order (requires ETag) |
| Delete Bucket | DeleteBucket | Delete a bucket (requires ETag) |
| List Bucket Tasks | ListBucketTasks | Get all tasks in a bucket |

### Rosters
| Operation | Operation ID | Description |
|-----------|-------------|-------------|
| Create Roster | CreateRoster | Create a new roster (non-group container) |
| Get Roster | GetRoster | Get a specific roster by ID |
| Delete Roster | DeleteRoster | Delete a roster |
| List Roster Members | ListRosterMembers | Get all members of a roster |
| Add Roster Member | AddRosterMember | Add a member to a roster |
| Remove Roster Member | RemoveRosterMember | Remove a member from a roster |
| List Roster Plans | ListRosterPlans | Get all plans in a roster |

### Business Scenarios (Preview)
| Operation | Operation ID | Description |
|-----------|-------------|-------------|
| List Business Scenarios | ListBusinessScenarios | Get all scenarios in the tenant |
| Get Business Scenario | GetBusinessScenario | Get a specific scenario by ID |
| Create Business Scenario | CreateBusinessScenario | Create a new business scenario |
| Update Business Scenario | UpdateBusinessScenario | Update scenario name/owners |
| Delete Business Scenario | DeleteBusinessScenario | Delete a scenario and all data |
| Get Scenario Planner | GetScenarioPlanner | Get Planner config for a scenario |
| Get Scenario Plan | GetScenarioPlan | Get the plan for a scenario + group |
| List Scenario Tasks | ListScenarioTasks | Get all tasks for a scenario |
| Create Scenario Task | CreateScenarioTask | Create a task for a scenario |
| Get Plan Configuration | GetPlanConfiguration | Get plan config (buckets, localization) |
| Get Task Configuration | GetTaskConfiguration | Get task config (edit policies) |

## MCP Tools (54 total)

| # | Tool Name | Description |
|---|-----------|-------------|
| 1 | list_group_plans | Get all Planner plans for a Microsoft 365 group |
| 2 | get_plan | Get a specific Planner plan by ID |
| 3 | create_plan | Create a new Planner plan in a group or roster |
| 4 | update_plan | Update a Planner plan title |
| 5 | delete_plan | Delete a Planner plan |
| 6 | get_plan_details | Get plan details including category labels |
| 7 | update_plan_details | Update plan category labels |
| 8 | list_plan_buckets | Get all buckets in a plan |
| 9 | list_plan_tasks | Get all tasks in a plan |
| 10 | get_task | Get a specific Planner task by ID |
| 11 | create_task | Create a new Planner task |
| 12 | update_task | Update a Planner task |
| 13 | complete_task | Mark a task as completed |
| 14 | delete_task | Delete a Planner task |
| 15 | get_task_details | Get task description, checklist, references |
| 16 | update_task_details | Update task description and preview type |
| 17 | list_my_tasks | Get tasks assigned to current user |
| 18 | get_bucket | Get a specific bucket by ID |
| 19 | create_bucket | Create a new bucket in a plan |
| 20 | update_bucket | Update a bucket name or order |
| 21 | delete_bucket | Delete a Planner bucket |
| 22 | list_bucket_tasks | Get all tasks in a bucket |
| 23 | create_roster | Create a new Planner roster |
| 24 | get_roster | Get a specific roster by ID |
| 25 | delete_roster | Delete a Planner roster |
| 26 | list_roster_members | Get all members of a roster |
| 27 | add_roster_member | Add a member to a roster |
| 28 | remove_roster_member | Remove a member from a roster |
| 29 | list_roster_plans | Get all plans in a roster |
| 30 | list_business_scenarios | Get all business scenarios |
| 31 | get_business_scenario | Get a specific business scenario |
| 32 | create_business_scenario | Create a new business scenario |
| 33 | update_business_scenario | Update a business scenario |
| 34 | delete_business_scenario | Delete a business scenario |
| 35 | get_scenario_planner | Get Planner config for a scenario |
| 36 | get_scenario_plan | Get the plan for a scenario + group |
| 37 | list_scenario_tasks | Get all tasks for a scenario |
| 38 | create_scenario_task | Create a task for a scenario |
| 39 | get_plan_configuration | Get plan configuration for a scenario |
| 40 | get_task_configuration | Get task configuration for a scenario |
| 41 | archive_plan | Archive a plan to make it read-only |
| 42 | unarchive_plan | Unarchive a previously archived plan |
| 43 | move_plan_to_container | Move a plan to a different container |
| 44 | list_my_plans | Get all plans for the current user |
| 45 | list_favorite_plans | Get plans marked as favorites |
| 46 | list_recent_plans | Get recently viewed plans |
| 47 | list_my_day_tasks | Get tasks in the user's My Day view |
| 48 | get_assigned_to_board_format | Get Assigned To task board format |
| 49 | update_assigned_to_board_format | Update Assigned To task board format |
| 50 | get_progress_board_format | Get Progress task board format |
| 51 | update_progress_board_format | Update Progress task board format |
| 52 | get_bucket_board_format | Get Bucket task board format |
| 53 | update_bucket_board_format | Update Bucket task board format |
| 54 | get_task_delta | Get incremental task changes via delta query |

## Important Notes

### API Versioning
This connector uses **Microsoft Graph v1.0** for stable, generally available operations (plans, tasks, buckets, board formats, my tasks, my plans) and **Microsoft Graph beta** for preview-only features (rosters, business scenarios, archive/unarchive, move plan, favorite plans, recent plans, my day tasks, and delta queries). Beta endpoints may change without notice.

### ETag Concurrency Control
Planner uses ETags (`@odata.etag`) for optimistic concurrency control on update and delete operations. You must first **read** the item (using get_plan, get_task, get_bucket, etc.) to obtain its current `@odata.etag` value, then pass that value as the `etag` parameter when updating or deleting.

### Priority Values
| Range | Label |
|-------|-------|
| 0-1 | Urgent |
| 2-4 | Important |
| 5-7 | Medium |
| 8-10 | Low |

### Task Completion
Set `percentComplete` to `100` to mark a task as complete, or use the dedicated `complete_task` tool. Set to `0` to reopen.

### Business Scenarios (Preview)
Business scenarios are a preview feature that allows applications to create and manage Planner tasks with custom policies. They require additional permissions (`BusinessScenarioConfig.ReadWrite.OwnedBy` and `BusinessScenarioData.ReadWrite.OwnedBy`) and admin consent may be required.

## Author
- **Name**: Troy Taylor
- **Email**: troy@troystaylor.com
- **GitHub**: https://github.com/troystaylor
