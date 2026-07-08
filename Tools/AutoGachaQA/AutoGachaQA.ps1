[CmdletBinding()]
param(
    [switch]$SmokeTest,
    [switch]$InspectTarget
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$nativeSource = @'
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DragonQaTools
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    public static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    }

    public class HotKeyForm : Form
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_TOGGLE = 1001;
        private const int HOTKEY_STOP = 1002;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public event EventHandler ToggleRequested;
        public event EventHandler StopRequested;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterHotKey(Handle, HOTKEY_TOGGLE, 0, (uint)Keys.F8);
            RegisterHotKey(Handle, HOTKEY_STOP, 0, (uint)Keys.F9);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotKey(Handle, HOTKEY_TOGGLE);
            UnregisterHotKey(Handle, HOTKEY_STOP);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_TOGGLE && ToggleRequested != null)
                    ToggleRequested(this, EventArgs.Empty);
                else if (id == HOTKEY_STOP && StopRequested != null)
                    StopRequested(this, EventArgs.Empty);
            }
            base.WndProc(ref m);
        }
    }
}
'@
Add-Type -TypeDefinition $nativeSource -ReferencedAssemblies 'System.Windows.Forms', 'System.Drawing'

$script:WindowTitlePattern = '^龙息\s*[:：]\s*神寂$'
$script:ProcessName = 'Dragonheir'

# 坐标均为游戏客户区比例。点击只落在按钮中央安全区，避免分辨率和窗口缩放导致边缘误触。
$script:Regions = @{
    MainButton       = [pscustomobject]@{ X1 = 0.875; Y1 = 0.895; X2 = 0.970; Y2 = 0.945 }
    MainButtonProbe  = [pscustomobject]@{ X1 = 0.840; Y1 = 0.865; X2 = 0.990; Y2 = 0.970 }
    SkipButton       = [pscustomobject]@{ X1 = 0.950; Y1 = 0.025; X2 = 0.980; Y2 = 0.080 }
    SkipButtonProbe  = [pscustomobject]@{ X1 = 0.930; Y1 = 0.005; X2 = 0.995; Y2 = 0.115 }
    ResultButton     = [pscustomobject]@{ X1 = 0.550; Y1 = 0.915; X2 = 0.665; Y2 = 0.965 }
    ResultButtonProbe = [pscustomobject]@{ X1 = 0.510; Y1 = 0.875; X2 = 0.700; Y2 = 0.990 }
}

$script:RunState = 'Idle'
$script:Phase = 'None'
$script:TargetWindow = [IntPtr]::Zero
$script:TargetRounds = 1
$script:StartedRounds = 0
$script:PhaseStartedAt = [DateTime]::MinValue
$script:LastProbeAt = [DateTime]::MinValue
$script:BackgroundMode = $false
$script:BackgroundFallbackLogged = $false

function Get-TargetWindow {
    $candidateHandles = @(
        [DragonQaTools.NativeMethods]::FindWindow($null, '龙息: 神寂')
        [DragonQaTools.NativeMethods]::FindWindow($null, '龙息:神寂')
        [DragonQaTools.NativeMethods]::FindWindow($null, '龙息：神寂')
    ) | Where-Object { $_ -ne [IntPtr]::Zero } | Select-Object -Unique

    $validatedHandles = @()
    foreach ($handle in $candidateHandles) {
        [uint32]$processId = 0
        [DragonQaTools.NativeMethods]::GetWindowThreadProcessId($handle, [ref]$processId) | Out-Null
        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($null -ne $process -and $process.ProcessName -eq $script:ProcessName) {
            $validatedHandles += [IntPtr]$handle
        }
    }

    if ($validatedHandles.Count -eq 1) {
        return [IntPtr]$validatedHandles[0]
    }

    $matches = @(Get-Process -Name $script:ProcessName -ErrorAction SilentlyContinue | Where-Object {
        $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle -match $script:WindowTitlePattern
    })

    if ($matches.Count -ne 1) {
        return [IntPtr]::Zero
    }

    return [IntPtr]$matches[0].MainWindowHandle
}

