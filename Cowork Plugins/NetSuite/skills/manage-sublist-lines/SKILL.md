---
name: manage-sublist-lines
description: |
  Add, update, or remove sublist lines (e.g., items on a sales order,
  addresses on a customer) in NetSuite. Use when the user asks "add a line
  item to SO [id]", "remove line [n] from [record]", "change the quantity
  on line [n]", "edit address book entry on customer [id]", or "manage
  sublist on NetSuite record".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: action
cowork.category: Finance
cowork.icon: Layers
---

# Manage Sublist Lines

1. Identify `recordType`, `recordId`, and `sublistId` (common: `item`, `addressBook`, `contactRoles`).
2. Call `get_sublist` to show current lines and capture line ids.
3. For each operation:
   - **Add**: call `add_sublist_line` with the new line's `fields`.
   - **Update**: call `update_sublist_line` with the `lineId` and changed `fields` only.
   - **Delete**: call `delete_sublist_line` with the `lineId`.
4. Always echo the proposed change and ask for explicit confirmation before each write.
5. After writes, re-fetch with `get_sublist` to confirm state.
