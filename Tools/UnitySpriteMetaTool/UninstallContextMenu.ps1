[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$shellKey = "HKCU:\Software\Classes\Directory\Background\shell\UnitySpriteMetaGenerator"

if (Test-Path -LiteralPath $shellKey) {
    Remove-Item -LiteralPath $shellKey -Recurse -Force
    Write-Host "Context menu removed: Generate Unity Sprite Meta"
} else {
    Write-Host "Context menu is not installed."
}

foreach ($extension in @('.png', '.jpg', '.jpeg')) {
    $imageShellKey = "HKCU:\Software\Classes\SystemFileAssociations\$extension\shell\UnitySpriteBorderEditor"
    if (Test-Path -LiteralPath $imageShellKey) {
        Remove-Item -LiteralPath $imageShellKey -Recurse -Force
    }
}
Write-Host "Image border context menu removed."
