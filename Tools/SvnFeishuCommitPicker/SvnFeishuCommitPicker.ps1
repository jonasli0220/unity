param(
    [Parameter(Position = 0)]
    [string]$PathListFile,

    [Parameter(Position = 1)]
    [string]$MessageFile,

    [Parameter(Position = 2)]
    [string]$Cwd,

    [string]$Project = "",

    [switch]$ListTickets,

    [string]$TestPickIds,

    [switch]$PasteClipboard,

    [switch]$DismissHookSuccess,

    [switch]$DebugArgs
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SampleConfigPath = Join-Path $ScriptDir "config.sample.json"
$LocalConfigPath = Join-Path $ScriptDir "config.local.json"
$UiSettingsPath = Join-Path $ScriptDir "settings.local.json"

try {
    Add-Type @"
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

public static class SvnPickerNative {
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const uint WM_PASTE = 0x0302;
    private const uint TTM_POP = 0x041C;
    private const int SW_HIDE = 0;

    private static bool IsTortoiseWindow(IntPtr hWnd) {
        uint processId;
        GetWindowThreadProcessId(hWnd, out processId);
        try {
            Process process = Process.GetProcessById((int)processId);
            return string.Equals(process.ProcessName, "TortoiseProc", StringComparison.OrdinalIgnoreCase);
        }
        catch {
            return false;
        }
    }

    private static bool PopVisibleTooltip(IntPtr hWnd) {
        if (!IsWindowVisible(hWnd)) {
            return false;
        }

        StringBuilder classNameBuilder = new StringBuilder(128);
        GetClassName(hWnd, classNameBuilder, classNameBuilder.Capacity);
        if (!string.Equals(classNameBuilder.ToString(), "tooltips_class32", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        SendMessage(hWnd, TTM_POP, IntPtr.Zero, IntPtr.Zero);
        ShowWindow(hWnd, SW_HIDE);
        return true;
    }

    private static string ReadWindowText(IntPtr hWnd) {
        int length = Math.Max(256, GetWindowTextLength(hWnd) + 1);
        StringBuilder text = new StringBuilder(length);
        GetWindowText(hWnd, text, text.Capacity);
        return text.ToString();
    }

    private static bool HideHookSuccessNotice(IntPtr topLevel) {
        if (!IsWindowVisible(topLevel)) {
            return false;
        }

        string title = ReadWindowText(topLevel).Trim();
        bool isNotice = string.Equals(title, "Notice", StringComparison.OrdinalIgnoreCase);
        bool hasSuccessText = title.IndexOf("hook script executed successfully", StringComparison.OrdinalIgnoreCase) >= 0;

        if (!hasSuccessText) {
            EnumChildWindows(topLevel, delegate(IntPtr child, IntPtr unused) {
                string childText = ReadWindowText(child);
                if (childText.IndexOf("hook script executed successfully", StringComparison.OrdinalIgnoreCase) >= 0) {
                    hasSuccessText = true;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }

        if (!isNotice && !hasSuccessText) {
            return false;
        }

        ShowWindow(topLevel, SW_HIDE);
        return true;
    }

    public static IntPtr FindTortoiseMessageBox() {
        IntPtr bestHandle = IntPtr.Zero;
        long bestScore = 0;

        EnumWindows(delegate(IntPtr topLevel, IntPtr unused) {
            if (!IsWindowVisible(topLevel)) {
                return true;
            }

            if (!IsTortoiseWindow(topLevel)) {
                return true;
            }

            EnumChildWindows(topLevel, delegate(IntPtr child, IntPtr childUnused) {
                if (!IsWindowVisible(child)) {
                    return true;
                }

                StringBuilder classNameBuilder = new StringBuilder(128);
                GetClassName(child, classNameBuilder, classNameBuilder.Capacity);
                string className = classNameBuilder.ToString();
                int classPriority = 0;
                if (className.IndexOf("Scintilla", StringComparison.OrdinalIgnoreCase) >= 0) {
                    classPriority = 3;
                }
                else if (className.IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) >= 0) {
                    classPriority = 2;
                }
                else if (string.Equals(className, "Edit", StringComparison.OrdinalIgnoreCase)) {
                    classPriority = 1;
                }

                if (classPriority == 0) {
                    return true;
                }

                Rect rect;
                if (!GetWindowRect(child, out rect)) {
                    return true;
                }

                long width = Math.Max(0, rect.Right - rect.Left);
                long height = Math.Max(0, rect.Bottom - rect.Top);
                long score = ((long)classPriority * 1000000000000L) + (width * height);
                if (score > bestScore) {
                    bestScore = score;
                    bestHandle = child;
                }

                return true;
            }, IntPtr.Zero);

            return true;
        }, IntPtr.Zero);

        return bestHandle;
    }

    public static bool PasteClipboardIntoTortoiseMessage() {
        IntPtr messageBox = FindTortoiseMessageBox();
        if (messageBox == IntPtr.Zero) {
            return false;
        }

        if (!IsWindowEnabled(messageBox)) {
            return false;
        }

        SendMessage(messageBox, WM_PASTE, IntPtr.Zero, IntPtr.Zero);
        return true;
    }

    public static bool DismissTortoiseTooltip() {
        bool successNoticeDismissed = false;

        EnumWindows(delegate(IntPtr topLevel, IntPtr unused) {
            if (!IsTortoiseWindow(topLevel)) {
                return true;
            }

            if (HideHookSuccessNotice(topLevel)) {
                successNoticeDismissed = true;
            }

            PopVisibleTooltip(topLevel);

            EnumChildWindows(topLevel, delegate(IntPtr child, IntPtr childUnused) {
                PopVisibleTooltip(child);
                return true;
            }, IntPtr.Zero);

            return true;
        }, IntPtr.Zero);

        return successNoticeDismissed;
    }
}
"@
    [SvnPickerNative]::SetProcessDPIAware() | Out-Null
}
catch {
    # Older systems can ignore this. The UI still works without DPI awareness.
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

function Write-JsonFile {
    param($Value, [string]$Path)

    $json = $Value | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($Path, $json, [System.Text.UTF8Encoding]::new($false))
}

function Resolve-ToolPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $Path
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $ScriptDir $Path
}

function ConvertTo-QueryValue {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    return [uri]::EscapeDataString($Value)
}

function Add-QueryParameter {
    param([string]$Uri, [string]$Name, [string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $Uri
    }

    $separator = "?"
    if ($Uri.Contains("?")) {
        $separator = "&"
    }

    return "$Uri$separator$Name=$(ConvertTo-QueryValue $Value)"
}

function Get-ProjectMatchText {
    $values = New-Object System.Collections.Generic.List[string]
    foreach ($value in @($Cwd, $PathListFile)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$value)) {
            $values.Add([string]$value) | Out-Null
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($PathListFile) -and (Test-Path -LiteralPath $PathListFile -PathType Leaf)) {
        try {
            foreach ($line in @(Get-Content -LiteralPath $PathListFile -ErrorAction Stop)) {
                if (-not [string]::IsNullOrWhiteSpace([string]$line)) {
                    $values.Add([string]$line) | Out-Null
                }
            }
        }
        catch {
        }
    }

    return ($values -join "`n")
}

function Resolve-ProjectProfile {
    param($Config, [string]$RequestedProject)

    $profiles = @($Config.projectProfiles)
    if ($profiles.Count -eq 0) {
        return $null
    }

    if (-not [string]::IsNullOrWhiteSpace($RequestedProject)) {
        $selected = @($profiles | Where-Object { [string]$_.id -ieq $RequestedProject } | Select-Object -First 1)
        if ($selected.Count -eq 0) {
            throw "Unknown project profile: $RequestedProject"
        }
        return $selected[0]
    }

    $matchText = Get-ProjectMatchText
    foreach ($profile in $profiles) {
        foreach ($pattern in @($profile.pathPatterns)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$pattern) -and
                $matchText.IndexOf([string]$pattern, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                return $profile
            }
        }
    }

    $defaultId = [string]$Config.defaultProject
    return @($profiles | Where-Object { [string]$_.id -ieq $defaultId } | Select-Object -First 1)[0]
}

function Apply-ProjectProfile {
    param($Config, $Profile)

    if ($null -eq $Profile) {
        return $Config
    }

    foreach ($property in $Profile.PSObject.Properties) {
        if ($property.Name -in @("pathPatterns")) {
            continue
        }
        $Config | Add-Member -MemberType NoteProperty -Name $property.Name -Value $property.Value -Force
    }
    return $Config
}

function Get-CurrentUserIdentity {
    param($Config)

    $windowsUser = [string]$env:USERNAME
    $email = ""
    $userKey = ""
    $displayName = ""

    $mapFile = $Config.userMapFile
    if ([string]::IsNullOrWhiteSpace($mapFile)) {
        $mapFile = "user-map.local.json"
    }

    $mapPath = Resolve-ToolPath $mapFile
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
            $email = [string]$entry.email
            $userKey = [string]$entry.userKey
            if ([string]::IsNullOrWhiteSpace($userKey)) {
                $userKey = [string]$entry.user_key
            }
            $displayName = [string]$entry.name
        }
    }

    if ([string]::IsNullOrWhiteSpace($email) -and -not [string]::IsNullOrWhiteSpace($Config.email)) {
        $email = [string]$Config.email
    }
    if ([string]::IsNullOrWhiteSpace($userKey) -and -not [string]::IsNullOrWhiteSpace($Config.userKey)) {
        $userKey = [string]$Config.userKey
    }

    if ([string]::IsNullOrWhiteSpace($email)) {
        $domain = [string]$Config.defaultEmailDomain
        if (-not [string]::IsNullOrWhiteSpace($domain)) {
            $email = "$windowsUser@$domain"
        }
    }

    return [pscustomobject]@{
        windowsUser = $windowsUser
        email = $email
        userKey = $userKey
        displayName = $displayName
    }
}

function Convert-ToTicketArray {
    param($Value)

    if ($null -eq $Value) {
        return @()
    }

    if ($Value -is [System.Array]) {
        return @($Value)
    }

    if ($Value.PSObject.Properties.Name -contains "tickets") {
        return @($Value.tickets)
    }

    if ($Value.PSObject.Properties.Name -contains "list") {
        return @($Value.list)
    }

    return @($Value)
}

function Normalize-Ticket {
    param($Ticket)

    $id = $Ticket.id
    if (-not $id) { $id = $Ticket.work_item_id }
    if (-not $id -and $Ticket.work_item_info) { $id = $Ticket.work_item_info.work_item_id }

    $title = $Ticket.title
    if (-not $title) { $title = $Ticket.name }
    if (-not $title) { $title = $Ticket.work_item_name }
    if (-not $title -and $Ticket.work_item_info) { $title = $Ticket.work_item_info.work_item_name }

    $type = $Ticket.type
    if (-not $type) { $type = $Ticket.work_item_type_key }
    if (-not $type -and $Ticket.work_item_info) { $type = $Ticket.work_item_info.work_item_type_key }

    $status = $Ticket.status
    if (-not $status) { $status = $Ticket.state }
    if (-not $status) { $status = "open" }

    $node = $Ticket.node
    if (-not $node) { $node = $Ticket.node_name }
    if (-not $node -and $Ticket.node_info) { $node = $Ticket.node_info.node_name }

    if ([string]::IsNullOrWhiteSpace([string]$id) -or [string]::IsNullOrWhiteSpace([string]$title)) {
        return $null
    }

    return [pscustomobject]@{
        id = [string]$id
        type = [string]$type
        title = [string]$title
        status = [string]$status
        node = [string]$node
    }
}

function Get-TicketsFromCommand {
    param($Config, $Identity)

    if ([string]::IsNullOrWhiteSpace($Config.providerCommand)) {
        return @()
    }

    $env:SVN_FEISHU_WINDOWS_USER = $Identity.windowsUser
    $env:SVN_FEISHU_EMAIL = $Identity.email
    $env:SVN_FEISHU_USER_KEY = $Identity.userKey
    $env:SVN_FEISHU_TOOL_DIR = $ScriptDir
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -Command $Config.providerCommand
    if ([string]::IsNullOrWhiteSpace([string]$output)) {
        return @()
    }

    return Convert-ToTicketArray (($output -join "`n") | ConvertFrom-Json)
}

function Get-TicketsFromEndpoint {
    param($Config, $Identity)

    if ([string]::IsNullOrWhiteSpace($Config.endpoint)) {
        return @()
    }

    Ensure-LocalTicketService $Config

    $timeout = 15
    if ($Config.timeoutSeconds) {
        $timeout = [int]$Config.timeoutSeconds
    }

    $uri = [string]$Config.endpoint
    $uri = Add-QueryParameter $uri "windowsUser" $Identity.windowsUser
    $uri = Add-QueryParameter $uri "email" $Identity.email
    $uri = Add-QueryParameter $uri "userKey" $Identity.userKey

    $headers = @{ "Cache-Control" = "no-cache" }
    try {
        $response = Invoke-RestMethod -Method Get -Uri $uri -Headers $headers -TimeoutSec $timeout
    }
    catch {
        if (-not (Test-LocalTicketEndpoint ([string]$Config.endpoint))) {
            throw
        }

        Restart-LocalTicketService $Config
        $response = Invoke-RestMethod -Method Get -Uri $uri -Headers $headers -TimeoutSec $timeout
    }
    return Convert-ToTicketArray $response
}

function Test-LocalTicketEndpoint {
    param([string]$Endpoint)

    if ([string]::IsNullOrWhiteSpace($Endpoint)) {
        return $false
    }

    try {
        $uri = [uri]$Endpoint
        if ($uri.Scheme -ne "http") {
            return $false
        }
        if ($uri.Host -notin @("127.0.0.1", "localhost")) {
            return $false
        }
        if ($uri.Port -ne 18765) {
            return $false
        }
        return $true
    }
    catch {
        return $false
    }
}

function Test-LocalTicketServiceHealth {
    try {
        $health = Invoke-RestMethod -Method Get -Uri "http://127.0.0.1:18765/health" -TimeoutSec 2
        return ($health.ok -eq $true)
    }
    catch {
        return $false
    }
}

function Stop-LocalTicketService {
    $connections = @()
    try {
        $connections = @(Get-NetTCPConnection -LocalPort 18765 -State Listen -ErrorAction Stop)
    }
    catch {
        return
    }

    foreach ($connection in $connections) {
        $pidValue = [int]$connection.OwningProcess
        if ($pidValue -le 0 -or $pidValue -eq $PID) {
            continue
        }

        $commandLine = ""
        try {
            $processInfo = Get-CimInstance Win32_Process -Filter "ProcessId = $pidValue"
            $commandLine = [string]$processInfo.CommandLine
        }
        catch {
            $commandLine = ""
        }

        if ($commandLine -notlike "*SvnFeishuCommitPicker*" -and $commandLine -notlike "*ticket-service.ps1*") {
            continue
        }

        try {
            Stop-Process -Id $pidValue -Force -ErrorAction Stop
        }
        catch {
        }
    }
}

function Ensure-LocalTicketService {
    param($Config)

    if (-not (Test-LocalTicketEndpoint ([string]$Config.endpoint))) {
        return
    }

    if (Test-LocalTicketServiceHealth) {
        return
    }

    $serviceCmd = Join-Path $ScriptDir "server\StartMeegoBaseTicketService.cmd"
    if (-not (Test-Path -LiteralPath $serviceCmd)) {
        return
    }

    $apiKey = [string]$env:MEEGO_BASE_API_KEY
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        $apiKey = [Environment]::GetEnvironmentVariable("MEEGO_BASE_API_KEY", "User")
    }
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        return
    }

    Start-Process -FilePath "cmd.exe" -ArgumentList @("/k", "`"$serviceCmd`"") -WorkingDirectory $ScriptDir -WindowStyle Minimized | Out-Null

    for ($i = 1; $i -le 20; $i += 1) {
        Start-Sleep -Milliseconds 500
        if (Test-LocalTicketServiceHealth) {
            return
        }
    }
}

function Restart-LocalTicketService {
    param($Config)

    if (-not (Test-LocalTicketEndpoint ([string]$Config.endpoint))) {
        return
    }

    Stop-LocalTicketService
    Start-Sleep -Milliseconds 500
    Ensure-LocalTicketService $Config
}

function Get-TicketsFromCache {
    param($Config)

    $cacheFile = $Config.cacheFile
    if ([string]::IsNullOrWhiteSpace($cacheFile)) {
        $cacheFile = "tickets.cache.json"
    }

    $cachePath = Resolve-ToolPath $cacheFile
    return Convert-ToTicketArray (Read-JsonFile $cachePath)
}

function Get-Tickets {
    param($Config)

    $rawTickets = @()
    $source = "none"
    $identity = Get-CurrentUserIdentity $Config
    $loadErrors = New-Object System.Collections.Generic.List[string]

    try {
        $rawTickets = Get-TicketsFromEndpoint $Config $identity
        if ($rawTickets.Count -gt 0) { $source = "endpoint" }
    }
    catch {
        $rawTickets = @()
        $loadErrors.Add($_.Exception.Message) | Out-Null
    }

    if ($rawTickets.Count -eq 0) {
        try {
            $rawTickets = Get-TicketsFromCommand $Config $identity
            if ($rawTickets.Count -gt 0) { $source = "command" }
        }
        catch {
            $rawTickets = @()
            $loadErrors.Add($_.Exception.Message) | Out-Null
        }
    }

    $allowCacheFallback = $true
    if ($Config.PSObject.Properties.Name -contains "allowCacheFallback") {
        $allowCacheFallback = [bool]$Config.allowCacheFallback
    }
    if ($rawTickets.Count -eq 0 -and $allowCacheFallback) {
        $rawTickets = Get-TicketsFromCache $Config
        $source = "cache"
    }
    elseif ($rawTickets.Count -eq 0 -and $loadErrors.Count -gt 0) {
        throw ($loadErrors -join "`r`n")
    }

    $tickets = @()
    foreach ($item in $rawTickets) {
        $ticket = Normalize-Ticket $item
        if ($null -eq $ticket) { continue }

        if (-not $Config.includeClosed) {
            $closedLabels = @("end", "ended", "closed", "done", "finish", "finished", "completed", "closed_done")
            $stateValues = @([string]$ticket.status) + @(([string]$ticket.node) -split ",")
            $isClosed = $false
            foreach ($value in $stateValues) {
                if ($closedLabels -contains ([string]$value).Trim().ToLowerInvariant()) {
                    $isClosed = $true
                    break
                }
            }
            if ($isClosed) { continue }
        }

        if ($Config.includeTypes -and $Config.includeTypes.Count -gt 0) {
            $type = $ticket.type.ToLowerInvariant()
            $wanted = @($Config.includeTypes | ForEach-Object { [string]$_ })
            $isBugAlias = ($type -eq "6745be52ca5bd28affaa7241" -and ($wanted -contains "bug"))
            if (($wanted -notcontains $ticket.type) -and (-not $isBugAlias)) { continue }
        }

        $tickets += $ticket
    }

    return [pscustomobject]@{
        source = $source
        identity = $identity
        tickets = @($tickets | Sort-Object type, id -Unique)
    }
}

function Format-CommitLine {
    param($Ticket, $Config)

    $format = $Config.messageFormat
    if ([string]::IsNullOrWhiteSpace($format)) {
        $format = "#{id} {title}"
    }

    return $format.Replace("{id}", $Ticket.id).Replace("{title}", $Ticket.title).Replace("{type}", $Ticket.type).Replace("{node}", $Ticket.node)
}

function Get-TicketUrl {
    param($Ticket, $Config)

    $type = [string]$Ticket.type
    if ($type -eq "bug") {
        $type = [string]$Config.bugDetailType
        if ([string]::IsNullOrWhiteSpace($type)) { $type = "bug2" }
    }
    elseif ($type -eq "6745be52ca5bd28affaa7241") {
        $type = [string]$Config.bugDetailType
        if ([string]::IsNullOrWhiteSpace($type)) { $type = "bug2" }
    }
    elseif ([string]::IsNullOrWhiteSpace($type)) {
        $type = "story"
    }

    $ticketBaseUrl = [string]$Config.ticketBaseUrl
    if ([string]::IsNullOrWhiteSpace($ticketBaseUrl)) {
        $ticketBaseUrl = "https://project.feishu.cn/dragon_heir"
    }
    return "$($ticketBaseUrl.TrimEnd('/'))/$type/detail/$($Ticket.id)"
}

function Start-DeferredTortoisePaste {
    $scriptPath = Join-Path $ScriptDir "SvnFeishuCommitPicker.ps1"

    $arguments = "-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File `"$scriptPath`" -PasteClipboard"
    Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -WindowStyle Hidden
}

function Start-DeferredHookSuccessDismiss {
    param($Config)

    try {
        $ticketEndpoint = [Uri]([string]$Config.endpoint)
        if ($ticketEndpoint.IsLoopback) {
            $dismissUri = "{0}://{1}/api/dismiss-hook-success" -f $ticketEndpoint.Scheme, $ticketEndpoint.Authority
            $response = Invoke-RestMethod -Method Post -Uri $dismissUri -TimeoutSec 2
            if ($response.armed) {
                return
            }
        }
    }
    catch {
        # Fall back to a direct helper when the local service is unavailable or outdated.
    }

    $scriptPath = Join-Path $ScriptDir "SvnFeishuCommitPicker.ps1"

    $arguments = "-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File `"$scriptPath`" -DismissHookSuccess"
    Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -WindowStyle Hidden
}

function Try-PasteIntoTortoiseMessage {
    try {
        if ([SvnPickerNative]::PasteClipboardIntoTortoiseMessage()) {
            return $true
        }
    }
    catch {
        # The clipboard remains available for manual Ctrl+V.
    }

    return $false
}

function Append-Message {
    param([string[]]$Lines)

    if ([string]::IsNullOrWhiteSpace($MessageFile)) {
        throw "TortoiseSVN did not pass the message file path."
    }

    $existing = ""
    if (Test-Path -LiteralPath $MessageFile) {
        $existing = Get-Content -LiteralPath $MessageFile -Raw -Encoding UTF8
    }

    $newLines = @()
    foreach ($line in $Lines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ([string]::IsNullOrEmpty($existing) -or $existing.IndexOf($line, [System.StringComparison]::Ordinal) -lt 0) {
            $newLines += $line
        }
    }

    if ($newLines.Count -eq 0) {
        return
    }

    $prefix = ""
    if (-not [string]::IsNullOrWhiteSpace($existing) -and -not $existing.EndsWith("`n")) {
        $prefix = "`r`n"
    }

    $clipboardText = $prefix + (($newLines -join "`r`n") + "`r`n")
    $text = $existing + $clipboardText
    [System.IO.File]::WriteAllText($MessageFile, $text, [System.Text.UTF8Encoding]::new($false))

    try {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.Clipboard]::SetText($clipboardText)
        if (-not (Try-PasteIntoTortoiseMessage)) {
            Start-DeferredTortoisePaste
        }
    }
    catch {
        # Message file write is still the primary Hook output.
    }
}

