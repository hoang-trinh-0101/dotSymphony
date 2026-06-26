---
description: Khởi tạo git submodules (lần đầu)
---

# Khởi tạo Submodules

## Các bước

1. Run script:
   - Windows: `.\scripts\init.ps1`
   - Linux/macOS: `./scripts/init.sh`
2. Đợi clone xong

Thủ công nếu script lỗi:
```bash
git submodule update --init --recursive
```

## Chức năng

- Khởi tạo, clone, checkout main cho tất cả 6 submodules
