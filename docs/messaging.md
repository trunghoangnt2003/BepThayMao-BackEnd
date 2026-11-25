# Vận hành outbox và consumer

Worker chạy độc lập hai vòng: hết hạn đơn mỗi 15 giây và messaging mỗi 5 giây. Messaging dùng kết nối RabbitMQ chung, channel riêng bật publisher confirms; mỗi batch tối đa 20 tin outbox và 20 tin in-app. Claim DB 30 giây, publish timeout 10 giây; không giữ transaction DB khi đợi broker. Worker lỗi sẽ ghi loại lỗi, không ghi exception có thể chứa credential/payload, rồi thử lại.

Exchange `bepthaymao.events` và các queue trong `MessagingTopology` đều durable; message persistent, publish mandatory và chờ confirm trước khi đánh dấu sent hoặc ack tin nguồn. Không có route giữ outbox pending, không chặn tin khác. Sau 5 lần claim, cảnh báo `messaging.outbox_stuck` được ghi một lần cho event; không xóa tin hoặc coi là đã gửi. Theo dõi bản ghi chưa publish và tuổi của chúng để phát hiện broker ngừng hoạt động trước khi claim.

Inbox `(consumer,eventId)` và tác dụng phụ cùng transaction. In-app chỉ xử lý `payment.late`/`payment.overpaid`, không cập nhật DishStat. Lỗi DB rollback rồi publish-confirm sang retry sau 2/4/8/16/32 giây; hết 5 lần retry chuyển DLQ. JSON/schema không hợp lệ đi thẳng DLQ. Cảnh báo `messaging.dead_letter` bền và idempotent; crash có thể giao lại tin nhưng không lặp tác dụng phụ. Các queue notifier/realtime/webhook-out/report giữ tin chờ phase sau; chưa có consumer tương ứng.

## Cấu hình và quyền

Local dùng `npm run dev:worker` và connection string của `deploy/.env`; không commit secret. k3s lấy PostgreSQL từ key `ConnectionStrings__Default` của Secret hiện có `bepthaymao-api-secrets`, AMQP từ key `ConnectionStrings__RabbitMq` của Secret riêng `bepthaymao-worker-secrets`. Operator phải tạo Secret này ngoài Git trước rollout; không chạy apply vào cluster từ bước verify. Không cấp Redis, JWT hoặc SePay key cho Worker.

Tạo user RabbitMQ riêng, không tag `administrator`, chỉ truy cập vhost của ứng dụng. Quyền configure/read/write chỉ giới hạn các tài nguyên cần dùng, ví dụ lệnh sau chạy bởi operator đã xác thực, với user `btm-worker` đã được tạo bằng secret an toàn:

```sh
rabbitmqctl set_permissions -p /btm btm-worker '^(bepthaymao\.events|(inapp|notifier|realtime|webhook-out|report)\.q(\.retry\.[1-5]|\.dlq)?)$' '^(bepthaymao\.events|amq\.default|(inapp|notifier|realtime|webhook-out|report)\.q(\.retry\.[1-5]|\.dlq)?)$' '^(bepthaymao\.events|inapp\.q)$'
```

Worker cần configure các queue tương lai để giữ event, write các queue để bind/dead-letter, read exchange để bind và read `inapp.q` để consume. Production infrastructure/vhost/TLS thuộc Phase 6; ví dụ này chưa chứng minh quyền trên cluster thật.

## Theo dõi và phục hồi

Meter `Bepthaymao.Messaging`: `bepthaymao.outbox.published`, `.unroutable`, `.publish_lag` (giây), `.dispatch_failures`; `bepthaymao.consumer.retried`, `.dead_lettered`. Không có tag payload/PII. Exporter/cảnh báo vận hành thật cần cấu hình ở Phase 6; meter trong tiến trình chưa chứng minh dashboard hoạt động. RabbitMQ management cho biết queue depth/DLQ và consumer; Worker không tự chạy consumer của phase sau.

Khi lỗi: kiểm tra kết nối/quyền broker, tuổi outbox pending và queue DLQ; sửa nguyên nhân trước khi operator redrive. Giữ nguyên envelope/eventId khi redrive để inbox chống tác dụng phụ trùng. Không purge hàng chờ và không cập nhật `published_at` thủ công để che lỗi. Quy trình này không tự gửi webhook đối tác, Zalo hoặc chuyển tiền.
