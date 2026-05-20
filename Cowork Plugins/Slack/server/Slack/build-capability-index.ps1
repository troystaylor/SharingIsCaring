# Build / refresh Slack capability index.
#
# TODO: scrape https://api.slack.com/methods into Slack/CapabilityIndex.json.
# Slack does not publish a single machine-readable manifest of all Web API
# methods, so the recommended approach is:
#
#   1. Pull https://api.slack.com/methods and the HTML lists at
#      https://api.slack.com/methods?filter=all (paginated by domain).
#   2. For each method link, fetch the page, extract:
#         - method name (title)
#         - one-line description (first paragraph)
#         - required scopes (from the OAuth scopes table)
#   3. Compose a `keywords` array — generally tokenize the description and
#      drop common words.
#   4. Emit Slack/CapabilityIndex.json sorted by domain then method name.
#
# The committed index is a curated representative set spanning every domain
# called out in the spec. Re-run this once Slack ships an OpenAPI spec we
# can consume directly (https://github.com/slackapi/slack-api-specs).

[CmdletBinding()]
param(
    [string]$Out = "$PSScriptRoot/CapabilityIndex.json"
)

Write-Host "TODO: implement scraper. Out -> $Out"
exit 1
