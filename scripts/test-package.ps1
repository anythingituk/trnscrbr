param(
    [string]$Runtime = "win-x64",
    [switch]$SkipInstallSmokeTest,
    [switch]$SkipLaunchSmokeTest
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

if ($SkipInstallSmokeTest) {
    Write-Host "Installed-app smoke test skipped."
    return
}

$installDir = Join-Path $env:TEMP "Trnscrbr-package-smoke-$([Guid]::NewGuid().ToString('N'))"
$installedExe = Join-Path $installDir "Trnscrbr.exe"

try {
    $install = Start-Process -FilePath $expectedInstaller -ArgumentList @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/NOICONS",
        "/DIR=$installDir",
        "/TASKS="
    ) -Wait -PassThru

    if ($install.ExitCode -ne 0) {
        throw "Installer failed with exit code $($install.ExitCode)"
    }

    if (-not (Test-Path $installedExe)) {
        throw "Installed executable was not found: $installedExe"
    }

    $installedVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($installedExe).ProductVersion
    if (-not $installedVersion.StartsWith($version, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Installed executable version mismatch. Expected $version, got $installedVersion."
    }

    Write-Host "Installed-app smoke test passed: $installedExe"
    Write-Host "Installed version: $installedVersion"

    if ($SkipLaunchSmokeTest) {
        Write-Host "Installed-app launch smoke test skipped."
    }
    elseif (Get-Process -Name "Trnscrbr" -ErrorAction SilentlyContinue) {
        Write-Host "Installed-app launch smoke test skipped because Trnscrbr is already running."
    }
    else {
        $launched = Start-Process -FilePath $installedExe -PassThru
        Start-Sleep -Seconds 2
        if ($launched.HasExited) {
            throw "Installed executable exited during launch smoke test with code $($launched.ExitCode)."
        }

        Stop-Process -Id $launched.Id -Force
        $launched.WaitForExit(5000) | Out-Null
        Write-Host "Installed-app launch smoke test passed."
    }
}
finally {
    $uninstaller = Get-ChildItem -Path $installDir -Filter "unins*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($uninstaller) {
        $uninstall = Start-Process -FilePath $uninstaller.FullName -ArgumentList @(
            "/VERYSILENT",
            "/SUPPRESSMSGBOXES",
            "/NORESTART"
        ) -Wait -PassThru
        if ($uninstall.ExitCode -ne 0) {
            Write-Warning "Uninstaller failed with exit code $($uninstall.ExitCode): $($uninstaller.FullName)"
        }
    }

    if (Test-Path $installDir) {
        Remove-Item -Recurse -Force $installDir -ErrorAction SilentlyContinue
    }
}
