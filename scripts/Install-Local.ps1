$ErrorActionPreference = "Stop"

$targetRoot = if ([string]::IsNullOrWhiteSpace($env:ASPHALT_PLANT_MANAGER_INSTALL_ROOT)) {
    Join-Path $env:LOCALAPPDATA "AsphaltPlantManager"
} else {
    $env:ASPHALT_PLANT_MANAGER_INSTALL_ROOT
}
$target = [IO.Path]::GetFullPath($targetRoot)
New-Item -ItemType Directory -Path $target -Force | Out-Null

Get-ChildItem -LiteralPath $PSScriptRoot -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force
}

$templateZip = Join-Path $target "templates.zip"
if (Test-Path -LiteralPath $templateZip) {
    Expand-Archive -LiteralPath $templateZip -DestinationPath $target -Force
    Remove-Item -LiteralPath $templateZip -Force
}

$appPath = Join-Path $target "AsphaltPlantManager.App.exe"
if ($env:ASPHALT_PLANT_MANAGER_SKIP_SHORTCUTS -ne "1") {
    $shell = New-Object -ComObject WScript.Shell
    $desktopDirectory = if ([string]::IsNullOrWhiteSpace($env:ASPHALT_PLANT_MANAGER_DESKTOP_ROOT)) {
        [Environment]::GetFolderPath("Desktop")
    } else {
        $env:ASPHALT_PLANT_MANAGER_DESKTOP_ROOT
    }
    $programsDirectory = if ([string]::IsNullOrWhiteSpace($env:ASPHALT_PLANT_MANAGER_PROGRAMS_ROOT)) {
        [Environment]::GetFolderPath("Programs")
    } else {
        $env:ASPHALT_PLANT_MANAGER_PROGRAMS_ROOT
    }
    New-Item -ItemType Directory -Path $desktopDirectory,$programsDirectory -Force | Out-Null
    $desktopShortcutPath = Join-Path $desktopDirectory "AsphaltPlantManager.lnk"
    $shortcut = $shell.CreateShortcut($desktopShortcutPath)
    $shortcut.TargetPath = $appPath
    $shortcut.WorkingDirectory = $target
    $shortcut.IconLocation = "$appPath,0"
    $shortcut.Save()

    $startMenuDirectory = Join-Path $programsDirectory "AsphaltPlantManager"
    New-Item -ItemType Directory -Path $startMenuDirectory -Force | Out-Null
    $startShortcut = $shell.CreateShortcut((Join-Path $startMenuDirectory "AsphaltPlantManager.lnk"))
    $startShortcut.TargetPath = $appPath
    $startShortcut.WorkingDirectory = $target
    $startShortcut.IconLocation = "$appPath,0"
    $startShortcut.Save()
}

if ($env:ASPHALT_PLANT_MANAGER_SKIP_LAUNCH -ne "1") {
    Start-Process -FilePath $appPath -WorkingDirectory $target
}