function Get-ClientGeometry {
    param([Parameter(Mandatory = $true)][IntPtr]$WindowHandle)

    $rect = New-Object DragonQaTools.RECT
    if (-not [DragonQaTools.NativeMethods]::GetClientRect($WindowHandle, [ref]$rect)) {
        throw '无法读取游戏客户区尺寸。'
    }

    $origin = New-Object DragonQaTools.POINT
    $origin.X = 0
    $origin.Y = 0
    if (-not [DragonQaTools.NativeMethods]::ClientToScreen($WindowHandle, [ref]$origin)) {
        throw '无法读取游戏客户区屏幕位置。'
    }

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -lt 800 -or $height -lt 450) {
        throw "游戏窗口过小或已最小化（客户区 ${width}x${height}）。"
    }

    return [pscustomobject]@{
        Left = $origin.X
        Top = $origin.Y
        Width = $width
        Height = $height
    }
}

function Get-ScaledRectangle {
    param(
        [Parameter(Mandatory = $true)]$Geometry,
        [Parameter(Mandatory = $true)]$Region
    )

    $left = [int][Math]::Floor($Geometry.Width * $Region.X1)
    $top = [int][Math]::Floor($Geometry.Height * $Region.Y1)
    $right = [int][Math]::Ceiling($Geometry.Width * $Region.X2)
    $bottom = [int][Math]::Ceiling($Geometry.Height * $Region.Y2)

    return [pscustomobject]@{
        Left = $left
        Top = $top
        Width = [Math]::Max(1, $right - $left)
        Height = [Math]::Max(1, $bottom - $top)
    }
}

function Test-TargetIsForeground {
    return [DragonQaTools.NativeMethods]::GetForegroundWindow() -eq $script:TargetWindow
}

