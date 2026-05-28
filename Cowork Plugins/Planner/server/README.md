# Planner Cowork MCP Server

ASP.NET Core MCP server for the Planner Cowork plugin.

## Endpoints

- POST `/mcp/full`
- POST `/mcp/federated`
- GET `/health/live`
- GET `/health/ready`
- GET `/status`

## Implemented tools

- list_group_plans
- list_plan_tasks
- list_plan_buckets
- list_my_tasks
- list_my_private_tasks
- list_my_personal_tasks
- my_personal_tasks_weekly_delta
- list_user_tasks
- get_task
- get_task_details
- create_task
- update_task
- update_task_details
- plan_health_summary

## Notes

- Uses user delegated bearer token from `Authorization` header.
- Write tools require caller-provided `etag` for `If-Match` concurrency.
- Graph base is `https://graph.microsoft.com/v1.0/`.
