---
name: tdx-e2e
description: >
  Core orchestrator: 8 steps / 2 streams. Stream 1 (Contract): fetch → gen test case → review → approve.
  Stream 2 (Build): implement → gen E2E → run E2E → merge (review diff → push → PR).
  Two-stream flow: test case approved before code, E2E spec generated from approved contract.
  Thin router — load companion skill content on-demand per step. Extensible by adding use cases.
  Trigger sau init-worktree, hoặc khi user cung cấp tdx-plane link / task prompt.
---

# E2E Pipeline — Core Router

Một skill chính, load companion content theo step. Thêm use case = thêm row vào routing table.

**Coordinator mode:** Main thread = điều phối viên. Load `coordinator.md` khi vào S1/S2/S6 để quyết định sub-step nào chạy song song qua `run_subagent`.

**Two-stream principle:** Test case (Stream 1) là frozen contract. E2E spec.ts (Stream 2) là materialize từ contract. Implement phải khớp contract. Contract revision chỉ qua grilling fast-path với user confirm.

## Pipeline Map (8 steps / 2 streams)

```
STREAM 1 — CONTRACT
  S1  Fetch & Analyze          ← P1
  S2  Generate Test Case       ← P2.1-P2.3 (explore + grilling + gen plan)
  S3  Review Test Case         ← P2.4 (test-plan-reviewer, mandatory feature)
  S4  Approve Test Case        ← P2.5 (human gate, contract frozen)

STREAM 2 — BUILD
  S5  Implement                ← P3 (locate, hypothesis, vertical TDD loop, ht-review)
  S6  Generate E2E             ← P5.1 (spec.ts from approved contract + locator capture)
  S7  Run E2E                  ← P5.2-P5.7 (run, heal, re-verify, regression, commit, coverage check)
  S8  Merge                    ← P6 + P4 merged (review BEFORE PR)
      S8.1 Review diff (local, dev-review-code)
      S8.2 Route verdict (fail → back to S5/S4/S7; pass → S8.3)
      S8.3 Push + Create PR
      S8.3b Link-back (Plane status + chat template)
      S8.4 Report to user
```

**Key change vs old pipeline:** Review (P6) chạy TRƯỚC khi tạo PR (P4) — review local diff, chỉ tạo PR khi pass. PR ra mắt đã clean. "Merge" = PR creation, không phải actual git merge.

## Context Contract (từ init-worktree)

Khi được trigger bởi `init-worktree`, nhận 4 biến:
```
WORKTREE_PATH=<worktree path>     # VD: D:/Work/bfe-aio-worktrees/SEE-4561-invoice-parens
WORKSPACE=<CIS|SEE>
BRANCH_NAME=<branch>
TASK_ID=<BLUEF-xxxx hoặc BLUF-xxxx>
```

**Bắt buộc làm việc trong `WORKTREE_PATH`**, không phải main worktree.
- Tất cả `git -C`, `dotnet test`, `dotnet build`, file edit phải dùng `WORKTREE_PATH`.
- Không bao giờ `cd` vào main worktree (`<ROOT>/<WORKSPACE>/`).
- Nếu trigger trực tiếp (không qua init-worktree) → hỏi user worktree path, hoặc trigger init-worktree trước.

## Use Case Routing

| Use case | Steps | Companion skills to load |
|---|---|---|
| Bug fix | S1 → S2-bug → S5 → S6 → S7 → S8 | `test-planner`, `dev-bugfix-test`, `test-generator`, `test-healer`, `dev-commit`, `dev-review-code` |
| Feature | S1 → S2 → S3 → S4 → S5 → S6 → S7 → S8 | `test-planner`, `test-plan-reviewer`, `test-generator`, `test-healer`, `dev-commit`, `dev-review-code` |
| Refactor | S1 → S5 → S7-skip → S8 | `test-healer` (regression only), `dev-commit`, `dev-review-code` |
| Investigate only | S1 → S2.1-S2.2 only | none |
| E2E test only | S6-S7 only (require approved plan input) | `test-generator`, `test-healer` |

New use case? Add row here. Steps below are reusable building blocks.

## Steps (detailed)

Mỗi sub-step có output cụ thể để validate trước khi qua step tiếp theo. Parallel/subagent rules: xem `coordinator.md`.

### S1: Fetch & Analyze (P1)

| Sub | Việc | Output validate | Gate |
|---|---|---|---|
| P1.1 | Fetch task (tdx-plane MCP hoặc prompt) | task object: `{id, title, desc, type, labels, azure_devops_id}` | — |
| P1.2 | Classify type | `{type: bug\|feature\|refactor\|investigate}` | ✅ nếu uncertain |
| P1.3 | Scope | `{scope: server\|client}` | — |
| P1.4 | Summary cho user | 1 đoạn text | — |

### S2: Test Case / Contract (P2)

