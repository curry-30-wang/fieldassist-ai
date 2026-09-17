[CmdletBinding()]
param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot "..\outputs"),
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$appProject = Join-Path $projectRoot "src\AsphaltPlantManager.App\AsphaltPlantManager.App.csproj"
$portableDirectory = Join-Path $OutputRoot "AsphaltPlantManager-Portable"
$zipPath = Join-Path $OutputRoot "AsphaltPlantManager-Portable.zip"

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
Remove-Item -LiteralPath $portableDirectory -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue

Push-Location $projectRoot
try {
    if (-not $SkipTests) {
        dotnet test AsphaltPlantManager.sln -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Tests failed; release stopped." }
    }

    dotnet publish $appProject -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $portableDirectory
    if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

    $readme = Join-Path $projectRoot "docs\user-guide.md"
    Copy-Item -LiteralPath $readme -Destination (Join-Path $portableDirectory "UserGuide.md") -Force
    Compress-Archive -LiteralPath $portableDirectory -DestinationPath $zipPath -Force
    Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath |
        ForEach-Object { "$($_.Hash)  $($_.Path | Split-Path -Leaf)" } |
        Set-Content -LiteralPath (Join-Path $OutputRoot "SHA256.txt") -Encoding utf8
}
finally {
    Pop-Location
}

Write-Host "Release complete: $portableDirectory"
