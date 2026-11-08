# Kiểm chứng backend trước khi merge develop

Ngày: 2026-11-09. Phạm vi: host nền tảng Phase 1, chưa hoàn tất toàn bộ phase.

## Thay đổi

- ProblemDetails cho lỗi validation/domain/500/404; phản hồi có correlationId, traceId khi có Activity; lỗi 500 không đưa chi tiết riêng tư ra response.
- Correlation ID được kiểm tra định dạng và trả đồng nhất ở header/body/log.
- Serilog JSON dùng logger theo host; request log không ghi Authorization, Cookie hoặc query chứa dữ liệu riêng tư.
- Forwarded headers chỉ tin mạng đã cấu hình; policy rate limit theo IP, 429 có Retry-After; giới hạn request body và không mở CORS tùy tiện.
- OpenAPI có schema lỗi bắt buộc title/status/correlationId; export tất định.
- Cho phép nhánh tích hợp develop, vẫn kiểm tên các nhánh feature bằng test.

## Kết quả thực tế

- `npm run lint`: dotnet format verify và build warnings-as-errors đều exit 0, 0 warning/0 error.
- `npm test`: 34 passed, 0 failed, 0 skipped (architecture 15, application 5, integration 14).
- `npm run build`: Release exit 0, 0 warning/0 error.
- `npm run openapi:check`: hai lần build và bản lưu trong repo trùng nhau, exit 0.
- `npm run test:scripts`: 3 passed, 0 failed, 0 skipped.

## Giới hạn

Policy rate limit đã được test trên endpoint của test host; endpoint nghiệp vụ thực phải gắn policy khi được triển khai. Readiness PostgreSQL/Redis/RabbitMQ, OpenTelemetry, compose và Branch API vẫn còn trong kế hoạch. Test hiện tại không chứng minh luồng đặt hàng đã hoạt động.
