---
auto_execution_mode: 3
description: Update git submodules lên main mới nhất
---
   
# Update Submodules

## Các bước

1. Chạy script:
   - Windows: `.\scripts\update.ps1`
   - Linux/macOS: `./scripts/update.sh`
2. Review `git status`
3. Commit và push:
   ```bash
   git commit -m "chore: update submodules"
   git push
   ```
