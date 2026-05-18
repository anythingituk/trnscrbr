param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$Runtime = "win-x64",
    [switch]$BuildInstaller,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$solution = Join-Path $repoRoot "Trnscrbr.sln"
$project = Join-Path $repoRoot "src\Trnscrbr\Trnscrbr.csproj"

function Write-Step {
    param([Parameter(Mandatory = $true)][string]$Message)

    Write-Host ""
    Write-Host "==> $Message"
}

function Resolve-CommandPath {
    param([Parameter(Mandatory = $true)][string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    return $null
}

function Resolve-DotNet {
    $dotnet = Resolve-CommandPath "dotnet"
    if ($dotnet) {
        return $dotnet
    }

    $candidates = @(
        (Join-Path $env:ProgramFiles "dotnet\dotnet.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "dotnet\dotnet.exe")
    )

    $resolved = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if ($resolved) {
        return $resolved
    }

    throw ".NET SDK was not found. Install the .NET 8 SDK, then rerun .\setup.ps1."
}

function Assert-Windows {
    if (-not $IsWindows) {
        throw "Trnscrbr is a Windows desktop app. Run setup on Windows 10/11."
    }
}

function Assert-DotNet8Sdk {
    param([Parameter(Mandatory = $true)][string]$DotNetPath)

    $sdks = & $DotNetPath --list-sdks
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to list installed .NET SDKs."
    }

    $hasDotNet8 = $sdks | Where-Object { $_ -match "^8\." } | Select-Object -First 1
    if (-not $hasDotNet8) {
        throw ".NET 8 SDK was not found. Install the .NET 8 SDK, then rerun .\setup.ps1."
    }
}

function Assert-InnoSetup {
    $iscc = Resolve-CommandPath "iscc.exe"
    if ($iscc) {
        return
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    $resolved = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $resolved) {
        throw "Inno Setup 6 was not found. Install it before running .\setup.ps1 -BuildInstaller."
    }
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

Assert-Windows

if (-not (Test-Path $solution)) {
    throw "Solution file was not found: $solution"
}

if (-not (Test-Path $project)) {
    throw "Project file was not found: $project"
}

$dotnet = Resolve-DotNet
Assert-DotNet8Sdk $dotnet

if ($BuildInstaller) {
    Assert-InnoSetup
}

Write-Step "Restoring NuGet packages"
Invoke-Checked $dotnet @("restore", $solution)

if (-not $SkipBuild) {
    Write-Step "Building Trnscrbr ($Configuration)"
    Invoke-Checked $dotnet @(
        "build",
        $solution,
        "--configuration",
        $Configuration,
        "--no-restore"
    )
}

if ($BuildInstaller) {
    Write-Step "Publishing and building installer ($Runtime)"
    & (Join-Path $repoRoot "scripts\publish-win-x64.ps1") `
        -Configuration "Release" `
        -Runtime $Runtime `
        -BuildInstaller

    if ($LASTEXITCODE -ne 0) {
        throw "Installer build failed with exit code $LASTEXITCODE."
    }
}

Write-Step "Setup complete"
Write-Host "Run app: dotnet run --project .\src\Trnscrbr\Trnscrbr.csproj"

if ($BuildInstaller) {
    $projectXml = [xml](Get-Content $project)
    $version = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
    Write-Host "Installer: .\artifacts\installer\Trnscrbr-Setup-$version-$Runtime.exe"
}
