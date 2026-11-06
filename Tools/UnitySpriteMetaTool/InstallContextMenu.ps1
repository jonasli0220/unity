[CmdletBinding()]
param(
    [string] $MenuName = (([string][char]29983) + ([string][char]25104) + " Unity Sprite Meta"),
    [string] $GeneratorPath = "",
    [string] $ConfigPath = "",
    [string] $ProjectRoot = "",
    [switch] $NoPrompt
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
    New-Item -Path $imageShellKey -Force | Out-Null
    New-Item -Path $imageCommandKey -Force | Out-Null
    Set-Item -Path $imageShellKey -Value '设置 Unity Sprite 九宫'
    Set-ItemProperty -Path $imageShellKey -Name 'Icon' -Value 'powershell.exe'
    $imageCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$borderEditorPath`" -ImagePath `"%1`" -ConfigPath `"$ConfigPath`""
    Set-Item -Path $imageCommandKey -Value $imageCommand
}

Write-Host "Context menu installed: $MenuName"
Write-Host "Location: folder background right-click"
Write-Host "Project root: $ProjectRoot"
Write-Host "UI root: $uiRoot"
Write-Host "Config: $ConfigPath"
Write-Host "Command: $command"
Write-Host "Image context menu installed: Set Unity Sprite border (.png, .jpg, .jpeg)"
