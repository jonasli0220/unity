param(
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$clientScript = Join-Path $scriptDir "SvnFeishuCommitPicker.ps1"
$sampleConfigPath = Join-Path $scriptDir "config.sample.json"
$localConfigPath = Join-Path $scriptDir "config.local.json"

function Wait-ForExit {
    if (-not $NoPause) {
        pause
    }
}

function Read-JsonFile {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    $raw = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $null
    }

    return $raw | ConvertFrom-Json
}

function Get-VerificationConfig {
    $config = Read-JsonFile $sampleConfigPath
    $localConfig = Read-JsonFile $localConfigPath
    if ($localConfig) {
        foreach ($property in $localConfig.PSObject.Properties) {
            $config | Add-Member -MemberType NoteProperty -Name $property.Name -Value $property.Value -Force
        }
    }
    return $config
}

function Get-CurrentUserIdentity {
    param($Config)

    $windowsUser = [string]$env:USERNAME
    $email = [string]$Config.email
    $userKey = [string]$Config.userKey
    $mapFile = [string]$Config.userMapFile
    if ([string]::IsNullOrWhiteSpace($mapFile)) {
        $mapFile = "user-map.local.json"
    }

    $mapPath = if ([System.IO.Path]::IsPathRooted($mapFile)) { $mapFile } else { Join-Path $scriptDir $mapFile }
    $map = Read-JsonFile $mapPath
    if ($map) {
        $entry = $null
        if ($map.PSObject.Properties.Name -contains $windowsUser) {
            $entry = $map.$windowsUser
        }
        elseif ($map -is [System.Array]) {
            $entry = @($map | Where-Object { $_.windowsUser -eq $windowsUser -or $_.username -eq $windowsUser } | Select-Object -First 1)
        }

        if ($entry) {
            if (-not [string]::IsNullOrWhiteSpace([string]$entry.email)) {
                $email = [string]$entry.email
            }
            if (-not [string]::IsNullOrWhiteSpace([string]$entry.userKey)) {
                $userKey = [string]$entry.userKey
            }
            elseif (-not [string]::IsNullOrWhiteSpace([string]$entry.user_key)) {
                $userKey = [string]$entry.user_key
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($email) -and -not [string]::IsNullOrWhiteSpace([string]$Config.defaultEmailDomain)) {
        $email = "$windowsUser@$($Config.defaultEmailDomain)"
    }

    return [pscustomobject]@{
        windowsUser = $windowsUser
        email = $email
        userKey = $userKey
    }
}

function Add-QueryParameter {
    param([string]$Uri, [string]$Name, [string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $Uri
    }
    $separator = if ($Uri.Contains("?")) { "&" } else { "?" }
    return "$Uri$separator$Name=$([Uri]::EscapeDataString($Value))"
}

function Get-RequestErrorMessage {
    param($ErrorRecord)

    $details = [string]$ErrorRecord.ErrorDetails.Message
    if (-not [string]::IsNullOrWhiteSpace($details)) {
        try {
            $payload = $details | ConvertFrom-Json
            if (-not [string]::IsNullOrWhiteSpace([string]$payload.error)) {
                $details = [string]$payload.error
            }
        }
        catch {}

        if ($details -match "Missing user identity") {
            return "Current user identity could not be resolved. Check defaultEmailDomain or user-map.local.json."
        }
        return $details
    }
    return [string]$ErrorRecord.Exception.Message
}

function Get-UserApiKey {
    $key = [string]$env:MEEGO_BASE_API_KEY
    if ([string]::IsNullOrWhiteSpace($key)) {
        $key = [Environment]::GetEnvironmentVariable("MEEGO_BASE_API_KEY", "User")
    }
    return $key
}

Clear-Host
Write-Host "SVN Feishu ticket picker verification" -ForegroundColor Cyan
Write-Host ""

$apiKey = Get-UserApiKey
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    Write-Host "[FAIL] MEEGO_BASE_API_KEY is not set." -ForegroundColor Red
    Write-Host "Run SetupLocalMeegoKey.cmd first."
    Wait-ForExit
    exit 1
}
Write-Host "[OK] API Key environment variable exists." -ForegroundColor Green

try {
    $health = Invoke-RestMethod -Uri "http://127.0.0.1:18765/health" -TimeoutSec 5
    if ($health.ok -ne $true) {
        throw "Unexpected health response."
    }
    Write-Host "[OK] Local ticket service is running." -ForegroundColor Green
}
catch {
    Write-Host "[FAIL] Local ticket service is not running." -ForegroundColor Red
    Write-Host "Start it with: server\StartMeegoBaseTicketService.cmd"
    Wait-ForExit
    exit 1
}

try {
    $config = Get-VerificationConfig
    $identity = Get-CurrentUserIdentity $config
    $endpoint = [string]$config.endpoint
    if ([string]::IsNullOrWhiteSpace($endpoint)) {
        $endpoint = "http://127.0.0.1:18765/api/my-open-workitems"
    }
    $endpoint = Add-QueryParameter $endpoint "windowsUser" $identity.windowsUser
    $endpoint = Add-QueryParameter $endpoint "email" $identity.email
    $endpoint = Add-QueryParameter $endpoint "userKey" $identity.userKey

    $response = Invoke-RestMethod -Uri $endpoint -Headers @{ "Cache-Control" = "no-cache" } -TimeoutSec 45
    $tickets = @($response)
    if ($tickets.Count -le 0) {
        Write-Host "[WARN] API returned 0 tickets." -ForegroundColor Yellow
    }
    else {
        Write-Host "[OK] API returned $($tickets.Count) tickets." -ForegroundColor Green
        $tickets | Select-Object -First 5 id, type, title | Format-Table -AutoSize
    }
}
catch {
    Write-Host "[FAIL] API request failed." -ForegroundColor Red
    Write-Host (Get-RequestErrorMessage $_)
    Wait-ForExit
    exit 1
}

try {
    $output = powershell.exe -NoProfile -ExecutionPolicy Bypass -File $clientScript -ListTickets
    if (($output -join "`n") -match '"source"\s*:\s*"endpoint"') {
        Write-Host "[OK] SVN picker reads from endpoint." -ForegroundColor Green
    }
    else {
        Write-Host "[WARN] SVN picker did not report endpoint source." -ForegroundColor Yellow
    }
}
catch {
    Write-Host "[FAIL] SVN picker verification failed." -ForegroundColor Red
    Write-Host $_.Exception.Message
    Wait-ForExit
    exit 1
}

Write-Host ""
Write-Host "Verification complete."
Wait-ForExit
