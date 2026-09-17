param(
    [string]$PublishDirectory = (Join-Path $PSScriptRoot '../artifacts/publish/win-x64')
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $PublishDirectory).Path
$executable = 'ModMenagerie.Desktop.exe'
if (!(Test-Path -LiteralPath (Join-Path $source $executable))) { throw 'Publish the Windows application first.' }
$destination = Join-Path $env:LOCALAPPDATA 'Programs/The Mod Menagerie'
$target = Join-Path $destination $executable
if (Get-Process -Name ModMenagerie.Desktop -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $target }) {
    throw 'Close the installed Mod Menagerie before updating it.'
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Get-ChildItem -LiteralPath $source | Copy-Item -Destination $destination -Recurse -Force
$programs = [Environment]::GetFolderPath('Programs')
New-Item -ItemType Directory -Path $programs -Force | Out-Null
$shortcutPath = Join-Path $programs 'The Mod Menagerie.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $target
$shortcut.WorkingDirectory = $destination
$shortcut.IconLocation = "$target,0"
$shortcut.Description = 'Minecraft modpack upgrade-readiness tracker'
$shortcut.Save()
if (!(Test-Path -LiteralPath $target) -or !(Test-Path -LiteralPath $shortcutPath)) { throw 'Installation verification failed.' }
Write-Output "Installed: $target"
Write-Output "Start menu: $shortcutPath"
Write-Output 'Your database and preferences remain in the separate TheModMenagerie app-data folder.'
