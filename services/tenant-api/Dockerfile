# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY services/_shared/TenantOps.Shared/TenantOps.Shared.csproj services/_shared/TenantOps.Shared/
COPY services/tenant-api/TenantOps.TenantApi.csproj services/tenant-api/
RUN dotnet restore services/tenant-api/TenantOps.TenantApi.csproj
COPY services/_shared/ services/_shared/
COPY services/tenant-api/ services/tenant-api/
RUN dotnet publish services/tenant-api/TenantOps.TenantApi.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "TenantOps.TenantApi.dll"]
