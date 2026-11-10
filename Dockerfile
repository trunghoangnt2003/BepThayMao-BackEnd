# Build context: backend/ (FR-022, builds from this repo alone). Targets: api, worker.
# SDK tag matches global.json; runtime images are chiseled and run as the non-root user "app".
FROM mcr.microsoft.com/dotnet/sdk:10.0.400-noble AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Bepthaymao.Domain/*.csproj src/Bepthaymao.Domain/packages.lock.json src/Bepthaymao.Domain/
COPY src/Bepthaymao.Application/*.csproj src/Bepthaymao.Application/packages.lock.json src/Bepthaymao.Application/
COPY src/Bepthaymao.Infrastructure/*.csproj src/Bepthaymao.Infrastructure/packages.lock.json src/Bepthaymao.Infrastructure/
COPY src/Bepthaymao.Api/*.csproj src/Bepthaymao.Api/packages.lock.json src/Bepthaymao.Api/
COPY src/Bepthaymao.Worker/*.csproj src/Bepthaymao.Worker/packages.lock.json src/Bepthaymao.Worker/
RUN dotnet restore src/Bepthaymao.Api --locked-mode && dotnet restore src/Bepthaymao.Worker --locked-mode
COPY src/ src/
# The committed openapi/v1.json is the contract; skip build-time generation inside the image.
RUN dotnet publish src/Bepthaymao.Api -c Release --no-restore -p:OpenApiGenerateDocuments=false -o /out/api \
 && dotnet publish src/Bepthaymao.Worker -c Release --no-restore -o /out/worker

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble-chiseled AS api
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /out/api ./
# Same image runs the one-off migration: args ["db", "migrate"].
ENTRYPOINT ["dotnet", "Bepthaymao.Api.dll"]

# Infrastructure references Serilog.AspNetCore (shared framework Microsoft.AspNetCore.App), so the worker needs aspnet too.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble-chiseled AS worker
WORKDIR /app
COPY --from=build /out/worker ./
ENTRYPOINT ["dotnet", "Bepthaymao.Worker.dll"]
