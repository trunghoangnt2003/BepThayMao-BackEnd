# Bếp Thầy Mão — Backend

.NET 10, Clean Architecture và CQRS. Repo độc lập trong workspace ba repo;
spec, ADR và kế hoạch nằm tại repo BepThayMao-Spec bên ngoài repo này.

Yêu cầu: .NET SDK 10.0.400; Node 24 (>=24.15,<25) cho hook/script; Docker Desktop
(Linux containers) cho test tích hợp và hạ tầng dev.

Chạy từ thư mục repo backend:

```sh
npm ci
dotnet tool restore
dotnet restore Bepthaymao.slnx --locked-mode
npm run lint          # dotnet format --verify-no-changes + build -warnaserror
npm test              # gồm Testcontainers PostgreSQL/Redis/RabbitMQ thật
npm run openapi:check # openapi/v1.json tất định và khớp mã
npm run contract:lint # Spectral 0 warning
```

`Domain` không phụ thuộc lớp khác; `Application` chỉ tham chiếu Domain;
`Infrastructure` triển khai cổng Application (EF Core + PostgreSQL); Api và Worker
là hai entry point. Use case mẫu: `GET /api/v1/branches` (`listPublicBranches`)
trả các cơ sở đang hoạt động với sáu trường công khai.

## Môi trường dev

`deploy/compose.dev.yml` dựng PostgreSQL, Redis, RabbitMQ và Traefik; mọi cổng chỉ
bind `127.0.0.1`. API và frontend chạy trên máy host, Traefik gọi tới qua
`host.docker.internal` (Linux đã có `extra_hosts`; đặt `API_BIND_ADDRESS` bằng địa
chỉ bridge Docker, không dùng `0.0.0.0`).

```sh
npm run dev:up        # tạo deploy/.env (mật khẩu ngẫu nhiên, bị .gitignore) rồi up --wait
npm run dev:api       # API tại http://127.0.0.1:5080
npm run dev:smoke -- --start-api --check-ready-503
npm run dev:down      # thêm -- --volumes chỉ khi được phép xóa dữ liệu dev
```

Cổng vào cùng origin: `http://127.0.0.1:8080` (`/api` → API, `/admin` → admin 4201,
còn lại → customer 4200). Cổng nào bị chiếm (Windows hay chặn 5432) thì đổi
`*_PORT` trong `deploy/.env` rồi chạy lại `dev:up`. SePay/Zalo chỉ nhận `Mode=Fake`.

## Dữ liệu mẫu

```sh
npm run dev:db -- migrate
npm run dev:db -- seed   # lần đầu inserted=3; chạy lại inserted=0 updated=0 unchanged=3
```

Seed đi qua command CQRS (validation + một transaction), chạy lại an toàn. Dữ liệu
thật đưa qua `SEED_BRANCHES_FILE` hoặc `-- seed --file <path>`, không commit. Mã
thoát: 0 thành công, 1 lỗi dữ liệu/DB, 2 sai cách dùng hoặc thiếu tệp.

Nhánh tích hợp là `develop`; commit qua hook format/secret scan/commitlint.
