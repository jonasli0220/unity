$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$configPath = Join-Path $scriptDir "config.local.json"
$serviceCmd = Join-Path $scriptDir "server\StartMeegoBaseTicketService.cmd"
$endpoint = "http://127.0.0.1:18765/api/my-open-workitems"
$healthEndpoint = "http://127.0.0.1:18765/health"
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

function Write-ConfigLocal {
    param([string]$Path)

    $config = [ordered]@{
        endpoint = "http://127.0.0.1:18765/api/my-open-workitems"
        userMapFile = "user-map.local.json"
        defaultEmailDomain = "tencent.com"
        providerCommand = ""
        cacheFile = "tickets.cache.json"
        defaultQuery = ""
        appendMode = "append"
        messageFormat = "#{id} {title}"
        includeTypes = @("story", "bug")
        includeClosed = $false
        timeoutSeconds = 45
    }

    $config | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Test-ServiceHealth {
    try {
        $health = Invoke-RestMethod -Uri $healthEndpoint -TimeoutSec 3
        return ($health.ok -eq $true)
    }
    catch {
        return $false
    }
}

function Start-TicketServiceWindow {
    if (Test-ServiceHealth) {
        Write-Host "[OK] Local ticket service is already running." -ForegroundColor Green
        return
    }

    if (-not (Test-Path -LiteralPath $serviceCmd)) {
        throw "Cannot find service launcher: $serviceCmd"
    }

    Write-Host "Starting local ticket service in a minimized window..."
    Start-Process -FilePath "cmd.exe" -ArgumentList @("/k", "`"$serviceCmd`"") -WorkingDirectory $scriptDir -WindowStyle Minimized | Out-Null

    for ($i = 1; $i -le 20; $i += 1) {
        Start-Sleep -Milliseconds 500
        if (Test-ServiceHealth) {
            Write-Host "[OK] Local ticket service started." -ForegroundColor Green
            return
        }
    }

    throw "Local ticket service did not become ready. Check the service window."
}

function Test-TicketEndpoint {
    Write-Host "Checking ticket API..."
    $raw = curl.exe -sS --max-time 45 $endpoint
    $json = [string]::Join("`n", [string[]]$raw)
    $tickets = @($json | ConvertFrom-Json)
    if ($tickets.Count -eq 0) {
        Write-Host "[WARN] API returned 0 tickets. The setup is valid, but no open ticket matched your account." -ForegroundColor Yellow
        return
    }

    Write-Host "[OK] API returned $($tickets.Count) tickets." -ForegroundColor Green
    $tickets | Select-Object -First 5 id, type, title | Format-Table -AutoSize
}

Clear-Host
Write-Host "SVN Feishu ticket picker setup" -ForegroundColor Cyan
Write-Host ""
Write-Host "This will:"
Write-Host "  1. Save your Meego Base API Key to your Windows user environment variable."
Write-Host "  2. Write config.local.json to use the local ticket service."
Write-Host "  3. Start the local ticket service."
Write-Host "  4. Verify the ticket API."
Write-Host ""
Write-Host "The API Key will NOT be written into this tool folder."
Write-Host ""
Write-Host "Create your API Key here:"
Write-Host "  $apiKeyPortalUrl" -ForegroundColor Cyan
Write-Host ""
$openPortal = Read-Host "Press Enter to open this page, or type n to skip"
if ($openPortal -ne "n" -and $openPortal -ne "N") {
    Start-Process $apiKeyPortalUrl
    Write-Host "After creating the API Key, paste it below."
}
Write-Host ""

$apiKey = Read-HostSecretPlainText "Paste your Meego Base API Key"
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    Write-Host ""
    Write-Host "No API Key entered. Setup cancelled." -ForegroundColor Yellow
    exit 1
}

[Environment]::SetEnvironmentVariable("MEEGO_BASE_API_KEY", $apiKey, "User")
$env:MEEGO_BASE_API_KEY = $apiKey
Write-ConfigLocal $configPath

Write-Host ""
Write-Host "Saved environment variable: MEEGO_BASE_API_KEY"
Write-Host "Updated config: $configPath"
Write-Host ""
Start-TicketServiceWindow
Test-TicketEndpoint
Write-Host ""
Write-Host "Setup complete." -ForegroundColor Green
Write-Host ""
Write-Host "Configure TortoiseSVN Hook command:"
Write-Host "  $scriptDir\RunSvnFeishuCommitPicker.cmd"
Write-Host ""
Write-Host "Keep the service window open while using TortoiseSVN."
Write-Host ""
pause
