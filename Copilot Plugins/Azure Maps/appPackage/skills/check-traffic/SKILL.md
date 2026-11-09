---
name: check-traffic
description: See traffic incidents, road closures and current road speeds around a place. Use when asked about accidents, closures, construction or how bad traffic is.
---

# Check Traffic

## When to use this skill

- "Any accidents around Seattle?", "is I-5 backed up?", "what roads are closed near the stadium?"

## How to do it

1. For incidents call `get_traffic_incidents` with `location` and `radiusKm` (default 10), or a `bbox`. The area must be under 10,000 square kilometers. Set `incidentTypes` to narrow to `Accident`, `Congestion`, `RoadHazard`, `Construction`, `DisabledVehicle`, `Weather` or `Miscellaneous`.
2. For the speed on one road call `get_traffic_flow_segment` with a `location` on or near it. Choose `zoom` by road class: low for highways, high for local streets.
3. Report closures and the worst incidents first, with the road and the delay.
4. To plan around traffic, use the plan-a-route or best-time-to-leave skill.

## What to watch for

- Coverage varies by country. If nothing is returned, say traffic data may not be available there.
- In clients that support MCP Apps, a live traffic map shows the incidents and lets the user tap a road for its speed.
