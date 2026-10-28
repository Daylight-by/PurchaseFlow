# syntax=docker/dockerfile:1
# PurchaseFlow demo container for SnapDeploy.
# .NET is NOT auto-detected by SnapDeploy, so this repo must ship its own Dockerfile.
# The build publishes the Web host and the DbMigrator; on every container start the
# DbMigrator runs first (creates/migrates the SQLite DB and seeds buyer/reviewer roles),
# then the Web host starts.
# The OpenIddict signing certificate is generated fresh inside the build for this demo;
# the throwaway password below is only used for the demo signing cert, never for anything
# sensitive. Override the runtime config via environment variables on the container page.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY app/PurchaseFlow/ ./pf/
WORKDIR /src/pf
RUN dotnet publish src/PurchaseFlow.Web/PurchaseFlow.Web.csproj -c Release -o /out/web
RUN dotnet publish src/PurchaseFlow.DbMigrator/PurchaseFlow.DbMigrator.csproj -c Release -o /out/migrator

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
EXPOSE 8080

# Runtime-only overrides so the demo boots with an absolute SQLite path and a real
# (throwaway) encryption passphrase. Each one can be overridden again on the container page.
ENV ConnectionStrings__Default=Data Source=/app/web/PurchaseFlow.db; \
    StringEncryption__DefaultPassPhrase=PurchaseFlow-Demo-Phrase-ChangeMe-1

COPY --from=build /out/web ./web
COPY --from=build /out/migrator ./migrator

# Generate a throwaway self-signed OpenIddict signing certificate for the demo.
# In non-Development the app refuses to start without OpenIddict:CertificatePassword.
RUN apt-get update \
    && apt-get install -y --no-install-recommends openssl \
    && rm -rf /var/lib/apt/lists/* \
    && openssl req -x509 -newkey rsa:2048 -keyout /tmp/sd-key.pem -out /tmp/sd-cert.pem -days 3650 -nodes -subj "/CN=PurchaseFlow Demo" \
    && openssl pkcs12 -export -out /app/web/openiddict.pfx -inkey /tmp/sd-key.pem -in /tmp/sd-cert.pem -passout pass:PurchaseFlow-Demo-Cert-ChangeMe-1 \
    && rm -f /tmp/sd-key.pem /tmp/sd-cert.pem
ENV OpenIddict__CertificatePassword=PurchaseFlow-Demo-Cert-ChangeMe-1

WORKDIR /app/web
# Start with the database migrator, then launch the web host.
CMD ["/bin/bash", "-c", "dotnet ../migrator/PurchaseFlow.DbMigrator.dll && dotnet PurchaseFlow.Web.dll"]
