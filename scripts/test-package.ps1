param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$installerDir = Join-Path $repoRoot "artifacts\installer"
$expectedInstaller = Join-Path $installerDir "Trnscrbr-Setup-0.1.0-$Runtime.exe"

& (Join-Path $PSScriptRoot "publish-win-x64.ps1") -Runtime $Runtime -BuildInstaller
if ($LASTEXITCODE -ne 0) {
    throw "Package build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $expectedInstaller)) {
    throw "Expected installer was not created: $expectedInstaller"
}

$installer = Get-Item $expectedInstaller
if ($installer.Length -le 0) {
    throw "Installer file is empty: $expectedInstaller"
}

Write-Host "Package smoke test passed: $expectedInstaller"
Write-Host "Size: $([Math]::Round($installer.Length / 1MB, 2)) MB"
