---
name: evidence-and-uncertainty
description: Rules for evidence-backed answers - every claim cites a source, restricted or missing content is stated, owners and numbers are never guessed, and conflicts are shown rather than resolved silently. Use for any answer built from Jira, Confluence, Bitbucket or other connector data.
---

# Evidence and Uncertainty

## Rules

1. **Cite every claim.** Each factual statement, number, owner, date or status carries a source in the format from `atlassian-access`. If you cannot cite it, do not state it as fact.
2. **Separate fact from inference.** Label inferences ("likely", "inferred from the status history") and say what they are based on.
3. **State what you could not see.** If a page, issue, comment or repository was restricted, missing, archived or returned empty, say so by name. Do not fill the gap from general knowledge, and do not imply completeness.
4. **Never guess an owner.** If ownership is not stated in a field, page property, CODEOWNERS file or comment, write `Owner unknown`. Do not infer it from who last edited a page.
5. **Numbers come from tool output only.** Copy counts, dates and versions from results. Do not round, extrapolate or compute from memory. Show the query behind each number.
6. **Show conflicts.** When two sources disagree, present both with citations, say which is newer, and do not pick a winner unless one is clearly authoritative (for example an approved page over a comment).
7. **Flag staleness.** Mark content older than the user's threshold (default: a year) or in draft status as possibly outdated.
8. **Scope honestly.** Say which products, projects, spaces and date windows were searched. "Nothing found" means "nothing found in X using Y".
9. **Confidence.** Give a short confidence level (high, medium, low) for recommendations and the one fact that would change it.
10. **Do not leak.** Quote only what is needed. Do not repeat secrets, tokens or personal data found in tickets or logs; redact them.

## Output footer

End with:

- **Sources**: the cited items.
- **Not checked / not available**: anything out of scope, restricted or unavailable.
- **Assumptions**: anything you assumed.

Apply this skill together with `atlassian-access`.
