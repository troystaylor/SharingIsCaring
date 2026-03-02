# Excel Online MCP

MCP-enabled connector for Excel Online (Microsoft Graph). This connector exposes workbook, worksheet, range, and table operations as MCP tools so Copilot Studio can orchestrate Excel changes with natural language.

## Why this works

Microsoft Graph exposes Excel workbook operations as discrete REST endpoints. Each MCP tool maps directly to one Graph endpoint, allowing Copilot Studio to chain tools without pre-built flows. By default, the tools run sessionless (atomic calls). Optional session support is available for performance.

## Prerequisites

- Entra app registration with delegated permissions:
  - Files.ReadWrite
  - Sites.ReadWrite.All
- Graph API access to the target workbook

## Authentication

- Identity provider: Azure Active Directory
- Resource URL: https://graph.microsoft.com
- Scope: Files.ReadWrite Sites.ReadWrite.All
- On-behalf-of (delegated): Enabled

## Tools

- orchestrate_core: Tier 1 enum-driven orchestration for worksheet/range operations.
- orchestrate_data: Tier 2 enum-driven orchestration for table/data operations.
- orchestrate_formulas: Tier 3 enum-driven orchestration for formulas/calculation.
- orchestrate_formatting: Tier 4 enum-driven orchestration for formatting/layout.
- orchestrate_charts: Tier 5 enum-driven orchestration for charts/visualizations.
- orchestrate_named_items: Tier 6 enum-driven orchestration for named item operations.
- orchestrate_advanced_features: Tier 7 enum-driven orchestration for validation/conditional format/pivot operations.
- orchestrate_governance: Tier 8 enum-driven orchestration for workbook governance/protection operations.
- orchestrate_bulk_operations: Tier 9 enum-driven orchestration for bulk and multi-sheet operations.
- orchestrate_enterprise: Tier 10 enum-driven orchestration for enterprise patterns and safe wrappers.
- orchestrate_comments: Tier 11 enum-driven orchestration for comments and annotations.
- orchestrate_metadata: Tier 12 enum-driven orchestration for properties and metadata.
- orchestrate_images: Tier 13 enum-driven orchestration for images and media.
- orchestrate_functions: Tier 14 enum-driven orchestration for function reference and validation.
- orchestrate_filtering: Tier 15 enum-driven orchestration for advanced filtering.
- orchestrate_arrays: Tier 16 enum-driven orchestration for array formulas and dynamic ranges.
- orchestrate_insights: Tier 17 enum-driven orchestration for workbook analysis and insights.
- list_worksheets: List worksheets in a workbook.
- list_tables: List workbook tables (or worksheet-specific tables).
- get_table_rows: Retrieve table rows with optional row limit.
- get_range: Read a range from a worksheet.
- update_range: Write values into a range.
- clear_range: Clear contents/formatting from a range.
- get_used_range: Retrieve the used range in a worksheet.
- add_table_rows: Append rows to a table.
- create_table: Create a table from a worksheet range.
- create_session: Create a workbook session (optional).
- close_session: Close a workbook session.
- discover_excel_graph: Discover Excel endpoints from Microsoft Learn.
- invoke_excel_graph: Invoke any Graph Excel endpoint (advanced).
- batch_invoke_excel_graph: Batch invoke up to 20 Excel endpoints.

## Tiered orchestration model

We are moving to `orchestrate_*` tools with enum-based `action` values to stay within Copilot Studio tool limits.

Tier 1 is available now in `orchestrate_core` with actions:

- list_worksheets
- get_worksheet
- add_worksheet
- rename_worksheet
- delete_worksheet
- copy_worksheet
- freeze_panes
- unfreeze_panes
- get_used_range
- get_range
- update_range
- clear_range

Next tiers can be added by extending action enums one tier at a time without adding many new tools.

Tier 2 is available now in `orchestrate_data` with actions:

- list_tables
- get_table_rows
- create_table
- delete_table
- add_table_rows
- update_table_row
- delete_table_row
- sort_range
- apply_filter
- clear_filter
- remove_duplicates

Tier 3 is available now in `orchestrate_formulas` with actions:

- set_formulas
- replace_formula
- copy_formula_down
- calculate_range
- recalculate_workbook

Tier 4 is available now in `orchestrate_formatting` with actions:

- set_number_format
- set_font
- set_fill
- set_borders
- set_alignment
- set_wrap_text
- set_column_width
- set_row_height
- autofit_columns
- autofit_rows

Tier 5 is available now in `orchestrate_charts` with actions:

