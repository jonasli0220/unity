$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$distDir = Join-Path $scriptDir "dist"
$stagingRoot = Join-Path $distDir "_staging"
$packageRoot = Join-Path $stagingRoot "SvnFeishuCommitPicker"
$archivePath = Join-Path $distDir "SvnFeishuCommitPicker-Team-20261028.zip"

$allowedFiles = @(
    "README.md",
    "config.sample.json",
    "user-map.sample.json",
    "RunSvnFeishuCommitPicker.cmd",
    "RunSvnFeishuCommitPicker-Life.cmd",
    "SetupLocalMeegoKey.cmd",
    "SetupLocalMeegoKey.ps1",
    "SetupLifeMeegoKey.cmd",
    "SetupLifeMeegoKey.ps1",
    "SvnFeishuCommitPicker.ps1",
    "VerifyLocalSetup.cmd",
    "VerifyLocalSetup.ps1",
    "server\StartMeegoBaseTicketService.cmd",
    "server\ticket-service.ps1",
    "server\meego-base-provider.ps1",
    "server\server.config.meego-base.sample.json"
)

$resolvedScriptDir = [System.IO.Path]::GetFullPath($scriptDir).TrimEnd("\")
$resolvedStagingRoot = [System.IO.Path]::GetFullPath($stagingRoot).TrimEnd("\")
if (-not $resolvedStagingRoot.StartsWith("$resolvedScriptDir\dist\", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe staging path: $resolvedStagingRoot"
}

New-Item -ItemType Directory -Path $distDir -Force | Out-Null
if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

foreach ($relativePath in $allowedFiles) {
    $sourcePath = Join-Path $scriptDir $relativePath
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Missing distribution file: $relativePath"
    }

    $targetPath = Join-Path $packageRoot $relativePath
    $targetDir = Split-Path -Parent $targetPath
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    Copy-Item -LiteralPath $sourcePath -Destination $targetPath
}

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -Path $packageRoot -DestinationPath $archivePath -CompressionLevel Optimal
Remove-Item -LiteralPath $stagingRoot -Recurse -Force

Write-Output $archivePath