function Get-RegionRedRatio {
    param(
        [Parameter(Mandatory = $true)][IntPtr]$WindowHandle,
        [Parameter(Mandatory = $true)]$Region
    )

    $geometry = Get-ClientGeometry -WindowHandle $WindowHandle
    $sampleRect = Get-ScaledRectangle -Geometry $geometry -Region $Region
    if ($script:BackgroundMode) {
        $bitmap = New-Object System.Drawing.Bitmap($geometry.Width, $geometry.Height)
    }
    else {
        $bitmap = New-Object System.Drawing.Bitmap($sampleRect.Width, $sampleRect.Height)
    }
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        if ($script:BackgroundMode) {
            $dc = $graphics.GetHdc()
            try {
                # PW_CLIENTONLY | PW_RENDERFULLCONTENT。部分 DirectX 客户端不支持，失败时返回 -1 触发保守等待。
                $captured = [DragonQaTools.NativeMethods]::PrintWindow($WindowHandle, $dc, 3)
            }
            finally {
                $graphics.ReleaseHdc($dc)
            }
            if (-not $captured) { return -1.0 }
            $offsetX = $sampleRect.Left
            $offsetY = $sampleRect.Top
        }
        else {
            $source = New-Object System.Drawing.Point(
                ($geometry.Left + $sampleRect.Left),
                ($geometry.Top + $sampleRect.Top)
            )
            $graphics.CopyFromScreen(
                $source,
                [System.Drawing.Point]::Empty,
                $bitmap.Size,
                [System.Drawing.CopyPixelOperation]::SourceCopy
            )
            $offsetX = 0
            $offsetY = 0
        }

        $step = [int][Math]::Max(3, [Math]::Floor([Math]::Min($sampleRect.Width, $sampleRect.Height) / 22))
        $redPixels = 0
        $sampledPixels = 0
        $brightness = 0

        for ($y = 1; $y -lt $sampleRect.Height; $y += $step) {
            for ($x = 1; $x -lt $sampleRect.Width; $x += $step) {
                $color = $bitmap.GetPixel(($offsetX + $x), ($offsetY + $y))
                $sampledPixels++
                $brightness += $color.R + $color.G + $color.B
                if ($color.R -ge 80 -and ($color.R - $color.G) -ge 25 -and ($color.R - $color.B) -ge 15) {
                    $redPixels++
                }
            }
        }

        if ($sampledPixels -eq 0) { return 0.0 }
        if ($script:BackgroundMode -and (($brightness / ($sampledPixels * 3.0)) -lt 4.0)) {
            return -1.0
        }
        return [double]$redPixels / [double]$sampledPixels
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Invoke-RegionClick {
    param(
        [Parameter(Mandatory = $true)][IntPtr]$WindowHandle,
        [Parameter(Mandatory = $true)]$Region
    )

    if (-not $script:BackgroundMode -and -not (Test-TargetIsForeground)) {
        throw '游戏窗口不在前台，已阻止点击。'
    }

    $geometry = Get-ClientGeometry -WindowHandle $WindowHandle
    $clickRect = Get-ScaledRectangle -Geometry $geometry -Region $Region
    $x = Get-Random -Minimum $clickRect.Left -Maximum ($clickRect.Left + $clickRect.Width)
    $y = Get-Random -Minimum $clickRect.Top -Maximum ($clickRect.Top + $clickRect.Height)
    if ($script:BackgroundMode) {
        if ([DragonQaTools.NativeMethods]::IsIconic($WindowHandle)) {
            throw '后台模式不支持最小化窗口，请先恢复游戏窗口。'
        }

        $lParamValue = (($y -band 0xFFFF) -shl 16) -bor ($x -band 0xFFFF)
        $lParam = [IntPtr]$lParamValue
        [DragonQaTools.NativeMethods]::PostMessage($WindowHandle, 0x0200, [IntPtr]::Zero, $lParam) | Out-Null
        $downOk = [DragonQaTools.NativeMethods]::PostMessage($WindowHandle, 0x0201, [IntPtr]1, $lParam)
        Start-Sleep -Milliseconds 35
        $upOk = [DragonQaTools.NativeMethods]::PostMessage($WindowHandle, 0x0202, [IntPtr]::Zero, $lParam)
        if (-not $downOk -or -not $upOk) {
            throw '目标窗口未接受后台点击消息。'
        }
    }
    else {
        $screenX = $geometry.Left + $x
        $screenY = $geometry.Top + $y

        if (-not [DragonQaTools.NativeMethods]::SetCursorPos($screenX, $screenY)) {
            throw '无法移动鼠标到目标按钮。'
        }

        [DragonQaTools.NativeMethods]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 35
        [DragonQaTools.NativeMethods]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    }
}

$form = New-Object DragonQaTools.HotKeyForm
$form.Text = '抽卡回归测试工具'
$form.StartPosition = 'CenterScreen'
$form.ClientSize = New-Object System.Drawing.Size(540, 600)
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
$form.MaximizeBox = $false
$form.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 9)

$titleLabel = New-Object System.Windows.Forms.Label
$titleLabel.Text = 'Dragonheir 抽卡回归测试'
$titleLabel.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 16, [System.Drawing.FontStyle]::Bold)
$titleLabel.Location = New-Object System.Drawing.Point(20, 18)
$titleLabel.Size = New-Object System.Drawing.Size(450, 34)
$form.Controls.Add($titleLabel)

$boundaryLabel = New-Object System.Windows.Forms.Label
$boundaryLabel.Text = '仅用于 Development Build 的 UI 回归测试，不包含反检测或安全绕过功能。'
$boundaryLabel.ForeColor = [System.Drawing.Color]::FromArgb(150, 70, 20)
$boundaryLabel.Location = New-Object System.Drawing.Point(22, 58)
$boundaryLabel.Size = New-Object System.Drawing.Size(495, 38)
$form.Controls.Add($boundaryLabel)

