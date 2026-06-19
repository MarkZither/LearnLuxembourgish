#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build and sign a Release Android AAB for LearnLuxembourgish.

.DESCRIPTION
    Produces a signed Android App Bundle (AAB) ready for upload to the
    Google Play Store.  All signing credentials are supplied as parameters
    so nothing secret is hard-coded or committed.

    Before running for the first time:
      1. Generate a keystore (one-time):
            keytool -genkeypair -v -keystore letzsprooch.jks -alias letzsprooch `
                    -keyalg RSA -keysize 2048 -validity 10000
         Keep this file somewhere SAFE and off the repo.

      2. Copy appsettings.release.json.template -> appsettings.release.json
         (in src/LearnLuxembourgish.Mobile/) and set the real production URL.

      3. Call this script with the keystore details:
            ./scripts/publish-android.ps1 `
                -KeystorePath  "C:\secrets\letzsprooch.jks" `
                -KeystorePass  "your-store-pass" `
                -KeyAlias      "letzsprooch" `
                -KeyPass       "your-key-pass"

.PARAMETER KeystorePath
    Full path to the .jks keystore file.

.PARAMETER KeystorePass
    Password for the keystore.

.PARAMETER KeyAlias
    Alias of the signing key inside the keystore.

.PARAMETER KeyPass
    Password for the signing key.

.PARAMETER Configuration
    Build configuration. Defaults to 'Release'.

.PARAMETER OutputDir
    Where to copy the final AAB. Defaults to ./artifacts/android.
#>
param(
    [Parameter(Mandatory)][string]$KeystorePath,
    [Parameter(Mandatory)][string]$KeystorePass,
    [Parameter(Mandatory)][string]$KeyAlias,
    [Parameter(Mandatory)][string]$KeyPass,
    [string]$Configuration = "Release",
    [string]$OutputDir = "$PSScriptRoot/../artifacts/android"
)

$ErrorActionPreference = "Stop"

$projectPath = "$PSScriptRoot/../src/LearnLuxembourgish.Mobile/LearnLuxembourgish.Mobile.csproj"
$releaseSettings = "$PSScriptRoot/../src/LearnLuxembourgish.Mobile/appsettings.release.json"

# -- Preflight checks ----------------------------------------------------------
if (-not (Test-Path $KeystorePath)) {
    Write-Error "Keystore not found at '$KeystorePath'. See script header for setup instructions."
}

if (-not (Test-Path $releaseSettings)) {
    Write-Warning "appsettings.release.json not found."
    Write-Warning "Copy appsettings.release.json.template -> appsettings.release.json and set the production API URL."
    Write-Warning "Continuing with base appsettings.json only."
}

# -- Absolute path for MSBuild (handles spaces) --------------------------------
$KeystorePath = (Resolve-Path $KeystorePath).Path

# -- Build ---------------------------------------------------------------------
Write-Host "`n==> Publishing Android ($Configuration)..." -ForegroundColor Cyan

dotnet publish $projectPath `
    -f net10.0-android `
    -c $Configuration `
    -p:AndroidKeyStore=true `
    -p:AndroidSigningKeyStore="$KeystorePath" `
    -p:AndroidSigningKeyAlias="$KeyAlias" `
    -p:AndroidSigningKeyPass="$KeyPass" `
    -p:AndroidSigningStorePass="$KeystorePass"

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# -- Copy output ---------------------------------------------------------------
$publishDir = "$PSScriptRoot/../src/LearnLuxembourgish.Mobile/bin/$Configuration/net10.0-android/publish"
$aab = Get-ChildItem -Path $publishDir -Filter "*.aab" -ErrorAction SilentlyContinue | Select-Object -First 1
$apk = Get-ChildItem -Path $publishDir -Filter "*-Signed.apk" -ErrorAction SilentlyContinue | Select-Object -First 1

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

if ($aab) {
    Copy-Item $aab.FullName -Destination $OutputDir -Force
    Write-Host "`n==> AAB copied to: $OutputDir\$($aab.Name)" -ForegroundColor Green
}
if ($apk) {
    Copy-Item $apk.FullName -Destination $OutputDir -Force
    Write-Host "==> APK copied to: $OutputDir\$($apk.Name)" -ForegroundColor Green
}

if (-not $aab -and -not $apk) {
    Write-Warning "No AAB or signed APK found in $publishDir"
    Get-ChildItem $publishDir | Select-Object Name | Format-Table
}
