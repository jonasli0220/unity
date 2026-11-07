# Shared path resolution for the standalone editor and meta generator.
function Resolve-UnityAssetPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    $target = Get-Item -LiteralPath ([System.IO.Path]::GetFullPath($Path)) -Force -ErrorAction Stop
    $directory = if ($target -is [System.IO.DirectoryInfo]) { $target } else { $target.Directory }
    $assets = $null
    while ($null -ne $directory) {
        if ($directory.Name -ieq 'Assets' -and $null -ne $directory.Parent -and
            (Test-Path -LiteralPath (Join-Path $directory.Parent.FullName 'ProjectSettings') -PathType Container)) {
            $assets = $directory
            break
        }
        $directory = $directory.Parent
    }
    if ($null -eq $assets) {
        throw '请选择 Unity 工程的 Assets 文件夹或其中的图片、子文件夹（Assets 同级应有 ProjectSettings）。'
    }
    $uiRoot = Join-Path $assets.FullName 'Content\UI'
    if (-not ($target.FullName.Equals($uiRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $target.FullName.StartsWith($uiRoot + '\', [StringComparison]::OrdinalIgnoreCase))) {
        throw 'Life 版用于 Assets\Content\UI 及其子目录，请在该目录内选择图片或文件夹。'
    }
    # Do not write through junctions or symbolic links outside the detected tree.
    $entry = $target
    while ($null -ne $entry) {
        if (($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "暂不支持链接目录或文件，请在实际 Unity Assets 路径操作：$($entry.FullName)"
        }
        $entry = if ($entry -is [System.IO.DirectoryInfo]) { $entry.Parent } else { $entry.Directory }
    }
    return [pscustomobject]@{
        TargetPath = $target.FullName
        AssetsRoot = $assets.FullName
        ProjectRoot = $assets.Parent.FullName
    }
}

function Test-LifeCursorPath {
    param([Parameter(Mandatory = $true)][string] $ImagePath)
    $context = Resolve-UnityAssetPath -Path $ImagePath
    $relative = $context.TargetPath.Substring($context.ProjectRoot.Length + 1).Replace('\', '/').ToLowerInvariant()
    return $relative.StartsWith('assets/content/ui/image/cursor')
}