$roundLabel = New-Object System.Windows.Forms.Label
$roundLabel.Text = '十连次数：'
$roundLabel.Location = New-Object System.Drawing.Point(22, 111)
$roundLabel.Size = New-Object System.Drawing.Size(90, 25)
$form.Controls.Add($roundLabel)

$roundInput = New-Object System.Windows.Forms.NumericUpDown
$roundInput.Location = New-Object System.Drawing.Point(112, 108)
$roundInput.Size = New-Object System.Drawing.Size(90, 28)
$roundInput.Minimum = 1
$roundInput.Maximum = 999
$roundInput.Value = 1
$form.Controls.Add($roundInput)

$pullsLabel = New-Object System.Windows.Forms.Label
$pullsLabel.Text = '（1 次 = 10 抽；最终停在结果页）'
$pullsLabel.ForeColor = [System.Drawing.Color]::DimGray
$pullsLabel.Location = New-Object System.Drawing.Point(215, 111)
$pullsLabel.Size = New-Object System.Drawing.Size(270, 25)
$form.Controls.Add($pullsLabel)

$confirmCheck = New-Object System.Windows.Forms.CheckBox
$confirmCheck.Text = '我确认当前是 Development Build 测试环境，并了解会消耗测试抽卡资源'
$confirmCheck.Location = New-Object System.Drawing.Point(22, 151)
$confirmCheck.Size = New-Object System.Drawing.Size(495, 28)
$form.Controls.Add($confirmCheck)

$backgroundCheck = New-Object System.Windows.Forms.CheckBox
$backgroundCheck.Text = '后台运行（实验）：不抢鼠标；不支持最小化；首次请只测 1 次十连'
$backgroundCheck.Location = New-Object System.Drawing.Point(22, 184)
$backgroundCheck.Size = New-Object System.Drawing.Size(495, 28)
$form.Controls.Add($backgroundCheck)

$targetLabel = New-Object System.Windows.Forms.Label
$targetLabel.Text = '目标：Dragonheir.exe / 窗口“龙息: 神寂”'
$targetLabel.Location = New-Object System.Drawing.Point(22, 222)
$targetLabel.Size = New-Object System.Drawing.Size(490, 25)
$form.Controls.Add($targetLabel)

$jitterLabel = New-Object System.Windows.Forms.Label
$jitterLabel.Text = '落点：按钮中央安全区内变化，用于窗口适配与热区覆盖'
$jitterLabel.Location = New-Object System.Drawing.Point(22, 250)
$jitterLabel.Size = New-Object System.Drawing.Size(490, 25)
$form.Controls.Add($jitterLabel)

$startButton = New-Object System.Windows.Forms.Button
$startButton.Text = '开始'
$startButton.Location = New-Object System.Drawing.Point(22, 287)
$startButton.Size = New-Object System.Drawing.Size(150, 40)
$form.Controls.Add($startButton)

$pauseButton = New-Object System.Windows.Forms.Button
$pauseButton.Text = '暂停 / 继续  F8'
$pauseButton.Location = New-Object System.Drawing.Point(185, 287)
$pauseButton.Size = New-Object System.Drawing.Size(160, 40)
$pauseButton.Enabled = $false
$form.Controls.Add($pauseButton)

$stopButton = New-Object System.Windows.Forms.Button
$stopButton.Text = '停止  F9'
$stopButton.Location = New-Object System.Drawing.Point(358, 287)
$stopButton.Size = New-Object System.Drawing.Size(160, 40)
$stopButton.Enabled = $false
$form.Controls.Add($stopButton)

$statusTitle = New-Object System.Windows.Forms.Label
$statusTitle.Text = '状态：'
$statusTitle.Location = New-Object System.Drawing.Point(22, 345)
$statusTitle.Size = New-Object System.Drawing.Size(55, 25)
$form.Controls.Add($statusTitle)

