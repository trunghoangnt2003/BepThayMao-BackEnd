## Thay đổi

Mô tả hành vi thay đổi và task/spec liên quan. Tiêu đề dùng Conventional Commits.

## Kiểm chứng

Dán đầu ra `npm run lint`, `npm test` và `npm run build`: 0 error, 0 warning,
không test bị bỏ qua.

- [ ] Không commit secret hoặc ghi token/mật khẩu/dữ liệu cá nhân vào log.
- [ ] Nếu đổi hợp đồng: commit backend trước, frontend sync và sinh client cùng đợt (ADR-0010).
- [ ] Hai developer đã review và duyệt.
