#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build and optionally push LearnLuxembourgish Docker images.

.DESCRIPTION
    Builds learnluxembourgish-api and learnluxembourgish-web images from the
    repository root using multi-stage Dockerfiles.  No secrets are baked into
    the images — they are passed at container runtime via env vars.

    Usage:
        # Build only (for local docker compose testing)
        ./scripts/docker-build-push.ps1 -Username myuser -BuildOnly

        # Build and push to Docker Hub
        ./scripts/docker-build-push.ps1 -Username myuser -Tag v1.2.3

        # Build, tag as latest, and push
        ./scripts/docker-build-push.ps1 -Username myuser

.PARAMETER Username
    Docker Hub username or organisation name.  Images are tagged as
    <Username>/learnluxembourgish-api and <Username>/learnluxembourgish-web.

.PARAMETER Tag
    Image tag.  Defaults to 'latest'.  When pushing you will typically also
    want to pass the git tag or SHA, e.g. -Tag v1.2.3.

.PARAMETER BuildOnly
    Skip the docker push step.  Useful for local testing.

.PARAMETER NoPush
    Alias for -BuildOnly.
#>
param(
    [Parameter(Mandatory)][string]$Username,
    [string]$Tag = "latest",
    [switch]$BuildOnly,
    [switch]$NoPush
)

$ErrorActionPreference = "Stop"
$push = -not ($BuildOnly -or $NoPush)

# Repo root is one directory up from this script
$repoRoot = (Resolve-Path "$PSScriptRoot/..").Path
Set-Location $repoRoot

$apiImage  = "${Username}/learnluxembourgish-api:${Tag}"
$webImage  = "${Username}/learnluxembourgish-web:${Tag}"
$apiLatest = "${Username}/learnluxembourgish-api:latest"
$webLatest = "${Username}/learnluxembourgish-web:latest"

function Build($image, $dockerfile) {
    Write-Host "`n==> Building $image ..." -ForegroundColor Cyan
    docker build -f $dockerfile -t $image .
    if ($LASTEXITCODE -ne 0) { Write-Error "docker build failed for $image"; exit 1 }
}

function Push($image) {
    Write-Host "`n==> Pushing $image ..." -ForegroundColor Cyan
    docker push $image
    if ($LASTEXITCODE -ne 0) { Write-Error "docker push failed for $image"; exit 1 }
}

# ── Build ──────────────────────────────────────────────────────────────────────
Build $apiImage "src/LearnLuxembourgish.Api/Dockerfile"
Build $webImage "src/LearnLuxembourgish.Web/Dockerfile"

# ── Tag as latest if a specific tag was provided ───────────────────────────────
if ($Tag -ne "latest") {
    Write-Host "`n==> Tagging $Tag images as latest ..." -ForegroundColor Cyan
    docker tag $apiImage $apiLatest
    docker tag $webImage $webLatest
}

# ── Push ───────────────────────────────────────────────────────────────────────
if ($push) {
    # Ensure we are logged in
    Write-Host "`n==> Checking Docker Hub login ..." -ForegroundColor Cyan
    docker login --username $Username
    if ($LASTEXITCODE -ne 0) { Write-Error "docker login failed"; exit 1 }

    Push $apiImage
    Push $webImage

    if ($Tag -ne "latest") {
        Push $apiLatest
        Push $webLatest
    }

    Write-Host "`n==> Done. Images pushed:" -ForegroundColor Green
    Write-Host "    $apiImage"
    Write-Host "    $webImage"
    if ($Tag -ne "latest") {
        Write-Host "    $apiLatest"
        Write-Host "    $webLatest"
    }
} else {
    Write-Host "`n==> Build complete (push skipped). To test locally:" -ForegroundColor Green
    Write-Host "    cp .env.template .env"
    Write-Host "    # Fill in .env with real values"
    Write-Host "    docker compose up"
}
