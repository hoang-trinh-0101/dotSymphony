# Template: default

Full handoff structure. Use when no specific audience or format required.

```markdown
# Handoff — <TASK_ID or session name>

## Context
- WORKTREE_PATH: <path>
- WORKSPACE: <CIS|SEE>
- BRANCH_NAME: <branch>
- TASK_ID: <id>
- Session focus: <from argument, or inferred from last task>

## Artifacts (reference, do not duplicate)
- Approved spec: <path or URL>
- Commits: <SHA list or git log ref>
- Diffs: <path or `git diff base..HEAD`>
- Plans/ADRs: <path>

## Decisions & Resolved Gaps
- <decision 1: what + why>
- <grilling resolution 1: question + answer>
- ...

## Suggested Skills
- <skill name>: <when to invoke, what for>

## Open Items
- <unresolved question>
- <follow-up task>
- <contract revision count remaining: N/3>

## Sensitive Info
- Redacted. <list what was redacted>
```
