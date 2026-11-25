# Thanh toán Phase 3

## Local

Từ backend chạy `npm run dev:up`, `npm run dev:db -- migrate`, `npm run dev:api` và `npm run dev:worker` ở hai terminal. `deploy/.env` tự sinh SePay__WebhookKey khi thiếu; không in/copy key vào Git. Worker dùng cùng DB/Rabbit với API. Thay đổi bank áp dụng đơn mới; đơn cũ giữ snapshot. Dữ liệu backfill migration dùng bank hiện tại cho đơn cũ trước tính năng snapshot, không thể khôi phục bank lịch sử đã mất.

## SePay

Kết nối tài khoản ngân hàng trong SePay, chọn webhook JSON POST tới `/api/v1/webhooks/sepay` trên HTTPS, API Key trùng `SePay__WebhookKey` trong Secret. Chọn gửi lại khi non2xx. Không dùng JWT Owner cho webhook. Nhận200 success:true là DB đã commit, chưa có nghĩa mọi giao dịch đã khớp; xem sổ thanh toán. Sai bank/account/nhiều mã/thiếu tiền giữ Unmatched. Không cộng các khoản thiếu. Dư tiền và tiền tới đơn đã chủ động hủy cần Owner xử lý thủ công. Không có hoàn tiền tự động.

## Quyền và trạng thái

Owner xác nhận không cần amount, note tùy chọn; có audit người/thời gian. Manager chỉ đọc cơ sở mình. Hết15 phút ẩn QR và Worker tự hủy; đủ tiền muộn khôi phục chỉ đơn hết hạn. Một giao dịch báo lặp không đổi stats lần nữa. Manual rồi tiền thực về vẫn giữ ledger để đối chiếu. Owner xem giao dịch không khớp được xác định owner duy nhất theo tài khoản; giao dịch không xác định được owner không trả cho tenant bất kỳ, cần operator đối chiếu DB an toàn.

## Messaging và vận hành

Outbox cùng transaction order/payment; publisher dùng durable topic, mandatory và publisher confirms; delivery ít nhất một lần. Claim30 giây, Worker poll5 giây, expiry15 giây; scoped system DB. Inbox key consumer/eventId và side-effect cùng transaction. InApp xử lý payment.late/overpaid; hàng chờ tương lai notifier/realtime/webhook-out được lưu nhưng consumer Zalo/SignalR thuộc phase4/6. Retry2/4/8/16/32 giây tối đa5 lần rồi inapp.q.dlq, có OperationalAlert bền vững. Operator theo dõi worker errors, outbox lag, expired pending và DLQ bằng Rabbit management/DB; không ghi raw payload/Authorization trong log.

Broker lỗi không rollback order đã commit; fix broker và đợi claim hết hạn. Replay DLQ giữ nguyên eventId/consumer, không xóa inbox để ép side effect. Sao lưu DB trước migration; rollback binary chỉ với schema còn tương thích. Không chạy migration Down sau khi đã nhận tiền vì mất ledger/stats/audit; hoàn tác release bằng bản tương thích và giữ dữ liệu, xử lý forward migration.

## Kiểm chứng thật

Cần Owner tự cho phép giao dịch thử và HTTPS callback/SePay credential. Chưa có thì UAT pending. Không tự mở tunnel hoặc chuyển tiền. Xem03-VERIFICATION/SUMMARY để biết test nào đã chạy.
