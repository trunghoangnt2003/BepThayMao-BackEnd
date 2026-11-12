FROM mcr.microsoft.com/dotnet/sdk:10.0.400-noble AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/Bepthaymao.Api --locked-mode && dotnet restore src/Bepthaymao.Worker --locked-mode
RUN dotnet publish src/Bepthaymao.Api -c Release --no-restore -p:OpenApiGenerateDocuments=false -o /out/api && dotnet publish src/Bepthaymao.Worker -c Release --no-restore -o /out/worker
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble-chiseled AS api
WORKDIR /app
COPY --from=build /out/api .
COPY seed ./seed
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Bepthaymao.Api.dll"]
FROM mcr.microsoft.com/dotnet/runtime:10.0.14-noble-chiseled AS worker
WORKDIR /app
COPY --from=build /out/worker .
USER $APP_UID
ENTRYPOINT ["dotnet", "Bepthaymao.Worker.dll"]
