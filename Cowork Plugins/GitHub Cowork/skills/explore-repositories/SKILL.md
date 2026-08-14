---
name: explore-repositories
description: |
  Finds and explores GitHub repositories, code, files, commits, branches, tags,
  and releases. Use when the user asks to "find the repo for", "search GitHub for",
  "show me the code that", "what's in the repo", "read the file", "who owns",
  "what changed recently in", "list the branches", "what's the latest release of",
  or names a GitHub repository and asks a question about its contents.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: discovery
---

# Explore GitHub Repositories

## What This Skill Does

Finds repositories, reads source files, searches code across an organization,
and summarizes recent commit activity — without the user needing to know
GitHub search syntax or navigate the web UI.

## When to Activate

- User is looking for a repository by name, topic, owner, or purpose
- User wants to read or summarize a specific file (README, config, workflow)
- User asks where a function, string, config key, or dependency is used
- User asks what changed recently, who committed it, or which branch it is on
- User asks about releases, tags, or version history

## Workflow

1. **Resolve the repository.** Most tools need `owner` and `repo`. If the user
   gives a full name (`github/github-mcp-server`), split it. If they give a
   URL, parse it. If they give only a partial name or a description, use
   `search_repositories` first and confirm the match before continuing.

   > `search_repositories(query: "org:contoso mcp server")`

2. **Pick the narrowest tool for the question.**

   | User is asking about | Tool |
   |---|---|
   | Which repo | `search_repositories` |
   | Where code lives | `search_code` |
   | A specific file or folder | `get_file_contents` |
   | Recent history | `list_commits`, then `get_commit` for a specific SHA |
   | A commit message or author | `search_commits` |
   | Branches / tags | `list_branches`, `list_tags`, `get_tag` |
   | Versions | `list_releases`, `get_latest_release`, `get_release_by_tag` |
   | Who can contribute | `list_repository_collaborators` |
   | A person or org member | `search_users`, `get_teams`, `get_team_members` |

3. **Read before summarizing.** When `search_code` returns hits, follow up with
   `get_file_contents` on the most relevant one or two files rather than
   summarizing from the snippet alone.

   > `get_file_contents(owner: "contoso", repo: "billing-api", path: "src/config/defaults.ts")`

4. **Answer the question, don't dump the data.** The user asked something
   specific. Lead with the answer, then show the supporting evidence and a
   link to the file or commit on github.com.

5. **Offer the natural next step.** "Want me to open an issue about this?",
   "Should I check whether a PR already changes this?", "I can summarize what
   shipped in the last release."

## Output Format

### For a repository lookup

**contoso/billing-api** — Private · TypeScript · Updated 3 days ago

Billing and invoicing service for the Contoso platform.

- **Default branch:** `main`
- **Latest release:** v4.2.0 (Aug 2, 2026)
- **Open issues:** 23 · **Open PRs:** 5

### For a code search

Found `MAX_RETRY_COUNT` in 3 places in **contoso/billing-api**:

| File | Line | Context |
|---|---|---|
| [src/config/defaults.ts](https://github.com/contoso/billing-api/blob/main/src/config/defaults.ts) | 14 | Defined as `5` |
| [src/http/client.ts](https://github.com/contoso/billing-api/blob/main/src/http/client.ts) | 88 | Passed to the retry policy |
| [test/client.test.ts](https://github.com/contoso/billing-api/blob/main/test/client.test.ts) | 31 | Overridden to `1` in tests |

The value is set once in `defaults.ts` — that is the place to change it.

### For recent activity

**contoso/billing-api** — last 5 commits on `main`

| When | Author | Message |
|---|---|---|
| 2 hours ago | jchen | Fix rounding on partial refunds (#412) |
| Yesterday | mpatel | Bump stripe SDK to 14.2 (#409) |

## Handling Edge Cases

- **Repo not found:** It may be private and outside the user's access, or the
  name may be wrong. Say which is more likely and offer to search by keyword
  instead of exact name.
- **Ambiguous repo name:** If `search_repositories` returns several plausible
  matches, list the top 3 with owner and description and ask which one — do
  not guess.
- **Large files:** `get_file_contents` on a very large file returns a lot of
  text. Summarize rather than reproducing it, and quote only the relevant
  section.
- **Directory vs. file:** `get_file_contents` with a folder path returns a
  listing. Use that to orient before reading a specific file.
- **Search returns nothing:** GitHub code search only indexes default branches
  and may exclude forks and very large files. Say so, and suggest searching
  commits or issues instead.
- **Rate limiting:** If GitHub returns a rate limit error, tell the user
  plainly and suggest narrowing the search rather than silently retrying.

## Handling Authentication

If a tool call fails because the user has not connected to GitHub yet:

1. Tell the user: "I need to connect to GitHub to look that up. You should see
   a sign-in prompt — please complete it and I'll try again."
2. Do NOT retry the tool call immediately. Wait for the user to confirm they
   have signed in.
3. If the user is already connected but gets a 401 or 403, the likely cause is
   scope rather than expiry: "That repository may be outside what this
   connection is authorized for. Try disconnecting and reconnecting GitHub
   from Sources & Skills, and make sure the right organization is approved."
4. Organization-owned repositories can require an org admin to approve the
   GitHub OAuth app. If access is denied for an org repo specifically, say
   that and suggest the user ask their GitHub org admin.

## Additional Resources

- **`references/github-search-syntax.md`** — Qualifiers for `search_repositories`,
  `search_code`, `search_commits`, `search_issues`, and `search_pull_requests`
