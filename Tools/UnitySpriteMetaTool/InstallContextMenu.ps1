[CmdletBinding()]
param(
    [string] $MenuName = (([string][char]29983) + ([string][char]25104) + " Unity Sprite Meta"),
    [string] $GeneratorPath = "",
    [string] $ConfigPath = "",
    [string] $ProjectRoot = "",
    [switch] $NoPrompt,
    [switch] $AllUsers
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

$defaultProjectRoot = "G:\Dragon\trunk\dragon"
$defaultLogRoot = "C:\tmp\UnityMetaGenerator"
$config = $null

if (Test-Path -LiteralPath $ConfigPath -PathType Leaf) {
    $config = Get-Content -Raw -Encoding UTF8 -LiteralPath $ConfigPath | ConvertFrom-Json
    if ($config.PSObject.Properties.Name -contains "menuName" -and -not [string]::IsNullOrWhiteSpace($config.menuName)) {
        $MenuName = $config.menuName
    }
    if ($config.PSObject.Properties.Name -contains "projectRoot" -and -not [string]::IsNullOrWhiteSpace($config.projectRoot)) {
        $defaultProjectRoot = $config.projectRoot
    }
    if ($config.PSObject.Properties.Name -contains "logRoot" -and -not [string]::IsNullOrWhiteSpace($config.logRoot)) {
        $defaultLogRoot = $config.logRoot
    }
}

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = $defaultProjectRoot
    if (-not $NoPrompt) {
        Write-Host ""
        Write-Host "Unity project root path:"
        Write-Host "Default: $defaultProjectRoot"
        $inputPath = Read-Host "Press Enter to use default, or paste another project root"
        if (-not [string]::IsNullOrWhiteSpace($inputPath)) {
            $ProjectRoot = $inputPath.Trim('"')
        }
    }
}

$ProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot).TrimEnd("\", "/")
$uiRoot = Join-Path $ProjectRoot "Assets\Content\UI"

if (-not (Test-Path -LiteralPath $ProjectRoot -PathType Container)) {
    throw "Project root does not exist: $ProjectRoot"
}

if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot "Assets") -PathType Container)) {
    throw "Assets folder not found under project root: $ProjectRoot"
}

if (-not (Test-Path -LiteralPath $uiRoot -PathType Container)) {
    throw "UI root not found: $uiRoot"
}

