---
name: common-skills
description: >
  Single-point access cho cross-role utilities. Router — load companion content theo intent.
  Components: handoff (compact session cho next agent). Thêm component = thêm row vào routing table.
  Trigger khi user nói "handoff", "pass to next agent", "compact session", hoặc khi skill caller (tdx-spec, tdx-e2e) reach handoff step.
---

# Common Skills — Router

Một skill chính, load companion content theo intent. Companion files trong `common-skills/<component>/`.

## Routing

| Intent trong prompt | Companion file | Mô tả |
|---|---|---|
| "handoff", "pass to next agent", "compact session", "pass to dev", "pass to reviewer" | `handoff/handoff.md` | Compact session thành handoff doc cho agent tiếp theo |
| "common-skills help", "what components" | — (inline) | List components + when to use |

### Quy trình routing

1. **Đọc prompt.** Tìm keywords trong Bảng Routing.
2. **Nếu khớp:** load companion content, follow instructions, output artifact.
3. **Nếu không khớp:** KHÔNG invoke. Báo user "no component matched".
4. **Thông báo** sẽ route đến (một dòng), rồi load companion content.

## Components

### handoff
Compact current session thành document cho agent tiếp theo. Save ra OS temp dir (không workspace). Include: context, artifact references, decisions, suggested skills, open items. Redact sensitive info.

**Used by:**
- `tdx-spec` S4 → handoff to dev (tdx-e2e)
- `tdx-e2e` S8.4 → handoff to reviewer / follow-up
- Standalone: user invoke directly

## Adding new components

1. Tạo `common-skills/<name>/<name>.md` (companion content).
2. Add row vào Bảng Routing.
3. Add section dưới `## Components`.
4. Sync sang `.devin/skills/common-skills/`.

## Fallback

Companion content không load được → run inline: `read`, `write`, `exec`.
3 retries fail → escalate to human.
