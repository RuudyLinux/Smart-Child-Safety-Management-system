# ChildGuard API. Build context is the repository root:
#   docker build -f src/ChildGuard.Api/Dockerfile .
# Configuration (connection string, secrets, CORS ...) comes from environment variables at run time;
# nothing secret is baked into the image.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, on project files only, so the layer is reused until a dependency changes.
COPY src/ChildGuard.Domain/ChildGuard.Domain.csproj src/ChildGuard.Domain/
COPY src/ChildGuard.Application/ChildGuard.Application.csproj src/ChildGuard.Application/
COPY src/ChildGuard.Infrastructure/ChildGuard.Infrastructure.csproj src/ChildGuard.Infrastructure/
COPY src/ChildGuard.Infrastructure.Postgres/ChildGuard.Infrastructure.Postgres.csproj src/ChildGuard.Infrastructure.Postgres/
COPY src/ChildGuard.Api/ChildGuard.Api.csproj src/ChildGuard.Api/
RUN dotnet restore src/ChildGuard.Api/ChildGuard.Api.csproj

COPY src/ src/
RUN dotnet publish src/ChildGuard.Api/ChildGuard.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Render sends traffic to port 10000. Production mode; the rolling log file is useless on an
# ephemeral disk, so logs go to the console only.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=10000 \
    LogToFile=false

# The base image ships an unprivileged user named "app".
USER app
EXPOSE 10000
ENTRYPOINT ["dotnet", "ChildGuard.Api.dll"]
