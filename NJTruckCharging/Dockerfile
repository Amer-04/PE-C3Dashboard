# C3 Site Finder API — container for Render (free web service). Build context: the NJTruckCharging/ folder.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY NJTruckCharging.Domain/NJTruckCharging.Domain.csproj NJTruckCharging.Domain/
COPY NJTruckCharging.Application/NJTruckCharging.Application.csproj NJTruckCharging.Application/
COPY NJTruckCharging.Infrastructure/NJTruckCharging.Infrastructure.csproj NJTruckCharging.Infrastructure/
COPY NJTruckCharging/NJTruckCharging.csproj NJTruckCharging/
RUN dotnet restore NJTruckCharging/NJTruckCharging.csproj
COPY NJTruckCharging.Domain/ NJTruckCharging.Domain/
COPY NJTruckCharging.Application/ NJTruckCharging.Application/
COPY NJTruckCharging.Infrastructure/ NJTruckCharging.Infrastructure/
COPY NJTruckCharging/ NJTruckCharging/
RUN dotnet publish NJTruckCharging/NJTruckCharging.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# Minimal runtime image (no shell, no package manager), runs as a non-root user.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=10000 \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTPS_PORT=443 \
    DOTNET_gcServer=0 \
    DOTNET_GCConserveMemory=5
EXPOSE 10000
ENTRYPOINT ["dotnet", "NJTruckCharging.dll"]
