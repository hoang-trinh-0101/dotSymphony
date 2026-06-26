# tdx-e2e — Usage Guide

> Orchestrator skill cho BFE-AIO. Hai luồng tách bạch: **test case là contract trước code**, E2E spec materialize từ contract sau code.

## Quick Start

### 1. Khởi tạo worktree

```powershell
# Trigger init-worktree skill trước
# Output: WORKTREE_PATH, WORKSPACE, BRANCH_NAME, TASK_ID
```

### 2. Trigger tdx-e2e

```
/tdx-e2e
```

Hoặc cung cấp task prompt / tdx-plane link — skill tự detect.

## Flow tổng quan

```
Luồng 1 (Contract):  P1 fetch → P2 test case → review → approve
                                    ↓
Luồng 2 (Build):     P3 implement → P4 PR → P5 E2E → P6 review
```

**Nguyên lý cốt lõi:** Test case (P2) là frozen contract. Code (P3) phải khớp contract. E2E spec.ts (P5) materialize từ contract. Revision contract chỉ qua grilling fast-path với user confirm.

## 6 Phases

### P1 — Fetch & Analyze
Fetch task từ tdx-plane MCP hoặc prompt. Classify type (bug/feature/refactor/investigate). Output: task object + summary.

### P2 — Test Case (Contract)
**Luồng 1 — sinh test case trước khi code.**

| Step | Việc | Khi nào |
|---|---|---|
| P2.1 | Explore app (read-only) | Luôn |
| P2.2 | Grilling (if gap) | Khi LLM gặp gap không tự solve |
| P2.3 | Generate Markdown plan | Luôn |
| P2.4 | Review plan | Feature only (mandatory) |
| P2.5 | Approve | Feature — human gate |

**Bug fix branch (P2-bug):** Stop tại red phase. P2-bug.3 sinh repro → P2-bug.4 run → FAIL = bug reproduced → sang P3. Skip P2.4/P2.5.

**Contract Revision:** Sau P2.5, contract frozen. Nếu P3/P5 phát hiện sai → mini-grilling với user → update `specs/<feature>.md` với `<!-- revised: <reason> -->`. Max 3 revisions.

### P3 — Implement
Locate code → hypothesis → implement (approach inline) → run test → retry (max 3). Bug fix thêm mock unit test evidence (P3.6). ht-review auto + ht-rfl auto-fix (P3.7-P3.9).

### P4 — PR Ready
Commit hygiene → push → create PR (inline, 6-section body). Human gate trước push + trước create PR.

### P5 — E2E Test (Materialize)
**Luồng 2 — materialize contract thành Playwright spec.ts.**

| Step | Việc |
|---|---|
| P5.1 | Generate spec.ts + verify contract conformance |
| P5.2 | Run tests |
| P5.3 | RCA + heal (3 bucket) |
| P5.4 | Re-verify (0 failures) |
| P5.5 | Regression |
| P5.6 | Commit |

**P5.1** explore app **sau code** để capture locator thật + verify UI khớp contract. Nếu mismatch → escalate grilling fast-path.

**P5.3 — 3 bucket classification:**

| Bucket | Triệu chứng | Fix gì |
|---|---|---|
| Locator stale | Selector changed, element moved, timing | Fix test (update locator) |
| Logic mismatch | App behavior sai vs contract | Fix code (implementation) |
| Spec ambiguous | Không rõ test hay code sai | Escalate grilling (Contract Revision) |

### P6 — Post-PR Review
Code review (`dev-review-code`) → flag fixme/skip từ P5 → route verdict (3 bucket) → report user.

**P6.3 verdict routing:**
- Code issue → back P3.3
- Test case issue → back P2.3 (Contract Revision)
- E2E issue → back P5.3

## Use Case Routing

| Use case | Phases | Companion skills |
|---|---|---|
| Bug fix | P1 → P2-bug → P3 → P4 → P5 → P6 | `test-planner`, `dev-bugfix-test`, `test-generator`, `test-healer`, `dev-commit`, `dev-review-code` |
| Feature | P1 → P2-feature → P3 → P4 → P5 → P6 | `test-planner`, `test-plan-reviewer`, `test-generator`, `test-healer`, `dev-commit`, `dev-review-code` |
| Refactor | P1 → P2-skip → P3 → P4 → P5-skip → P6 | `test-healer` (regression), `dev-commit`, `dev-review-code` |
| Investigate only | P1 → P2.1-P2.2 only | none |
| E2E test only | P5 only (require approved plan input) | `test-generator`, `test-healer` |