$statusLabel = New-Object System.Windows.Forms.Label
$statusLabel.Text = '待机'
$statusLabel.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 10, [System.Drawing.FontStyle]::Bold)
$statusLabel.ForeColor = [System.Drawing.Color]::FromArgb(35, 85, 140)
$statusLabel.Location = New-Object System.Drawing.Point(77, 343)
$statusLabel.Size = New-Object System.Drawing.Size(430, 28)
$form.Controls.Add($statusLabel)

$progressBar = New-Object System.Windows.Forms.ProgressBar
$progressBar.Location = New-Object System.Drawing.Point(22, 376)
$progressBar.Size = New-Object System.Drawing.Size(496, 20)
$progressBar.Minimum = 0
$progressBar.Maximum = 1
$form.Controls.Add($progressBar)

$logBox = New-Object System.Windows.Forms.TextBox
$logBox.Location = New-Object System.Drawing.Point(22, 412)
$logBox.Size = New-Object System.Drawing.Size(496, 138)
$logBox.Multiline = $true
$logBox.ReadOnly = $true
$logBox.ScrollBars = [System.Windows.Forms.ScrollBars]::Vertical
$logBox.BackColor = [System.Drawing.Color]::White
$form.Controls.Add($logBox)

$hintLabel = New-Object System.Windows.Forms.Label
$hintLabel.Text = '开始后请勿操作鼠标；切换到其他窗口会自动暂停。'
$hintLabel.ForeColor = [System.Drawing.Color]::DimGray
$hintLabel.Location = New-Object System.Drawing.Point(22, 561)
$hintLabel.Size = New-Object System.Drawing.Size(496, 24)
$form.Controls.Add($hintLabel)

function Add-Log {
    param([string]$Message)
    $line = '[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss'), $Message
    if ($logBox.Lines.Count -gt 100) {
        $logBox.Lines = @($logBox.Lines | Select-Object -Last 80)
    }
    $logBox.AppendText($line + [Environment]::NewLine)
    $logBox.SelectionStart = $logBox.TextLength
    $logBox.ScrollToCaret()
}

function Set-UiForState {
    switch ($script:RunState) {
        'Running' {
            $startButton.Enabled = $false
            $roundInput.Enabled = $false
            $confirmCheck.Enabled = $false
            $backgroundCheck.Enabled = $false
            $pauseButton.Enabled = $true
            $stopButton.Enabled = $true
        }
        'Paused' {
            $startButton.Enabled = $false
            $roundInput.Enabled = $false
            $confirmCheck.Enabled = $false
            $backgroundCheck.Enabled = $false
            $pauseButton.Enabled = $true
            $stopButton.Enabled = $true
        }
        default {
            $startButton.Enabled = $true
            $roundInput.Enabled = $true
            $confirmCheck.Enabled = $true
            $backgroundCheck.Enabled = $true
            $pauseButton.Enabled = $false
            $stopButton.Enabled = $false
        }
    }
}

function Pause-Automation {
    param([string]$Reason)
    if ($script:RunState -ne 'Running') { return }
    $script:RunState = 'Paused'
    $statusLabel.Text = '已暂停：' + $Reason
    $statusLabel.ForeColor = [System.Drawing.Color]::DarkOrange
    Add-Log ('已暂停：' + $Reason + '；按 F8 继续，或按 F9 停止。')
    Set-UiForState
}

function Stop-Automation {
    param([string]$Reason = '用户停止')
    if ($script:RunState -eq 'Idle') { return }
    $script:RunState = 'Idle'
    $script:Phase = 'None'
    $script:TargetWindow = [IntPtr]::Zero
    $statusLabel.Text = $Reason
    $statusLabel.ForeColor = [System.Drawing.Color]::FromArgb(35, 85, 140)
    Add-Log $Reason
    Set-UiForState
}

