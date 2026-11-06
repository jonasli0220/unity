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
    $progIds = New-Object System.Collections.Generic.List[string]
    if (Test-Path -LiteralPath $imageShellKey) {
        $registration = Get-ItemProperty -LiteralPath $imageShellKey
        if ($registration.PSObject.Properties.Name -contains 'DragonSpriteMetaProgId') {
            $stored = $registration.DragonSpriteMetaProgId
            if (-not [string]::IsNullOrWhiteSpace($stored)) { $progIds.Add($stored) }
        }
        Remove-Item -LiteralPath $imageShellKey -Recurse -Force
    }
    $choiceKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\$extension\UserChoice"
    if (Test-Path -LiteralPath $choiceKey) {
        $current = (Get-ItemProperty -LiteralPath $choiceKey).ProgId
        if (-not [string]::IsNullOrWhiteSpace($current)) { $progIds.Add($current) }
    }
    foreach ($progId in ($progIds | Select-Object -Unique)) {
        if ($progId -notmatch '^[A-Za-z0-9_.-]+$') { continue }
        $progShellKey = "HKCU:\Software\Classes\$progId\shell\UnitySpriteBorderEditor"
        if (Test-Path -LiteralPath $progShellKey) { Remove-Item -LiteralPath $progShellKey -Recurse -Force }
    }
}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class UnitySpriteMetaShellNotify {
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
'@
[UnitySpriteMetaShellNotify]::SHChangeNotify(0x08000000, 0x1000, [IntPtr]::Zero, [IntPtr]::Zero)

$sendToLink = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::SendTo)) '设置 Unity Sprite 九宫.lnk'
if (Test-Path -LiteralPath $sendToLink) {
    $shortcutShell = New-Object -ComObject WScript.Shell
    $shortcut = $shortcutShell.CreateShortcut($sendToLink)
    if ($shortcut.Description -like 'UnitySpriteMetaTool:*') {
        Remove-Item -LiteralPath $sendToLink -Force
    }
}
Write-Host "Image border context menu removed."
