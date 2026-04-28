param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$BuildInstaller
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "src\Trnscrbr\Trnscrbr.csproj"
$publishDir = Join-Path $repoRoot "artifacts\publish\$Runtime"

function Resolve-DotNet {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) {
        return $dotnet.Source
    }

    $candidates = @(
        (Join-Path $env:ProgramFiles "dotnet\dotnet.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "dotnet\dotnet.exe")
    )

    $resolved = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($resolved) {
        return $resolved
    }

    throw ".NET SDK was not found. Install the .NET 8 SDK or add dotnet to PATH."
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [string[]]$Arguments
    )

    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -Wait -PassThru -NoNewWindow
    if ($process.ExitCode -ne 0) {
        throw "$FilePath failed with exit code $($process.ExitCode)"
    }
}

$dotnetPath = Resolve-DotNet

Invoke-Checked $dotnetPath @("restore", $project, "-r", $Runtime)

if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}

Invoke-Checked $dotnetPath @(
    "publish",
    $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-o", $publishDir
)

if ($BuildInstaller) {
    $iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
    $isccPath = if ($iscc) {
        $iscc.Source
    } else {
        $candidates = @(
            (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
            "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
            "C:\Program Files\Inno Setup 6\ISCC.exe"
        )

        $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    }

    if (-not $isccPath) {
        throw "Inno Setup compiler (iscc.exe) was not found. Install Inno Setup or run without -BuildInstaller."
    }

    Invoke-Checked $isccPath @((Join-Path $repoRoot "installer\Trnscrbr.iss"))
}

Write-Host "Published to $publishDir"