function Complete-Automation {
    $script:RunState = 'Completed'
    $script:Phase = 'None'
    $statusLabel.Text = "完成：已发起 $($script:StartedRounds) 次十连，停在结果页"
    $statusLabel.ForeColor = [System.Drawing.Color]::DarkGreen
    Add-Log "任务完成，共发起 $($script:StartedRounds) 次十连（$($script:StartedRounds * 10) 抽）。"
    [System.Media.SystemSounds]::Asterisk.Play()
    Set-UiForState
}

function Resume-Automation {
    if ($script:RunState -ne 'Paused') { return }
    $window = Get-TargetWindow
    if ($window -eq [IntPtr]::Zero) {
        [System.Windows.Forms.MessageBox]::Show(
            '没有找到唯一的“龙息: 神寂”游戏窗口。',
            '无法继续',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Warning
        ) | Out-Null
        return
    }

    $script:TargetWindow = $window
    if (-not $script:BackgroundMode) {
        [DragonQaTools.NativeMethods]::SetForegroundWindow($script:TargetWindow) | Out-Null
        Start-Sleep -Milliseconds 180
        if (-not (Test-TargetIsForeground)) {
            $statusLabel.Text = '继续失败：请先把游戏窗口切到前台'
            return
        }
    }
    elseif ([DragonQaTools.NativeMethods]::IsIconic($script:TargetWindow)) {
        $statusLabel.Text = '继续失败：后台模式不支持最小化，请恢复游戏窗口'
        return
    }

    $script:RunState = 'Running'
    $script:PhaseStartedAt = Get-Date
    $statusLabel.Text = '运行中：继续等待当前界面'
    $statusLabel.ForeColor = [System.Drawing.Color]::DarkGreen
    Add-Log '已继续。'
    Set-UiForState
}

function Toggle-Automation {
    if ($script:RunState -eq 'Running') {
        Pause-Automation -Reason '用户暂停'
    }
    elseif ($script:RunState -eq 'Paused') {
        Resume-Automation
    }
}

$startButton.Add_Click({
    if (-not $confirmCheck.Checked) {
        [System.Windows.Forms.MessageBox]::Show(
            '请先确认当前是 Development Build 测试环境。',
            '需要确认',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Information
        ) | Out-Null
        return
    }

    if ($backgroundCheck.Checked -and [int]$roundInput.Value -gt 1) {
        $choice = [System.Windows.Forms.MessageBox]::Show(
            '后台模式依赖当前客户端接收窗口消息。首次使用建议先设为 1 次十连验证。确定继续执行多次十连吗？',
            '后台模式提示',
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Warning
        )
        if ($choice -ne [System.Windows.Forms.DialogResult]::Yes) { return }
    }

    $window = Get-TargetWindow
    if ($window -eq [IntPtr]::Zero) {
        [System.Windows.Forms.MessageBox]::Show(
            '没有找到唯一的 Dragonheir.exe 窗口“龙息: 神寂”。请先打开游戏并进入日芒召唤首页。',
            '未找到目标窗口',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Warning
        ) | Out-Null
        return
    }

    try {
        Get-ClientGeometry -WindowHandle $window | Out-Null
    }
    catch {
        [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, '窗口不可用') | Out-Null
        return
    }

    $script:TargetWindow = $window
    $script:TargetRounds = [int]$roundInput.Value
    $script:BackgroundMode = $backgroundCheck.Checked
    $script:BackgroundFallbackLogged = $false
    $script:StartedRounds = 0
    $script:Phase = 'WaitMain'
    $script:PhaseStartedAt = Get-Date
    $script:LastProbeAt = [DateTime]::MinValue
    $script:RunState = 'Running'
    $progressBar.Maximum = $script:TargetRounds
    $progressBar.Value = 0
    if ($script:BackgroundMode) {
        $statusLabel.Text = '后台运行中：等待召唤首页'
    }
    else {
        $statusLabel.Text = '运行中：等待召唤首页'
    }
    $statusLabel.ForeColor = [System.Drawing.Color]::DarkGreen
    $modeText = if ($script:BackgroundMode) { '后台实验模式' } else { '前台稳定模式' }
    Add-Log "开始任务：$($script:TargetRounds) 次十连；$modeText。"
    Set-UiForState

    if (-not $script:BackgroundMode) {
        [DragonQaTools.NativeMethods]::SetForegroundWindow($script:TargetWindow) | Out-Null
    }
})

