[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $ImagePath,
    [string] $ConfigPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Set awareness before creating any HWND so Windows does not bitmap-scale the UI.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class SpriteBorderDpi {
    [DllImport("user32.dll")]
    public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
'@
[void][SpriteBorderDpi]::SetThreadDpiAwarenessContext([IntPtr](-4))
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
trap {
    [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, '九宫设置无法打开', 'OK', 'Error') | Out-Null
    exit 1
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $scriptRoot 'config.json'
}
$generatorPath = Join-Path $scriptRoot 'GenerateUnitySpriteMeta.ps1'
$ImagePath = [System.IO.Path]::GetFullPath($ImagePath)
if (-not (Test-Path -LiteralPath $ImagePath -PathType Leaf)) {
    throw "图片不存在：$ImagePath"
}
if (@('.png', '.jpg', '.jpeg') -notcontains [System.IO.Path]::GetExtension($ImagePath).ToLowerInvariant()) {
    throw '九宫设置支持 PNG、JPG、JPEG 图片。'
}
. (Join-Path $scriptRoot 'UnityAssetPath.ps1')
$assetContext = Resolve-UnityAssetPath -Path $ImagePath
$ImagePath = $assetContext.TargetPath

$loaded = [System.Drawing.Image]::FromFile($ImagePath)
try {
    $previewImage = New-Object System.Drawing.Bitmap($loaded)
} finally {
    $loaded.Dispose()
}
$imageWidth = $previewImage.Width
$imageHeight = $previewImage.Height

$initial = @{ Left = 0; Top = 0; Right = 0; Bottom = 0 }
$metaPath = "$ImagePath.meta"
if (Test-Path -LiteralPath $metaPath -PathType Leaf) {
    $metaText = [System.IO.File]::ReadAllText($metaPath)
    if ($metaText -notmatch '(?m)^TextureImporter:\s*$' -or
        $metaText -notmatch '(?m)^  textureType: 8\s*$' -or
        $metaText -notmatch '(?m)^  spriteMode: 1\s*$') {
        $previewImage.Dispose()
        throw '现有 meta 不是单张 Sprite，无法安全修改九宫参数。'
    }
    $match = [regex]::Match($metaText, '(?m)^  spriteBorder: \{x: (?<left>\d+), y: (?<bottom>\d+), z: (?<right>\d+), w: (?<top>\d+)\}(?=\r?$)')
    if (-not $match.Success) {
        $previewImage.Dispose()
        throw '无法读取现有 meta 的 spriteBorder。'
    }
    $initial.Left = [int]$match.Groups['left'].Value
    $initial.Top = [int]$match.Groups['top'].Value
    $initial.Right = [int]$match.Groups['right'].Value
    $initial.Bottom = [int]$match.Groups['bottom'].Value
}

$form = New-Object System.Windows.Forms.Form
$form.SuspendLayout()
$form.AutoScaleDimensions = New-Object System.Drawing.SizeF(96, 96)
$form.AutoScaleMode = [System.Windows.Forms.AutoScaleMode]::Dpi
$form.Text = '设置 Unity Sprite 九宫'
$form.ClientSize = New-Object System.Drawing.Size(920, 650)
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
$form.MaximizeBox = $false
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
$form.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 9)

$title = New-Object System.Windows.Forms.Label
$title.Text = [System.IO.Path]::GetFileName($ImagePath)
$title.Location = New-Object System.Drawing.Point(20, 15)
$title.Size = New-Object System.Drawing.Size(875, 25)
$title.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 11, [System.Drawing.FontStyle]::Bold)
$title.UseMnemonic = $false
$form.Controls.Add($title)

$sizeLabel = New-Object System.Windows.Forms.Label
$sizeLabel.Text = "图片尺寸：${imageWidth} × ${imageHeight} px    拖动绿线调整边距，也可以输入数字"
$sizeLabel.Location = New-Object System.Drawing.Point(20, 43)
$sizeLabel.Size = New-Object System.Drawing.Size(875, 25)
$form.Controls.Add($sizeLabel)

$picture = New-Object System.Windows.Forms.PictureBox
$picture.Location = New-Object System.Drawing.Point(20, 75)
$picture.Size = New-Object System.Drawing.Size(670, 500)
$picture.BackColor = [System.Drawing.Color]::FromArgb(45, 45, 45)
$picture.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
$picture.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
$picture.Image = $null
$form.Controls.Add($picture)

$panel = New-Object System.Windows.Forms.GroupBox
$panel.Text = '九宫边距（像素）'
$panel.Location = New-Object System.Drawing.Point(705, 75)
$panel.Size = New-Object System.Drawing.Size(195, 285)
$form.Controls.Add($panel)

