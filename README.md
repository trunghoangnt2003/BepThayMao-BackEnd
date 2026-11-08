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
Test kiến trúc kiểm ProjectReference và dependency assembly. Skeleton hiện chưa
có endpoint nghiệp vụ; OpenAPI, hạ tầng và use case công khai làm theo plan sau.
