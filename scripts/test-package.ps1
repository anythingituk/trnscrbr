param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "src\Trnscrbr\Trnscrbr.csproj"
$installerDir = Join-Path $repoRoot "artifacts\installer"
$projectXml = [xml](Get-Content $project)
$version = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
$expectedInstaller = Join-Path $installerDir "Trnscrbr-Setup-$version-$Runtime.exe"

if (Test-Path $expectedInstaller) {
    Remove-Item -Force $expectedInstaller
}

& (Join-Path $PSScriptRoot "publish-win-x64.ps1") -Runtime $Runtime -BuildInstaller
if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
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