$controls = @{}
$items = @(
    @{ Key = 'Left'; Text = '左 L'; Y = 35; Max = $imageWidth },
    @{ Key = 'Top'; Text = '上 T'; Y = 90; Max = $imageHeight },
    @{ Key = 'Right'; Text = '右 R'; Y = 145; Max = $imageWidth },
    @{ Key = 'Bottom'; Text = '下 B'; Y = 200; Max = $imageHeight }
)
foreach ($item in $items) {
    $label = New-Object System.Windows.Forms.Label
    $label.Text = $item.Text
    $label.Location = New-Object System.Drawing.Point(15, $item.Y)
    $label.Size = New-Object System.Drawing.Size(55, 25)
    $panel.Controls.Add($label)

    $number = New-Object System.Windows.Forms.NumericUpDown
    $number.Location = New-Object System.Drawing.Point(75, ($item.Y - 2))
    $number.Size = New-Object System.Drawing.Size(100, 25)
    $number.Minimum = 0
    $number.Maximum = $item.Max
    $number.Value = $initial[$item.Key]
    $number.Add_ValueChanged({ $picture.Invalidate() })
    $panel.Controls.Add($number)
    $controls[$item.Key] = $number
}

function Get-PreviewGeometry {
    $box = $picture.ClientRectangle
    $scale = [Math]::Min($box.Width / [double]$imageWidth, $box.Height / [double]$imageHeight)
    $width = $imageWidth * $scale
    $height = $imageHeight * $scale
    $x = ($box.Width - $width) / 2
    $y = ($box.Height - $height) / 2
    return @{ X=$x; Y=$y; Width=$width; Height=$height; Scale=$scale }
}
$dragState = @{ Edge = $null }
function Get-HitEdge($point) {
    $g = Get-PreviewGeometry
    $tolerance = 10 * $picture.DeviceDpi / 96.0
    if ($point.X -lt $g.X-$tolerance -or $point.X -gt $g.X+$g.Width+$tolerance -or
        $point.Y -lt $g.Y-$tolerance -or $point.Y -gt $g.Y+$g.Height+$tolerance) { return $null }
    $distances = @{
        Left = [Math]::Abs($point.X - ($g.X + [double]$controls.Left.Value*$g.Scale))
        Right = [Math]::Abs($point.X - ($g.X + ($imageWidth-[double]$controls.Right.Value)*$g.Scale))
        Top = [Math]::Abs($point.Y - ($g.Y + [double]$controls.Top.Value*$g.Scale))
        Bottom = [Math]::Abs($point.Y - ($g.Y + ($imageHeight-[double]$controls.Bottom.Value)*$g.Scale))
    }
    $hit = $distances.GetEnumerator() | Sort-Object Value | Select-Object -First 1
    if ($hit.Value -le $tolerance) { return $hit.Key }
    return $null
}
$picture.Add_MouseDown({
    if ($_.Button -ne [System.Windows.Forms.MouseButtons]::Left) { return }
    $dragState.Edge = Get-HitEdge $_.Location
    if ($null -ne $dragState.Edge) { $picture.Capture = $true }
})
$picture.Add_MouseMove({
    if ($null -ne $dragState.Edge) {
        $g = Get-PreviewGeometry
        $px = [Math]::Round(($_.X-$g.X)/$g.Scale)
        $py = [Math]::Round(($_.Y-$g.Y)/$g.Scale)
        switch ($dragState.Edge) {
            Left { $value=$px; $max=$imageWidth-1-[int]$controls.Right.Value }
            Right { $value=$imageWidth-$px; $max=$imageWidth-1-[int]$controls.Left.Value }
            Top { $value=$py; $max=$imageHeight-1-[int]$controls.Bottom.Value }
            Bottom { $value=$imageHeight-$py; $max=$imageHeight-1-[int]$controls.Top.Value }
        }
        $controls[$dragState.Edge].Value = [Math]::Max(0,[Math]::Min($max,$value))
    }
    $edge = if ($null -ne $dragState.Edge) { $dragState.Edge } else { Get-HitEdge $_.Location }
    $picture.Cursor = if ($edge -in @('Left','Right')) { [System.Windows.Forms.Cursors]::SizeWE } elseif ($edge -in @('Top','Bottom')) { [System.Windows.Forms.Cursors]::SizeNS } else { [System.Windows.Forms.Cursors]::Default }
})
$picture.Add_MouseUp({ $dragState.Edge=$null; $picture.Capture=$false })
$picture.Add_MouseCaptureChanged({ if (-not $picture.Capture) { $dragState.Edge=$null } })
$picture.Add_Paint({
    $g = Get-PreviewGeometry
    $x=$g.X; $y=$g.Y; $width=$g.Width; $height=$g.Height; $scale=$g.Scale
    $_.Graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $_.Graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $rect = New-Object System.Drawing.RectangleF([float]$x,[float]$y,[float]$width,[float]$height)
    $_.Graphics.DrawImage($previewImage,$rect)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::Lime, ([single](1.5*$picture.DeviceDpi/96.0)))
    try {
        $leftX = $x + [double]$controls.Left.Value * $scale
        $rightX = $x + ($imageWidth - [double]$controls.Right.Value) * $scale
        $topY = $y + [double]$controls.Top.Value * $scale
        $bottomY = $y + ($imageHeight - [double]$controls.Bottom.Value) * $scale
        $_.Graphics.DrawLine($pen, [float]$leftX, [float]$y, [float]$leftX, [float]($y + $height))
        $_.Graphics.DrawLine($pen, [float]$rightX, [float]$y, [float]$rightX, [float]($y + $height))
        $_.Graphics.DrawLine($pen, [float]$x, [float]$topY, [float]($x + $width), [float]$topY)
        $_.Graphics.DrawLine($pen, [float]$x, [float]$bottomY, [float]($x + $width), [float]$bottomY)
    } finally {
        $pen.Dispose()
    }
})

