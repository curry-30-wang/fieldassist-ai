[CmdletBinding()]
param(
    [string]$OutputRoot,
    [string]$Version = "1.0.3",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $projectRoot "outputs"
}
$outputDirectory = (Resolve-Path (New-Item -ItemType Directory -Path $OutputRoot -Force)).Path
$appProject = Join-Path $projectRoot "src\AsphaltPlantManager.App\AsphaltPlantManager.App.csproj"
$setupProject = Join-Path $projectRoot "installer\SetupBootstrapper\SetupBootstrapper.csproj"
$publishDirectory = Join-Path $outputDirectory "Installer-Publish-$Version"
$setupPublishDirectory = Join-Path $outputDirectory "Installer-Setup-Publish-$Version"
$payloadZip = Join-Path $outputDirectory "Installer-Payload-$Version.zip"
$installerPath = Join-Path $outputDirectory "AsphaltPlantManager-Setup-$Version.exe"
$hashPath = "$installerPath.sha256.txt"

Remove-Item -LiteralPath $publishDirectory -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $setupPublishDirectory -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $payloadZip -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $installerPath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $hashPath -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $publishDirectory,$setupPublishDirectory -Force | Out-Null

$localSdk = Join-Path $projectRoot "work\dotnet-sdk\8.0.100\dotnet.exe"
if (Test-Path -LiteralPath $localSdk) {
    $dotnet = $localSdk
    $env:DOTNET_ROOT = Split-Path -Parent $localSdk
} else {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}

Push-Location $projectRoot
try {
    if (-not $SkipTests) {
        & $dotnet test AsphaltPlantManager.sln --no-restore --configuration Release
        if ($LASTEXITCODE -ne 0) { throw "Tests failed; installer build stopped." }
    }

    & $dotnet publish $appProject --configuration Release --runtime win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishTrimmed=false -p:DebugType=None -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

    Copy-Item -LiteralPath (Join-Path $projectRoot "docs\user-guide.md") -Destination (Join-Path $publishDirectory "UserGuide.md") -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot "scripts\Install-Local.ps1") -Destination (Join-Path $publishDirectory "Install-Local.ps1") -Force
    Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $payloadZip -Force

    & $dotnet publish $setupProject --configuration Release --runtime win-x64 --self-contained true `
        -p:PayloadPath=$payloadZip -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishTrimmed=false -p:DebugType=None -o $setupPublishDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Installer bootstrapper build failed."
    }

    $setupExecutable = Join-Path $setupPublishDirectory "SetupBootstrapper.exe"
    if (-not (Test-Path -LiteralPath $setupExecutable)) {
        throw "Installer bootstrapper executable was not produced."
    }
    Copy-Item -LiteralPath $setupExecutable -Destination $installerPath -Force

    $hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
    Set-Content -LiteralPath $hashPath -Value "$hash  $(Split-Path -Leaf $installerPath)" -Encoding ASCII

    Remove-Item -LiteralPath $payloadZip,$publishDirectory,$setupPublishDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
finally {
    Pop-Location
}

Write-Host "Installer: $installerPath"
Write-Host "SHA256:    $hashPath"