Test case là frozen contract, sinh từ task spec trước khi code. E2E spec.ts (S6) materialize từ contract này.

| Sub | Việc | Companion skill | Output validate | Gate |
|---|---|---|---|---|
| P2.1 | Explore app (browser, read-only, toàn bộ app hiện tại) | `test-planner.md` | list nav paths, UI elements, interactive elements | — |
| P2.2 | Grilling (if gap) — invoke `/grilling` subroutine | `grilling` skill | resolved gaps log | — |
| P2.3 | Generate test case (Markdown plan) | `test-planner.md` | `specs/<feature>.md` path | — |

**P2.1:** Explore toàn bộ app ở trạng thái hiện tại (main branch). Read-only — không modify. Mục đích: hiểu UI context để viết test case chính xác.

**P2.2:** Grilling là exception, không phải default. Trigger khi LLM gặp gap không tự solve được (uncertain state, spec mơ hồ, conflict giữa task desc và app thực tế). Invoke `/grilling` skill → grill từng câu đến khi hết gap → trả control về P2.3.

**P2.3:** Sinh Markdown plan theo `test-planner.md` format. Plan là contract — steps + expected result, không có locator cụ thể (locator là implementation detail, thuộc S6).

#### S3: Review Test Case (P2.4, feature only, mandatory)

| Sub | Việc | Companion skill | Output validate | Gate |
|---|---|---|---|---|
| P2.4 | Review plan | `test-plan-reviewer.md` | verdict ✅/⚠️/❌ + findings table | ✅ |

**P2.4:** Bắt buộc với feature. Bug fix skip (xem S2-bug branch dưới). Reject → back to P2.3 với top findings.

#### S4: Approve Test Case (P2.5, human gate)

| Sub | Việc | Output validate | Gate |
|---|---|---|---|
| P2.5 | Approve (human gate) | ✅/❌ | ✅ |

**P2.5:** Human approve. Sau approve, contract frozen. Revision chỉ qua grilling fast-path (xem Contract Revision dưới).

#### S2-bug: Bug fix branch

Bug fix stop tại red phase, không cần S3/S4:

| Sub | Việc | Companion skill | Output validate | Gate |
|---|---|---|---|---|
| P2-bug.1 | Explore app + locate bug area | `test-planner.md` | list file:line + UI elements liên quan | — |
| P2-bug.2 | Grilling (if gap) | `grilling` skill | resolved gaps log | — |
| P2-bug.3 | Generate repro test case (Markdown plan, bug format) | `test-planner.md` | `specs/<bug>.md` path | — |
| P2-bug.4 | Run repro test | — | test result: FAIL (repro confirmed) | ✅ |

**P2-bug.4 stop condition:** `test case FAIL = bug reproduced = sang S5`. User confirm "đúng bug" → sang S5. Không cần S3/S4 ceremony. Repro FAIL tự nó là evidence + approval ngầm.

P2-bug.4 không tái hiện → báo user, hỏi decision.

#### Contract Revision (fast-path)

Contract frozen sau S4. Khi implement (S5) hoặc E2E (S6/S7) phát hiện contract sai:
1. Spawn mini-grilling session với user — grill chỉ phần conflict.
2. User confirm revision.
3. Update `specs/<feature>.md` với comment `<!-- revised: <reason> -->`.
4. Continue implement/E2E với contract mới.
5. Max 3 revisions per task → escalate (spec quá ambiguous, cần BrSE clarify).

### S5: Implement (P3)

Vertical TDD loop (Matt Pocock): tracer bullet + incremental RED→GREEN per scenario. **Không horizontal slicing** — không viết tất cả test trước rồi implement tất cả (crap tests). Một test → một implement → repeat.

| Sub | Việc | Companion skill | Output validate | Gate |
|---|---|---|---|---|
| P3.1 | Locate code (`grep`, `glob`, `read` trong `WORKTREE_PATH`) | — | list `file:line` liên quan | — |
| P3.2 | Hypothesis + evidence | — | text: "giả thiết X, evidence Y" | — |
| P3.3 | Tracer bullet — write first test (RED) | — | test file path + test result: FAIL | — |
| P3.4 | Incremental loop — RED→GREEN per scenario | — | pass count (all scenarios green) | — |
| P3.5 | Refactor (only when all green) | — | diff + full test suite PASS | — |
| P3.6 | Bug fix evidence (bug only, if no qa/staging access) | `dev-bugfix-test.md` | mock unit test file path + pass count | — |
| P3.7 | ht-review auto (impl + test files) | `ht/ht-review.md` | findings list | — |
| P3.8 | ht-rfl auto-fix (if findings > 0) | `ht/ht-rfl.md` (subagent_general) | diff sau fix | — |
| P3.9 | Re-run test | — | pass count | — |
| P3r.1 | Implement refactor changes | — | diff | — |
| P3r.2 | Run existing tests | — | pass count (must not regress) | — |

