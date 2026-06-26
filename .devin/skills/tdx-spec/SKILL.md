---
name: tdx-spec
description: >
  BrSE/QA skill: fetch task → explore app → generate test case (contract) → review → approve.
  Output: approved spec markdown, frozen contract, handoff-ready for dev (tdx-e2e).
  Trigger khi BrSE/QA cần sinh spec từ task, hoặc khi user cung cấp tdx-plane link / task prompt cho contract phase.
---

# TDX Spec — Contract Pipeline (BrSE/QA)

Sinh test case (contract) từ task spec. Output là approved `specs/<feature>.md` — frozen contract, handoff-ready cho dev qua `tdx-e2e`.

**Không implement code.** Không generate E2E spec.ts. Không tạo PR. Chỉ sinh contract.

## Context Contract (từ init-worktree)

Khi được trigger bởi `init-worktree`, nhận 4 biến:
```
WORKTREE_PATH=<worktree path>     # VD: D:/Work/bfe-aio-worktrees/SEE-4561-invoice-parens
WORKSPACE=<CIS|SEE>
BRANCH_NAME=<branch>
TASK_ID=<BLUEF-xxxx hoặc BLUF-xxxx>
```

**Bắt buộc làm việc trong `WORKTREE_PATH`**, không phải main worktree.
- Tất cả `git -C`, file edit phải dùng `WORKTREE_PATH`.
- Không bao giờ `cd` vào main worktree (`<ROOT>/<WORKSPACE>/`).
- Nếu trigger trực tiếp (không qua init-worktree) → hỏi user worktree path, hoặc trigger init-worktree trước.

## Use Case Routing

| Use case | Steps | Companion skills to load |
|---|---|---|
| Feature spec | S1 → S2 → S3 → S4 | `test-planner`, `test-plan-reviewer` |
| Bug repro spec | S1 → S2-bug | `test-planner` |
| Investigate only | S1 → S2.1-S2.2 only | none |

New use case? Add row here.

## Steps (detailed)

Mỗi sub-step có output cụ thể để validate trước khi qua step tiếp theo.

### S1: Fetch & Analyze

| Sub | Việc | Output validate | Gate |
|---|---|---|---|
| P1.1 | Fetch task (tdx-plane MCP hoặc prompt) | task object: `{id, title, desc, type, labels, azure_devops_id}` | — |
| P1.2 | Classify type | `{type: bug\|feature\|investigate}` | ✅ nếu uncertain |
| P1.3 | Scope | `{scope: server\|client}` | — |
| P1.4 | Summary cho user | 1 đoạn text | — |

### S2: Test Case / Contract

Test case là frozen contract, sinh từ task spec. Contract này là handoff artifact cho dev (tdx-e2e S5-S8).

| Sub | Việc | Companion skill | Output validate | Gate |
|---|---|---|---|---|
| P2.1 | Explore app (browser, read-only, toàn bộ app hiện tại) | `test-planner.md` | list nav paths, UI elements, interactive elements | — |
| P2.2 | Grilling (if gap) — invoke `/grilling` subroutine | `grilling` skill | resolved gaps log | — |
| P2.3 | Generate test case (Markdown plan) | `test-planner.md` | `specs/<feature>.md` path | — |

**P2.1:** Explore toàn bộ app ở trạng thái hiện tại (main branch). Read-only — không modify. Mục đích: hiểu UI context để viết test case chính xác.

**P2.2:** Grilling là exception, không phải default. Trigger khi LLM gặp gap không tự solve được (uncertain state, spec mơ hồ, conflict giữa task desc và app thực tế). Invoke `/grilling` skill → grill từng câu đến khi hết gap → trả control về P2.3.

**P2.3:** Sinh Markdown plan theo `test-planner.md` format. Plan là contract — steps + expected result, không có locator cụ thể (locator là implementation detail, thuộc dev phase).

#### S3: Review Test Case (feature only, mandatory)

| Sub | Việc | Companion skill | Output validate | Gate |
|---|---|---|---|---|
| P2.4 | Review plan | `test-plan-reviewer.md` | verdict ✅/⚠️/❌ + findings table | ✅ |

**P2.4:** Bắt buộc với feature. Bug fix skip (xem S2-bug branch dưới). Reject → back to P2.3 với top findings.

#### S4: Approve Test Case (human gate)

| Sub | Việc | Output validate | Gate |
|---|---|---|---|
| P2.5 | Approve (human gate) | ✅/❌ | ✅ |

**P2.5:** Human approve. Sau approve, contract frozen. Revision chỉ qua grilling fast-path (xem Contract Revision dưới).

#### S2-bug: Bug fix branch

Bug fix stop tại repro, không cần S3/S4:

| Sub | Việc | Companion skill | Output validate | Gate |
|---|---|---|---|---|
| P2-bug.1 | Explore app + locate bug area | `test-planner.md` | list file:line + UI elements liên quan | — |
| P2-bug.2 | Grilling (if gap) | `grilling` skill | resolved gaps log | — |
| P2-bug.3 | Generate repro test case (Markdown plan, bug format) | `test-planner.md` | `specs/<bug>.md` path | — |

**S2-bug output:** `specs/<bug>.md` — repro steps + expected behavior. Handoff cho dev (tdx-e2e S2-bug → S5).

#### Contract Revision (fast-path)

Contract frozen sau S4. Khi dev (tdx-e2e) phát hiện contract sai trong quá trình implement:
1. Dev spawn mini-grilling session với BrSE/QA — grill chỉ phần conflict.
2. BrSE/QA confirm revision.
3. Update `specs/<feature>.md` với comment `<!-- revised: <reason> -->`.
4. Dev continue implement với contract mới.
5. Max 3 revisions per task → escalate (spec quá ambiguous, cần BrSE clarify).

## Handoff

Sau S4 (hoặc S2-bug output), contract ready cho dev:
- Artifact: `specs/<feature>.md` hoặc `specs/<bug>.md` trong `WORKTREE_PATH`.
- Dev invoke `tdx-e2e` với cùng `WORKTREE_PATH` → tdx-e2e detect approved spec → vào S5 (Implement).

## Human Gates

**Default mode: grilling.** tdx-spec mặc định grilling — per-point grilling với user cho mỗi quyết định quan trọng. Auto opt-in chỉ khi user explicitly request. E2E môi trường dev = mặc định, không hỏi.

- **P1.2**: Classification uncertain
- **P2.4 (S3)**: Review plan (feature only, mandatory)
- **P2.5 (S4)**: Approve test case (feature)

## Fallback

Companion skill không invoke được → run inline: `read`, `edit`, `write`, `exec`, `grep`, `glob`.
3 retries fail → escalate to human.

## External Action Approval

Hỏi user trước khi: comment tdx-plane, submit spec review.
Show content → explicit confirm → execute. **No exceptions.**
