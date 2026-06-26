---
name: grilling
description: Phỏng vấn user không ngừng nghỉ về một plan hoặc design. Dùng khi user muốn stress-test plan trước khi build, hoặc dùng các từ khóa 'grill'. Drill: kích hoạt bằng từ khóa 'drill' trong prompt — đọc decisions đã grill, drill vào phần chưa grill thay vì hỏi lại.
---

Phỏng vấn tôi không ngừng nghỉ về mọi khía cạnh của plan này cho đến khi chúng ta đạt được hiểu chung. Đi xuống từng nhánh của cây design, giải quyết dependency giữa các quyết định từng cái một. Với mỗi câu hỏi, đưa ra câu trả lời recommend của bạn.

Hỏi từng câu một, chờ feedback cho mỗi câu trước khi tiếp tục. Hỏi nhiều câu cùng lúc gây hoang mang.

Nếu một câu hỏi có thể trả lời được bằng cách khám phá codebase, thì khám phá codebase thay vì hỏi.

## Drill (kích hoạt bằng keyword)

Default: grilling từ đầu — hỏi từ surface, không cần context cũ.

Khi prompt chứa từ khóa **"drill"** (VD: `/grilling drill`, "drill vào plan này", "drill tiếp"): activate drill mechanism.

### Drill mechanism

Trước khi hỏi, xác định đã grill gì rồi:

1. **Đọc conversation hiện tại** — tìm decisions đã chốt, câu hỏi đã trả lời. Primary source.
2. **Nếu session mới (fork via handoff)** — đọc handoff doc nếu có, section "Decisions & Resolved Gaps". Cross-session source.
3. **Drill vào phần chưa grill** — không hỏi lại câu đã trả lời. Mục tiêu drill:
   - Edge cases của decision vừa chốt
   - Trade-offs chưa explore
   - Implementation details của option đã chọn
   - Dependency giữa decisions chưa giải quyết

## Khi nào dừng

- User nói "skip" → bỏ câu hiện tại, qua câu tiếp
- User nói "stop grilling" / "enough" → dừng hoàn toàn
- Drill: tất cả decisions đã drill đến implementation details → hiểu chung đạt được

## End-of-session Drill Assessment (drill mode only)

Sau khi user nói stop/enough, nếu drill mode active — chạy 1 bước assessment trước khi kết thúc:

1. **Scan decisions đã chốt** — list ra các decisions đã resolve trong session.
2. **Identify remaining gaps** — decisions chưa drill đến implementation details, edge cases chưa explore, dependencies giữa decisions chưa giải quyết.
3. **If gaps tồn tại → invoke `common-skills/handoff`** — tạo handoff doc với:
   - Decisions & Resolved Gaps (đã chốt)
   - Open Items (gap còn drill)
   - Suggested Skills: `grilling drill` (để session sau drill tiếp)
4. **If không gap → kết thúc bình thường**, không handoff.

Default grilling (không drill) không cần assessment — start từ scratch, không có cross-session context để handoff.