**P3.3 — Tracer bullet:** Write ONE xUnit test for the first scenario — test via public interface, mock chỉ system boundary (DB, external API). Test fails (RED). Proves the path works end-to-end. Approach merge inline: trước khi code, output 1 đoạn text ngắn "approach: files X, Y, steps 1-2-3" rồi viết test. Gate nhẹ: user có thể interrupt nếu approach sai.

**P3.4 — Incremental loop:** For each remaining scenario: write next test (RED) → minimal code to pass (GREEN) → run test. One test at a time, only enough code to pass current test, don't anticipate future tests. Retry max 3 per scenario → escalate. **UI-only scenarios** (pure UI, no logic — VD: render form, navigation) skip P3.3-P3.5 — handled in S6 (Playwright).

**P3.5 — Refactor:** Only when ALL scenarios green. Extract duplication, deepen modules. Run full test suite after each refactor step. **Never refactor while RED.**

**P3.6:** Condition: `if P1.2.type == "bug" && no qa/staging access`. Mock unit test (xUnit/NUnit) cho bug fix evidence.

**P3.7:** ht-review trên `git diff --name-only <base>..HEAD`. P3.8 spawn subagent_general pass WORKTREE_PATH + file list (per `coordinator.md` contract). P3.9 verify ht-rfl không break test.

### S6: Generate E2E (P5.1)

Materialize approved contract (S2) thành Playwright spec.ts. **S5 = xUnit logic tests (vertical TDD loop). S6 = Playwright UI tests** — materialize từ contract sau khi app đã có feature (S5 done). Không ép Playwright red trong S5 khi UI chưa tồn tại. Explore app sau code để capture locator thật + verify contract conformance.

| Sub | Việc | Companion skill | Output validate | Gate |
|---|---|---|---|---|
| P5.1 | Generate spec.ts + verify contract conformance | `test-generator.md` | `tests/<feature>/*.spec.ts` paths + contract conformance check | — |

**P5.1:** Read approved plan → explore app (sau code) → capture locators thật → write spec.ts. **Verify contract conformance:** nếu UI không khớp plan (VD plan nói "form có field X" nhưng code không implement) → escalate grilling fast-path (Contract Revision). Explore ở P5.1 có 2 job: capture locator + verify contract.

### S7: Run E2E (P5.2-P5.6)

| Sub | Việc | Companion skill | Output validate | Gate |
|---|---|---|---|---|
| P5.2 | Run tests | `test-healer.md` | pass/fail count | — |
| P5.3 | RCA + heal per failure (3 bucket) | `test-healer.md` | root cause + fix per test | — |
| P5.4 | Re-verify | `test-healer.md` | 0 failures | — |
| P5.5 | Regression (`npx playwright test`) | — | pass/fail count | — |
| P5.6 | Commit | — | commit SHA | ✅ |
| P5.7 | Coverage check (LLM semantic match) | — | coverage report: matched/missing scenarios | — |

**P5.3 — 3 bucket classification:**
- **Locator stale/UI change:** fix test (update locator). Không sửa code.
- **Logic mismatch:** fix code (implement sai contract). Không sửa test.
- **Spec ambiguous:** escalate grilling fast-path (Contract Revision). Không tự sửa.

**Quality gate:** After P5.4, grep `waitForTimeout` > 100ms → flag for refactor. Apply `tdx-e2e/test-quality-rules.md`.

**P5.7 — Coverage check:** LLM semantic match spec scenarios (từ `specs/<feature>.md`) vs test descriptions (xUnit + Playwright). Match bất kỳ loại test — không quan tâm xUnit hay Playwright, chỉ cần scenario được cover. Gap (scenario thiếu test, hoặc code có nhưng assertion sai) → back to S5 vertical loop (P3.4) cho scenario thiếu: red→green. Tự xử lý cả "code+test thiếu" và "code có nhưng assertion sai".

**E2E only use case:** S6-S7 standalone nhưng bắt buộc input `specs/<feature>.md` đã approved (plan đến từ ngoài pipeline — QA agent, user import). Không sinh plan trong S6.

### S8: Merge (P6 review → P4 PR)

Review local diff TRƯỚC khi tạo PR. PR ra mắt đã clean. "Merge" = PR creation.

| Sub | Việc | Companion skill | Output validate | Gate |
|---|---|---|---|---|
| S8.1 | Code review (local diff `base..HEAD`) | `custom-skills/skills/dev-review-code/SKILL.md` (manual load) | review report path + verdict | — |
| S8.2 | Route verdict (3 bucket) | — | routing decision | — |
| S8.3 | Commit hygiene + Push + Create PR | `dev-commit.md` | commit SHA + remote ref + PR URL | ✅ |
| S8.3b | Link-back: Plane status update + chat template | `config/chat-reviewers-<workspace>.json` | Plane status updated + chat template path | — |
| S8.4 | Report to user | — | verdict + fixme flags + next steps | ✅ |

