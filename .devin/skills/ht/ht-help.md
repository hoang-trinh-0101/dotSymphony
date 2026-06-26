# ht Help

Hiển thị tham khảo nhanh, không persist/thay đổi mode.

## Levels

| Level | Trigger | Mô tả |
|-------|---------|-------|
| **Lite** | `/ht lite` | Build những gì được yêu cầu, nêu lazy alternative. |
| **Full** | `/ht` | YAGNI → stdlib → native → một dòng → minimum. Mặc định. |
| **Ultra** | `/ht ultra` | YAGNI cực đoan. Xóa trước khi thêm. Challenge requirement. |

## Skills

| Skill | Trigger | Làm gì |
|-------|---------|----------|
| **ht** | `/ht` | Lazy mode chính. Code ít nhất có thể. |
| **ht-review** | `/ht-review` | Review over-engineering, boilerplate. |
| **ht-help** | `/ht-help` | Thẻ này. |

## Tắt

Nói "stop ht" hoặc "normal mode" hoặc `/ht off`.

## Cấu hình Default Mode

Default mode = `full`. Thay đổi qua:
- **Env**: `export HT_DEFAULT_MODE=ultra`
- **Config** (`~/.config/ht/config.json`): `{ "defaultMode": "lite" }`

Thứ tự ưu tiên: env > config > `full`.

## Update

Tự động qua `/plugin` hoặc thủ công: `/plugin marketplace update ht` rồi `/reload-plugins`.
Nâng cấp claude-code nếu lỗi: `npm install -g @anthropic-ai/claude-code@latest`.

## Xem thêm

Tài liệu chi tiết: `skills/ht/SKILL.md`
