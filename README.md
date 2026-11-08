# Bếp Thầy Mão — Backend

.NET 10, Clean Architecture và CQRS. Repo độc lập trong workspace ba repo;
spec, ADR và kế hoạch nằm tại repo BepThayMao-Spec bên ngoài repo này.

Yêu cầu: .NET SDK 10.0.400; Node 24.22.2 cho hook/script; Docker Desktop cho
test tích hợp và hạ tầng dev ở các bước tiếp theo.

Chạy từ thư mục repo backend:

```sh
dotnet restore Bepthaymao.slnx --locked-mode
dotnet format Bepthaymao.slnx --verify-no-changes
dotnet build Bepthaymao.slnx -warnaserror
dotnet test --solution Bepthaymao.slnx
```

`Domain` không phụ thuộc lớp khác; `Application` chỉ tham chiếu Domain;
`Infrastructure` triển khai cổng Application; Api và Worker là hai entry point.
Test kiến trúc kiểm ProjectReference và dependency assembly. Host đã có OpenAPI
versioned, CQRS decorators, ProblemDetails, correlation ID và structured logging.
Chưa có endpoint nghiệp vụ; persistence, readiness hạ tầng thực và use case công
khai tiếp tục theo kế hoạch Phase 1.

Chạy host local:

```sh
dotnet run --project src/Bepthaymao.Api --no-launch-profile --urls http://127.0.0.1:5080
```

Kiểm tra `/health/live` và `/openapi/v1.json`. `/health/ready` hiện chưa có check
PostgreSQL/Redis/RabbitMQ nên không dùng kết quả đó để khẳng định hạ tầng đã sẵn sàng.

Nhánh tích hợp là `develop`; commit qua hook format/secret scan/commitlint.