function Show-TicketDialog {
    param($Config, $TicketResult)

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing

    [System.Windows.Forms.Application]::EnableVisualStyles()
    [System.Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)

    $uiFont = New-Object System.Drawing.Font("Microsoft YaHei UI", 10)
    $buttonFont = New-Object System.Drawing.Font("Microsoft YaHei UI", 10)
    $uiSettings = Read-JsonFile $UiSettingsPath

    $form = New-Object System.Windows.Forms.Form
    $projectName = [string]$Config.projectName
    if ([string]::IsNullOrWhiteSpace($projectName)) { $projectName = "Feishu" }
    $form.Text = "Choose $projectName ticket for SVN commit message"
    $form.StartPosition = "CenterScreen"
    $form.Size = New-Object System.Drawing.Size(900, 560)
    $form.MinimumSize = New-Object System.Drawing.Size(760, 420)
    $form.Font = $uiFont
    if ($uiSettings -and $uiSettings.window) {
        $savedWidth = [int]$uiSettings.window.width
        $savedHeight = [int]$uiSettings.window.height
        if ($savedWidth -ge 760 -and $savedHeight -ge 420) {
            $form.Size = New-Object System.Drawing.Size($savedWidth, $savedHeight)
        }
    }

    $summary = New-Object System.Windows.Forms.Label
    $summary.AutoSize = $false
    $summary.Location = New-Object System.Drawing.Point(12, 12)
    $summary.Size = New-Object System.Drawing.Size(860, 24)
    $identityLabel = $TicketResult.identity.windowsUser
    if (-not [string]::IsNullOrWhiteSpace($TicketResult.identity.email)) {
        $identityLabel = "$identityLabel / $($TicketResult.identity.email)"
    }
    $summary.Text = "Project: $projectName. Source: $($TicketResult.source). User: $identityLabel. Select tickets, then click Copy message."
    $form.Controls.Add($summary)

    $search = New-Object System.Windows.Forms.TextBox
    $search.Location = New-Object System.Drawing.Point(12, 42)
    $search.Size = New-Object System.Drawing.Size(860, 28)
    $search.Text = [string]$Config.defaultQuery
    $search.Font = $uiFont
    $form.Controls.Add($search)

    $list = New-Object System.Windows.Forms.ListView
    $list.Location = New-Object System.Drawing.Point(12, 78)
    $list.Size = New-Object System.Drawing.Size(860, 330)
    $list.Anchor = "Top,Bottom,Left,Right"
    $list.View = [System.Windows.Forms.View]::Details
    $list.FullRowSelect = $true
    $list.MultiSelect = $true
    $list.HideSelection = $false
    $list.GridLines = $true
    $list.Font = $uiFont
    $rowImages = New-Object System.Windows.Forms.ImageList
    $rowImages.ImageSize = New-Object System.Drawing.Size(1, 28)
    $list.SmallImageList = $rowImages
    $defaultColumns = [ordered]@{
        ID = 110
        Type = 80
        Node = 120
        Title = 520
    }
    if ($uiSettings -and $uiSettings.columns) {
        foreach ($key in @("ID", "Type", "Node", "Title")) {
            if ($uiSettings.columns.PSObject.Properties.Name -contains $key) {
                $value = [int]$uiSettings.columns.$key
                if ($value -ge 40) {
                    $defaultColumns[$key] = $value
                }
            }
        }
    }
    [void]$list.Columns.Add("ID", $defaultColumns.ID)
    [void]$list.Columns.Add("Type", $defaultColumns.Type)
    [void]$list.Columns.Add("Node", $defaultColumns.Node)
    [void]$list.Columns.Add("Title", $defaultColumns.Title)
    $form.Controls.Add($list)

    $allTickets = @($TicketResult.tickets)

    $reloadList = {
        $keyword = $search.Text.Trim()
        $list.BeginUpdate()
        $list.Items.Clear()
        foreach ($ticket in $allTickets) {
            $haystack = "$($ticket.id) $($ticket.type) $($ticket.node) $($ticket.title)"
            if ($keyword -and $haystack.IndexOf($keyword, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
                continue
            }
            $item = New-Object System.Windows.Forms.ListViewItem($ticket.id)
            [void]$item.SubItems.Add($ticket.type)
            [void]$item.SubItems.Add($ticket.node)
            [void]$item.SubItems.Add($ticket.title)
            $item.Tag = $ticket
            [void]$list.Items.Add($item)
        }
        $list.EndUpdate()
    }

    $search.Add_TextChanged($reloadList)
    & $reloadList

    $status = New-Object System.Windows.Forms.Label
    $status.AutoSize = $false
    $status.Location = New-Object System.Drawing.Point(12, 430)
    $status.Size = New-Object System.Drawing.Size(860, 28)
    $status.Anchor = "Bottom,Left,Right"
    $status.Text = "$($allTickets.Count) tickets. Search by title, ID, type, or node."
    $form.Controls.Add($status)

    $openTicket = New-Object System.Windows.Forms.Button
    $openTicket.Text = "Open link"
    $openTicket.Location = New-Object System.Drawing.Point(374, 472)
    $openTicket.Size = New-Object System.Drawing.Size(140, 40)
    $openTicket.Anchor = "Bottom,Right"
    $openTicket.Font = $buttonFont
    $form.Controls.Add($openTicket)

    $ok = New-Object System.Windows.Forms.Button
    $ok.Text = "Copy message"
    $ok.Location = New-Object System.Drawing.Point(538, 472)
    $ok.Size = New-Object System.Drawing.Size(140, 40)
    $ok.Anchor = "Bottom,Right"
    $ok.Font = $buttonFont
    $form.Controls.Add($ok)

    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = "Cancel"
    $cancel.Location = New-Object System.Drawing.Point(702, 472)
    $cancel.Size = New-Object System.Drawing.Size(140, 40)
    $cancel.Anchor = "Bottom,Right"
    $cancel.Font = $buttonFont
    $form.Controls.Add($cancel)

    $applyLayout = {
        $margin = 12
        $buttonWidth = 140
        $buttonHeight = 40
        $buttonGap = 24
        $bottomMargin = 22
        $statusHeight = 28
        $buttonY = $form.ClientSize.Height - $bottomMargin - $buttonHeight
        $statusY = $buttonY - $statusHeight - 12
        $listBottom = $statusY - 12
        $listHeight = [math]::Max(180, $listBottom - $list.Top)
        $rightEdge = $form.ClientSize.Width - $margin

        $summary.Width = $form.ClientSize.Width - ($margin * 2)
        $search.Width = [math]::Max(240, $form.ClientSize.Width - ($margin * 2))
        $list.Width = [math]::Max(300, $form.ClientSize.Width - ($margin * 2))
        $list.Height = $listHeight
        $status.Top = $statusY
        $status.Width = $form.ClientSize.Width - ($margin * 2)

        $cancel.Left = $rightEdge - $buttonWidth
        $cancel.Top = $buttonY
        $cancel.Width = $buttonWidth
        $cancel.Height = $buttonHeight

        $ok.Left = $cancel.Left - $buttonGap - $buttonWidth
        $ok.Top = $buttonY
        $ok.Width = $buttonWidth
        $ok.Height = $buttonHeight

        $openTicket.Left = $ok.Left - $buttonGap - $buttonWidth
        $openTicket.Top = $buttonY
        $openTicket.Width = $buttonWidth
        $openTicket.Height = $buttonHeight
    }

    $form.Add_Shown($applyLayout)
    $form.Add_Resize($applyLayout)

    $form.AcceptButton = $ok
    $form.CancelButton = $cancel

    $selectedLines = New-Object System.Collections.Generic.List[string]

    $copySelectedAndClose = {
        $selectedLines.Clear()
        foreach ($item in $list.SelectedItems) {
            $ticket = $item.Tag
            $selectedLines.Add((Format-CommitLine $ticket $Config))
        }

        if ($selectedLines.Count -eq 0) {
            [System.Windows.Forms.MessageBox]::Show("Select at least one row first.", "No row selected", "OK", "Information") | Out-Null
            return
        }

        $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
        $form.Close()
    }

    $ok.Add_Click($copySelectedAndClose)

    $openTicket.Add_Click({
        $targetItem = $null
        if ($list.SelectedItems.Count -gt 0) {
            $targetItem = $list.SelectedItems[0]
        }

        if ($null -eq $targetItem) {
            [System.Windows.Forms.MessageBox]::Show("Select one row first.", "No row selected", "OK", "Information") | Out-Null
            return
        }

        $url = Get-TicketUrl $targetItem.Tag $Config
        Start-Process $url
    })

    $list.Add_DoubleClick({
        if ($list.SelectedItems.Count -gt 0) {
            & $copySelectedAndClose
        }
    })

    $cancel.Add_Click({
        $form.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
        $form.Close()
    })

    $result = $form.ShowDialog()
    $settings = [ordered]@{
        window = [ordered]@{
            width = $form.Width
            height = $form.Height
        }
        columns = [ordered]@{
            ID = $list.Columns[0].Width
            Type = $list.Columns[1].Width
            Node = $list.Columns[2].Width
            Title = $list.Columns[3].Width
        }
    }
    Write-JsonFile $settings $UiSettingsPath
    if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
        return @($selectedLines)
    }

    return @()
}

