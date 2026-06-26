# General Code Review Rules

Quy tắc review chung áp dụng cho tất cả submodules trong CIS-AIO.

## Project Invariants & Core Principles
1. **Chạy script init** trước khi làm việc, không assume submodule đã được init.
2. **Không sửa code submodule trực tiếp từ root repo** — hãy thao tác trong submodule directory.
3. **Single source of truth**: CLAUDE.md và AGENTS.md tại root.
4. **Resolve all compiler warnings/errors**, không suppress bừa bãi.
5. **Feature mới phải pass test** trước khi viết tiếp.
6. Xưng hô: Linh = BrSE/Developer; dùng tiếng Việt cho trao đổi nội bộ, tiếng Anh cho technical terms, tiếng Nhật (弊社案) cho khách hàng.

## Code Quality Foundations

### SOLID & Design
- **Single Responsibility (S)**, **KISS** (đơn giản nhất có thể), **YAGNI** (không code speculative).
- **DRY**: Trích xuất logic lặp lại, composition > inheritance.
- Tránh over-engineering, chỉ tối ưu khi thực sự có performance bottleneck được đo đạc.

### Naming Conventions
- **Classes/Methods**: PascalCase. Tên rõ ràng, mô tả intent, không mô tả implementation.
- **Variables**: camelCase. Tránh viết tắt tối nghĩa.
- **Files**: Trùng tên class/function.

### Comments & Documentation
- **Không thêm comments mới** trừ khi cần giải thích lý do (WHY, không phải WHAT).
- Giữ comment cũ trừ khi sai. Xóa comment giải thích dòng code làm gì.
- Document các public APIs quan trọng, update README khi đổi workflow.

## Testing Standards
- Test critical paths và edge cases, giữ test độc lập và tái lập được.
- Pattern AAA (Arrange-Act-Assert). Một assertion cho mỗi test concept.
- Naming:
  - Backend: `Given{Context}_When{Action}_Then{Outcome}`
  - Frontend: Sentence titles (`given ..., when ..., then ...`)
  - Giới hạn tên test ≤ ~100 characters.

## Security & Performance
- Validate user input tại system boundary. Parameterized queries tránh SQL injection.
- Không log dữ liệu nhạy cảm (passwords, tokens, PII).
- Tránh N+1 query. Dùng phân trang cho large result sets.
- Async triệt để cho I/O-bound operations, không block `.Result` hay `.Wait()`.

## Guardrails Hierarchy & Anti-Patterns
1. **Build**: Compiler errors, analyzers.
2. **Lint**: Dotnet format, eslint (pre-commit/CI).
3. **Test**: Unit/integration, convention tests.
4. **Guidance**: Documentation (AI-assisted).

### Tránh:
- **Over-Engineering**: Xây dựng abstraction "đề phòng tương lai".
- **Under-Engineering**: Copy-paste bừa bãi, magic values thay vì hằng số.
- **Thiếu Tests**: "Sẽ viết test sau", chỉ test happy path.