$hint = New-Object System.Windows.Forms.Label
$hint.Text = "拖动绿线或输入像素值。`r`n保存后更新图片旁的 .meta。"
$hint.Location = New-Object System.Drawing.Point(705, 380)
$hint.Size = New-Object System.Drawing.Size(190, 85)
$form.Controls.Add($hint)

$save = New-Object System.Windows.Forms.Button
$save.Text = '保存九宫参数'
$save.Location = New-Object System.Drawing.Point(705, 495)
$save.Size = New-Object System.Drawing.Size(195, 38)
$save.Add_Click({
    $left = [int]$controls.Left.Value
    $top = [int]$controls.Top.Value
    $right = [int]$controls.Right.Value
    $bottom = [int]$controls.Bottom.Value
    if ($left + $right -ge $imageWidth -or $top + $bottom -ge $imageHeight) {
        [System.Windows.Forms.MessageBox]::Show('九宫中心必须留有宽度和高度，请减小边距。', '参数无效', 'OK', 'Warning') | Out-Null
        return
    }
    try {
        & $generatorPath -Path $ImagePath -ConfigPath $ConfigPath -BorderLeft $left -BorderTop $top -BorderRight $right -BorderBottom $bottom | Out-Null
        [System.Windows.Forms.MessageBox]::Show("已保存：`n$metaPath", '完成', 'OK', 'Information') | Out-Null
        $form.Close()
    } catch {
        [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, '保存失败', 'OK', 'Error') | Out-Null
    }
})
$form.Controls.Add($save)
$form.AcceptButton = $save

$cancel = New-Object System.Windows.Forms.Button
$cancel.Text = '取消'
$cancel.Location = New-Object System.Drawing.Point(705, 540)
$cancel.Size = New-Object System.Drawing.Size(195, 35)
$cancel.Add_Click({ $form.Close() })
$form.Controls.Add($cancel)
$form.CancelButton = $cancel

# Measure text after DPI scaling; fixed label heights can clip Chinese text or
# overlap the next row when Windows text size differs from the layout scale.
$layoutState = @{ Busy = $false }
function Update-TextLayout {
    if ($layoutState.Busy) { return }
    $layoutState.Busy = $true
    try {
        $gap = [Math]::Max(6, [int](8 * $form.DeviceDpi / 96.0))
        $textWidth = $panel.Right - $title.Left
        $title.Size = New-Object System.Drawing.Size($textWidth, ($title.GetPreferredSize((New-Object System.Drawing.Size($textWidth, 0))).Height))
        $sizeLabel.Top = $title.Bottom + $gap
        $sizeLabel.Size = New-Object System.Drawing.Size($textWidth, ($sizeLabel.GetPreferredSize((New-Object System.Drawing.Size($textWidth, 0))).Height))
        $contentTop = $sizeLabel.Bottom + $gap
        $picture.Top = $contentTop
        $panel.Top = $contentTop
        $hint.Top = $panel.Bottom + 2 * $gap
        $hint.Size = New-Object System.Drawing.Size($panel.Width, ($hint.GetPreferredSize((New-Object System.Drawing.Size($panel.Width, 0))).Height))
        $save.Top = $hint.Bottom + 2 * $gap
        $cancel.Top = $save.Bottom + $gap
        $picture.Height = [Math]::Max($picture.Height, $cancel.Bottom - $contentTop)
        $requiredHeight = [Math]::Max($picture.Bottom, $cancel.Bottom) + 2 * $gap
        if ($form.ClientSize.Height -lt $requiredHeight) {
            $form.ClientSize = New-Object System.Drawing.Size($form.ClientSize.Width, $requiredHeight)
        }
    } finally {
        $layoutState.Busy = $false
    }
}
$form.Add_Shown({ Update-TextLayout })
$form.Add_DpiChanged({ $form.BeginInvoke([System.Action]{ Update-TextLayout }) | Out-Null })
$form.ResumeLayout($true)

$form.Add_FormClosed({ $picture.Image = $null; $previewImage.Dispose() })
try {
    [void]$form.ShowDialog()
} finally {
    $form.Dispose()
}