try {
    if ($DismissHookSuccess) {
        # The helper is prewarmed while the picker is open, so the post-Hook
        # success notice can be dismissed without PowerShell startup latency.
        Start-Sleep -Milliseconds 25
        for ($attempt = 0; $attempt -lt 6000; $attempt++) {
            try {
                if ([SvnPickerNative]::DismissTortoiseTooltip()) {
                    exit 0
                }
            }
            catch {
                exit 0
            }
            Start-Sleep -Milliseconds 50
        }
        exit 0
    }

    if ($PasteClipboard) {
        for ($attempt = 0; $attempt -lt 20; $attempt++) {
            if (Try-PasteIntoTortoiseMessage) {
                exit 0
            }
            Start-Sleep -Milliseconds 150
        }
        exit 0
    }

    if ($DebugArgs) {
        $PSBoundParameters | ConvertTo-Json -Depth 4
        exit 0
    }

    $config = Read-JsonFile $SampleConfigPath
    $localConfig = Read-JsonFile $LocalConfigPath
    if ($localConfig) {
        foreach ($property in $localConfig.PSObject.Properties) {
            $config | Add-Member -MemberType NoteProperty -Name $property.Name -Value $property.Value -Force
        }
    }

    $projectProfile = Resolve-ProjectProfile $config $Project
    $config = Apply-ProjectProfile $config $projectProfile

    if (-not $ListTickets -and [string]::IsNullOrWhiteSpace($TestPickIds)) {
        Start-DeferredHookSuccessDismiss $config
    }

    $ticketResult = Get-Tickets $config
    if ($ListTickets) {
        $ticketResult | ConvertTo-Json -Depth 6
        exit 0
    }

    if (-not [string]::IsNullOrWhiteSpace($TestPickIds)) {
        $wantedIds = @($TestPickIds.Split(",") | ForEach-Object { [string]$_.Trim() } | Where-Object { $_ })
        $lines = @()
        foreach ($ticket in $ticketResult.tickets) {
            $ticketId = [string]$ticket.id
            if ($wantedIds -contains $ticketId) {
                $lines += (Format-CommitLine $ticket $config)
            }
        }
        if ($ListTickets) {
            $lines | ConvertTo-Json -Depth 3
            exit 0
        }

        if ($lines.Count -eq 0) {
            throw "No matching tickets for TestPickIds: $TestPickIds"
        }
        Append-Message $lines
        exit 0
    }

    if ($ticketResult.tickets.Count -eq 0) {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show("No open tickets were loaded. Check config.local.json or tickets.cache.json.", "No tickets", "OK", "Warning") | Out-Null
        exit 0
    }

    $lines = Show-TicketDialog $config $ticketResult
    if ($lines.Count -gt 0) {
        Append-Message $lines
    }
}
catch {
    if ($ListTickets -or -not [string]::IsNullOrWhiteSpace($TestPickIds)) {
        Write-Error $_.Exception.Message
    }
    else {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "Feishu ticket picker error", "OK", "Error") | Out-Null
    }
    exit 1
}
