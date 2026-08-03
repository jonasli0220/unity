[CmdletBinding()]
param(
    [switch]$NoStartService
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
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
    if ($NoStartService) {
        return
    }

    if (Test-TicketServiceHealth) {
        Write-Host "[OK] Ticket service is already running." -ForegroundColor Green
        return
    }

    if (-not (Test-Path -LiteralPath $ticketServiceCmd -PathType Leaf)) {
        Write-Host ("[WARN] Cannot find ticket service launcher: {0}" -f $ticketServiceCmd) -ForegroundColor Yellow
        return
    }

    Write-Host "Starting QuickReleaseMerge ticket service..."
    $serviceArgument = "/k `"$ticketServiceCmd`""
    Start-Process -FilePath "cmd.exe" -ArgumentList $serviceArgument -WorkingDirectory $scriptRoot -WindowStyle Minimized | Out-Null
    for ($i = 1; $i -le 20; $i += 1) {
        Start-Sleep -Milliseconds 500
        if (Test-TicketServiceHealth) {
            Write-Host "[OK] Ticket service started." -ForegroundColor Green
            return
        }
    }

    Write-Host "[WARN] Ticket service did not become ready. Re-run this script later or check the service window." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "QuickReleaseMerge API Key setup"
Write-Host ""

$currentKey = Get-UserApiKey
if (-not [string]::IsNullOrWhiteSpace($currentKey)) {
    Write-Host "[OK] MEEGO_BASE_API_KEY already exists." -ForegroundColor Green
    $replace = Read-Host "Press Enter to keep it, or type r to replace it"
    if ($replace -ne "r" -and $replace -ne "R") {
        Start-TicketService
        exit 0
    }
}

Write-Host "Create or copy your Meego Base API Key:"
Write-Host "  $apiKeyPortalUrl" -ForegroundColor Cyan
$openPortal = Read-Host "Press Enter to open this page, or type n to skip"
if ($openPortal -ne "n" -and $openPortal -ne "N") {
    Start-Process $apiKeyPortalUrl
}

$apiKey = Read-HostSecretPlainText "Paste your Meego Base API Key"
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    Write-Host "[WARN] API Key was not entered. Re-run this script later to configure it." -ForegroundColor Yellow
    exit 1
}

[Environment]::SetEnvironmentVariable("MEEGO_BASE_API_KEY", $apiKey, "User")
$env:MEEGO_BASE_API_KEY = $apiKey
Write-Host "[OK] Saved to Windows user environment variable MEEGO_BASE_API_KEY." -ForegroundColor Green

Start-TicketService