**S8.1:** `dev-review-code` không distribute qua `init.ps1` (nằm trong `custom-skills/`, tác phẩm người khác — không touch). Load manual: read `custom-skills/skills/dev-review-code/SKILL.md` từ root repo → follow workflow → load `references/` + `templates/` theo signal → output report vào `reviews/`.

Review trên `git diff <base>..HEAD` local — không cần PR. Base từ branch convention (default `main`; nếu branch starts with `fix/` → `ask_user_question` [main, hotfix/<latest>, other]).

Flag fixme/skip từ S7: nếu P5 có test bị `test.fixme()` hoặc `test.skip()` → flag trong review report: "contract chưa full verified, fixme/skip list + unblock condition". Không có fixme/skip → skip flagging.

**S8.2 — 3 bucket verdict routing:**
- **Code issue** (reviewer nêu code sai): → back to S5 (P3.3) with findings.
- **Test case issue** (reviewer nêu contract sai): → back to S2 (P2.3, grilling fast-path Contract Revision).
- **E2E issue** (reviewer nêu test technique sai): → back to S7 (P5.3, heal).

Pass → tiếp S8.3.

**S8.3:** Load `dev-commit.md` — sinh commit message theo conventions (English, Conventional Commits, no PII, no bot tags), strip `ht:` comments khỏi staged files, `git commit -F` (PowerShell heredoc không hoạt động).

PR body 6 sections (English, hardcode template):
- **Description:** từ task object (tdx-plane MCP nếu TASK_ID) hoặc commit body.
- **Work Item Link:** `<TASK_ID>` (từ branch suffix hoặc tdx-plane).
- **Changes Made:** `git log <base>..HEAD --oneline`.
- **Impact Area:** `git diff --stat <base>..HEAD`.
- **Evidence:** từ P3.4/P3.6 test result (pass count + brief summary).
- **Degrade Impact Check:** "No degrade — minimal removal" hoặc heuristic từ diff.

Show draft → user approve (S8.3 gate) → `git push -u origin` → write `PR_BODY.md` temp (`Set-Content -Encoding utf8`) → `gh pr create --base <base> --body-file PR_BODY.md` → `Remove-Item PR_BODY.md`.

**S8.3b — Link-back:** Pipeline update Plane status + output chat template. Dev send chat (không tự động send).
1. **Plane status update:** Update task status trên tdx-plane (MCP) → "In Review" (PR created). Hỏi user trước khi update (External Action Approval).
2. **Chat template:** Load `config/chat-reviewers-<WORKSPACE>.json` (root level, per workspace) → auto-fill reviewers + cc. LLM output hỗ trợ (PR summary, change highlights) → human finalize. Output template to `reviews/chat-template-<TASK_ID>.md`.
3. Dev review template → send chat manually (Teams/Slack/email per team convention).

**S8.4:** Báo user verdict + fixme flags + next steps. Human gate — user decide next (merge / follow-up / close). Actual git merge là human decision, không tự động.

## Human Gates

**Default mode: auto.** tdx-e2e chạy tự động trừ khi gặp gate cứng hoặc LLM uncertain signal. HITL cho Dev chọn mode từ đầu (default = auto). Default = có E2E.

**Hard gates (luôn HITL):**
- **P1.2**: Classification uncertain
- **P2.4 (S3)**: Review plan (feature only, mandatory)
- **P2.5 (S4)**: Approve test case (feature)
- **P2-bug.4**: Confirm red phase (bug fix)
- **S8.3**: Commit + Push + Create PR (draft shown → explicit confirm)
- **P5.6 (S7)**: Commit tests
- **S8.4**: Review verdict report → user decide next

**LLM uncertain signals (escalate to HITL ngoài hard gates):**
1. **Contract conflict >3 revisions** — spec quá ambiguous, cần BrSE clarify.
2. **Test heal bucket 3 (spec ambiguous)** — P5.3 không tự sửa được, escalate grilling fast-path.
3. **Retry loop exhausted (max 3)** — P3.4/P5.3 hết 3 lần retry → escalate.
4. **Classification uncertain (P1.2 gate)** — đã là hard gate.

**Không escalate cho:** locator stale, logic mismatch, coverage gap, ht-review findings — các case này có action rõ ràng (fix test / fix code / back to S5 / auto-fix).

## Fallback

Companion skill không invoke được → run inline: `read`, `edit`, `write`, `exec`, `grep`, `glob`.
3 retries fail → escalate to human.

## External Action Approval

Hỏi user trước khi: comment tdx-plane, push, tạo PR, submit PR review.
Show content → explicit confirm → execute. **No exceptions.**
