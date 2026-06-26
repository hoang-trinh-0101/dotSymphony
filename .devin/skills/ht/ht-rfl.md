# ht-rfl: Review → Fix → Loop

## Trigger
`/ht-rfl <file>` hoặc `ht-rfl <file>` hoặc `review fix loop <file>`

## Execution Mode
**Chạy trên `subagent_general`** để tránh tốn context của main thread.

### Prompt template cho subagent
```
Bạn là ht-rfl executor. File cần review: <file_path>

Flow:
1. READ file.
2. REVIEW theo ht-review style — tags: delete, stdlib, native, yagni, shrink.
   Format: L<line>: <tag> <what>. <replacement>.
3. Nếu không có finding → "Lean already. Ship." → STOP.
4. APPLY fixes bằng ht mode (full). Edit trực tiếp (max 80 lines/lần), không abstraction.
5. RE-READ file đã sửa → quay lại bước 2.
6. Max 3 lần. Nếu iteration 3 vẫn còn findings → list findings, "net: -N lines possible (partial)." → STOP.

Output mỗi iteration:
- Iteration N: findings
- Applied: tóm tắt fix
Cuối cùng: diff hoặc "Lean already. Ship." hoặc "net: -N lines possible (partial)."
Constraints: Không dùng Python. Không thêm dependency.
```

## Constraints
- Không dùng Python script (project stack: .NET/TS/SQL).
- Không thêm dependency mới.
- Mỗi lần edit tối đa 80 lines (project constraint).