$pauseButton.Add_Click({ Toggle-Automation })
$stopButton.Add_Click({ Stop-Automation -Reason '已停止' })
$form.add_ToggleRequested({ Toggle-Automation })
$form.add_StopRequested({ Stop-Automation -Reason '已由 F9 紧急停止' })

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 350
$timer.Add_Tick({
    if ($script:RunState -ne 'Running') { return }

    if ((Get-TargetWindow) -ne $script:TargetWindow) {
        Pause-Automation -Reason '目标窗口已关闭、重启或不唯一'
        return
    }

    if (-not $script:BackgroundMode -and -not (Test-TargetIsForeground)) {
        Pause-Automation -Reason '检测到已切换到其他窗口'
        return
    }

    if ($script:BackgroundMode -and [DragonQaTools.NativeMethods]::IsIconic($script:TargetWindow)) {
        Pause-Automation -Reason '后台模式不支持最小化，请恢复游戏窗口'
        return
    }

    $now = Get-Date
    if (($now - $script:LastProbeAt).TotalMilliseconds -lt 650) { return }
    $script:LastProbeAt = $now
    $phaseSeconds = ($now - $script:PhaseStartedAt).TotalSeconds

    try {
        switch ($script:Phase) {
            'WaitMain' {
                $mainTimeout = if ($script:BackgroundMode) { 35 } else { 20 }
                if ($phaseSeconds -gt $mainTimeout) {
                    Pause-Automation -Reason "$mainTimeout 秒内未识别到召唤首页按钮"
                    return
                }

                $ratio = Get-RegionRedRatio -WindowHandle $script:TargetWindow -Region $script:Regions.MainButtonProbe
                if ($script:BackgroundMode -and $ratio -lt 0 -and -not $script:BackgroundFallbackLogged) {
                    Add-Log '当前 DirectX 客户端不支持后台画面捕获，已改用保守固定等待；请先完成 1 次十连验证。'
                    $script:BackgroundFallbackLogged = $true
                }
                $mainReady = ($ratio -ge 0.075) -or ($script:BackgroundMode -and $phaseSeconds -ge 2.5)
                if ($mainReady) {
                    Invoke-RegionClick -WindowHandle $script:TargetWindow -Region $script:Regions.MainButton
                    $script:StartedRounds = 1
                    $progressBar.Value = 1
                    $script:Phase = 'WaitSkip'
                    $script:PhaseStartedAt = Get-Date
                    $statusLabel.Text = "运行中：第 1/$($script:TargetRounds) 次十连，等待跳过"
                    Add-Log '已点击召唤10次，等待跳过按钮。'
                }
            }
            'WaitSkip' {
                $skipTimeout = if ($script:BackgroundMode) { 40 } else { 25 }
                if ($phaseSeconds -gt $skipTimeout) {
                    Pause-Automation -Reason "$skipTimeout 秒内未识别到跳过按钮"
                    return
                }
                if ($phaseSeconds -lt 1.0) { return }

                $ratio = Get-RegionRedRatio -WindowHandle $script:TargetWindow -Region $script:Regions.SkipButtonProbe
                if ($script:BackgroundMode -and $ratio -lt 0 -and -not $script:BackgroundFallbackLogged) {
                    Add-Log '当前 DirectX 客户端不支持后台画面捕获，已改用保守固定等待；请先完成 1 次十连验证。'
                    $script:BackgroundFallbackLogged = $true
                }
                $skipReady = ($ratio -ge 0.018) -or ($script:BackgroundMode -and $phaseSeconds -ge 6.0)
                if ($skipReady) {
                    Invoke-RegionClick -WindowHandle $script:TargetWindow -Region $script:Regions.SkipButton
                    $script:Phase = 'WaitResult'
                    $script:PhaseStartedAt = Get-Date
                    $statusLabel.Text = "运行中：第 $($script:StartedRounds)/$($script:TargetRounds) 次十连，等待结果页"
                    Add-Log '已点击跳过，等待抽卡结果。'
                }
            }
            'WaitResult' {
                $resultTimeout = if ($script:BackgroundMode) { 40 } else { 20 }
                if ($phaseSeconds -gt $resultTimeout) {
                    Pause-Automation -Reason "$resultTimeout 秒内未识别到结果页十连按钮"
                    return
                }
                if ($phaseSeconds -lt 1.0) { return }

                $ratio = Get-RegionRedRatio -WindowHandle $script:TargetWindow -Region $script:Regions.ResultButtonProbe
                if ($script:BackgroundMode -and $ratio -lt 0 -and -not $script:BackgroundFallbackLogged) {
                    Add-Log '当前 DirectX 客户端不支持后台画面捕获，已改用保守固定等待；请先完成 1 次十连验证。'
                    $script:BackgroundFallbackLogged = $true
                }
                $resultReady = ($ratio -ge 0.075) -or ($script:BackgroundMode -and $phaseSeconds -ge 10.0)
                if ($resultReady) {
                    if ($script:StartedRounds -ge $script:TargetRounds) {
                        Complete-Automation
                        return
                    }

                    Invoke-RegionClick -WindowHandle $script:TargetWindow -Region $script:Regions.ResultButton
                    $script:StartedRounds++
                    $progressBar.Value = $script:StartedRounds
                    $script:Phase = 'WaitSkip'
                    $script:PhaseStartedAt = Get-Date
                    $statusLabel.Text = "运行中：第 $($script:StartedRounds)/$($script:TargetRounds) 次十连，等待跳过"
                    Add-Log "已从结果页发起第 $($script:StartedRounds) 次十连。"
                }
            }
        }
    }
    catch {
        Pause-Automation -Reason $_.Exception.Message
    }
})