- list_charts
- get_chart
- add_chart
- update_chart
- delete_chart
- set_chart_title
- set_chart_legend
- set_chart_axes
- get_chart_image

Tier 6 is available now in `orchestrate_named_items` with actions:

- list_workbook_named_items
- get_workbook_named_item
- add_workbook_named_item
- delete_workbook_named_item
- list_worksheet_named_items
- get_worksheet_named_item
- add_worksheet_named_item
- delete_worksheet_named_item
- set_named_item_comment
- get_named_item_range

Tier 7 is available now in `orchestrate_advanced_features` with actions:

- get_data_validation
- set_data_validation_rule
- set_data_validation_prompt
- set_data_validation_error_alert
- clear_data_validation
- list_conditional_formats
- add_conditional_format
- get_conditional_format
- delete_conditional_format
- list_pivot_tables
- add_pivot_table
- refresh_pivot_table
- delete_pivot_table

Tier 8 is available now in `orchestrate_governance` with actions:

- create_session
- close_session
- calculate_workbook
- get_workbook_application
- get_worksheet_protection
- protect_worksheet
- unprotect_worksheet

Tier 9 is available now in `orchestrate_bulk_operations` with actions:

- bulk_update_values
- bulk_clear_ranges
- bulk_apply_format
- copy_across_sheets
- aggregate_ranges
- merge_data_from_sheets

Tier 10 is available now in `orchestrate_enterprise` with actions:

- safe_invoke
- safe_batch_invoke
- discover_with_limits
- check_permissions

Tier 11 is available now in `orchestrate_comments` with actions:

- list_comments
- get_comment
- add_comment
- delete_comment
- reply_to_comment

Tier 12 is available now in `orchestrate_metadata` with actions:

- get_workbook_properties
- set_workbook_properties
- get_worksheet_properties
- set_worksheet_properties
- list_workbook_functions

Tier 13 is available now in `orchestrate_images` with actions:

- list_worksheet_images
- get_image
- add_image
- delete_image
- set_image_properties

Tier 14 is available now in `orchestrate_functions` with actions:

- list_workbook_functions
- get_function_details
- validate_formula_syntax
- suggest_functions

Tier 15 is available now in `orchestrate_filtering` with actions:

- apply_advanced_filter
- create_filter_view
- get_filter_state
- suggest_filters
- export_filtered_data

Tier 16 is available now in `orchestrate_arrays` with actions:

- set_dynamic_array_formula
- list_dynamic_ranges
- create_implicit_range
- get_range_dependencies
- recalculate_dependent_cells

Tier 17 is available now in `orchestrate_insights` with actions:

- get_workbook_stats
- analyze_performance
- get_change_history
- audit_workbook
- suggest_optimizations

## Orchestration layer

This connector includes a Graph orchestration layer similar to Graph Power Orchestration:

- Dynamic discovery via MS Learn MCP for Excel-related Graph endpoints.
- Generic invoke and batch tools for advanced scenarios.
- Permission hints based on discovered endpoints.

## MS Learn guardrails

The discovery path includes explicit guardrails:

- MS Learn endpoint allowlist: only `https://learn.microsoft.com/api/mcp` is allowed.
- Tool allowlist: only `microsoft_docs_search` is invoked.
- Input validation: query length limits and allowlisted categories.
- Output bounds: response/chunk size limits to reduce oversized payload risk.
- Trusted source filtering: only `https://learn.microsoft.com` Graph documentation URLs are processed.
- Excel scope filtering: extracted endpoints must be Graph-shaped and Excel-related (`/workbook`, `/worksheets`, `/tables`, `/range`, `/charts`).
- Invoke guardrails: generic invoke tools only allow Microsoft Graph Excel workbook endpoints.

## Workbook identification

Tools accept:
- itemId (required): Drive item ID for the workbook
- driveId (optional): Drive ID. If omitted, /me/drive is used.

## Session support

By default, tools are sessionless. To use sessions:
1. Call create_session to get a sessionId.
2. Pass sessionId to subsequent tools.
3. Call close_session when done.

## Setup

1. Create a new custom connector in Power Platform.
2. Import apiDefinition.swagger.json.
3. Configure OAuth using the Entra app (delegated).
4. Optional: Enable "Use Microsoft Graph beta" if needed.
5. Test tools in Copilot Studio.

## Notes

- Sessionless calls persist changes immediately.
- Use sessions for multi-step edits to improve performance.
- Orchestration tools are powerful; prefer explicit tools when possible.
