# Lotse as a container: the same app as `dotnet run`, data in a volume, behind any TLS-terminating proxy.
# Build:  docker build -t lotse .
# Run:    docker compose up -d   (see compose.yaml and docs/DEPLOY.md)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore first, from the project files alone, so a content or code change does not re-download every package.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Lotse.Core/Lotse.Core.csproj src/Lotse.Core/
COPY src/Lotse.Infrastructure/Lotse.Infrastructure.csproj src/Lotse.Infrastructure/
COPY src/Lotse.Web/Lotse.Web.csproj src/Lotse.Web/
RUN dotnet restore src/Lotse.Web/Lotse.Web.csproj
COPY src/ src/
COPY content/ content/
RUN dotnet publish src/Lotse.Web/Lotse.Web.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# SQLite file, Data Protection key ring and the speech cache live in one volume; the image itself stays stateless.
RUN mkdir -p /data && chown "$APP_UID" /data
VOLUME /data
ENV Lotse__DataDirectory=/data \
    Lotse__ContentDirectory=/app/content \
    ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# The non-root user the .NET images ship with.
USER $APP_UID
ENTRYPOINT ["dotnet", "Lotse.Web.dll"]
