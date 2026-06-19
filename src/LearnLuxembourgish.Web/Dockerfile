# ── Stage 1: build ────────────────────────────────────────────────────────────
# Build context must be the repository root:
#   docker build -f src/LearnLuxembourgish.Web/Dockerfile -t learnlux-web .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first for better layer caching
COPY LearnLuxembourgish.slnx ./
COPY src/LearnLuxembourgish.Web/LearnLuxembourgish.Web.csproj                         src/LearnLuxembourgish.Web/
COPY src/LearnLuxembourgish.Shared/LearnLuxembourgish.Shared.csproj                   src/LearnLuxembourgish.Shared/
COPY src/LearnLuxembourgish.ServiceDefaults/LearnLuxembourgish.ServiceDefaults.csproj src/LearnLuxembourgish.ServiceDefaults/

RUN dotnet restore src/LearnLuxembourgish.Web/LearnLuxembourgish.Web.csproj

# Copy the rest of the relevant source
COPY src/LearnLuxembourgish.Web/           src/LearnLuxembourgish.Web/
COPY src/LearnLuxembourgish.Shared/        src/LearnLuxembourgish.Shared/
COPY src/LearnLuxembourgish.ServiceDefaults/ src/LearnLuxembourgish.ServiceDefaults/

RUN dotnet publish src/LearnLuxembourgish.Web/LearnLuxembourgish.Web.csproj \
    -c Release -o /app/publish --no-self-contained

# ── Stage 2: runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

RUN rm -f appsettings.Development.json

# All sensitive configuration is supplied at runtime via environment variables.
# See .env.template for the full list.  Required at startup:
#   AzureAd__TenantId, AzureAd__ClientId
#   Services__api__http__0  (URL of the API container, e.g. http://api:8080)

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

RUN adduser --disabled-password --gecos "" appuser
USER appuser

ENTRYPOINT ["dotnet", "LearnLuxembourgish.Web.dll"]
