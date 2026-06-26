# ht-review

Review diff gồm hai phần:
1. **Correctness review** — đọc `general-review-rules.md` và `backend-review-rules.md`, review theo checklist (logic, security, performance, naming, testing).
2. **Over-engineering review** — tìm complexity không cần thiết.

Format: `L<line>: <tag> <what>. <replacement>.` (hoặc `<file>:L<line>: ...` nếu nhiều file).

Tags:
- `delete:` dead code, unused flexibility, speculative feature. Thay bằng: không gì.
- `stdlib:` viết tay mà stdlib đã có. Nêu tên function.
- `native:` dependency hoặc code làm những gì platform đã có sẵn.
- `yagni:` abstraction 1 implementation, config không ai đặt, layer 1 caller.
- `shrink:` logic giống, ít dòng hơn.

## Ví dụ

❌ "EmailValidator class này phức tạp..."
✅ `L12-38: stdlib: validator class 27 dòng. "@" trong email, 1 dòng, validate thực sự là gửi mail xác nhận.`
✅ `L4: native: moment.js cho format 1 lần. Intl.DateTimeFormat, 0 dep.`
✅ `repo.py:L88: yagni: AbstractRepository chỉ một implementation. Inline.`
✅ `L52-71: delete: retry wrapper quanh idempotent local call.`
✅ `L30-44: shrink: manual loop build dict. dict(zip(keys, values)), 1 dòng.`

## Scoring

Kết thúc bằng metric duy nhất: `net: -<N> lines possible.`
Nếu không có gì để cắt, nói `Lean already. Ship.`

## Boundaries

Chỉ hạn complexity — correctness bug, security hole, và performance thuộc về normal review pass. Smoke test đơn giản hay `assert` self-check là ht minimum, không được xóa. Không áp dụng fix, chỉ liệt kê.
"stop ht-review" / "normal mode" để revert.

## GH CLI — Bắt buộc hỏi user approval

**Không tự động chạy `gh` CLI để post review, comment, hoặc bất kỳ action nào lên PR/issue.**

Quy trình:
1. Review xong → hiển thị findings trong chat.
2. Hỏi user: "Post review lên PR không?" (hoặc tương tự).
3. Chỉ chạy `gh pr review` / `gh api` / `gh pr comment` khi user xác nhận.

Lý do: `gh pr review --request-changes` là action có tác dụng phụ (notify author, block merge). User có thể chỉ muốn xem review locally, chưa muốn submit.