$form.Add_FormClosing({
    if ($script:RunState -eq 'Running' -or $script:RunState -eq 'Paused') {
        $choice = [System.Windows.Forms.MessageBox]::Show(
            '任务尚未结束，确定关闭工具吗？',
            '确认关闭',
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Question
        )
        if ($choice -ne [System.Windows.Forms.DialogResult]::Yes) {
            $_.Cancel = $true
            return
        }
    }
    $timer.Stop()
})

$timer.Start()
Add-Log '工具已就绪。请将游戏停在日芒召唤首页后开始。'

if ($SmokeTest) {
    $timer.Stop()
    Write-Output 'Smoke test: OK'
    return
}

if ($InspectTarget) {
    $timer.Stop()
    $inspectWindow = Get-TargetWindow
    if ($inspectWindow -eq [IntPtr]::Zero) {
        throw 'Inspect target: 未找到唯一目标窗口。'
    }
    $script:TargetWindow = $inspectWindow
    $geometry = Get-ClientGeometry -WindowHandle $inspectWindow
    $script:BackgroundMode = $false
    $foregroundRatio = Get-RegionRedRatio -WindowHandle $inspectWindow -Region $script:Regions.MainButtonProbe
    $script:BackgroundMode = $true
    $backgroundRatio = Get-RegionRedRatio -WindowHandle $inspectWindow -Region $script:Regions.MainButtonProbe
    Write-Output ("Inspect target: {0}x{1}; foregroundRatio={2:N4}; backgroundRatio={3:N4}" -f $geometry.Width, $geometry.Height, $foregroundRatio, $backgroundRatio)
    return
}

[System.Windows.Forms.Application]::Run($form)
