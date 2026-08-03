[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$shellKey = "HKCU:\Software\Classes\Directory\Background\shell\DragonQuickReleaseMerge"

if (Test-Path -LiteralPath $shellKey) {
    Remove-Item -LiteralPath $shellKey -Recurse -Force
    Write-Host "Context menu removed: Quick release merge"
}
else {
    Write-Host "Context menu is not installed."
}
