# ChildGuard AdminWeb (server-rendered admin UI). Build context is the repository root:
#   docker build -f src/ChildGuard.AdminWeb/Dockerfile .
# It talks to the API over Api__BaseUrl; configuration and secrets come from environment variables.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/ChildGuard.Domain/ChildGuard.Domain.csproj src/ChildGuard.Domain/
COPY src/ChildGuard.Application/ChildGuard.Application.csproj src/ChildGuard.Application/
COPY src/ChildGuard.AdminWeb/ChildGuard.AdminWeb.csproj src/ChildGuard.AdminWeb/
RUN dotnet restore src/ChildGuard.AdminWeb/ChildGuard.AdminWeb.csproj

COPY src/ src/
RUN dotnet publish src/ChildGuard.AdminWeb/ChildGuard.AdminWeb.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Listens on 10000 by default (Render's port). A host that assigns its own port in the PORT environment
# variable is honoured instead; ASPNETCORE_URLS overrides both. Api__BaseUrl may be any http(s) address,
# including a path prefix (https://school.org/childguard-api).
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=10000

USER app
EXPOSE 10000
ENTRYPOINT ["dotnet", "ChildGuard.AdminWeb.dll"]
