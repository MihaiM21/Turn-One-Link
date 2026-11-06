# build_installer.ps1
# Publishes Turn One Link and packs it with Velopack: Setup.exe + delta update packages in ./Releases.
#
#   ./build_installer.ps1                     # stable channel, unsigned (local test build)
#   ./build_installer.ps1 -Channel beta
#   ./build_installer.ps1 -SignMetadata azure-trusted-signing.json   # signed (CI passes this)
#
# Requires the Velopack CLI: dotnet tool install -g vpk --version 1.2.161
param(
    [string]$Channel = "stable",
    [string]$SignMetadata = "",
    [string]$SentryDsn = ""
)

$ErrorActionPreference = "Stop"

[xml]$proj = Get-Content "Turn One Link.csproj"
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { Write-Error "No <Version> in Turn One Link.csproj"; exit 1 }

Write-Host "1/2: Publishing $version (self-contained, win-x64)..." -ForegroundColor Cyan
if (Test-Path ./publish) { Remove-Item ./publish -Recurse -Force }
dotnet publish 'Turn One Link.csproj' -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:SentryDsn=$SentryDsn -o ./publish
if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed."; exit $LASTEXITCODE }

Write-Host "2/2: Packing with Velopack (channel: $Channel)..." -ForegroundColor Cyan
$packArgs = @(
    "pack",
    "--packId", "TurnOneLink",
    "--packVersion", $version,
    "--packTitle", "Turn One Link",
    "--packAuthors", "Turn One",
    "--packDir", "./publish",
    "--mainExe", "Turn One Link.exe",
    "--icon", "Content/app_icon.ico",
    "--channel", $Channel,
    "--outputDir", "./Releases"
)
if ($SignMetadata) { $packArgs += @("--azureTrustedSignFile", $SignMetadata) }

& vpk @packArgs
if ($LASTEXITCODE -ne 0) { Write-Error "Velopack pack failed."; exit $LASTEXITCODE }

Write-Host "`nDone: ./Releases/TurnOneLink-win-Setup.exe ($version, $Channel)" -ForegroundColor Green
