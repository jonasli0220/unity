[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Position = 0)]
    [string] $Path = ".",

    [string] $ProjectRoot = "",

    [string] $UiRoot = "",

    [string] $ConfigPath = "",

    [ValidateRange(0, 2147483647)][int] $BorderLeft = 0,
    [ValidateRange(0, 2147483647)][int] $BorderTop = 0,
    [ValidateRange(0, 2147483647)][int] $BorderRight = 0,
    [ValidateRange(0, 2147483647)][int] $BorderBottom = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$imageExtensions = @(".png", ".jpg", ".jpeg", ".tga", ".psd")
$logRoot = "C:\tmp\UnityMetaGenerator"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $scriptRoot "config.json"
}

function Read-ToolConfig {
    param([Parameter(Mandatory = $true)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    $json = Get-Content -Raw -Encoding UTF8 -LiteralPath $Path
    return $json | ConvertFrom-Json
}

function Get-FullNormalizedPath {
    param([Parameter(Mandatory = $true)][string] $InputPath)

    return [System.IO.Path]::GetFullPath($InputPath).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
}

function New-UnityGuid {
    return ([guid]::NewGuid().ToString("N")).ToLowerInvariant()
}

function Convert-ToUnityPath {
    param(
        [Parameter(Mandatory = $true)][string] $FullPath,
        [Parameter(Mandatory = $true)][string] $ProjectRootPath
    )

    $rootWithSlash = $ProjectRootPath.TrimEnd("\", "/") + [System.IO.Path]::DirectorySeparatorChar
    $rootUri = New-Object System.Uri($rootWithSlash)
    $pathUri = New-Object System.Uri($FullPath)
    $relative = [System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString())
    return $relative.Replace("\", "/")
}

function New-FolderMetaText {
    param([Parameter(Mandatory = $true)][string] $Guid)

    return @"
fileFormatVersion: 2
guid: $Guid
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"@
}

function New-SpriteMetaText {
    param([Parameter(Mandatory = $true)][string] $Guid)

    return @"
fileFormatVersion: 2
guid: $Guid
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 11
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMasterTextureLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 0
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {x: 0.5, y: 0.5}
  spritePixelsToUnits: 100
  spriteBorder: {x: 0, y: 0, z: 0, w: 0}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 3
    buildTarget: Standalone
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 3
    buildTarget: iPhone
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: 50
    textureCompression: 1
    compressionQuality: 100
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 1
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 3
    buildTarget: Android
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: 50
    textureCompression: 1
    compressionQuality: 100
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 1
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 3
    buildTarget: OpenHarmony
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: 50
    textureCompression: 1
    compressionQuality: 100
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 1
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID: 5e97eb03825dee720800000000000000
    internalID: 0
    vertices: []
    indices: 
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable: {}
  spritePackingTag: 
  pSDRemoveMatte: 0
  pSDShowRemoveMatteOption: 0
  userData: 
  assetBundleName: 
  assetBundleVariant: 
  importRegionVersion: 0
"@
}

function Write-Utf8NoBomFile {
    param(
        [Parameter(Mandatory = $true)][string] $FilePath,
        [Parameter(Mandatory = $true)][string] $Content
    )

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($FilePath, $Content, $utf8NoBom)
}

function Set-SpriteBorderText {
    param(
        [Parameter(Mandatory = $true)][string] $Content,
        [Parameter(Mandatory = $true)][int] $Left,
        [Parameter(Mandatory = $true)][int] $Top,
        [Parameter(Mandatory = $true)][int] $Right,
        [Parameter(Mandatory = $true)][int] $Bottom
    )

    $pattern = '(?m)^  spriteBorder: \{[^\r\n]*\}(?=\r?$)'
    if ([regex]::Matches($Content, $pattern).Count -ne 1) {
        throw "Expected exactly one TextureImporter spriteBorder field."
    }
    # Unity Vector4 order is left, bottom, right, top (x, y, z, w).
    $replacement = "  spriteBorder: {x: $Left, y: $Bottom, z: $Right, w: $Top}"
    return [regex]::Replace($Content, $pattern, $replacement)
}

function Save-SpriteBorderMeta {
    param(
        [Parameter(Mandatory = $true)][string] $ImagePath,
        [Parameter(Mandatory = $true)][int] $Left,
        [Parameter(Mandatory = $true)][int] $Top,
        [Parameter(Mandatory = $true)][int] $Right,
        [Parameter(Mandatory = $true)][int] $Bottom
    )

    Add-Type -AssemblyName System.Drawing
    $image = [System.Drawing.Image]::FromFile($ImagePath)
    try {
        $width = $image.Width
        $height = $image.Height
    } finally {
        $image.Dispose()
    }
    if ($Left + $Right -ge $width -or $Top + $Bottom -ge $height) {
        throw "Border exceeds image size ${width}x${height}: L=$Left T=$Top R=$Right B=$Bottom"
    }

    $metaPath = "$ImagePath.meta"
    $exists = Test-Path -LiteralPath $metaPath -PathType Leaf
    $utf8 = New-Object System.Text.UTF8Encoding($false, $true)
    $bom = $false
    if ($exists) {
        $bytes = [System.IO.File]::ReadAllBytes($metaPath)
        $bom = $bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191
        $offset = if ($bom) { 3 } else { 0 }
        $content = $utf8.GetString($bytes, $offset, $bytes.Length - $offset)
        if ($content -notmatch '(?m)^TextureImporter:\s*$' -or
            $content -notmatch '(?m)^  textureType: 8\s*$' -or
            $content -notmatch '(?m)^  spriteMode: 1\s*$') {
            throw "Existing meta is not a single Sprite TextureImporter: $metaPath"
        }
    } else {
        $content = (New-SpriteMetaText -Guid (New-UnityGuid)) + "`n"
    }
    $updated = Set-SpriteBorderText -Content $content -Left $Left -Top $Top -Right $Right -Bottom $Bottom
    $newBytes = $utf8.GetBytes($updated)
    if ($bom) {
        $newBytes = @([byte]239, [byte]187, [byte]191) + $newBytes
    }
    if ($PSCmdlet.ShouldProcess($metaPath, $(if ($exists) { 'Update Sprite border' } else { 'Create Sprite meta with border' }))) {
        $suffix = [guid]::NewGuid().ToString('N')
        $temporary = "$metaPath.$suffix.tmp"
        $backup = "$metaPath.$suffix.bak"
        try {
            [System.IO.File]::WriteAllBytes($temporary, [byte[]]$newBytes)
            if ($exists) {
                [System.IO.File]::Replace($temporary, $metaPath, $backup)
            } else {
                [System.IO.File]::Move($temporary, $metaPath)
            }
        } finally {
            if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
            if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force }
        }
    }
    Write-Host "Sprite border $(if ($WhatIfPreference) { 'preview' } else { 'saved' }): $metaPath (L=$Left T=$Top R=$Right B=$Bottom)"
}

$config = Read-ToolConfig -Path $ConfigPath

# Legacy ProjectRoot/UiRoot arguments and config values no longer constrain the
# selected asset. Always resolve its own project so branch paths remain correct.
. (Join-Path $scriptRoot 'UnityAssetPath.ps1')
$assetContext = Resolve-UnityAssetPath -Path $Path
$projectRootFull = $assetContext.ProjectRoot
$assetsRootFull = $assetContext.AssetsRoot
$targetFull = $assetContext.TargetPath
if ($null -ne $config -and $config.PSObject.Properties.Name -contains "logRoot" -and -not [string]::IsNullOrWhiteSpace($config.logRoot)) {
    $logRoot = $config.logRoot
}
if (Test-Path -LiteralPath $targetFull -PathType Leaf) {
    if (@('.png', '.jpg', '.jpeg') -notcontains [System.IO.Path]::GetExtension($targetFull).ToLowerInvariant()) {
        throw "Border editing supports PNG and JPEG images: $targetFull"
    }
    Save-SpriteBorderMeta -ImagePath $targetFull -Left $BorderLeft -Top $BorderTop -Right $BorderRight -Bottom $BorderBottom
    return
}

if ($PSBoundParameters.ContainsKey('BorderLeft') -or $PSBoundParameters.ContainsKey('BorderTop') -or
    $PSBoundParameters.ContainsKey('BorderRight') -or $PSBoundParameters.ContainsKey('BorderBottom')) {
    throw "Border parameters require a PNG or JPEG file path."
}

New-Item -ItemType Directory -Force -Path $logRoot -WhatIf:$false | Out-Null

$createdFolders = New-Object System.Collections.Generic.List[string]
$createdImages = New-Object System.Collections.Generic.List[string]
$skippedImages = 0

# Walk explicitly without following directory links into another asset tree.
$directories = New-Object System.Collections.Generic.List[System.IO.DirectoryInfo]
$images = New-Object System.Collections.Generic.List[System.IO.FileInfo]
$pending = New-Object System.Collections.Generic.Stack[System.IO.DirectoryInfo]
$pending.Push((Get-Item -LiteralPath $targetFull))
while ($pending.Count -gt 0) {
    $directory = $pending.Pop()
    if ($directory.FullName -ine $assetsRootFull) { $directories.Add($directory) }
    foreach ($child in (Get-ChildItem -LiteralPath $directory.FullName -Force)) {
        if (($child.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            Write-Warning "Skipping linked asset: $($child.FullName)"
            continue
        }
        if ($child.PSIsContainer) { $pending.Push($child) }
        elseif ($imageExtensions -contains $child.Extension.ToLowerInvariant()) { $images.Add($child) }
    }
}
foreach ($directory in $directories) {
    $metaPath = "$($directory.FullName).meta"
    if (Test-Path -LiteralPath $metaPath) {
        continue
    }

    $unityPath = Convert-ToUnityPath -FullPath $directory.FullName -ProjectRootPath $projectRootFull
    if ($PSCmdlet.ShouldProcess($metaPath, "Create folder meta")) {
        Write-Utf8NoBomFile -FilePath $metaPath -Content ((New-FolderMetaText -Guid (New-UnityGuid)) + "`n")
    }
    $createdFolders.Add($unityPath)
}


foreach ($image in $images) {
    $metaPath = "$($image.FullName).meta"
    if (Test-Path -LiteralPath $metaPath) {
        $skippedImages += 1
        continue
    }

    $unityPath = Convert-ToUnityPath -FullPath $image.FullName -ProjectRootPath $projectRootFull
    if ($PSCmdlet.ShouldProcess($metaPath, "Create Sprite image meta")) {
        Write-Utf8NoBomFile -FilePath $metaPath -Content ((New-SpriteMetaText -Guid (New-UnityGuid)) + "`n")
    }
    $createdImages.Add($unityPath)
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$logPath = Join-Path $logRoot "UnitySpriteMeta_$timestamp.log"
$modeText = if ($WhatIfPreference) { "WhatIf preview, no files written" } else { "Files written" }
$targetUnityPath = Convert-ToUnityPath -FullPath $targetFull -ProjectRootPath $projectRootFull

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("Unity Sprite Meta generation log")
$lines.Add("Mode: $modeText")
$lines.Add("Project root: $projectRootFull")
$lines.Add("Scan folder: $targetUnityPath")
$lines.Add("Created image meta: $($createdImages.Count)")
$lines.Add("Created folder meta: $($createdFolders.Count)")
$lines.Add("Skipped existing image meta: $skippedImages")
$lines.Add("Log file: $logPath")
$lines.Add("")
$lines.Add("Created image paths, ready to copy:")
if ($createdImages.Count -gt 0) {
    foreach ($item in $createdImages) {
        $lines.Add($item)
    }
} else {
    $lines.Add("None")
}

if ($createdFolders.Count -gt 0) {
    $lines.Add("")
    $lines.Add("Created folder meta:")
    foreach ($item in $createdFolders) {
        $lines.Add($item)
    }
}

$logText = $lines -join [Environment]::NewLine
Write-Utf8NoBomFile -FilePath $logPath -Content $logText
Write-Host $logText

if (-not $WhatIfPreference) {
    try {
        if ($createdImages.Count -gt 0) {
            Set-Clipboard -Value (($createdImages | ForEach-Object { $_ }) -join [Environment]::NewLine)
            Write-Host ""
            Write-Host "Created image paths copied to clipboard."
        }
    } catch {
        Write-Host ""
        Write-Host "Clipboard copy failed. Copy paths from the log above."
    }
}
