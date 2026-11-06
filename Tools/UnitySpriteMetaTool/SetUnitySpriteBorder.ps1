[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $ImagePath,
    [string] $ConfigPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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
if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
    throw "请先运行 install.bat 配置工具：$ConfigPath"
}
$config = Get-Content -Raw -Encoding UTF8 -LiteralPath $ConfigPath | ConvertFrom-Json
$uiRoot = [System.IO.Path]::GetFullPath([string]$config.uiRoot).TrimEnd('\', '/')
if (-not ($ImagePath.StartsWith($uiRoot + '\', [System.StringComparison]::OrdinalIgnoreCase))) {
    throw "图片必须位于 UI 目录内：$uiRoot"
}

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
    $match = [regex]::Match($metaText, '(?m)^  spriteBorder: \{x: (?<left>\d+), y: (?<bottom>\d+), z: (?<right>\d+), w: (?<top>\d+)\}$')
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
$form.Controls.Add($title)

$sizeLabel = New-Object System.Windows.Forms.Label
$sizeLabel.Text = "图片尺寸：${imageWidth} × ${imageHeight} px    绿色线条是九宫切线"
$sizeLabel.Location = New-Object System.Drawing.Point(20, 43)
$sizeLabel.Size = New-Object System.Drawing.Size(875, 25)
$form.Controls.Add($sizeLabel)

$picture = New-Object System.Windows.Forms.PictureBox
$picture.Location = New-Object System.Drawing.Point(20, 75)
$picture.Size = New-Object System.Drawing.Size(670, 500)
$picture.BackColor = [System.Drawing.Color]::FromArgb(45, 45, 45)
$picture.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
$picture.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
$picture.Image = $previewImage
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

$picture.Add_Paint({
    $box = $picture.ClientRectangle
    $scale = [Math]::Min($box.Width / [double]$imageWidth, $box.Height / [double]$imageHeight)
    $width = $imageWidth * $scale
    $height = $imageHeight * $scale
    $x = ($box.Width - $width) / 2
    $y = ($box.Height - $height) / 2
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::Lime, 1.5)
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
$hint.Text = '左 + 右须小于图片宽度；上 + 下须小于图片高度。保存后会创建或更新同名 .meta。'
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

$form.Add_FormClosed({ $picture.Image = $null; $previewImage.Dispose() })
try {
    [void]$form.ShowDialog()
} finally {
    $form.Dispose()
}
