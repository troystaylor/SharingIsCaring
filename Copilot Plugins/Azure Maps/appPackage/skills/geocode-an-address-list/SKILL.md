---
name: geocode-an-address-list
description: Turn a list of addresses into coordinates, or a list of coordinates into addresses, and check the match quality. Use when asked to geocode, clean or map a list of addresses.
---

# Geocode an Address List

## When to use this skill

- "Geocode these 40 addresses", "which of these addresses didn't match", "what are the addresses for these coordinates".

## How to do it

1. For addresses call `batch_geocode` with `addresses` (up to 100 per call). Use `items` instead when the parts are already separate (street, city, state, postal code, country).
2. For coordinates call `batch_reverse_geocode` with `points`.
3. Report how many matched with High, Medium and Low confidence and list the rows that need review. Don't treat Low confidence as correct.
4. For more than 100 rows, split into batches.

## What to watch for

- A match can be a larger area, such as the city, when the street isn't found. Check the `type` and `confidence`.
- In clients that support MCP Apps, a table shows the results with a map, lets the user fix rows and download a CSV.
- Don't change an address to make it match without telling the user.
