# ── Stage 1: build ────────────────────────────────────────────────────────────
# Build context must be the repository root:
#   docker build -f src/LearnLuxembourgish.Api/Dockerfile -t learnlux-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first for better layer caching
COPY LearnLuxembourgish.slnx ./
COPY src/LearnLuxembourgish.Api/LearnLuxembourgish.Api.csproj                         src/LearnLuxembourgish.Api/
COPY src/LearnLuxembourgish.Data.Shared/LearnLuxembourgish.Data.Shared.csproj         src/LearnLuxembourgish.Data.Shared/
COPY src/LearnLuxembourgish.Data.SQLite/LearnLuxembourgish.Data.SQLite.csproj         src/LearnLuxembourgish.Data.SQLite/
COPY src/LearnLuxembourgish.Shared/LearnLuxembourgish.Shared.csproj                   src/LearnLuxembourgish.Shared/
COPY src/LearnLuxembourgish.ServiceDefaults/LearnLuxembourgish.ServiceDefaults.csproj src/LearnLuxembourgish.ServiceDefaults/

RUN dotnet restore src/LearnLuxembourgish.Api/LearnLuxembourgish.Api.csproj

# Copy the rest of the relevant source
COPY src/LearnLuxembourgish.Api/           src/LearnLuxembourgish.Api/
COPY src/LearnLuxembourgish.Data.Shared/   src/LearnLuxembourgish.Data.Shared/
COPY src/LearnLuxembourgish.Data.SQLite/   src/LearnLuxembourgish.Data.SQLite/
COPY src/LearnLuxembourgish.Shared/        src/LearnLuxembourgish.Shared/
COPY src/LearnLuxembourgish.ServiceDefaults/ src/LearnLuxembourgish.ServiceDefaults/

RUN dotnet publish src/LearnLuxembourgish.Api/LearnLuxembourgish.Api.csproj \
    -c Release -o /app/publish --no-self-contained

# ── Stage 2: runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Ensure the Development appsettings is NEVER present in the image.
# (It shouldn't be in publish output, but this is an explicit safety net.)
RUN rm -f appsettings.Development.json

# All sensitive configuration is supplied at runtime via environment variables.
# See .env.template for the full list.  Required at startup:
#   Jwt__SecretKey, AzureAd__TenantId, AzureAd__ClientId
#   ConnectionStrings__DefaultConnection (or mount a SQLite volume)
#
# Optional (features degrade gracefully if absent):
#   Translation__DeepL__ApiKey, Translation__Mistral__ApiKey
#   Grammar__Mistral__ApiKey, Grammar__Groq__ApiKey
#   AllowedOrigins__0

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080
# SQLite DB is expected under /data at runtime (mount a volume there)
ENV SQLITE_DATA_DIR=/data

EXPOSE 8080

# Create a non-root user
RUN adduser --disabled-password --gecos "" appuser && mkdir /data && chown appuser /data
USER appuser

ENTRYPOINT ["dotnet", "LearnLuxembourgish.Api.dll"]
