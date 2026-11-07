[CmdletBinding()]
param(
    [string] $MenuName = (([string][char]29983) + ([string][char]25104) + " Life Sprite Meta"),
    [string] $GeneratorPath = "",
    [string] $ConfigPath = "",
    [string] $ProjectRoot = "",
    [switch] $NoPrompt,
    [switch] $AllUsers
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($AllUsers -and -not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $process = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -Wait -PassThru -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`" -AllUsers -NoPrompt"
    if ($process.ExitCode -ne 0) { throw 'Life menu installation did not complete.' }
    return
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$borderEditorPath = Join-Path $scriptRoot "SetUnitySpriteBorder.ps1"

if ([string]::IsNullOrWhiteSpace($GeneratorPath)) {
    $GeneratorPath = Join-Path $scriptRoot "GenerateUnitySpriteMeta.ps1"
}

if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $scriptRoot "config.json"
}

if (-not (Test-Path -LiteralPath $GeneratorPath -PathType Leaf)) {
    throw "Generator script not found: $GeneratorPath"
}
if (-not (Test-Path -LiteralPath $borderEditorPath -PathType Leaf)) {
    throw "Border editor script not found: $borderEditorPath"
}

# Installation is project-independent; the selected Assets path identifies the
# project at runtime. Keep ProjectRoot/NoPrompt accepted for older launchers.
$defaultLogRoot = "C:\tmp\LifeMetaGenerator"
if (Test-Path -LiteralPath $ConfigPath -PathType Leaf) {
    $config = Get-Content -Raw -Encoding UTF8 -LiteralPath $ConfigPath | ConvertFrom-Json
    if ($config.PSObject.Properties.Name -contains 'menuName' -and -not [string]::IsNullOrWhiteSpace($config.menuName)) { $MenuName = $config.menuName }
    if ($config.PSObject.Properties.Name -contains 'logRoot' -and -not [string]::IsNullOrWhiteSpace($config.logRoot)) { $defaultLogRoot = $config.logRoot }
}
if (-not (Test-Path -LiteralPath (Join-Path $scriptRoot 'UnityAssetPath.ps1') -PathType Leaf)) {
    throw 'UnityAssetPath.ps1 is missing. Extract the complete tool package before installing.'
}
$newConfig = [ordered]@{ menuName = $MenuName; logRoot = $defaultLogRoot }
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$configJson = ($newConfig | ConvertTo-Json -Depth 4)
[System.IO.File]::WriteAllText($ConfigPath, $configJson + [Environment]::NewLine, $utf8NoBom)

$shellKey = "HKCU:\Software\Classes\Directory\Background\shell\LifeSpriteMetaGenerator"
$commandKey = Join-Path $shellKey "command"
$command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -NoExit -File `"$GeneratorPath`" -Path `"%V`" -ConfigPath `"$ConfigPath`""

New-Item -Path $shellKey -Force | Out-Null
New-Item -Path $commandKey -Force | Out-Null

Set-Item -Path $shellKey -Value $MenuName
Set-ItemProperty -Path $shellKey -Name "Icon" -Value "powershell.exe"
Set-Item -Path $commandKey -Value $command

# Use one verb in the normal file-command group. The X prefix sorts after
# VSCode and WeType without forcing the command above Open or below Delete.
$directKey = 'HKCU:\Software\Classes\*\shell\X.LifeSpriteBorder'
if ($AllUsers) { $directKey = 'HKLM:\Software\Classes\*\shell\X.LifeSpriteBorder' }
$directCommand = Join-Path $directKey 'command'
$powershellPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
New-Item -Path $directCommand -Force | Out-Null
Set-Item -LiteralPath $directKey -Value '设置 Life Sprite 九宫'
Set-ItemProperty -LiteralPath $directKey -Name 'MUIVerb' -Value '设置 Life Sprite 九宫'
Set-ItemProperty -LiteralPath $directKey -Name 'Icon' -Value $powershellPath
if ((Get-Item -LiteralPath $directKey).GetValueNames() -contains 'Position') {
    Remove-ItemProperty -LiteralPath $directKey -Name 'Position'
}
Set-ItemProperty -LiteralPath $directKey -Name 'MultiSelectModel' -Value 'Single'
Set-ItemProperty -LiteralPath $directKey -Name 'AppliesTo' -Value 'System.FileExtension:=".png" OR System.FileExtension:=".jpg" OR System.FileExtension:=".jpeg"'
Set-Item -LiteralPath $directCommand -Value "`"$powershellPath`" -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$borderEditorPath`" -ImagePath `"%1`" -ConfigPath `"$ConfigPath`""

# Migrate only this tool's old registrations; duplicate association verbs can
# interfere with visibility and must not be recreated on reinstall.
$obsoleteKeys = @('HKCU:\Software\Classes\*\shell\Life.SpriteBorder')
if ($AllUsers) {
    $obsoleteKeys += 'HKLM:\Software\Classes\*\shell\Life.SpriteBorder'
    $obsoleteKeys += 'HKCU:\Software\Classes\*\shell\X.LifeSpriteBorder'
}
foreach ($extension in @('.png', '.jpg', '.jpeg')) {
    $associationKey = "HKCU:\Software\Classes\SystemFileAssociations\$extension\shell\LifeSpriteBorderEditor"
    $progIds = @()
    if (Test-Path -LiteralPath $associationKey) {
        $progIds += (Get-Item -LiteralPath $associationKey).GetValue('LifeSpriteMetaProgId')
    }
    $choiceKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\$extension\UserChoice"
    if (Test-Path -LiteralPath $choiceKey) { $progIds += (Get-Item -LiteralPath $choiceKey).GetValue('ProgId') }
    foreach ($extensionKey in @("HKCU:\Software\Classes\$extension", "Registry::HKEY_CLASSES_ROOT\$extension")) {
        if (Test-Path -LiteralPath $extensionKey) { $progIds += (Get-Item -LiteralPath $extensionKey).GetValue('') }
    }
    foreach ($progId in ($progIds | Select-Object -Unique)) {
        if ($progId -match '^[A-Za-z0-9_.-]+$') { $obsoleteKeys += "HKCU:\Software\Classes\$progId\shell\LifeSpriteBorderEditor" }
    }
    $obsoleteKeys += $associationKey
}
foreach ($obsoleteKey in ($obsoleteKeys | Select-Object -Unique)) {
    if (Test-Path -LiteralPath $obsoleteKey) { Remove-Item -LiteralPath $obsoleteKey -Recurse -Force }
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

$sendToFolder = [Environment]::GetFolderPath([Environment+SpecialFolder]::SendTo)
$sendToLink = Join-Path $sendToFolder '设置 Life Sprite 九宫.lnk'
$shortcutShell = New-Object -ComObject WScript.Shell
$shortcut = $shortcutShell.CreateShortcut($sendToLink)
$shortcut.TargetPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$borderEditorPath`" -ImagePath"
$shortcut.WorkingDirectory = $scriptRoot
$shortcut.IconLocation = "$($shortcut.TargetPath),0"
$shortcut.Description = 'LifeSpriteMetaTool: set Sprite border and generate meta'
$shortcut.Save()

Write-Host "Context menu installed: $MenuName"
Write-Host "Location: folder background right-click"
Write-Host "Scope: Assets/Content/UI in Life projects (project path detected automatically)."

Write-Host "Config: $ConfigPath"
Write-Host "Command: $command"
Write-Host "Image context menu installed: Set Unity Sprite border (.png, .jpg, .jpeg)"
Write-Host "Send To fallback installed: $sendToLink"
Write-Host "Windows 11: right-click an image, then choose Show more options (or press Shift+F10) to find the border editor."
