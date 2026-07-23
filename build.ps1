param(
    [string]$Configuration = "Debug",
    [string]$GStarCADPath = "C:\Program Files\GStarCAD\GStarCAD 2022",
    [switch]$NoDeploy
)

$ErrorActionPreference = "Stop"
$projectDir = "$PSScriptRoot\src\GStarCad.Net.Demo"
$outputDir = "$projectDir\bin\$Configuration\net48"

Write-Host "=== GStarCAD Plugin Build ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host ""

Write-Host "[1/2] Building..." -ForegroundColor Yellow
dotnet build "$projectDir\GStarCad.Net.Demo.csproj" -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "Build succeeded." -ForegroundColor Green

if ($NoDeploy) {
    Write-Host "[2/2] Skipping deploy (--NoDeploy)." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Output: $outputDir" -ForegroundColor Cyan
    exit 0
}

$pluginDir = "$GStarCADPath\Plugins"
Write-Host "[2/2] Deploying to $pluginDir ..." -ForegroundColor Yellow

if (-not (Test-Path $pluginDir)) {
    New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
}

Copy-Item -Path "$outputDir\*" -Destination $pluginDir -Recurse -Force
Write-Host "Deploy complete." -ForegroundColor Green
Write-Host ""
Write-Host "Plugin deployed to: $pluginDir" -ForegroundColor Cyan
Write-Host "Use NETLOAD command in GStarCAD to load GStarCad.Net.Demo.dll" -ForegroundColor Cyan
