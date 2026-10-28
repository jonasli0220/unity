$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$providerPath = Join-Path $scriptDir "server\meego-base-provider.ps1"
$apiKeyPortalUrl = "https://sgra.woa.com/meego-base/"
$lifeProjectKey = "66d18041dc33a1a07a8e6d85"

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

Clear-Host
Write-Host "Life SVN Feishu ticket picker setup" -ForegroundColor Cyan
Write-Host ""
Write-Host "Create or select a read-only API Key with access to project: sgra_life"
Write-Host "  $apiKeyPortalUrl" -ForegroundColor Cyan
Write-Host ""
$openPortal = Read-Host "Press Enter to open this page, or type n to skip"
if ($openPortal -ne "n" -and $openPortal -ne "N") {
    Start-Process $apiKeyPortalUrl
    Write-Host "After creating the Life API Key, paste it below."
}

$apiKey = Read-HostSecretPlainText "Paste your Life Meego Base API Key"
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    Write-Host "No API Key entered. Setup cancelled." -ForegroundColor Yellow
    exit 1
}

[Environment]::SetEnvironmentVariable("MEEGO_BASE_API_KEY_LIFE", $apiKey, "User")
$env:MEEGO_BASE_API_KEY_LIFE = $apiKey
Write-Host ""
Write-Host "Saved environment variable: MEEGO_BASE_API_KEY_LIFE" -ForegroundColor Green
Write-Host "Checking Life ticket API..."

try {
    $json = & $providerPath `
        -ProjectKey $lifeProjectKey `
        -WorkItemTypeKeys @("story", "issue") `
        -BugTypeKeys @("issue", "bug") `
        -OpenStorySubStages @() `
        -OpenBugStateKeys @("OPEN", "RESOLVED") `
        -ApiKey $apiKey `
        -Email "$env:USERNAME@tencent.com"

    $tickets = @(([string]::Join("`n", [string[]]$json)) | ConvertFrom-Json)
    Write-Host "[OK] Life API returned $($tickets.Count) tickets." -ForegroundColor Green
    $tickets | Select-Object -First 5 id, type, title | Format-Table -AutoSize
}
catch {
    Write-Host "[FAIL] Life API validation failed." -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host "Confirm that this API Key is enabled and has access to sgra_life." -ForegroundColor Yellow
    pause
    exit 1
}

Write-Host ""
Write-Host "Setup complete. Use this Hook command for both Life trunk and dev:" -ForegroundColor Green
Write-Host "  $scriptDir\RunSvnFeishuCommitPicker-Life.cmd"
Write-Host ""
pause
