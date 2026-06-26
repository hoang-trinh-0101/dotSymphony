---
name: init-worktree
description: >
  Khởi tạo worktree mới từ root bfe-aio, init submodule theo workspace, checkout branch.
  Hỗ trợ 3 use case: checkout branch có sẵn, tạo branch mới, tạo từ tdx-plane link.
  Trigger khi user nói "init worktree", "setup workspace", "chuẩn bị worktree cho CIS/SEE".
---

# Init Worktree

## Bước 1: Chọn use case

Hỏi user: "Bạn muốn khởi tạo worktree theo cách nào?"
- **Checkout branch có sẵn** — branch đã tồn tại trên remote
- **Tạo branch mới** — tạo từ branch khác (main, feature branch, v.v.)
- **Tạo từ tdx-plane link** — cung cấp link task, tự động sinh branch name + trigger `tdx-e2e` skill

## Bước 2: Nhập thông tin

### Use case 1: Checkout branch có sẵn

Hỏi:
1. "Workspace nào?" — **CIS** hoặc **SEE**
2. "Branch name?" (VD: `feat/server/implement-excel-builder-for-generated-energy-export`)

### Use case 2: Tạo branch mới

Hỏi:
1. "Workspace nào?" — **CIS** hoặc **SEE**
2. "Branch mới tên gì?" (VD: `feat/server/add-export-endpoint`)
3. "Tạo từ branch nào?" (default: `main`, hoặc tên branch khác)

### Use case 3: Tạo từ tdx-plane link

Hỏi:
1. "tdx-plane link?" (VD: `https://plane.tdx.vn/...`)

**Tự động xác định workspace từ ticket code prefix (knowhow nội bộ):**
- `BLUEF` → **SEE**
- `BLUF` → **CIS**
- Nếu prefix không khớp → hỏi user thủ công

**Từ link tdx-plane, dùng tdx-plane MCP tool để:**
- Fetch task detail (title, description, labels, type)
- Xác định workspace từ ticket code prefix (xem trên)
- Phân tích task type → prefix: `feat` (feature) hoặc `fix` (bug fix)
- Phân tích scope từ task content → `server` hoặc `client`
- Trích xuất Azure DevOps number từ task title (VD: `[PBI 4624]` → `4624`). Nếu không tìm thấy → hỏi user.
- Sinh branch name: `<prefix>/<scope>/<azure-devops-number>` (tối thiểu) hoặc `<prefix>/<scope>/<azure-devops-number>-<short-description>` (số trước, nội dung sau — mô tả ngắn tối đa 15 từ, slugified)

**Confirm branch name với user trước khi checkout.**

## Bước 3: Thực hiện

### Use case 1: Checkout branch có sẵn

```powershell
git submodule update --init <WORKSPACE>
git -C <WORKSPACE> fetch origin <BRANCH_NAME>
git -C <WORKSPACE> checkout <BRANCH_NAME>
```

### Use case 2: Tạo branch mới

```powershell
git submodule update --init <WORKSPACE>
git -C <WORKSPACE> fetch origin <BASE_BRANCH>
git -C <WORKSPACE> checkout <BASE_BRANCH>
git -C <WORKSPACE> pull origin <BASE_BRANCH>
git -C <WORKSPACE> checkout -b <NEW_BRANCH>
```

### Use case 3: Tạo từ tdx-plane link

```powershell
git submodule update --init <WORKSPACE>
git -C <WORKSPACE> fetch origin main
git -C <WORKSPACE> checkout main
git -C <WORKSPACE> pull origin main
git -C <WORKSPACE> checkout -b <GENERATED_BRANCH_NAME>
```

Sau khi checkout xong → trigger skill `tdx-e2e`.

## Lưu ý

- **Không chạy `git submodule update --init` không có argument** — init tất cả 6 submodules, chậm không cần thiết.
- **Luôn dùng `git -C <WORKSPACE>`** thay vì `cd` — tránh thay đổi working directory.
- **Submodule sau `update --init` ở detached HEAD** — bắt buộc checkout branch ở bước tiếp.
- **Nếu branch không tồn tại trên remote** — báo user, hỏi có muốn tạo branch mới không.
- **Tự động switch GitHub account trước khi git operations** — chạy `gh auth status` đầu tiên. Nếu active account không có suffix `_bfe` → tự động switch sang account `_bfe` bằng `gh auth switch -u <account>_bfe`. Không hiển thị tên account thật trong output — chỉ ghi nhận "đã switch sang account `_bfe`". Nếu không có account `_bfe` nào → báo user, dừng.

## External Action Approval

Bắt buộc hỏi user trước khi:
- Push branch lên remote
- Tạo PR
- Submit PR review / comment trên PR
- Comment/update tdx-plane

Hiển thị nội dung sẽ submit → hỏi explicit → chỉ execute khi confirm.