$newConfig = [ordered]@{
    projectRoot = $ProjectRoot
    uiRoot = $uiRoot
    menuName = $MenuName
    logRoot = $defaultLogRoot
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$configJson = ($newConfig | ConvertTo-Json -Depth 4)
[System.IO.File]::WriteAllText($ConfigPath, $configJson + [Environment]::NewLine, $utf8NoBom)

$shellKey = "HKCU:\Software\Classes\Directory\Background\shell\UnitySpriteMetaGenerator"
$commandKey = Join-Path $shellKey "command"
$command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -NoExit -File `"$GeneratorPath`" -Path `"%V`" -ConfigPath `"$ConfigPath`""

New-Item -Path $shellKey -Force | Out-Null
New-Item -Path $commandKey -Force | Out-Null

Set-Item -Path $shellKey -Value $MenuName
Set-ItemProperty -Path $shellKey -Name "Icon" -Value "powershell.exe"
Set-Item -Path $commandKey -Value $command

foreach ($extension in @('.png', '.jpg', '.jpeg')) {
    $imageShellKey = "HKCU:\Software\Classes\SystemFileAssociations\$extension\shell\UnitySpriteBorderEditor"
    $imageCommandKey = Join-Path $imageShellKey 'command'
    $choiceKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\$extension\UserChoice"
    $progId = if (Test-Path -LiteralPath $choiceKey) { (Get-ItemProperty -LiteralPath $choiceKey).ProgId } else { $null }
    if ([string]::IsNullOrWhiteSpace($progId)) {
        $extensionKey = "HKCU:\Software\Classes\$extension"
        if (Test-Path -LiteralPath $extensionKey) { $progId = (Get-Item -LiteralPath $extensionKey).GetValue('') }
    }
    if ([string]::IsNullOrWhiteSpace($progId)) {
        $extensionKey = "Registry::HKEY_CLASSES_ROOT\$extension"
        if (Test-Path -LiteralPath $extensionKey) { $progId = (Get-Item -LiteralPath $extensionKey).GetValue('') }
    }
    if ([string]::IsNullOrWhiteSpace($progId) -or $progId -notmatch '^[A-Za-z0-9_.-]+$') {
        Write-Warning "No safe ProgID for $extension; using SystemFileAssociations and Send To."
        $progId = $null
    }
    $oldProgId = $null
    $oldRegistration = $null
    if (Test-Path -LiteralPath $imageShellKey) {
        $oldRegistration = Get-ItemProperty -LiteralPath $imageShellKey
        if ($oldRegistration.PSObject.Properties.Name -contains 'DragonSpriteMetaProgId') {
            $oldProgId = $oldRegistration.DragonSpriteMetaProgId
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($oldProgId) -and $oldProgId -ne $progId) {
        $oldProgKey = "HKCU:\Software\Classes\$oldProgId\shell\UnitySpriteBorderEditor"
        if (Test-Path -LiteralPath $oldProgKey) { Remove-Item -LiteralPath $oldProgKey -Recurse -Force }
    }
    New-Item -Path $imageShellKey -Force | Out-Null
    New-Item -Path $imageCommandKey -Force | Out-Null
    Set-Item -Path $imageShellKey -Value '设置 Unity Sprite 九宫'
    Set-ItemProperty -Path $imageShellKey -Name 'Icon' -Value 'powershell.exe'
    $imageCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$borderEditorPath`" -ImagePath `"%1`" -ConfigPath `"$ConfigPath`""
    Set-Item -Path $imageCommandKey -Value $imageCommand
    if ($null -ne $progId) {
        Set-ItemProperty -Path $imageShellKey -Name 'DragonSpriteMetaProgId' -Value $progId

        # The selected image application's ProgID takes priority in Explorer's association array.
        $progShellKey = "HKCU:\Software\Classes\$progId\shell\UnitySpriteBorderEditor"
        $progCommandKey = Join-Path $progShellKey 'command'
        New-Item -Path $progShellKey -Force | Out-Null
        New-Item -Path $progCommandKey -Force | Out-Null
        Set-Item -Path $progShellKey -Value '设置 Unity Sprite 九宫'
        Set-ItemProperty -Path $progShellKey -Name 'Icon' -Value 'powershell.exe'
        Set-Item -Path $progCommandKey -Value $imageCommand
    } elseif ($oldRegistration -and $oldRegistration.PSObject.Properties.Name -contains 'DragonSpriteMetaProgId') {
        Remove-ItemProperty -LiteralPath $imageShellKey -Name 'DragonSpriteMetaProgId'
    }
}

# Explorer may keep file association verbs cached for the lifetime of the process.
$directKey = 'HKCU:\Software\Classes\*\shell\Dragon.SpriteBorder'
if ($AllUsers) { $directKey = 'HKLM:\Software\Classes\*\shell\Dragon.SpriteBorder' }
$directCommand = Join-Path $directKey 'command'
$powershellPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
New-Item -Path $directCommand -Force | Out-Null
Set-Item -LiteralPath $directKey -Value '设置 Unity Sprite 九宫'
Set-ItemProperty -LiteralPath $directKey -Name 'MUIVerb' -Value '设置 Unity Sprite 九宫'
Set-ItemProperty -LiteralPath $directKey -Name 'Icon' -Value $powershellPath
# Use normal shell ordering rather than putting this tool above Open.
foreach ($orderingKey in @($directKey, 'HKCU:\Software\Classes\*\shell\Dragon.SpriteBorder') | Select-Object -Unique) {
    if ((Test-Path -LiteralPath $orderingKey) -and (Get-Item -LiteralPath $orderingKey).GetValueNames() -contains 'Position') {
        Remove-ItemProperty -LiteralPath $orderingKey -Name 'Position'
    }
}
Set-ItemProperty -LiteralPath $directKey -Name 'MultiSelectModel' -Value 'Single'
Set-ItemProperty -LiteralPath $directKey -Name 'AppliesTo' -Value 'System.FileExtension:=".png" OR System.FileExtension:=".jpg" OR System.FileExtension:=".jpeg"'
Set-Item -LiteralPath $directCommand -Value "`"$powershellPath`" -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$borderEditorPath`" -ImagePath `"%1`" -ConfigPath `"$ConfigPath`""

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
$sendToLink = Join-Path $sendToFolder '设置 Unity Sprite 九宫.lnk'
$shortcutShell = New-Object -ComObject WScript.Shell
$shortcut = $shortcutShell.CreateShortcut($sendToLink)
$shortcut.TargetPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$borderEditorPath`" -ImagePath"
$shortcut.WorkingDirectory = $scriptRoot
$shortcut.IconLocation = "$($shortcut.TargetPath),0"
$shortcut.Description = 'UnitySpriteMetaTool: set Sprite border and generate meta'
$shortcut.Save()

Write-Host "Context menu installed: $MenuName"
Write-Host "Location: folder background right-click"
Write-Host "Project root: $ProjectRoot"
Write-Host "UI root: $uiRoot"
Write-Host "Config: $ConfigPath"
Write-Host "Command: $command"
Write-Host "Image context menu installed: Set Unity Sprite border (.png, .jpg, .jpeg)"
Write-Host "Send To fallback installed: $sendToLink"
Write-Host "Windows 11: right-click an image, then choose Show more options (or press Shift+F10) to find the border editor."