## Human Gates

| Gate | Khi nào | User làm gì |
|---|---|---|
| P1.2 | Classification uncertain | Confirm type |
| P2.4 | Review plan (feature) | Approve/reject findings |
| P2.5 | Approve test case (feature) | Approve contract |
| P2-bug.4 | Confirm red phase (bug) | Confirm "đúng bug" |
| P4.2 | Push | Approve push |
| P4.3 | Create PR | Approve PR draft |
| P5.6 | Commit tests | Approve commit |
| P6.4 | Review verdict | Decide next (merge/follow-up/close) |

## Companion Skills

| File | Role | Phase |
|---|---|---|
| `test-planner.md` | Explore app + sinh Markdown plan (bug/feature format) | P2.1, P2.3, P2-bug.1, P2-bug.3 |
| `test-plan-reviewer.md` | Review plan (6 nhóm checklist, verdict) | P2.4 |
| `test-generator.md` | Materialize plan → spec.ts + verify contract | P5.1 |
| `test-healer.md` | RCA + heal 3 bucket | P5.2-P5.4 |
| `test-quality-rules.md` | Quality gate (waitForTimeout, locators, etc.) | P5.4 |
| `dev-bugfix-test.md` | Mock unit test evidence (xUnit/NUnit) | P3.6 |
| `dev-commit.md` | Commit message conventions + strip ht: comments | P4.1, P5.6 |
| `coordinator.md` | Parallel/subagent rules | P1, P2, P5 |
| `grilling` (external skill) | HITL grilling subroutine | P2.2, P2-bug.2, Contract Revision |

## Parallel Execution

Xem `coordinator.md` chi tiết. Tóm tắt:

| Group | Song song | Lý do |
|---|---|---|
| P1+P2.fetch | P1.1 (fetch task) ‖ P2.1 (explore app) | Độc lập I/O, read-only |
| P5.1.scenarios | Mỗi scenario 1 subagent | Ghi file riêng, reuse storageState |
| P5.3.rca | RCA per failed test | Đọc test + log, không ghi |
| P5.3.fix | Fix per failed test file | File khác nhau → safe |

**Không parallel:** P3.3 (implement), P2.2 (grilling), P5.2 (run tests), bất kỳ step có Human Gate.

## What Changed (Refactor Summary)

### Trước
```
task → code → E2E test (explore app đã implement → plan → review → generate → heal) → merge
```
**Vấn đề:** Test plan viết sau khi code → bias implementation → test sai so với expect.

### Sau
```
Luồng 1: task → explore app (hiện tại) → generate test case → review → approve (contract)
Luồng 2: implement code → PR → generate E2E from contract → run → heal → merge
```
**Giải pháp:** Test case là frozen contract trước code. E2E spec materialize từ contract. Grilling ép HITL khi gặp gap. Healer phân loại failure 3 bucket.

### Key decisions
- Test case sinh từ task spec + explore app hiện tại (không explore feature mới)
- Grilling là exception (trigger khi gap), không phải default
- Contract frozen + fast-path revision (max 3)
- Bug fix stop tại red phase (không cần full luồng 1)
- Reviewer bắt buộc với feature, skip với bug
- Healer 3 bucket: locator stale / logic mismatch / spec ambiguous
- P6 flag fixme/skip + 3-bucket verdict routing
- `dev-bugfix-test.md` giữ (mock unit test, khác layer E2E)

## Constraints

- **Không Python** — project stack: .NET (C#), TypeScript, SQL
- **Work trong `WORKTREE_PATH`** — không bao giờ `cd` vào main worktree
- **Max 80 lines/edit** — chia file lớn thành nhiều lần
- **UTF-8 encoding** — `Set-Content -Encoding utf8` trong PowerShell
- **External action approval** — hỏi user trước push/PR/comment tdx-plane. No exceptions.
