param(
    [string]$ConfigPath = "",
    [switch]$Once
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $ScriptDir "server.config.local.json"
    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        $ConfigPath = Join-Path $ScriptDir "server.config.sample.json"
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

function Resolve-ServicePath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $Path
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $ScriptDir $Path
}

function Write-JsonResponse {
    param($Response, $Value, [int]$StatusCode = 200)

    $json = $Value | ConvertTo-Json -Depth 8
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $Response.StatusCode = $StatusCode
    $Response.ContentType = "application/json; charset=utf-8"
    $Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0"
    $Response.Headers["Pragma"] = "no-cache"
    $Response.ContentLength64 = $bytes.Length
    $Response.OutputStream.Write($bytes, 0, $bytes.Length)
    $Response.OutputStream.Close()
}

function Get-RequestIdentity {
    param($Request)

    return [pscustomobject]@{
        windowsUser = [string]$Request.QueryString["windowsUser"]
        email = [string]$Request.QueryString["email"]
        userKey = [string]$Request.QueryString["userKey"]
    }
}

function Get-IdentityKeys {
    param($Identity)

    $keys = New-Object System.Collections.Generic.List[string]
    foreach ($value in @($Identity.windowsUser, $Identity.email, $Identity.userKey)) {
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            $keys.Add($value.ToLowerInvariant()) | Out-Null
            if ($value.Contains("@")) {
                $keys.Add(($value.Split("@")[0]).ToLowerInvariant()) | Out-Null
            }
        }
    }

    return @($keys | Select-Object -Unique)
}

function Normalize-Ticket {
    param($Ticket, [string]$DefaultType)

    $id = [string]$Ticket.id
    if ([string]::IsNullOrWhiteSpace($id)) { $id = [string]$Ticket.work_item_id }

    $title = [string]$Ticket.title
    if ([string]::IsNullOrWhiteSpace($title)) { $title = [string]$Ticket.name }
    if ([string]::IsNullOrWhiteSpace($title)) { $title = [string]$Ticket.work_item_name }

    $type = [string]$Ticket.type
    if ([string]::IsNullOrWhiteSpace($type)) { $type = [string]$Ticket.work_item_type_key }
    if ([string]::IsNullOrWhiteSpace($type)) { $type = $DefaultType }

    $status = [string]$Ticket.status
    if ([string]::IsNullOrWhiteSpace($status)) { $status = "open" }

    $node = [string]$Ticket.node
    if ([string]::IsNullOrWhiteSpace($node)) { $node = [string]$Ticket.node_name }

    if ([string]::IsNullOrWhiteSpace($id) -or [string]::IsNullOrWhiteSpace($title)) {
        return $null
    }

    return [pscustomobject]@{
        id = $id
        type = $type
        title = $title
        status = $status
        node = $node
    }
}

function Get-TicketsFromProviderCommand {
    param($Config, $Identity)

    if ([string]::IsNullOrWhiteSpace([string]$Config.providerCommand)) {
        return @()
    }

    $timeout = 20
    if ($Config.providerTimeoutSeconds) {
        $timeout = [int]$Config.providerTimeoutSeconds
    }

    $env:SVN_FEISHU_WINDOWS_USER = $Identity.windowsUser
    $env:SVN_FEISHU_EMAIL = $Identity.email
    $env:SVN_FEISHU_USER_KEY = $Identity.userKey

    $command = "`$ProgressPreference = 'SilentlyContinue'; " + [string]$Config.providerCommand
    $encoded = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($command))
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = "powershell.exe"
    $startInfo.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encoded"
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $process.Start() | Out-Null
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    if (-not $process.WaitForExit($timeout * 1000)) {
        try { $process.Kill() } catch {}
        throw "Provider command timed out after $timeout seconds."
    }

    $stdout = $stdoutTask.Result
    $stderr = $stderrTask.Result

    if ($process.ExitCode -ne 0) {
        throw "Provider command failed: $stderr"
    }

    if ([string]::IsNullOrWhiteSpace($stdout)) {
        return @()
    }

    $raw = $stdout | ConvertFrom-Json
    $rawTickets = @()
    if ($raw -is [System.Array]) {
        $rawTickets = @($raw)
    }
    elseif ($raw.PSObject.Properties.Name -contains "tickets") {
        $rawTickets = @($raw.tickets)
    }
    elseif ($raw.PSObject.Properties.Name -contains "list") {
        $rawTickets = @($raw.list)
    }
    else {
        $rawTickets = @($raw)
    }

    $tickets = @()
    foreach ($rawTicket in $rawTickets) {
        $ticket = Normalize-Ticket $rawTicket ([string]$Config.defaultTicketType)
        if ($null -ne $ticket) {
            $tickets += $ticket
        }
    }

    return @($tickets)
}

function Get-TicketsFromMock {
    param($Config, $Identity)

    $mockPath = Resolve-ServicePath ([string]$Config.mockTicketsFile)
    $mock = Read-JsonFile $mockPath
    if ($null -eq $mock) {
        return @()
    }

    $identityKeys = @(Get-IdentityKeys $Identity)
    foreach ($key in $identityKeys) {
        if ($mock.PSObject.Properties.Name -contains $key) {
            $rawTickets = @($mock.$key)
            $tickets = @()
            foreach ($rawTicket in $rawTickets) {
                $ticket = Normalize-Ticket $rawTicket ([string]$Config.defaultTicketType)
                if ($null -ne $ticket) {
                    $tickets += $ticket
                }
            }
            return @($tickets)
        }
    }

    return @()
}

function Handle-Request {
    param($Context, $Config)

    $request = $Context.Request
    $response = $Context.Response
    $path = $request.Url.AbsolutePath.TrimEnd("/")

    if ($path -eq "/health") {
        Write-JsonResponse $response ([pscustomobject]@{ ok = $true })
        return
    }

    if ($path -ne "/api/my-open-workitems") {
        Write-JsonResponse $response ([pscustomobject]@{ error = "not_found" }) 404
        return
    }

    $identity = Get-RequestIdentity $request
    $tickets = @(Get-TicketsFromProviderCommand $Config $identity)
    if ($tickets.Count -eq 0) {
        $tickets = @(Get-TicketsFromMock $Config $identity)
    }
    Write-JsonResponse $response $tickets
}

$config = Read-JsonFile $ConfigPath
if ($null -eq $config) {
    throw "Cannot read config: $ConfigPath"
}

$prefix = [string]$config.prefix
if ([string]::IsNullOrWhiteSpace($prefix)) {
    $prefix = "http://127.0.0.1:18765/"
}
if (-not $prefix.EndsWith("/")) {
    $prefix = "$prefix/"
}

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add($prefix)
$listener.Start()
Write-Host "QuickReleaseMerge ticket service listening on $prefix"
Write-Host "Config: $ConfigPath"

try {
    do {
        $context = $listener.GetContext()
        try {
            Handle-Request $context $config
        }
        catch {
            Write-JsonResponse $context.Response ([pscustomobject]@{ error = $_.Exception.Message }) 500
        }
    } while (-not $Once)
}
finally {
    $listener.Stop()
    $listener.Close()
}
