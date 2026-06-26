# Coordinator — Parallel & Subagent Rules

Load companion content này khi vào P1/P2/P5. Main thread = coordinator: quyết định spawn subagent hay chạy inline.

## Nguyên tắc

1. **Sequential mặc định.** Chỉ parallel khi 2 sub-step độc lập I/O và không share file đang ghi.
2. **Subagent = isolated context.** Pass `WORKTREE_PATH`, task object, artifact path qua prompt. Subagent không thấy context main.
3. **Gate blocks parallel.** Human gate (P1.2/P2.4/P2.5/P2-bug.4/P4.2/P4.3/P5.6/P6.4) → bắt buộc chờ user, không bao giờ spawn subagent vượt gate.
4. **External action = main thread only.** Push/PR/comment tdx-plane không delegate cho subagent.

## Parallel map

| Group | Sub-steps chạy song song | Profile | Rationale |
|---|---|---|---|
| **P1+P2.fetch** | P1.1 (fetch task) ‖ P2.1 (explore app) | `subagent_explore` × 2 | Độc lập I/O: fetch task object vs explore app. Cùng read-only, không ghi |
| **P1.parallel** | P1.2 (classify) + P1.3 (scope) sau P1.1 | `subagent_explore` | Cùng đọc task object, không ghi |
| **P5.1.scenarios** | P5.1 per scenario (mỗi scenario 1 subagent) | `subagent_general` | Mỗi scenario ghi file riêng `tests/<feature>/<scenario>.spec.ts`. Reuse `storageState` từ `auth.setup.ts` |
| **P5.3.rca** | P5.3 RCA per failed test | `subagent_explore` | Đọc test + log, không ghi |
| **P5.3.fix** | P5.3 fix per failed test file | `subagent_general` | File khác nhau → safe parallel; cùng file → sequential |

## Không parallel

- **P3.3 (implement):** single-thread, file edits có thể xung đột.
- **P2.3 (generate test case):** phụ thuộc P2.1 + P2.2 output.
- **P2.2 (grilling):** HITL, sequential by nature.
- **P5.2 (run tests):** single Playwright process.
- **P5.5 (regression):** single process.
- **Bất kỳ step có Human Gate.**

## Subagent prompt contract

Mỗi subagent prompt phải chứa:
```
WORKTREE_PATH=<path>
WORKSPACE=<CIS|SEE>
ARTIFACT_IN=<path hoặc task object>
OUTPUT_EXPECTED=<format>
CONSTRAINTS: không push, không tạo PR, không comment external. Chỉ ghi trong WORKTREE_PATH.
```

## Coordinator decision flow

```
step = current sub-step
if step in PARALLEL_MAP:
  spawn N subagents (is_background=true) → collect via read_subagent
  if any fail → retry inline (fallback rule trong SKILL.md)
else:
  run inline
```

## Anti-pattern

- ❌ Spawn subagent cho 1-step task (overhead > gain).
- ❌ Parallel subagent ghi cùng file.
- ❌ Subagent vượt human gate.
- ❌ Subagent gọi external action (push/PR/comment).

`ht:` coordinator là router, không phải executor. Minimum subagent = minimum overhead.
