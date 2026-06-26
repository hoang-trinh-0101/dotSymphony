# Template: dev-handoff

Handoff giữa các bước trong SDLC. Dựa trên văn hóa引継ぎ Nhật (fairsystem, Qiita Team) + Tallyfy 5-section.

Khác biệt với bản cũ: tập trung WHY/目的, 暗黙知/tribal knowledge, ボツ理由/failed approaches, escalation theo trouble type, confirmation question, assumption verification, scope delta.

```markdown
# Handoff — <Phase From> → <Phase To>

## 1. Mục đích·Tổng quan
- TASK_ID: <id>
- Chuyển giao: <Phase From> → <Phase To>
- Vì sao chuyển giao: <1-2 câu — business context, không chỉ task name>
- WORKSPACE: <CIS|SEE>
- WORKTREE_PATH: <path>

## 2. Sản phẩm bàn giao
| Sản phẩm | Vị trí (path/URL) | Trạng thái | Vấn đề đã biết |
|----------|-------------------|-----------|----------------|
| <artifact 1> | <path> | <draft/reviewed/approved/blocked> | <issue hoặc none> |
| <artifact 2> | <path> | <status> | <issue hoặc none> |

## 3. Stakeholders & liên lạc
| Vai trò | Họ tên | Liên lạc | Backup | Loại trouble → contact |
|---------|--------|----------|--------|------------------------|
| Bàn giao | <name> | <contact> | <backup> | — |
| Nhận bàn giao | <name> | <contact> | <backup> | — |
| <role> | <name> | <contact> | <backup> | <trouble type → contact> |
- Ngày chuyển giao: <date/time ownership transfers>

## 4. Tri thức ngầm·Quyết định口头
<!-- Thông tin không có trong artifact. Quyết định口头, approach đã thử và reject. -->
- <Quyết định口头 1: what + when + who decided>
- <Approach đã thử và reject: what was tried + vì sao reject — để người nhận không lặp lại cùng một suy nghĩ>
- <Sensitivities stakeholder: "X ghái Y", "Z sếp thích format A" etc.>
- <Thỏa thuận không chính thức: lời hứa口头 với team khác>

## 5. Giả định·Assumption (kế thừa + cần verify)
<!-- Giả định kế thừa từ phase trước. Người nhận phải verify, không accept mù. -->
| Giả định | Nguồn | Trạng thái verify | Cách verify |
|----------|-------|-------------------|-------------|
| <assumption 1> | <phase/doc> | <unverified/verified> | <how to verify> |

## 6. Chênh lệch scope
- Kế hoạch: <what was originally planned>
- Thực tế: <what was actually delivered>
- Chênh lệch: <gap — gì dời sang phase sau, gì cut, gì thêm>
- Lý do: <why delta occurred>

## 7. Người nhận cần verify
- [ ] Quyền truy cập: <systems/tools/files — accessible hết?>
- [ ] Deadline·phụ thuộc: <understood?>
- [ ] Review sản phẩm: <reviewed + questions answered?>
- [ ] Liên lạc upstream/downstream stakeholder: <possible?>
- **Câu hỏi xác nhận:** "<1 câu — nếu trả lời 'no' thì KHÔNG được chuyển phase>"
  - Trả lời: <yes/no — if no, 10min gap-fill rồi escalate>

## 8. Xử lý khi có trouble
- Escalation path: <contact ai cho loại issue nào>
- Deadline báo cáo vấn đề handoff: <24-48h — sau đó coi như accepted>
- Fallback: <khi nào cần kéo người bàn giao gốc trở lại — conditions>
- Định nghĩa hoàn thành: <khi nào handoff "complete" vs "in progress">

## 9. Suggested Skills
- <skill name>: <khi nào invoke, làm gì>

## Open Items
- <Câu hỏi chưa giải quyết>
- <Follow-up task>
- <Cảnh báo cho phase sau: "Vendor X hay trễ deadline 2 tuần. Plan accordingly" — lessons learned thẳng thắn>

## Sensitive Info
- Redacted. <list what was redacted>
```
