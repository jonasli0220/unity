[CmdletBinding()]
param(
    [string]$MenuName = "",
    [string]$ToolPath = "",
    [string]$ConfigPath = "",
    [string]$TortoiseProcPath = "",
    [string]$TrunkUiRoot = "",
    [string]$ReleaseUiRoot = "",
    [string]$NaReleaseUiRoot = "",
    [switch]$NoPrompt
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sampleConfigPath = Join-Path $scriptRoot "config.sample.json"
$ticketServiceCmd = Join-Path $scriptRoot "server\StartMeegoBaseTicketService.cmd"
$apiKeyPortalUrl = "https://sgra.woa.com/meego-base/"

function Read-HostSecretPlainText {
    param([string]$Prompt)

    $secure = Read-Host -Prompt $Prompt -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        if ($bstr -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
}

function Get-UserApiKey {
    $key = [string]$env:MEEGO_BASE_API_KEY
    if ([string]::IsNullOrWhiteSpace($key)) {
        $key = [Environment]::GetEnvironmentVariable("MEEGO_BASE_API_KEY", "User")
    }
    return $key
}

function Test-TicketServiceHealth {
    try {
        $health = Invoke-RestMethod -Uri "http://127.0.0.1:18765/health" -TimeoutSec 3
        return ($health.ok -eq $true)
    }
    catch {
        return $false
    }
}

function Start-TicketService {
    if (Test-TicketServiceHealth) {
        Write-Host "[OK] Local ticket service is already running." -ForegroundColor Green
        return
    }

    if (-not (Test-Path -LiteralPath $ticketServiceCmd -PathType Leaf)) {
        Write-Host "[WARN] Cannot find local ticket service launcher: $ticketServiceCmd" -ForegroundColor Yellow
        return
    }

    Write-Host "Starting local ticket service..."
    Start-Process -FilePath "cmd.exe" -ArgumentList @("/k", "`"$ticketServiceCmd`"") -WorkingDirectory $scriptRoot -WindowStyle Minimized | Out-Null
    for ($i = 1; $i -le 20; $i += 1) {
        Start-Sleep -Milliseconds 500
        if (Test-TicketServiceHealth) {
            Write-Host "[OK] Local ticket service started." -ForegroundColor Green
            return
        }
    }

    Write-Host "[WARN] Local ticket service did not become ready. You can run QuickReleaseMerge\SetupLocalMeegoKey.cmd later." -ForegroundColor Yellow
}

function Ensure-MeegoApiKey {
    if ($NoPrompt) {
        return
    }

    Write-Host ""
    Write-Host "Meego Base API Key:"
    $currentKey = Get-UserApiKey
    if (-not [string]::IsNullOrWhiteSpace($currentKey)) {
        Write-Host "[OK] MEEGO_BASE_API_KEY already exists." -ForegroundColor Green
        $replace = Read-Host "Press Enter to keep it, or type r to replace it"
        if ($replace -ne "r" -and $replace -ne "R") {
            Start-TicketService
            return
        }
    }

    Write-Host "Create or copy your API Key here:"
    Write-Host "  $apiKeyPortalUrl" -ForegroundColor Cyan
    $openPortal = Read-Host "Press Enter to open this page, or type n to skip"
    if ($openPortal -ne "n" -and $openPortal -ne "N") {
        Start-Process $apiKeyPortalUrl
    }

    $apiKey = Read-HostSecretPlainText "Paste your Meego Base API Key"
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        Write-Host "[WARN] API Key was not entered. The right-click menu will be installed, but ticket loading will fail until the key is configured." -ForegroundColor Yellow
        return
    }

    [Environment]::SetEnvironmentVariable("MEEGO_BASE_API_KEY", $apiKey, "User")
    $env:MEEGO_BASE_API_KEY = $apiKey
    Write-Host "[OK] Saved MEEGO_BASE_API_KEY to Windows user environment variable." -ForegroundColor Green
    Start-TicketService
}

function Resolve-TortoiseProcPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path.Trim('"'))
    if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
        return $fullPath
    }

    $directCandidate = Join-Path $fullPath "TortoiseProc.exe"
    if (Test-Path -LiteralPath $directCandidate -PathType Leaf) {
        return $directCandidate
    }

    $binCandidate = Join-Path $fullPath "bin\TortoiseProc.exe"
    if (Test-Path -LiteralPath $binCandidate -PathType Leaf) {
        return $binCandidate
    }

    return $fullPath
}

if ([string]::IsNullOrWhiteSpace($ToolPath)) {
    $ToolPath = Join-Path $scriptRoot "QuickReleaseMerge.ps1"
}
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $scriptRoot "config.local.json"
}
if (-not (Test-Path -LiteralPath $ToolPath -PathType Leaf)) {
    throw "Tool script not found: $ToolPath"
}
if (-not (Test-Path -LiteralPath $sampleConfigPath -PathType Leaf)) {
    throw "Sample config not found: $sampleConfigPath"
}

$config = Get-Content -LiteralPath $sampleConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($MenuName)) {
    $MenuName = [string]$config.menuName
}
if ([string]::IsNullOrWhiteSpace($MenuName)) {
    $MenuName = "快速 merge"
}
if ([string]::IsNullOrWhiteSpace($TortoiseProcPath)) {
    $TortoiseProcPath = [string]$config.tortoiseProcPath
}
if ([string]::IsNullOrWhiteSpace($TrunkUiRoot)) {
    $TrunkUiRoot = [string]$config.trunkUiRoot
}

if (-not $NoPrompt) {
    Write-Host ""
    Write-Host "TortoiseSVN path:"
    Write-Host "Default: $TortoiseProcPath"
    $inputSvn = Read-Host "Press Enter to use default, or paste TortoiseProc.exe / TortoiseSVN install folder"
    if (-not [string]::IsNullOrWhiteSpace($inputSvn)) {
        $TortoiseProcPath = $inputSvn.Trim('"')
    }

    Write-Host ""
    Write-Host "trunk UI path:"
    Write-Host "Default: $TrunkUiRoot"
    $inputTrunk = Read-Host "Press Enter to use default, or paste another trunk UI path"
    if (-not [string]::IsNullOrWhiteSpace($inputTrunk)) {
        $TrunkUiRoot = $inputTrunk.Trim('"')
    }
}

$TortoiseProcPath = Resolve-TortoiseProcPath $TortoiseProcPath
$TrunkUiRoot = [System.IO.Path]::GetFullPath($TrunkUiRoot).TrimEnd("\", "/")

if (-not (Test-Path -LiteralPath $TortoiseProcPath -PathType Leaf)) {
    throw "TortoiseSVN program does not exist: $TortoiseProcPath"
}
if (-not (Test-Path -LiteralPath $TrunkUiRoot -PathType Container)) {
    throw "trunk UI path does not exist: $TrunkUiRoot"
}

Ensure-MeegoApiKey

$config | Add-Member -MemberType NoteProperty -Name menuName -Value $MenuName -Force
$config | Add-Member -MemberType NoteProperty -Name tortoiseProcPath -Value $TortoiseProcPath -Force
$config | Add-Member -MemberType NoteProperty -Name trunkUiRoot -Value $TrunkUiRoot -Force
if (-not [string]::IsNullOrWhiteSpace($ReleaseUiRoot)) {
    $config | Add-Member -MemberType NoteProperty -Name releaseUiRoot -Value ([System.IO.Path]::GetFullPath($ReleaseUiRoot).TrimEnd("\", "/")) -Force
}
if (-not [string]::IsNullOrWhiteSpace($NaReleaseUiRoot)) {
    $config | Add-Member -MemberType NoteProperty -Name naReleaseUiRoot -Value ([System.IO.Path]::GetFullPath($NaReleaseUiRoot).TrimEnd("\", "/")) -Force
}

$json = $config | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText($ConfigPath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

$shellKey = "HKCU:\Software\Classes\Directory\Background\shell\DragonQuickReleaseMerge"
$commandKey = Join-Path $shellKey "command"
$command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$ToolPath`" -StartPath `"%V`" -ConfigPath `"$ConfigPath`""

New-Item -Path $shellKey -Force | Out-Null
New-Item -Path $commandKey -Force | Out-Null
Set-Item -Path $shellKey -Value $MenuName
Set-ItemProperty -Path $shellKey -Name "Icon" -Value "powershell.exe"
Set-Item -Path $commandKey -Value $command

Write-Host ""
Write-Host "Context menu installed: $MenuName" -ForegroundColor Green
Write-Host "Location: folder background right-click"
Write-Host "TortoiseSVN: $TortoiseProcPath"
Write-Host "trunk UI: $TrunkUiRoot"
Write-Host "CN release UI: first merge to CN release will ask for the path."
Write-Host "NA release UI: first merge to NA release will ask for the path."
Write-Host "RemoteAssets: auto-detected when a ticket actually contains remote assets; asks only if detection fails."
Write-Host "Config: $ConfigPath"
Write-Host "Command: $command"
