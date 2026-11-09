# Bếp Thầy Mão — Backend

.NET 10, Clean Architecture và CQRS. Repo độc lập trong workspace ba repo;
spec, ADR và kế hoạch nằm tại repo BepThayMao-Spec bên ngoài repo này.

Yêu cầu: .NET SDK 10.0.400; Node 24.22.2 cho hook/script; Docker Desktop cho
test tích hợp và hạ tầng dev. Chạy `npm ci` và `dotnet tool restore` trước.

Chạy từ thư mục repo backend:

```sh
dotnet restore Bepthaymao.slnx --locked-mode
dotnet format Bepthaymao.slnx --verify-no-changes
dotnet build Bepthaymao.slnx -warnaserror
dotnet test --solution Bepthaymao.slnx
```

`Domain` không phụ thuộc lớp khác; `Application` chỉ tham chiếu Domain;
`Infrastructure` triển khai cổng Application; Api và Worker là hai entry point.
Test kiến trúc kiểm ProjectReference và dependency assembly. `GET /api/v1/branches`
trả cơ sở hoạt động với đúng sáu trường công khai; OpenAPI xuất lúc build tại
`openapi/v1.json`, kiểm tất định bằng `npm run openapi:check`.

## Môi trường dev và dữ liệu mẫu

`npm run dev:up` dựng PostgreSQL, Redis, RabbitMQ, Traefik và tạo deploy/.env nếu
chưa có. Nếu cổng 5432 bị Windows giữ, đổi POSTGRES_PORT trong deploy/.env sang cổng
trống, ví dụ 25432. Không commit tệp này.

Chạy `npm run dev:db -- migrate`, rồi `npm run dev:db -- seed` hai lần. Lần hai phải
in `inserted=0 updated=0 unchanged=3`. Dữ liệu mẫu có tên/địa chỉ giả. Dùng
SEED_BRANCHES_FILE để cấp dữ liệu thật ngoài Git. Seed kiểm toàn bộ và SaveChanges
một lần, không ghi một phần khi có lỗi. Migration không tự chạy lúc API khởi động.

`npm run dev:api` chạy API ở 5080; `/health/live` độc lập hạ tầng, `/health/ready`
kiểm đủ ba dịch vụ và cấu hình. Traefik ở http://127.0.0.1:8080 định tuyến /api,
/admin và /. `npm run dev:down` dừng hạ tầng; chỉ thêm `-- --volumes` khi được phép
xóa dữ liệu dev đó.

## Cổng giao hàng

`npm run lint`, `npm test`, `npm run test:scripts`, `npm run build`,
`npm run openapi:check`, `npm run contract:lint`, `npm run contract:breaking`,
`npm run licenses:check`, `npm run deps:vulnerable`, `npm run secrets:scan` và
`npm run k3s:validate`. `npm run drill` cần repo sạch và cố ý tạo lỗi rồi khôi phục.
`npm run docker:build` tạo API/worker non-root; `npm run docker:smoke` cần compose
đã migrate/seed và kiểm image chạy thật. CI tại .github/workflows/ci.yml.

Hướng dẫn đầy đủ ở repo docs: docs/huong-dan/README.md. Xác thực thuộc phase 1.1.
