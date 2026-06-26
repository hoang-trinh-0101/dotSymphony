# dev-commit — Commit Hygiene Workflow

Companion content cho P4.1. Load khi đến step commit. Sinh commit message theo conventions + strip `ht:` comments khỏi staged files.

## Conventions (hardcode — ổn định toàn project)

- **English only** (customer-facing repo).
- **Conventional Commits:** `<type>(<scope>): <subject>`
- **Subject:** ≤50 chars, imperative mood, no task ID, no bug tag, no PII.
- **Body:** what + why + 1 dòng `Task: <ID>` (task ID từ branch suffix hoặc tdx-plane).
- **No bot tags:** không Co-Authored-By, không "Generated with Devin".
- **PowerShell:** `Set-Content` temp file + `git commit -F` (heredoc không hoạt động).

## Type + Scope detection

**Branch name primary** (user đã follow convention khi tạo branch via init-worktree):
- `fix/...` → type=fix
- `feat/...` → type=feat
- `refactor/...` → type=refactor
- `test/...` → type=test
- `chore/...` → type=chore

**Scope:** branch path segment sau type (`fix/server/...` → server, `feat/client/...` → client) hoặc file extension trong diff (.cs→server, .tsx→client).

**Fallback (branch không có prefix):** infer từ diff + P1.2 task type. Test-only diff → test; src+test → feat/fix per P1.2; refactor diff → refactor.

## PII scan (trước khi gen message)

Scan staged diff cho patterns, genericize trong commit message (không edit code, chỉ filter khỏi message):

| Pattern | Replacement |
|---|---|
| Email regex `[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}` | `<email>` |
| Customer names: `BFE`, `Bluefield` | `<customer>` |
| Internal domains: `tdx.vn`, `tdx.local` | `<internal>` |

Nếu diff chứa PII → ghi warning trong output summary, genericize trong message.

## `ht:` comment strip (trước khi commit)

**Scope: chỉ staged files** (surgical — không touch files ngoài staged).

1. `git diff --cached` → grep `ht:` trong added/modified lines.
2. Nếu có → edit staged files: remove dòng comment chứa `ht:` (hoặc remove `ht:` prefix giữ comment nếu comment có giá trị standalone).
3. `git add <files đã edit>` → stage lại.
4. Output: list files đã strip.

## Flow

```
1. read staged diff: `git diff --cached`
2. detect type + scope (branch name primary, diff fallback)
3. gen subject (≤50 chars, imperative, no task ID/PII/bug tag)
4. gen body (what + why + `Task: <ID>`)
5. scan PII in message → genericize
6. strip `ht:` from staged files → re-stage
7. write msg to temp file: `Set-Content .commit-msg.txt -Encoding utf8`
8. git commit -F .commit-msg.txt [--amend if flag]
9. Remove-Item .commit-msg.txt
10. output: subject + stripped items summary
```

## `--amend` flag

Amend commit cuối thay vì commit mới. Giữ author, override message. Dùng khi user refine message sau commit.

## Output

```text
Committed: <short-sha>
Subject: <type>(<scope>): <subject>
Stripped: ht: comments in <N> files · PII genericized: <M> items
```

## Constraints

- Không Python (project stack: .NET/TS/SQL).
- Không thêm dependency.
- Max 80 lines/edit (project constraint).
- UTF-8 encoding khi ghi file.
