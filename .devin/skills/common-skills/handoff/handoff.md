---
name: handoff
description: >
  Compact current session into a handoff document for the next agent to pick up.
  Save to OS temp dir (not workspace). Include suggested skills section.
  Do not duplicate artifacts already captured (specs, plans, commits, diffs) — reference by path.
  Support --template param for different output formats (multiple handoffs, different audiences).
  Support --back flag for detour return (handoff back to origin session).
  Trigger khi user nói "handoff", "pass to next agent", "compact for handoff", hoặc khi skill caller (tdx-spec, tdx-e2e) reach final step.
argument-hint: "[--template <name|path>] [--back] [session focus description]"
---

# Handoff — Compact Session for Next Agent

Viết handoff document tóm tắt session hiện tại để agent tiếp theo continue work. Lưu vào temp dir của OS, không phải workspace.

Handoff = fork, không phải continue. Mở session mới reference file đó. Bridge 2 chiều: forward (out) và return (--back).

## Template Selection

Parse `--template <name|path>` từ argument:
- **Named template:** load `handoff/templates/<name>.md`. VD: `--template dev-handoff`, `--template pr-review`.
- **Custom path:** load file tại path. VD: `--template ./my-template.md`.
- **No template:** dùng `templates/default.md` (full structure).

### dev-handoff (SDLC phase transition)

`--template dev-handoff` — Handoff giữa các bước SDLC. Dựa trên văn hóa引継ぎ Nhật + Tallyfy 5-section. 9 section: Mục đích, Sản phẩm, Stakeholders, Tri thức ngầm, Giả định, Chênh lệch scope, Verify+confirmation question, Xử lý trouble, Skills. Tham chiếu: `docs/sdlc-handoff-templates.md`.

Template override **Document Structure** only. Rules + Invocation Patterns stay same across templates.

Add template: tạo `handoff/templates/<name>.md` với structure definition. No code change needed.

## Direction

Parse `--back` flag từ argument:

- **Forward (default):** handoff OUT sang next agent. Session hiện tại compact → next agent pick up.
- **Return (`--back`):** handoff BACK sang origin session. Dùng sau detour — findings/learnings mang về thread gốc.

Return handoff filename: `handoff-<TASK_ID>-<template>-back-<timestamp>.md` để distinguish từ forward.

## Output

Save to: `$env:TEMP\handoff-<TASK_ID>-<template>-<timestamp>.md` (PowerShell) hoặc `/tmp/handoff-<TASK_ID>-<template>-<timestamp>.md` (bash).

Filename include template name + direction để distinguish multiple handoffs cho cùng task.

## Smart Zone Guard

Khi session approach token limit (~120k tokens, smart zone boundary) trước khi complete current phase:
- **Don't push on degraded** — model reasoning quality drops near limit.
- **Invoke handoff** để fork sang fresh session.
- Continue work trong new session, reference handoff file.

Auto-trigger: skill caller (tdx-spec, tdx-e2e) detect session length approaching limit → invoke handoff before degrade. Manual: user nói "session too long", "context full", "fork to fresh session".

## Rules

1. **Do not duplicate** content đã capture trong artifacts (specs, plans, commits, diffs, ADRs). Reference bằng path hoặc URL.
2. **Redact** API keys, passwords, PII. List what was redacted.
3. **Suggested skills** phải cụ thể: skill name + khi nào invoke + làm gì.
4. **Tailor theo argument** — phần còn lại sau `--template`/`--back` là session focus description.
5. **Decisions & Resolved Gaps** — capture grilling outcomes, contract revisions, trade-offs. Đây là info mà artifacts không tự contain.
6. **Open Items** — unresolved questions, follow-ups, remaining revision budget.
7. **Template conformance** — output phải khớp template structure. Nếu template yêu cầu section mà không có data → ghi "N/A" thay vì bỏ section.
8. **Return handoff** (`--back`) — focus vào findings/learnings từ detour, không repeat context đã có ở origin session.

## Invocation Patterns

### From tdx-spec (S4 → handoff forward)
Sau khi spec approved, tdx-spec invoke handoff:
- Template: `dev-handoff` (SDLC phase transition)
- Argument: "Dev implement from approved spec"
- Artifacts: `specs/<feature>.md` path
- Suggested skills: `tdx-e2e` (dev implement + E2E + PR)
- Decisions: grilling resolutions, contract scope decisions
- Open Items: contract revision budget (3/3 remaining)

### SDLC phase transition handoff (human-driven)
Handoff giữa các phase SDLC, invoke trực tiếp hoặc từ skill caller:
- Template: `dev-handoff` (generic cho mọi phase transition)
- Confirmation Question: 1 câu hỏi — nếu "no" thì không chuyển phase
- Tribal Knowledge: tri thức ngầm, approach đã reject, stakeholder preferences — thứ không có trong artifact

### From tdx-e2e (S8.4 → handoff forward)
Sau khi PR created + verdict reported, tdx-e2e invoke handoff:
- Template: `pr-review` (default for dev→reviewer) hoặc `follow-up` (default for findings)
- Argument: "Reviewer review PR" hoặc "Follow-up on findings"
- Artifacts: PR URL, commit SHAs, review report path
- Suggested skills: `dev-review-code` (human reviewer), `tdx-spec` (if contract revision needed)
- Decisions: review verdict, fixme flags, routing decisions
- Open Items: follow-up findings, unmerged PR

### Detour return (--back)
Khi dev (tdx-e2e) phát hiện contract sai trong implement → detour qua tdx-spec grilling fast-path → handoff back:
- Flag: `--back`
- Template: `dev-handoff` (reversed direction)
- Argument: "Contract revision findings from implement"
- Artifacts: revised `specs/<feature>.md` path
- Decisions: what changed in contract, why
- Open Items: remaining revision budget (N/3)
- Origin session (tdx-e2e) reference return handoff → continue implement với contract mới

### Smart zone guard
Khi session approach token limit:
- Template: `default` (preserve full context)
- Argument: "Session approaching limit, fork to continue"
- Artifacts: all current artifacts
- Suggested skills: current skill (tdx-spec/tdx-e2e) to resume from handoff
- Open Items: current phase + sub-step to resume from

### Standalone (user invoke directly)
User nói "handoff" mà không từ skill caller:
- Template: `default` (if not specified) hoặc user-provided
- Direction: forward (default) hoặc `--back` if user specify
- Argument: user prompt (sau flags nếu có)
- Tự infer context từ current session
- Suggested skills: infer từ session work
