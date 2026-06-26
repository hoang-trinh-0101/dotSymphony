# Backend Code Review Rules (CIS/)

Review rules cho backend code trong CustomerInformationSystem submodule.

## Architecture & Layer Separation

### Endpoint Pattern & Placement
- Endpoint mỏng: bind/authorize → delegate to MediatR command/query → map to response.
- Business rules nằm trong handlers, không ở endpoint. Handlers độc lập framework.
- Domain aggregates (`Core/`) chứa entities, value objects, enums.
- `SharedKernel/` chứa framework abstractions. Không đặt domain concepts ở đây.

### Persistence (EF Core)
- Config rõ ràng trong `EntityTypeConfiguration`. `HasConversion` cho value objects, unique slugs.
- Query read-only dùng `AsNoTracking()`. Dùng eager loading (`Include`) tránh N+1.
- Filter qua specifications, soft-delete: filtered unique index.

## Code Quality

### Naming & Style
- Hậu tố: `*Request`, `*Response`, `*Dto`. Chỉ expose whitelisted fields qua DTOs.
- Ưu tiên functional composition (Func, Action, delegates) hơn OO inheritance.
- C# hiện đại: switch expressions, pattern matching, early return, no deep nesting.
- Không magic numbers, max length constants đặt tại entity class.
- Hậu tố `Spec` chỉ dành cho `Ardalis.Specification` subclasses.

### Comments
- **Không thêm comments mới** trừ khi thực sự cần. Tự giải thích qua naming và structure.
- Không xóa comment cũ trừ khi sai/contradictory.

## Validation & Error Handling
- Mutating request bắt buộc có FluentValidation. Trả về 422 với consistent problem details.
- Message dùng `IStringLocalizer<SharedResource>` để tự động dịch.
- Trả về lỗi qua HTTP status, dùng `ProducesProblemDetails()`.

## Security & Performance
- Gộp auth ở FastEndpoints `Group` nếu ≥2 endpoint dùng chung.
- Split read/write auth thay vì gate cả feature ở strictest level.
- Validate tại boundary. Không code over-defensive (try/catch bừa bãi).
- Dùng `ListPaginatedAsync(spec)` hoặc query trả về `PaginatedResult<TDto>`.
- Mọi list response phải phân trang. Thuộc tính đếm luôn là `Count`.
- Dùng async/await cho I/O, không chặn async (.Result, .Wait()).

## Testing
- Feature mới phải pass test trước khi viết tiếp. Verify với minimal tests.
- Naming: `Given{Context}_When{Action}_Then{Outcome}`.
- Test độc lập, mock dependency đúng cách. Assert outcome, không assert detail.

## Guardrails Hierarchy
1. **Build**: Compiler errors, Roslyn analyzers (immediate)
2. **Lint**: Dotnet format, stylelint (pre-commit/CI)
3. **Test**: Convention tests, ArchUnit (CI)
4. **Guidance**: Documentation (AI-assisted)

## Anti-Patterns cần tránh
- **N+1**: Loop database query trong handler. Thay bằng eager loading/specs.
- **Over-Defensive**: Try/catch/null-checks cho values không thể lỗi/null.
- **Copy-Paste**: Copy code mà không đối chiếu với rules.

## Checklist nhanh
- [ ] Đúng pattern (CQRS, chia layer, thin endpoint)
- [ ] Đặt tên đúng, không comment thừa
- [ ] FluentValidation cho mutating requests, Consistent ProblemDetails
- [ ] Phân trang đầy đủ, I/O async triệt để
- [ ] Auth chia nhỏ read/write, validate ở boundary
- [ ] Test đầy đủ, độc lập, đặt tên đúng chuẩn
- [ ] Không dính anti-patterns (N+1, over-defensive)
