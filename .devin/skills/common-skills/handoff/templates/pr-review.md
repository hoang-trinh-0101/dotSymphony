# Template: pr-review

Handoff từ Dev (tdx-e2e) sang Reviewer. Focus: PR + evidence + review context.

```markdown
# Handoff to Reviewer — <TASK_ID>

## PR
- PR URL: <url>
- Branch: <branch> → <base>
- Commits: <SHA list or `git log base..HEAD --oneline`>

## Evidence
- Unit tests: <pass count>
- E2E tests: <pass count, fixme/skip list if any>
- Review report: <path or N/A>

## Verdict
- Self-review: <pass|fail with findings>
- Fixme flags: <list or none>

## Suggested Skills
- dev-review-code: review branch diff (--source <base> --target <branch>)
- tdx-spec: if contract revision needed (back to S2)

## Open Items
- <follow-up finding>
- <unmerged PR — user decide merge>

## Sensitive Info
- Redacted. <list>
```
