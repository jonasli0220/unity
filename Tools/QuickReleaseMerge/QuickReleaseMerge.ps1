param(
    [string]$StartPath = "",
    [string]$ConfigPath = "",
    [switch]$ListTickets,
    [switch]$SmokeUi,
    [string]$TestTicketId
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SampleConfigPath = Join-Path $ScriptDir "config.sample.json"
$DefaultLocalConfigPath = Join-Path $ScriptDir "config.local.json"
$UiSettingsPath = Join-Path $ScriptDir "settings.local.json"
$script:WinFormsInitialized = $false
$script:ReleaseStatusCache = @{}

try {
    Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class QuickReleaseMergeDpi {
    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();
}
"@
    [QuickReleaseMergeDpi]::SetProcessDPIAware() | Out-Null
}
catch {
}

function Initialize-WinForms {
    Add-Type -AssemblyName System.Windows.Forms
    if (-not $script:WinFormsInitialized) {
        try {
            [System.Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
        }
        catch {
            # This setting is optional and must happen before any WinForms window exists.
        }
        try {
            [System.Windows.Forms.Application]::EnableVisualStyles()
        }
        catch {
        }
        $script:WinFormsInitialized = $true
    }
}

function Read-JsonFile {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
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

    $json = $Value | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
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

function Get-PropertyValue {
    param($Object, [string[]]$Names)

    if ($null -eq $Object) {
        return ""
    }

    foreach ($name in $Names) {
        if ($Object.PSObject.Properties.Name -contains $name) {
            $value = $Object.$name
            if ($null -ne $value -and -not [string]::IsNullOrWhiteSpace([string]$value)) {
                return $value
            }
        }
    }

    return ""
}

function Get-StringListProperty {
    param($Object, [string[]]$Names)

    $values = New-Object System.Collections.Generic.List[string]
    foreach ($name in $Names) {
        if ($null -eq $Object -or -not ($Object.PSObject.Properties.Name -contains $name)) {
            continue
        }

        $rawValue = $Object.$name
        if ($null -eq $rawValue) {
            continue
        }

        if ($rawValue -is [System.Array]) {
            foreach ($item in @($rawValue)) {
                $text = [string]$item
                if (-not [string]::IsNullOrWhiteSpace($text)) {
                    $values.Add($text)
                }
            }
        }
        else {
            $text = [string]$rawValue
            if (-not [string]::IsNullOrWhiteSpace($text)) {
                $values.Add($text)
            }
        }
    }

    return @($values | Select-Object -Unique)
}

function Get-TargetNodeFilters {
    param($Config)

    $targetNodes = @(Get-StringListProperty $Config @("targetNodes", "targetNodeAliases"))
    $singleTargetNode = [string](Get-PropertyValue $Config @("targetNode"))
    if (-not [string]::IsNullOrWhiteSpace($singleTargetNode)) {
        $targetNodes += $singleTargetNode
    }

    $targetNodes += @(
        "待提交 CN release",
        "待提交CN release",
        "提交 CN release",
        "提交CN release"
    )

    return @($targetNodes | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -Unique)
}

function Test-TicketTargetNode {
    param($Ticket, [string[]]$TargetNodes)

    $node = [string]$Ticket.node
    if ([string]::IsNullOrWhiteSpace($node)) {
        return $false
    }

    foreach ($targetNode in @($TargetNodes)) {
        if ([string]::IsNullOrWhiteSpace($targetNode)) {
            continue
        }
        if ($node.IndexOf($targetNode, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }

    return $false
}

function Merge-Config {
    if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
        $script:ConfigPath = $DefaultLocalConfigPath
    }

    $config = Read-JsonFile $SampleConfigPath
    if ($null -eq $config) {
        $config = [pscustomobject]@{}
    }

    $localConfig = Read-JsonFile $ConfigPath
    if ($localConfig) {
        foreach ($property in $localConfig.PSObject.Properties) {
            $config | Add-Member -MemberType NoteProperty -Name $property.Name -Value $property.Value -Force
        }
    }

    return $config
}

function Save-Config {
    param($Config)

    if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
        $script:ConfigPath = $DefaultLocalConfigPath
    }

    Write-JsonFile $Config $ConfigPath
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

function Get-CurrentUserIdentity {
    param($Config)

    $windowsUser = [string]$env:USERNAME
    $email = ""
    $userKey = ""
    $displayName = ""

    $mapFile = [string]$Config.userMapFile
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
            $email = [string](Get-PropertyValue $entry @("email"))
            $userKey = [string](Get-PropertyValue $entry @("userKey", "user_key"))
            $displayName = [string](Get-PropertyValue $entry @("name", "displayName", "display_name"))
        }
    }

    if ([string]::IsNullOrWhiteSpace($email)) {
        $email = [string](Get-PropertyValue $Config @("email"))
    }
    if ([string]::IsNullOrWhiteSpace($userKey)) {
        $userKey = [string](Get-PropertyValue $Config @("userKey", "user_key"))
    }
    if ([string]::IsNullOrWhiteSpace($email)) {
        $domain = [string](Get-PropertyValue $Config @("defaultEmailDomain"))
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

    $id = Get-PropertyValue $Ticket @("id", "work_item_id")
    if (-not $id -and $Ticket.work_item_info) {
        $id = Get-PropertyValue $Ticket.work_item_info @("work_item_id")
    }

    $title = Get-PropertyValue $Ticket @("title", "name", "work_item_name")
    if (-not $title -and $Ticket.work_item_info) {
        $title = Get-PropertyValue $Ticket.work_item_info @("work_item_name")
    }

    $type = Get-PropertyValue $Ticket @("type", "work_item_type_key")
    if (-not $type -and $Ticket.work_item_info) {
        $type = Get-PropertyValue $Ticket.work_item_info @("work_item_type_key")
    }
    if ([string]$type -eq "6745be52ca5bd28affaa7241") {
        $type = "bug"
    }

    $status = Get-PropertyValue $Ticket @("status", "state")
    if ([string]::IsNullOrWhiteSpace([string]$status)) {
        $status = "open"
    }

    $node = Get-PropertyValue $Ticket @("node", "node_name")
    if (-not $node -and $Ticket.node_info) {
        $node = Get-PropertyValue $Ticket.node_info @("node_name", "name")
    }

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

function Test-LocalTicketEndpoint {
    param([string]$Endpoint)

    if ([string]::IsNullOrWhiteSpace($Endpoint)) {
        return $false
    }

    try {
        $uri = [uri]$Endpoint
        return ($uri.Scheme -eq "http" -and $uri.Host -in @("127.0.0.1", "localhost") -and $uri.Port -eq 18765)
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

function Ensure-LocalTicketService {
    param($Config)

    $endpoint = [string](Get-PropertyValue $Config @("ticketEndpoint", "endpoint"))
    if (-not (Test-LocalTicketEndpoint $endpoint)) {
        return
    }

    if (Test-LocalTicketServiceHealth) {
        return
    }

    $serviceCommand = [string](Get-PropertyValue $Config @("ticketServiceCommand"))
    if ([string]::IsNullOrWhiteSpace($serviceCommand)) {
        $serviceCommand = "server\StartMeegoBaseTicketService.cmd"
    }

    $serviceCmd = Resolve-ToolPath $serviceCommand
    if (-not (Test-Path -LiteralPath $serviceCmd -PathType Leaf)) {
        return
    }

    $apiKey = [string]$env:MEEGO_BASE_API_KEY
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        $apiKey = [Environment]::GetEnvironmentVariable("MEEGO_BASE_API_KEY", "User")
    }
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        return
    }

    Start-Process -FilePath "cmd.exe" -ArgumentList @("/k", "`"$serviceCmd`"") -WorkingDirectory (Split-Path -Parent $serviceCmd) -WindowStyle Minimized | Out-Null
    for ($i = 1; $i -le 20; $i += 1) {
        Start-Sleep -Milliseconds 500
        if (Test-LocalTicketServiceHealth) {
            return
        }
    }
}

function Get-TicketsFromEndpoint {
    param($Config, $Identity, [bool]$ForceRefresh)

    $endpoint = [string](Get-PropertyValue $Config @("ticketEndpoint", "endpoint"))
    if ([string]::IsNullOrWhiteSpace($endpoint)) {
        return @()
    }

    Ensure-LocalTicketService $Config

    $timeout = 45
    if ($Config.PSObject.Properties.Name -contains "timeoutSeconds") {
        $timeout = [int]$Config.timeoutSeconds
    }

    $uri = $endpoint
    $uri = Add-QueryParameter $uri "windowsUser" $Identity.windowsUser
    $uri = Add-QueryParameter $uri "email" $Identity.email
    $uri = Add-QueryParameter $uri "userKey" $Identity.userKey
    if ($ForceRefresh) {
        $uri = Add-QueryParameter $uri "forceRefresh" "1"
        $uri = Add-QueryParameter $uri "_ts" ([string][DateTimeOffset]::Now.ToUnixTimeMilliseconds())
    }
    $response = Invoke-RestMethod -Method Get -Uri $uri -Headers @{ "Cache-Control" = "no-cache" } -TimeoutSec $timeout
    return Convert-ToTicketArray $response
}

function Get-TicketsFromCache {
    param($Config)

    $cacheFile = [string](Get-PropertyValue $Config @("cacheFile"))
    if ([string]::IsNullOrWhiteSpace($cacheFile)) {
        $cacheFile = "tickets.cache.json"
    }

    return Convert-ToTicketArray (Read-JsonFile (Resolve-ToolPath $cacheFile))
}

function Get-Tickets {
    param($Config)

    $identity = Get-CurrentUserIdentity $Config
    $source = "cache"
    $errorMessage = ""
    $rawTickets = @()

    $forceTicketRefresh = $true
    if ($Config.PSObject.Properties.Name -contains "forceTicketRefresh") {
        $forceTicketRefresh = [System.Convert]::ToBoolean($Config.forceTicketRefresh)
    }

    $allowCacheFallback = $false
    if ($Config.PSObject.Properties.Name -contains "allowCacheFallback") {
        $allowCacheFallback = [System.Convert]::ToBoolean($Config.allowCacheFallback)
    }

    try {
        $rawTickets = @(Get-TicketsFromEndpoint $Config $identity $forceTicketRefresh)
        $source = "endpoint"
    }
    catch {
        $rawTickets = @()
        $source = "endpoint_error"
        $errorMessage = $_.Exception.Message
    }

    if ($rawTickets.Count -eq 0 -and $allowCacheFallback) {
        $rawTickets = @(Get-TicketsFromCache $Config)
        $source = "cache"
    }

    $includeTypes = @("story", "bug")
    if ($Config.PSObject.Properties.Name -contains "includeTypes" -and $Config.includeTypes) {
        $includeTypes = @($Config.includeTypes | ForEach-Object { [string]$_ })
    }

    $targetNodes = @(Get-TargetNodeFilters $Config)
    $targetNodeText = ($targetNodes -join " / ")

    $tickets = @()
    foreach ($raw in $rawTickets) {
        $ticket = Normalize-Ticket $raw
        if ($null -eq $ticket) { continue }

        $type = $ticket.type.ToLowerInvariant()
        if ($includeTypes -notcontains $type) { continue }

        if (-not (Test-TicketTargetNode $ticket $targetNodes)) {
            continue
        }

        $tickets += $ticket
    }

    return [pscustomobject]@{
        source = $source
        identity = $identity
        targetNode = $targetNodeText
        errorMessage = $errorMessage
        forceTicketRefresh = $forceTicketRefresh
        allowCacheFallback = $allowCacheFallback
        tickets = @($tickets | Sort-Object @{ Expression = { [int64]$_.id }; Descending = $true } -Unique)
    }
}

function Invoke-ExternalCommand {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory
    )

    $oldLocation = Get-Location
    try {
        if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
            Set-Location -LiteralPath $WorkingDirectory
        }
        $output = & $FilePath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Set-Location $oldLocation
    }

    $text = ($output | ForEach-Object { [string]$_ }) -join "`n"
    return [pscustomobject]@{
        exitCode = $exitCode
        stdout = $text
    }
}

function Invoke-Svn {
    param([string[]]$Arguments, [string]$WorkingDirectory)

    $svnExe = "svn.exe"
    return Invoke-ExternalCommand -FilePath $svnExe -Arguments $Arguments -WorkingDirectory $WorkingDirectory
}

function Normalize-RepoPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    return $Path.Replace("\", "/").Trim("/")
}

function Get-RelativeUiPath {
    param([string]$ChangedPath, $Config)

    $root = Normalize-RepoPath ([string](Get-PropertyValue $Config @("trunkRepoPath")))
    if ([string]::IsNullOrWhiteSpace($root)) {
        $root = "trunk/trunk_cn/Content/UI"
    }

    $path = Normalize-RepoPath $ChangedPath
    if ($path -eq $root) {
        return "."
    }

    $prefix = "$root/"
    if ($path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $path.Substring($prefix.Length)
    }

    return $null
}

function ConvertTo-LocalRelativePath {
    param([string]$RelativePath)

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or $RelativePath -eq ".") {
        return "."
    }

    return $RelativePath.Replace("/", "\")
}

function Get-MaxRevisionFromSpec {
    param([string]$RevisionSpec)

    $matches = [regex]::Matches([string]$RevisionSpec, "\d+")
    if ($matches.Count -eq 0) {
        throw "无法从 revision spec 解析 revision：$RevisionSpec"
    }

    $maxRevision = 0
    foreach ($match in $matches) {
        $revision = [int]$match.Value
        if ($revision -gt $maxRevision) {
            $maxRevision = $revision
        }
    }

    return $maxRevision
}

function Get-RevisionListFromGroup {
    param($Group)

    $revisionValues = New-Object System.Collections.Generic.List[int]
    foreach ($revision in @($Group.revisions)) {
        if ($null -eq $revision) {
            continue
        }

        $text = [string]$revision
        if ($text -match "^\d+$") {
            $revisionValues.Add([int]$text) | Out-Null
        }
    }

    if ($revisionValues.Count -eq 0) {
        foreach ($match in [regex]::Matches([string]$Group.revisionSpec, "\d+")) {
            $revisionValues.Add([int]$match.Value) | Out-Null
        }
    }

    $revisions = @($revisionValues | Sort-Object -Unique)
    if ($revisions.Count -eq 0) {
        throw "无法解析 merge revision：$($Group.revisionSpec)"
    }

    return $revisions
}

function Join-SvnUrlPath {
    param([string]$BaseUrl, [string]$RelativePath)

    $base = ([string]$BaseUrl).TrimEnd("/")
    $relative = Normalize-RepoPath $RelativePath
    if ([string]::IsNullOrWhiteSpace($relative) -or $relative -eq ".") {
        return $base
    }

    return "$base/$relative"
}

function Get-ConflictPathUnderMergeTarget {
    param([string]$ConflictPath, [string]$TargetPath)

    $conflict = Normalize-RepoPath $ConflictPath
    $target = Normalize-RepoPath $TargetPath

    if ([string]::IsNullOrWhiteSpace($target) -or $target -eq ".") {
        return $conflict
    }

    if ($conflict.Equals($target, [System.StringComparison]::OrdinalIgnoreCase)) {
        return "."
    }

    $prefix = "$target/"
    if ($conflict.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $conflict.Substring($prefix.Length)
    }

    return $conflict
}

function Get-WorkingCopyFullPath {
    param([string]$WorkingDirectory, [string]$RelativePath)

    $localPath = ConvertTo-LocalRelativePath $RelativePath
    if ([System.IO.Path]::IsPathRooted($localPath)) {
        throw "SVN status 返回了绝对路径，已阻止处理：$RelativePath"
    }

    $root = [System.IO.Path]::GetFullPath($WorkingDirectory).TrimEnd("\", "/")
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $WorkingDirectory $localPath))
    if ($fullPath -ne $root -and -not $fullPath.StartsWith("$root\", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "冲突路径不在目标工作副本内，已阻止处理：$RelativePath"
    }

    return $fullPath
}

function Get-MergeParentRelativePath {
    param([string]$RelativePath)

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or $RelativePath -eq ".") {
        return "."
    }

    $local = ConvertTo-LocalRelativePath $RelativePath
    $parent = Split-Path -Parent $local
    if ([string]::IsNullOrWhiteSpace($parent)) {
        return "."
    }

    return $parent.Replace("\", "/")
}

function Test-RelativePathUnderParent {
    param([string]$RelativePath, [string]$ParentRelativePath)

    $path = Normalize-RepoPath $RelativePath
    $parent = Normalize-RepoPath $ParentRelativePath
    if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq ".") {
        return $true
    }

    if ($path.Equals($parent, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    return $path.StartsWith("$parent/", [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-MergeParentContains {
    param([string]$AncestorParent, [string]$ChildParent)

    $ancestor = Normalize-RepoPath $AncestorParent
    $child = Normalize-RepoPath $ChildParent
    if ([string]::IsNullOrWhiteSpace($ancestor) -or $ancestor -eq ".") {
        return $true
    }

    if ($child.Equals($ancestor, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    return $child.StartsWith("$ancestor/", [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-TopMergeParents {
    param([object[]]$Parents)

    $topParents = New-Object System.Collections.Generic.List[string]
    $ordered = @($Parents |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
        ForEach-Object { Normalize-RepoPath ([string]$_) } |
        Sort-Object @{ Expression = { if ($_ -eq ".") { 0 } else { $_.Length } } }, @{ Expression = { $_ } } -Unique)

    foreach ($parent in $ordered) {
        $covered = $false
        foreach ($existing in $topParents) {
            if (Test-MergeParentContains -AncestorParent $existing -ChildParent $parent) {
                $covered = $true
                break
            }
        }

        if (-not $covered) {
            $topParents.Add($parent) | Out-Null
        }
    }

    return @($topParents)
}

function Get-SourceUrlForGroup {
    param($Config, [string]$ParentRelativePath)

    $trunkUrl = [string](Get-PropertyValue $Config @("trunkSvnUrl"))
    $trunkUrl = $trunkUrl.TrimEnd("/")
    if ([string]::IsNullOrWhiteSpace($ParentRelativePath) -or $ParentRelativePath -eq ".") {
        return $trunkUrl
    }

    return "$trunkUrl/$($ParentRelativePath.Replace('\', '/'))"
}

function Get-LocalTargetForGroup {
    param([string]$ParentRelativePath)

    if ([string]::IsNullOrWhiteSpace($ParentRelativePath) -or $ParentRelativePath -eq ".") {
        return "."
    }

    return ConvertTo-LocalRelativePath $ParentRelativePath
}

function Get-ReleaseTargets {
    param($Config)

    $targets = @()

    $cnRoot = [string](Get-PropertyValue $Config @("releaseUiRoot", "cnReleaseUiRoot"))
    $targets += [pscustomobject]@{
        key = "cn"
        name = "CN release"
        buttonText = "merge to CN release"
        configKey = "releaseUiRoot"
        defaultPath = "G:\Dragon\release\dragon\Assets\Content\UI"
        uiRoot = $cnRoot
        svnUrl = [string](Get-PropertyValue $Config @("releaseSvnUrl", "cnReleaseSvnUrl"))
    }

    $naRoot = [string](Get-PropertyValue $Config @("naReleaseUiRoot", "naReleaseRoot"))
    $targets += [pscustomobject]@{
        key = "na"
        name = "NA release"
        buttonText = "merge to NA release"
        configKey = "naReleaseUiRoot"
        defaultPath = "G:\Dragon\NA release UI"
        uiRoot = $naRoot
        svnUrl = [string](Get-PropertyValue $Config @("naReleaseSvnUrl"))
    }

    return @($targets)
}

function Get-TargetAssessmentByKey {
    param($Analysis, [string]$TargetKey)

    return @($Analysis.targetAssessments | Where-Object { $_.target.key -eq $TargetKey } | Select-Object -First 1)
}

function Add-Risk {
    param([System.Collections.Generic.List[object]]$Risks, [string]$Level, [string]$Message)

    $Risks.Add([pscustomobject]@{
        level = $Level
        message = $Message
    }) | Out-Null
}

function ConvertFrom-SvnLogText {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return @()
    }

    $entries = @()
    $logMatches = [regex]::Matches($Text, '<logentry\s+revision="(?<revision>\d+)">(?<body>.*?)</logentry>', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    foreach ($logMatch in $logMatches) {
        $body = [string]$logMatch.Groups["body"].Value
        $dateText = ""
        $dateMatch = [regex]::Match($body, '<date>(?<value>.*?)</date>', [System.Text.RegularExpressions.RegexOptions]::Singleline)
        if ($dateMatch.Success) {
            $dateText = [System.Net.WebUtility]::HtmlDecode([string]$dateMatch.Groups["value"].Value)
        }
        $paths = @()
        $pathMatches = [regex]::Matches($body, '<path\b(?<attrs>[^>]*)>(?<path>.*?)</path>', [System.Text.RegularExpressions.RegexOptions]::Singleline)
        foreach ($pathMatch in $pathMatches) {
            $attrs = [string]$pathMatch.Groups["attrs"].Value
            $action = ""
            $actionMatch = [regex]::Match($attrs, 'action="(?<action>[^"]+)"')
            if ($actionMatch.Success) {
                $action = [string]$actionMatch.Groups["action"].Value
            }

            $pathText = [string]$pathMatch.Groups["path"].Value
            try {
                $pathText = [System.Net.WebUtility]::HtmlDecode($pathText)
            }
            catch {
            }

            $paths += [pscustomobject]@{
                action = $action
                path = $pathText
            }
        }

        $entries += [pscustomobject]@{
            revision = [int]$logMatch.Groups["revision"].Value
            author = ""
            date = $dateText
            message = ""
            paths = @($paths)
            searchText = $body
        }
    }

    return @($entries | Sort-Object revision)
}

function Get-TicketsSvnEntryMap {
    param([object[]]$Tickets, $Config)

    $trunkUrl = [string](Get-PropertyValue $Config @("trunkSvnUrl"))
    if ([string]::IsNullOrWhiteSpace($trunkUrl)) {
        throw "Config missing trunkSvnUrl."
    }

    $ticketIds = @($Tickets | ForEach-Object { [string]$_.id } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    $entryMap = @{}
    foreach ($ticketId in $ticketIds) {
        $entryMap[$ticketId] = @()
    }
    if ($ticketIds.Count -eq 0) {
        return $entryMap
    }

    $batchSize = 40
    if ($Config.PSObject.Properties.Name -contains "svnLogSearchBatchSize") {
        $candidateBatchSize = [int]$Config.svnLogSearchBatchSize
        if ($candidateBatchSize -ge 1 -and $candidateBatchSize -le 100) {
            $batchSize = $candidateBatchSize
        }
    }

    for ($offset = 0; $offset -lt $ticketIds.Count; $offset += $batchSize) {
        $batchIds = @($ticketIds | Select-Object -Skip $offset -First $batchSize)
        $arguments = @("log", "--xml", "-v")
        foreach ($ticketId in $batchIds) {
            $arguments += @("--search", $ticketId)
        }
        $arguments += $trunkUrl

        $result = Invoke-Svn -Arguments $arguments -WorkingDirectory ([string]$Config.trunkUiRoot)
        if ($result.exitCode -ne 0) {
            throw $result.stdout
        }

        foreach ($entry in @(ConvertFrom-SvnLogText $result.stdout)) {
            foreach ($ticketId in $batchIds) {
                if ([string]$entry.searchText -like "*$ticketId*") {
                    $entryMap[$ticketId] = @($entryMap[$ticketId]) + $entry
                }
            }
        }
    }

    foreach ($ticketId in $ticketIds) {
        $entryMap[$ticketId] = @($entryMap[$ticketId] | Sort-Object revision -Unique)
    }
    return $entryMap
}

function Get-TicketSvnEntries {
    param($Ticket, $Config)

    $entryMap = Get-TicketsSvnEntryMap -Tickets @($Ticket) -Config $Config
    return @($entryMap[[string]$Ticket.id])
}

function Get-ReleaseLocalStatuses {
    param([string[]]$RelativePaths, $Target)

    $localPaths = @($RelativePaths | ForEach-Object { ConvertTo-LocalRelativePath $_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    if ($localPaths.Count -eq 0) {
        return @()
    }

    $cacheKey = ([string]$Target.uiRoot).TrimEnd("\", "/").ToLowerInvariant()
    if ($script:ReleaseStatusCache.ContainsKey($cacheKey)) {
        $cachedStatuses = $script:ReleaseStatusCache[$cacheKey]
        $hasAllPaths = $true
        foreach ($localPath in $localPaths) {
            if (-not $cachedStatuses.ContainsKey($localPath)) {
                $hasAllPaths = $false
                break
            }
        }
        if ($hasAllPaths) {
            return @($localPaths | ForEach-Object { @($cachedStatuses[$_]) } | ForEach-Object { $_ } | Sort-Object -Unique)
        }
    }

    $arguments = @("status", "--quiet", "--") + $localPaths
    $result = Invoke-Svn -Arguments $arguments -WorkingDirectory ([string]$Target.uiRoot)
    if ($result.exitCode -ne 0) {
        return @("svn status failed: $($result.stdout)")
    }

    if ([string]::IsNullOrWhiteSpace($result.stdout)) {
        return @()
    }

    return @($result.stdout -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Initialize-ReleaseStatusCache {
    param([string[]]$RelativePaths, $Target)

    $targetRoot = [string]$Target.uiRoot
    if ([string]::IsNullOrWhiteSpace($targetRoot) -or -not (Test-Path -LiteralPath $targetRoot -PathType Container)) {
        return
    }

    $localPaths = @($RelativePaths | ForEach-Object { ConvertTo-LocalRelativePath $_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    if ($localPaths.Count -eq 0) {
        return
    }

    $pathStatuses = @{}
    foreach ($localPath in $localPaths) {
        $pathStatuses[$localPath] = @()
    }

    $arguments = @("status", "--quiet", "--") + $localPaths
    $result = Invoke-Svn -Arguments $arguments -WorkingDirectory $targetRoot
    if ($result.exitCode -ne 0) {
        $errorText = "svn status failed: $($result.stdout)"
        foreach ($localPath in $localPaths) {
            $pathStatuses[$localPath] = @($errorText)
        }
    }
    elseif (-not [string]::IsNullOrWhiteSpace($result.stdout)) {
        foreach ($statusLine in @($result.stdout -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
            $reportedPath = ""
            if ($statusLine.Length -gt 8) {
                $reportedPath = $statusLine.Substring(8).Trim().Replace("/", "\").TrimStart(".\")
            }

            foreach ($localPath in $localPaths) {
                $normalizedLocalPath = $localPath.Replace("/", "\").TrimStart(".\").TrimEnd("\")
                $matchesPath = [string]::IsNullOrWhiteSpace($reportedPath)
                if (-not $matchesPath) {
                    $matchesPath = ($reportedPath -ieq $normalizedLocalPath) -or
                        $reportedPath.StartsWith("$normalizedLocalPath\", [System.StringComparison]::OrdinalIgnoreCase) -or
                        $normalizedLocalPath.StartsWith("$reportedPath\", [System.StringComparison]::OrdinalIgnoreCase)
                }
                if ($matchesPath) {
                    $pathStatuses[$localPath] = @($pathStatuses[$localPath]) + $statusLine
                }
            }
        }
    }

    $cacheKey = $targetRoot.TrimEnd("\", "/").ToLowerInvariant()
    $script:ReleaseStatusCache[$cacheKey] = $pathStatuses
}

function Get-TargetMergeAssessment {
    param($Target, $Config, $UiChanges, $BaseRisks)

    $risks = New-Object System.Collections.Generic.List[object]
    foreach ($risk in @($BaseRisks)) {
        Add-Risk $risks ([string]$risk.level) ([string]$risk.message)
    }

    $targetRoot = [string]$Target.uiRoot
    if ([string]::IsNullOrWhiteSpace($targetRoot)) {
        Add-Risk $risks "Warn" "$($Target.name) UI 目录未配置，点击 merge 时会提示选择。"
    }
    elseif (-not (Test-Path -LiteralPath $targetRoot -PathType Container)) {
        Add-Risk $risks "Warn" "$($Target.name) UI 目录不可用，点击 merge 时会提示重新选择：$targetRoot"
    }
    else {
        $targetPaths = @()
        $targetPaths += @($UiChanges | ForEach-Object { $_.relativePath })
        $targetPaths += @($UiChanges | ForEach-Object { Get-MergeParentRelativePath $_.relativePath })
        $targetPaths = @($targetPaths | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique)
        foreach ($statusLine in @(Get-ReleaseLocalStatuses -RelativePaths $targetPaths -Target $Target)) {
            Add-Risk $risks "Block" "$($Target.name) 目标已有本地状态：$statusLine"
        }
    }

    $parents = @(Get-TopMergeParents @($UiChanges | ForEach-Object { Get-MergeParentRelativePath $_.relativePath }))
    $mergeGroups = @()
    foreach ($parent in $parents) {
        $revisions = @($UiChanges | Where-Object { Test-RelativePathUnderParent -RelativePath ([string]$_.relativePath) -ParentRelativePath $parent } | ForEach-Object { [int]$_.revision } | Sort-Object -Unique)
        if ($revisions.Count -eq 0) { continue }

        $mergeGroups += [pscustomobject]@{
            parentRelativePath = $parent
            revisions = @($revisions)
            revisionSpec = ($revisions -join ",")
            sourceUrl = Get-SourceUrlForGroup $Config $parent
            targetPath = Get-LocalTargetForGroup $parent
        }
    }

    $hasBlock = @($risks | Where-Object { $_.level -eq "Block" }).Count -gt 0
    $hasWarn = @($risks | Where-Object { $_.level -eq "Warn" }).Count -gt 0
    $state = "Ready"
    if ($hasBlock) {
        $state = "Blocked"
    }
    elseif ($hasWarn) {
        $state = "Warning"
    }

    $riskText = "可 merge"
    if ($risks.Count -gt 0) {
        $riskText = (@($risks | ForEach-Object { $_.message }) -join "；")
    }

    return [pscustomobject]@{
        target = $Target
        state = $state
        riskText = $riskText
        mergeGroups = @($mergeGroups)
        risks = @($risks | ForEach-Object { $_ })
        merged = $false
        commitRequested = $false
        commitRevision = 0
        flowDone = $false
    }
}

function Get-TicketAnalysis {
    param($Ticket, $Config, [object[]]$SvnEntries)

    $risks = New-Object System.Collections.Generic.List[object]
    $entries = @()
    $uiChanges = @()
    $outsidePaths = @()

    if ($PSBoundParameters.ContainsKey("SvnEntries")) {
        $entries = @($SvnEntries)
    }
    else {
        try {
            $entries = @(Get-TicketSvnEntries $Ticket $Config)
        }
        catch {
            Add-Risk $risks "Block" "读取 trunk UI SVN 日志失败：$($_.Exception.Message)"
        }
    }

    foreach ($entry in $entries) {
        foreach ($path in @($entry.paths)) {
            if ([string]::IsNullOrWhiteSpace($path.path)) { continue }

            $relativePath = Get-RelativeUiPath $path.path $Config
            if ($null -eq $relativePath) {
                $outsidePaths += $path.path
                continue
            }

            $uiChanges += [pscustomobject]@{
                revision = [int]$entry.revision
                action = [string]$path.action
                repoPath = [string]$path.path
                relativePath = [string]$relativePath
            }
        }
    }

    if ($entries.Count -eq 0 -and $risks.Count -eq 0) {
        Add-Risk $risks "Block" "没有找到包含单号 #$($Ticket.id) 的 trunk UI 提交。"
    }

    if ($uiChanges.Count -eq 0 -and $entries.Count -gt 0) {
        Add-Risk $risks "Block" "找到单号提交，但没有 trunk UI 路径变更。"
    }

    $outsidePaths = @($outsidePaths | Sort-Object -Unique)
    if ($outsidePaths.Count -gt 0) {
        Add-Risk $risks "Warn" "提交含 UI 外路径 $($outsidePaths.Count) 个，仅会合入 UI 路径。"
    }

    $parents = @($uiChanges | ForEach-Object { Get-MergeParentRelativePath $_.relativePath } | Sort-Object -Unique)
    foreach ($parent in $parents) {
        if ($parent -eq ".") {
            Add-Risk $risks "Warn" "包含 UI 根目录变更，可能受 mixed-revision 工作副本影响。"
        }
    }

    $revisionList = @($uiChanges | ForEach-Object { [int]$_.revision } | Sort-Object -Unique)
    $latestEntry = @($entries | Sort-Object revision -Descending | Select-Object -First 1)
    $lastRevision = 0
    $lastCommitTime = "无"
    if ($latestEntry.Count -gt 0) {
        $lastRevision = [int]$latestEntry[0].revision
        $commitDate = [DateTimeOffset]::MinValue
        if ([DateTimeOffset]::TryParse([string]$latestEntry[0].date, [ref]$commitDate)) {
            $lastCommitTime = $commitDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        }
    }
    $riskList = @($risks | ForEach-Object { $_ })
    $targetAssessments = @()
    foreach ($target in @(Get-ReleaseTargets $Config)) {
        $targetAssessments += Get-TargetMergeAssessment -Target $target -Config $Config -UiChanges @($uiChanges) -BaseRisks @($riskList)
    }
    $defaultAssessment = $targetAssessments | Where-Object { $_.target.key -eq "cn" } | Select-Object -First 1
    if ($null -eq $defaultAssessment) {
        $defaultAssessment = $targetAssessments | Select-Object -First 1
    }

    $analysis = New-Object psobject
    $analysis | Add-Member -MemberType NoteProperty -Name ticket -Value $Ticket
    $analysis | Add-Member -MemberType NoteProperty -Name state -Value $defaultAssessment.state
    $analysis | Add-Member -MemberType NoteProperty -Name riskText -Value $defaultAssessment.riskText
    $analysis | Add-Member -MemberType NoteProperty -Name revisions -Value @($revisionList)
    $analysis | Add-Member -MemberType NoteProperty -Name lastRevision -Value $lastRevision
    $analysis | Add-Member -MemberType NoteProperty -Name lastCommitTime -Value $lastCommitTime
    $analysis | Add-Member -MemberType NoteProperty -Name uiChanges -Value @($uiChanges)
    $analysis | Add-Member -MemberType NoteProperty -Name outsidePaths -Value @($outsidePaths)
    $analysis | Add-Member -MemberType NoteProperty -Name mergeGroups -Value @($defaultAssessment.mergeGroups)
    $analysis | Add-Member -MemberType NoteProperty -Name risks -Value @($defaultAssessment.risks)
    $analysis | Add-Member -MemberType NoteProperty -Name targetAssessments -Value @($targetAssessments)
    $analysis | Add-Member -MemberType NoteProperty -Name merged -Value $false
    return $analysis
}

function Test-DryRunOutputHasConflict {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    return ($Text -match "(?m)^\s*C\s+" -or $Text -match "Summary of conflicts" -or $Text -match "conflict")
}

function Limit-MessageText {
    param([string]$Text, [int]$MaxLength = 1800)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }
    if ($Text.Length -le $MaxLength) {
        return $Text
    }

    return "$($Text.Substring(0, $MaxLength))`r`n..."
}

function Get-SvnConflictPaths {
    param([string]$WorkingDirectory, [string]$TargetPath)

    $result = Invoke-Svn -Arguments @("status", "--quiet", $TargetPath) -WorkingDirectory $WorkingDirectory
    if ($result.exitCode -ne 0) {
        throw "读取冲突状态失败：$($result.stdout)"
    }

    $paths = New-Object System.Collections.Generic.List[string]
    foreach ($line in ($result.stdout -split "`n")) {
        $text = [string]$line
        if ([string]::IsNullOrWhiteSpace($text)) {
            continue
        }

        $match = [regex]::Match($text, "^(?<status>.{1,8})\s+(?<path>.+?)\s*$")
        if (-not $match.Success) {
            continue
        }

        $statusText = [string]$match.Groups["status"].Value
        if ($statusText.IndexOf("C", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            continue
        }

        $path = [string]$match.Groups["path"].Value
        if (-not [string]::IsNullOrWhiteSpace($path)) {
            $paths.Add($path)
        }
    }

    return @($paths | Sort-Object -Unique)
}

function Resolve-SvnConflictsWithTrunk {
    param([string]$WorkingDirectory, $Group)

    $outputs = New-Object System.Collections.Generic.List[string]
    $targetPath = [string]$Group.targetPath
    $sourceUrl = [string]$Group.sourceUrl
    $sourceRevision = Get-MaxRevisionFromSpec ([string]$Group.revisionSpec)
    $conflictPaths = @(Get-SvnConflictPaths -WorkingDirectory $WorkingDirectory -TargetPath $targetPath)
    if ($conflictPaths.Count -eq 0) {
        return "SVN 报告过冲突，但 svn status 已没有未解决冲突；通常表示 --accept theirs-full 已自动处理完文本冲突，继续后续 revision。"
    }

    foreach ($path in $conflictPaths) {
        $resolved = $false
        $first = Invoke-Svn -Arguments @("resolve", "--accept", "theirs-full", $path) -WorkingDirectory $WorkingDirectory
        $outputs.Add("resolve --accept theirs-full $path`n$($first.stdout)") | Out-Null
        if ($first.exitCode -eq 0) {
            $resolved = $true
        }
        else {
            $second = Invoke-Svn -Arguments @("resolve", "--accept", "theirs-conflict", $path) -WorkingDirectory $WorkingDirectory
            $outputs.Add("resolve --accept theirs-conflict $path`n$($second.stdout)") | Out-Null
            if ($second.exitCode -eq 0) {
                $resolved = $true
            }
        }

        if (-not $resolved) {
            $relativeToGroup = Get-ConflictPathUnderMergeTarget -ConflictPath $path -TargetPath $targetPath
            $conflictSourceUrl = Join-SvnUrlPath -BaseUrl $sourceUrl -RelativePath $relativeToGroup
            $destinationPath = Get-WorkingCopyFullPath -WorkingDirectory $WorkingDirectory -RelativePath $path
            $destinationParent = Split-Path -Parent $destinationPath
            if (-not [string]::IsNullOrWhiteSpace($destinationParent) -and -not (Test-Path -LiteralPath $destinationParent -PathType Container)) {
                New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
            }

            $export = Invoke-Svn -Arguments @("export", "--force", "-r", [string]$sourceRevision, $conflictSourceUrl, $destinationPath) -WorkingDirectory $WorkingDirectory
            $outputs.Add("export trunk r$sourceRevision $conflictSourceUrl -> $path`n$($export.stdout)") | Out-Null
            if ($export.exitCode -ne 0) {
                throw "无法从 trunk 导出冲突路径：$path`r`n$($export.stdout)"
            }

            $working = Invoke-Svn -Arguments @("resolve", "--accept", "working", $path) -WorkingDirectory $WorkingDirectory
            $outputs.Add("resolve --accept working $path`n$($working.stdout)") | Out-Null
            if ($working.exitCode -ne 0) {
                throw "已导出 trunk 内容，但无法标记冲突已解决：$path`r`n$($working.stdout)"
            }
        }
    }

    $remaining = @(Get-SvnConflictPaths -WorkingDirectory $WorkingDirectory -TargetPath $targetPath)
    if ($remaining.Count -gt 0) {
        throw "仍有未解决冲突：$($remaining -join ', ')"
    }

    return ($outputs -join "`n`n")
}

function Invoke-MergeDryRun {
    param($Assessment, [switch]$AllowConflicts)

    $outputs = @()
    foreach ($group in @($Assessment.mergeGroups)) {
        foreach ($revision in @(Get-RevisionListFromGroup $group)) {
            $result = Invoke-Svn -Arguments @("merge", "--dry-run", "-c", [string]$revision, [string]$group.sourceUrl, [string]$group.targetPath) -WorkingDirectory ([string]$Assessment.target.uiRoot)
            $outputs += "[$($group.targetPath)] r$revision`n$($result.stdout)"
            $hasConflict = Test-DryRunOutputHasConflict $result.stdout
            if ($result.exitCode -ne 0) {
                if ($AllowConflicts -and $hasConflict) {
                    continue
                }

                throw "dry-run 失败：$($result.stdout)"
            }
            if ($hasConflict -and -not $AllowConflicts) {
                throw "dry-run 发现冲突：$($result.stdout)"
            }
        }
    }

    return ($outputs -join "`n`n")
}

function Invoke-ActualMerge {
    param($Assessment, [switch]$AcceptTheirsFull)

    $outputs = @()
    foreach ($group in @($Assessment.mergeGroups)) {
        foreach ($revision in @(Get-RevisionListFromGroup $group)) {
            $arguments = @("merge")
            if ($AcceptTheirsFull) {
                $arguments += @("--accept", "theirs-full")
            }
            $arguments += @("-c", [string]$revision, [string]$group.sourceUrl, [string]$group.targetPath)

            $result = Invoke-Svn -Arguments $arguments -WorkingDirectory ([string]$Assessment.target.uiRoot)
            $outputs += "[$($group.targetPath)] r$revision`n$($result.stdout)"
            if ($result.exitCode -ne 0) {
                if ($AcceptTheirsFull -and (Test-DryRunOutputHasConflict $result.stdout)) {
                    try {
                        $singleRevisionGroup = [pscustomobject]@{
                            targetPath = [string]$group.targetPath
                            sourceUrl = [string]$group.sourceUrl
                            revisionSpec = [string]$revision
                            revisions = @($revision)
                        }
                        $resolveOutput = Resolve-SvnConflictsWithTrunk -WorkingDirectory ([string]$Assessment.target.uiRoot) -Group $singleRevisionGroup
                        $outputs += "[$($group.targetPath)] r$revision resolve conflicts with trunk`n$resolveOutput"
                        continue
                    }
                    catch {
                        throw "merge 失败，且自动用 trunk 覆盖冲突未完成：$($_.Exception.Message)`r`n`r`nSVN 输出：$($result.stdout)"
                    }
                }

                throw "merge 失败：$($result.stdout)"
            }
            if (Test-DryRunOutputHasConflict $result.stdout) {
                if ($AcceptTheirsFull) {
                    $singleRevisionGroup = [pscustomobject]@{
                        targetPath = [string]$group.targetPath
                        sourceUrl = [string]$group.sourceUrl
                        revisionSpec = [string]$revision
                        revisions = @($revision)
                    }
                    $resolveOutput = Resolve-SvnConflictsWithTrunk -WorkingDirectory ([string]$Assessment.target.uiRoot) -Group $singleRevisionGroup
                    $outputs += "[$($group.targetPath)] r$revision resolve conflicts with trunk`n$resolveOutput"
                }
                else {
                    throw "merge 后发现冲突提示：$($result.stdout)"
                }
            }
        }
    }

    return ($outputs -join "`n`n")
}

function Get-TicketUrl {
    param($Ticket)

    $type = [string]$Ticket.type
    if ($type -eq "bug" -or $type -eq "6745be52ca5bd28affaa7241") {
        $type = "bug2"
    }
    elseif ([string]::IsNullOrWhiteSpace($type)) {
        $type = "story"
    }

    return "https://project.feishu.cn/dragon_heir/$type/detail/$($Ticket.id)"
}

function Get-CommitMessage {
    param($Ticket)

    return "#$($Ticket.id) $($Ticket.title)"
}

function Copy-TextToClipboard {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return [pscustomobject]@{
            ok = $false
            error = "提交信息为空。"
        }
    }

    try {
        Initialize-WinForms
        [System.Windows.Forms.Clipboard]::SetText($Text)
        return [pscustomobject]@{ ok = $true; error = "" }
    }
    catch {
        try {
            Set-Clipboard -Value $Text
            return [pscustomobject]@{ ok = $true; error = "" }
        }
        catch {
            return [pscustomobject]@{
                ok = $false
                error = $_.Exception.Message
            }
        }
    }
}

function Resolve-TortoiseProcPath {
    param($Config)

    $configuredPath = [string](Get-PropertyValue $Config @("tortoiseProcPath", "tortoiseSvnPath"))
    if ([string]::IsNullOrWhiteSpace($configuredPath)) {
        $configuredPath = "C:\Program Files\TortoiseSVN\bin\TortoiseProc.exe"
    }

    if (Test-Path -LiteralPath $configuredPath -PathType Leaf) {
        return $configuredPath
    }

    if (Test-Path -LiteralPath $configuredPath -PathType Container) {
        $directCandidate = Join-Path $configuredPath "TortoiseProc.exe"
        if (Test-Path -LiteralPath $directCandidate -PathType Leaf) {
            return $directCandidate
        }

        $binCandidate = Join-Path $configuredPath "bin\TortoiseProc.exe"
        if (Test-Path -LiteralPath $binCandidate -PathType Leaf) {
            return $binCandidate
        }
    }

    throw "找不到 TortoiseSVN 提交程序：$configuredPath"
}

function Invoke-TortoiseSvnCommitDialog {
    param($Config, $Target)

    $targetRoot = [System.IO.Path]::GetFullPath([string]$Target.uiRoot)
    if (-not (Test-Path -LiteralPath $targetRoot -PathType Container)) {
        throw "$($Target.name) UI 目录不存在：$targetRoot"
    }

    $tortoiseProc = Resolve-TortoiseProcPath $Config
    $arguments = "/command:commit /path:`"$targetRoot`""
    Start-Process -FilePath $tortoiseProc -ArgumentList $arguments -WorkingDirectory $targetRoot | Out-Null
}

function Get-TargetCommitEntries {
    param($Ticket, $Target)

    $logTarget = [string](Get-PropertyValue $Target @("svnUrl"))
    if ([string]::IsNullOrWhiteSpace($logTarget)) {
        $infoResult = Invoke-Svn -Arguments @("info", [string]$Target.uiRoot) -WorkingDirectory ([string]$Target.uiRoot)
        if ($infoResult.exitCode -ne 0) {
            throw "读取 $($Target.name) SVN URL 失败：$($infoResult.stdout)"
        }
        $urlMatch = [regex]::Match($infoResult.stdout, "(?m)^URL:\s*(?<url>\S+)\s*$")
        if (-not $urlMatch.Success) {
            throw "无法从 svn info 中解析 $($Target.name) URL。"
        }
        $logTarget = [string]$urlMatch.Groups["url"].Value
    }

    $result = Invoke-Svn -Arguments @("log", "--xml", "-v", "--search", [string]$Ticket.id, $logTarget) -WorkingDirectory ([string]$Target.uiRoot)
    if ($result.exitCode -ne 0) {
        throw "读取 $($Target.name) SVN 提交日志失败：$($result.stdout)"
    }

    $entries = @()
    $logMatches = [regex]::Matches($result.stdout, '<logentry\s+revision="(?<revision>\d+)">(?<body>.*?)</logentry>', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    foreach ($logMatch in $logMatches) {
        $body = [string]$logMatch.Groups["body"].Value
        $paths = @()
        $pathMatches = [regex]::Matches($body, '<path\b(?<attrs>[^>]*)>(?<path>.*?)</path>', [System.Text.RegularExpressions.RegexOptions]::Singleline)
        foreach ($pathMatch in $pathMatches) {
            $pathText = [string]$pathMatch.Groups["path"].Value
            try {
                $pathText = [System.Net.WebUtility]::HtmlDecode($pathText)
            }
            catch {
            }
            $paths += $pathText
        }

        $entries += [pscustomobject]@{
            revision = [int]$logMatch.Groups["revision"].Value
            paths = @($paths)
        }
    }

    return @($entries | Sort-Object revision)
}

function Assert-TargetCommitted {
    param($Analysis, $Assessment)

    $dirty = @()
    $statusPaths = @()
    $statusPaths += @($Analysis.uiChanges | ForEach-Object { $_.relativePath })
    $statusPaths += @($Assessment.mergeGroups | ForEach-Object { $_.targetPath })
    foreach ($relativePath in @($statusPaths | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique)) {
        $localPath = ConvertTo-LocalRelativePath $relativePath
        $status = Invoke-Svn -Arguments @("status", "--quiet", $localPath) -WorkingDirectory ([string]$Assessment.target.uiRoot)
        if ($status.exitCode -ne 0) {
            throw "检查 $($Assessment.target.name) 本地状态失败：$($status.stdout)"
        }
        if (-not [string]::IsNullOrWhiteSpace($status.stdout)) {
            $dirty += "$localPath`n$($status.stdout)"
        }
    }

    if ($dirty.Count -gt 0) {
        throw "$($Assessment.target.name) 还有未提交状态，暂不标记已提交：`r`n$($dirty -join "`r`n")"
    }

    $entries = @(Get-TargetCommitEntries $Analysis.ticket $Assessment.target)
    if ($entries.Count -eq 0) {
        throw "$($Assessment.target.name) SVN 日志里还没找到单号 #$($Analysis.ticket.id)。请确认 TortoiseSVN 已提交且 message 包含单号。"
    }

    $latest = $entries | Sort-Object revision -Descending | Select-Object -First 1
    return [pscustomobject]@{
        revision = [int]$latest.revision
        paths = @($latest.paths)
    }
}

function Assert-StartPathAllowed {
    param($Config)

    $trunkUiRoot = [System.IO.Path]::GetFullPath([string]$Config.trunkUiRoot).TrimEnd("\", "/")

    if (-not (Test-Path -LiteralPath $trunkUiRoot -PathType Container)) {
        throw "trunk UI 目录不存在：$trunkUiRoot"
    }
    if ([string]::IsNullOrWhiteSpace($StartPath)) {
        return
    }

    $fullStartPath = [System.IO.Path]::GetFullPath($StartPath).TrimEnd("\", "/")
    if ($fullStartPath -ne $trunkUiRoot -and -not $fullStartPath.StartsWith("$trunkUiRoot\", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "请在 trunk UI 目录内使用快速 merge：$trunkUiRoot"
    }
}

function Save-ToolLog {
    param($Config, [string]$TicketId, [string]$TargetKey, [string]$Text)

    $logRoot = [string](Get-PropertyValue $Config @("logRoot"))
    if ([string]::IsNullOrWhiteSpace($logRoot)) {
        $logRoot = "C:\tmp\QuickReleaseMerge"
    }

    if (-not (Test-Path -LiteralPath $logRoot -PathType Container)) {
        New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
    }

    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    if ([string]::IsNullOrWhiteSpace($TargetKey)) {
        $TargetKey = "release"
    }
    $path = Join-Path $logRoot "merge_${TicketId}_${TargetKey}_$timestamp.log"
    [System.IO.File]::WriteAllText($path, $Text, [System.Text.UTF8Encoding]::new($false))
    return $path
}

function Show-QuickMergeDialog {
    param($Config, $TicketResult, $Analyses, [switch]$SmokeOnly)

    Initialize-WinForms
    Add-Type -AssemblyName System.Drawing

    $uiFont = New-Object System.Drawing.Font("Microsoft YaHei UI", 10)
    $smallFont = New-Object System.Drawing.Font("Microsoft YaHei UI", 9)
    $uiSettings = Read-JsonFile $UiSettingsPath

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "UI 快速 merge 到 CN/NA release"
    $form.StartPosition = "CenterScreen"
    $form.Size = New-Object System.Drawing.Size(1280, 700)
    $form.MinimumSize = New-Object System.Drawing.Size(1020, 560)
    $form.Font = $uiFont
    if ($uiSettings -and $uiSettings.window) {
        $savedWidth = [int]$uiSettings.window.width
        $savedHeight = [int]$uiSettings.window.height
        if ($savedWidth -ge 1020 -and $savedHeight -ge 560) {
            $form.Size = New-Object System.Drawing.Size($savedWidth, $savedHeight)
        }
    }

    $savedRowHeight = 42
    if ($uiSettings -and $uiSettings.grid -and $uiSettings.grid.PSObject.Properties.Name -contains "rowHeight") {
        $candidateRowHeight = [int]$uiSettings.grid.rowHeight
        if ($candidateRowHeight -ge 30 -and $candidateRowHeight -le 96) {
            $savedRowHeight = $candidateRowHeight
        }
    }

    $savedColumnWidths = @{}
    if ($uiSettings -and $uiSettings.grid -and $uiSettings.grid.columns) {
        foreach ($property in $uiSettings.grid.columns.PSObject.Properties) {
            $candidateWidth = [int]$property.Value
            if ($candidateWidth -ge 40 -and $candidateWidth -le 1200) {
                $savedColumnWidths[$property.Name] = $candidateWidth
            }
        }
    }

    $getSavedColumnWidth = {
        param([string]$Name, [int]$DefaultWidth)

        if ($savedColumnWidths.ContainsKey($Name)) {
            return [int]$savedColumnWidths[$Name]
        }

        return $DefaultWidth
    }

    $summary = New-Object System.Windows.Forms.Label
    $summary.AutoSize = $false
    $summary.Location = New-Object System.Drawing.Point(12, 12)
    $summary.Size = New-Object System.Drawing.Size(1240, 26)
    $summary.Text = "节点：$($TicketResult.targetNode)    来源：$($TicketResult.source)    用户：$($TicketResult.identity.windowsUser) / $($TicketResult.identity.email)"
    $form.Controls.Add($summary)

    $search = New-Object System.Windows.Forms.TextBox
    $search.Location = New-Object System.Drawing.Point(12, 44)
    $search.Size = New-Object System.Drawing.Size(1240, 28)
    $search.Font = $uiFont
    $form.Controls.Add($search)

    $grid = New-Object System.Windows.Forms.DataGridView
    $grid.Location = New-Object System.Drawing.Point(12, 82)
    $grid.Size = New-Object System.Drawing.Size(1240, 390)
    $grid.Anchor = "Top,Bottom,Left,Right"
    $grid.AllowUserToAddRows = $false
    $grid.AllowUserToDeleteRows = $false
    $grid.AllowUserToResizeRows = $true
    $grid.MultiSelect = $false
    $grid.ReadOnly = $true
    $grid.RowHeadersVisible = $true
    $grid.RowHeadersWidth = 24
    $grid.RowHeadersWidthSizeMode = [System.Windows.Forms.DataGridViewRowHeadersWidthSizeMode]::DisableResizing
    $grid.SelectionMode = [System.Windows.Forms.DataGridViewSelectionMode]::FullRowSelect
    $grid.AutoSizeRowsMode = [System.Windows.Forms.DataGridViewAutoSizeRowsMode]::None
    $grid.RowTemplate.Height = $savedRowHeight
    $grid.ColumnHeadersHeightSizeMode = [System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode]::DisableResizing
    $grid.ColumnHeadersHeight = 34
    $grid.Font = $smallFont
    $grid.EnableHeadersVisualStyles = $false
    $grid.ColumnHeadersDefaultCellStyle.Font = $uiFont
    $form.Controls.Add($grid)

    $columns = @(
        @("id", "单号", 100),
        @("type", "类型", 55),
        @("node", "节点", 130),
        @("title", "标题", 260),
        @("revisions", "UI revision", 100),
        @("lastCommitTime", "最后提交时间", 145),
        @("cnRisk", "CN 风险", 220),
        @("naRisk", "NA 风险", 220)
    )
    foreach ($column in $columns) {
        $col = New-Object System.Windows.Forms.DataGridViewTextBoxColumn
        $col.Name = $column[0]
        $col.HeaderText = $column[1]
        $col.Width = [int](& $getSavedColumnWidth ([string]$column[0]) ([int]$column[2]))
        $col.SortMode = [System.Windows.Forms.DataGridViewColumnSortMode]::NotSortable
        if ($col.Name -eq "lastCommitTime") {
            $col.ToolTipText = "列表默认按最后 UI revision 从旧到新排列"
        }
        [void]$grid.Columns.Add($col)
    }

    $buttonColumnCn = New-Object System.Windows.Forms.DataGridViewButtonColumn
    $buttonColumnCn.Name = "merge_cn"
    $buttonColumnCn.HeaderText = "CN 操作"
    $buttonColumnCn.Text = "merge to CN release"
    $buttonColumnCn.UseColumnTextForButtonValue = $false
    $buttonColumnCn.Width = [int](& $getSavedColumnWidth "merge_cn" 165)
    $buttonColumnCn.SortMode = [System.Windows.Forms.DataGridViewColumnSortMode]::NotSortable
    [void]$grid.Columns.Add($buttonColumnCn)

    $buttonColumnNa = New-Object System.Windows.Forms.DataGridViewButtonColumn
    $buttonColumnNa.Name = "merge_na"
    $buttonColumnNa.HeaderText = "NA 操作"
    $buttonColumnNa.Text = "merge to NA release"
    $buttonColumnNa.UseColumnTextForButtonValue = $false
    $buttonColumnNa.Width = [int](& $getSavedColumnWidth "merge_na" 165)
    $buttonColumnNa.SortMode = [System.Windows.Forms.DataGridViewColumnSortMode]::NotSortable
    [void]$grid.Columns.Add($buttonColumnNa)

    $detail = New-Object System.Windows.Forms.TextBox
    $detail.Location = New-Object System.Drawing.Point(12, 484)
    $detail.Size = New-Object System.Drawing.Size(1240, 82)
    $detail.Anchor = "Bottom,Left,Right"
    $detail.Multiline = $true
    $detail.ScrollBars = "Vertical"
    $detail.ReadOnly = $true
    $detail.Font = $smallFont
    $form.Controls.Add($detail)

    $status = New-Object System.Windows.Forms.Label
    $status.AutoSize = $false
    $status.Location = New-Object System.Drawing.Point(12, 576)
    $status.Size = New-Object System.Drawing.Size(760, 28)
    $status.Anchor = "Bottom,Left,Right"
    $form.Controls.Add($status)

    $openTicket = New-Object System.Windows.Forms.Button
    $openTicket.Text = "打开单子"
    $openTicket.Location = New-Object System.Drawing.Point(682, 606)
    $openTicket.Size = New-Object System.Drawing.Size(120, 36)
    $openTicket.Anchor = "Bottom,Right"
    $form.Controls.Add($openTicket)

    $refresh = New-Object System.Windows.Forms.Button
    $refresh.Text = "重新加载"
    $refresh.Location = New-Object System.Drawing.Point(818, 606)
    $refresh.Size = New-Object System.Drawing.Size(120, 36)
    $refresh.Anchor = "Bottom,Right"
    $form.Controls.Add($refresh)

    $close = New-Object System.Windows.Forms.Button
    $close.Text = "关闭"
    $close.Location = New-Object System.Drawing.Point(954, 606)
    $close.Size = New-Object System.Drawing.Size(120, 36)
    $close.Anchor = "Bottom,Right"
    $form.Controls.Add($close)

    $allAnalyses = @($Analyses | Sort-Object @{
        Expression = {
            if ([int64]$_.lastRevision -gt 0) { return [int64]$_.lastRevision }
            return [int64]::MaxValue
        }
        Ascending = $true
    }, @{
        Expression = { [int64]$_.ticket.id }
        Ascending = $true
    })
    $rowHeightState = [pscustomobject]@{
        isApplying = $false
        rowHeight = $savedRowHeight
    }

    $saveUiSettings = {
        $columnSettings = [ordered]@{}
        foreach ($column in $grid.Columns) {
            if (-not [string]::IsNullOrWhiteSpace([string]$column.Name)) {
                $columnSettings[[string]$column.Name] = [int]$column.Width
            }
        }

        $settings = [ordered]@{
            window = [ordered]@{
                width = $form.Width
                height = $form.Height
            }
            grid = [ordered]@{
                rowHeight = $rowHeightState.rowHeight
                columns = $columnSettings
            }
        }
        Write-JsonFile $settings $UiSettingsPath
    }

    $applySavedRowHeight = {
        $rowHeightState.isApplying = $true
        try {
            $grid.RowTemplate.Height = $rowHeightState.rowHeight
            foreach ($row in $grid.Rows) {
                $row.Height = $rowHeightState.rowHeight
            }
        }
        finally {
            $rowHeightState.isApplying = $false
        }
    }

    $copyAnalysisProperties = {
        param($TargetAnalysis, $UpdatedAnalysis)

        foreach ($property in $UpdatedAnalysis.PSObject.Properties) {
            $TargetAnalysis | Add-Member -MemberType NoteProperty -Name $property.Name -Value $property.Value -Force
        }
    }

    $ensureTargetPath = {
        param($analysis, [string]$targetKey)

        $assessment = Get-TargetAssessmentByKey $analysis $targetKey
        if ($null -eq $assessment) {
            [System.Windows.Forms.MessageBox]::Show("没有配置这个 release 目标。", "快速 merge", "OK", "Information") | Out-Null
            return $null
        }

        $target = $assessment.target
        $currentPath = [string]$target.uiRoot
        if (-not [string]::IsNullOrWhiteSpace($currentPath) -and (Test-Path -LiteralPath $currentPath -PathType Container)) {
            return $assessment
        }

        $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
        $dialog.Description = "选择 $($target.name) UI 工作副本目录"
        $dialog.ShowNewFolderButton = $false

        $defaultPath = [string]$target.defaultPath
        if (-not [string]::IsNullOrWhiteSpace($currentPath) -and (Test-Path -LiteralPath $currentPath -PathType Container)) {
            $dialog.SelectedPath = $currentPath
        }
        elseif (-not [string]::IsNullOrWhiteSpace($defaultPath) -and (Test-Path -LiteralPath $defaultPath -PathType Container)) {
            $dialog.SelectedPath = $defaultPath
        }

        $result = $dialog.ShowDialog($form)
        if ($result -ne [System.Windows.Forms.DialogResult]::OK) {
            return $null
        }

        $selectedPath = [System.IO.Path]::GetFullPath($dialog.SelectedPath).TrimEnd("\", "/")
        if (-not (Test-Path -LiteralPath $selectedPath -PathType Container)) {
            [System.Windows.Forms.MessageBox]::Show("$($target.name) UI 目录不存在：$selectedPath", "路径无效", "OK", "Warning") | Out-Null
            return $null
        }

        $configKey = [string]$target.configKey
        if ([string]::IsNullOrWhiteSpace($configKey)) {
            if ($targetKey -eq "na") {
                $configKey = "naReleaseUiRoot"
            }
            else {
                $configKey = "releaseUiRoot"
            }
        }
        $Config | Add-Member -MemberType NoteProperty -Name $configKey -Value $selectedPath -Force
        Save-Config $Config

        $updatedAnalysis = Get-TicketAnalysis $analysis.ticket $Config
        & $copyAnalysisProperties $analysis $updatedAnalysis
        & $reloadGrid

        $status.Text = "$($target.name) UI 路径已保存：$selectedPath"
        return (Get-TargetAssessmentByKey $analysis $targetKey)
    }

    $getActionText = {
        param($Assessment)

        if ($null -eq $Assessment) {
            return ""
        }
        if ($Assessment.flowDone) {
            return "已提交"
        }
        if ($Assessment.commitRequested) {
            return "检查提交"
        }
        if ($Assessment.merged) {
            return "提交"
        }

        return [string]$Assessment.target.buttonText
    }

    $applyRowStyle = {
        param($row, $analysis)

        $targetAssessments = @($analysis.targetAssessments)
        $allMerged = ($targetAssessments.Count -gt 0 -and @($targetAssessments | Where-Object { -not $_.merged }).Count -eq 0)
        $hasBlocked = @($targetAssessments | Where-Object { $_.state -eq "Blocked" }).Count -gt 0
        $hasWarning = @($targetAssessments | Where-Object { $_.state -eq "Warning" }).Count -gt 0

        if ($allMerged) {
            $row.DefaultCellStyle.BackColor = [System.Drawing.Color]::Honeydew
            $row.DefaultCellStyle.ForeColor = [System.Drawing.Color]::DarkGreen
        }
        elseif ($hasBlocked) {
            $row.DefaultCellStyle.BackColor = [System.Drawing.Color]::MistyRose
            $row.DefaultCellStyle.ForeColor = [System.Drawing.Color]::DarkRed
        }
        elseif ($hasWarning) {
            $row.DefaultCellStyle.BackColor = [System.Drawing.Color]::LightYellow
            $row.DefaultCellStyle.ForeColor = [System.Drawing.Color]::SaddleBrown
        }
        else {
            $row.DefaultCellStyle.BackColor = [System.Drawing.Color]::White
            $row.DefaultCellStyle.ForeColor = [System.Drawing.Color]::Black
        }
    }

    $reloadGrid = {
        $keyword = $search.Text.Trim()
        $rowHeightState.isApplying = $true
        try {
            $grid.Rows.Clear()
            foreach ($analysis in $allAnalyses) {
                $ticket = $analysis.ticket
                $haystack = "$($ticket.id) $($ticket.type) $($ticket.node) $($ticket.title) $($analysis.riskText) $($analysis.revisions -join ',') $($analysis.lastCommitTime)"
                if ($keyword -and $haystack.IndexOf($keyword, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
                    continue
                }

                $revisionText = "无"
                if ($analysis.revisions.Count -gt 0) {
                    $revisionText = "r" + (($analysis.revisions | Sort-Object -Unique) -join ",")
                }
                $cnAssessment = Get-TargetAssessmentByKey $analysis "cn"
                $naAssessment = Get-TargetAssessmentByKey $analysis "na"
                $cnRiskText = "未配置"
                $naRiskText = "未配置"
                if ($cnAssessment) {
                    $cnRiskText = [string]$cnAssessment.riskText
                }
                if ($naAssessment) {
                    $naRiskText = [string]$naAssessment.riskText
                }
                $rowValues = [object[]]@(
                    [string]$ticket.id,
                    [string]$ticket.type,
                    [string]$ticket.node,
                    [string]$ticket.title,
                    [string]$revisionText,
                    [string]$analysis.lastCommitTime,
                    [string]$cnRiskText,
                    [string]$naRiskText,
                    [string](& $getActionText $cnAssessment),
                    [string](& $getActionText $naAssessment)
                )
                $rowIndex = $grid.Rows.Add($rowValues)
                $row = $grid.Rows[$rowIndex]
                $row.Height = $rowHeightState.rowHeight
                $row.Tag = $analysis
                & $applyRowStyle $row $analysis
            }
        }
        finally {
            $rowHeightState.isApplying = $false
        }

        $statusParts = New-Object System.Collections.Generic.List[string]
        foreach ($target in @(Get-ReleaseTargets $Config)) {
            $blockedCount = 0
            $warningCount = 0
            $readyCount = 0
            foreach ($analysis in $allAnalyses) {
                $assessment = Get-TargetAssessmentByKey $analysis $target.key
                if ($null -eq $assessment) { continue }
                if ($assessment.state -eq "Blocked") {
                    $blockedCount += 1
                }
                elseif ($assessment.state -eq "Warning") {
                    $warningCount += 1
                }
                elseif ($assessment.state -eq "Ready") {
                    $readyCount += 1
                }
            }
            $statusParts.Add("$($target.name)：可 $readyCount，风险 $warningCount，阻止 $blockedCount") | Out-Null
        }
        $status.Text = "$($allAnalyses.Count) 个待 release 单；$($statusParts -join '；')。"

        if ($grid.Rows.Count -gt 0 -and $grid.SelectedRows.Count -eq 0) {
            $grid.Rows[0].Selected = $true
            $grid.CurrentCell = $grid.Rows[0].Cells[0]
        }
        & $showSelectedDetail
    }

    $showSelectedDetail = {
        if ($rowHeightState.isApplying) {
            return
        }

        if ($grid.SelectedRows.Count -eq 0) {
            $detail.Text = ""
            return
        }

        $analysis = $grid.SelectedRows[0].Tag
        if ($null -eq $analysis) {
            $detail.Text = ""
            return
        }

        $ticket = $analysis.ticket
        $lines = New-Object System.Collections.Generic.List[string]
        $lines.Add("#$($ticket.id) $($ticket.title)") | Out-Null
        $lines.Add("状态：$($analysis.state)    revision：$($analysis.revisions -join ', ')    最后提交：$($analysis.lastCommitTime)") | Out-Null
        foreach ($assessment in @($analysis.targetAssessments)) {
            $lines.Add("$($assessment.target.name)：$($assessment.state)    $($assessment.riskText)") | Out-Null
            if ($assessment.commitRequested -and -not $assessment.flowDone) {
                $lines.Add("  已打开提交窗口；提交后点击【检查提交】。") | Out-Null
            }
            if ($assessment.commitRevision -gt 0) {
                $lines.Add("  release 提交：r$($assessment.commitRevision)") | Out-Null
            }
            if ($assessment.flowDone) {
                $lines.Add("  已提交；请点击【打开单子】手动流转流程。") | Out-Null
            }
            if ($assessment.mergeGroups.Count -gt 0) {
                foreach ($group in @($assessment.mergeGroups)) {
                    $targetPath = Join-Path ([string]$assessment.target.uiRoot) ([string]$group.targetPath)
                    $lines.Add("  r$($group.revisionSpec)  $($group.sourceUrl) -> $targetPath") | Out-Null
                }
            }
        }
        $detail.Text = ($lines -join "`r`n")
    }

    $search.Add_TextChanged($reloadGrid)
    $grid.Add_SelectionChanged($showSelectedDetail)
    $grid.Add_RowHeightChanged({
        param($sender, $event)

        if ($rowHeightState.isApplying) {
            return
        }
        if ($null -eq $event.Row) {
            return
        }

        $newRowHeight = [int]$event.Row.Height
        if ($newRowHeight -lt 30 -or $newRowHeight -gt 96) {
            return
        }

        $rowHeightState.rowHeight = $newRowHeight
        & $applySavedRowHeight
        & $saveUiSettings
    })
    $grid.Add_ColumnWidthChanged({
        param($sender, $event)

        if ($null -eq $event.Column) {
            return
        }
        if ([string]::IsNullOrWhiteSpace([string]$event.Column.Name)) {
            return
        }

        & $saveUiSettings
    })

    $runCommit = {
        param($analysis, [string]$targetKey)

        if ($null -eq $analysis) {
            return
        }

        $assessment = Get-TargetAssessmentByKey $analysis $targetKey
        if ($null -eq $assessment) {
            [System.Windows.Forms.MessageBox]::Show("没有配置这个 release 目标。", "快速 merge", "OK", "Information") | Out-Null
            return
        }

        $commitMessage = Get-CommitMessage $analysis.ticket
        $copyResult = Copy-TextToClipboard $commitMessage
        if (-not $copyResult.ok) {
            [System.Windows.Forms.MessageBox]::Show("提交信息复制失败：$($copyResult.error)`r`n`r`n提交信息：$commitMessage", "复制失败", "OK", "Warning") | Out-Null
        }

        try {
            Invoke-TortoiseSvnCommitDialog $Config $assessment.target
            $assessment.commitRequested = $true
            $assessment.riskText = "已打开 $($assessment.target.name) SVN 提交窗口；提交完成后点击【检查提交】。"
            & $reloadGrid
            $status.Text = "已打开 $($assessment.target.name) SVN 提交窗口；提交信息已复制：$commitMessage"
        }
        catch {
            [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "打开 SVN 提交窗口失败", "OK", "Error") | Out-Null
        }
    }

    $runCheckSubmitted = {
        param($analysis, [string]$targetKey)

        if ($null -eq $analysis) {
            return
        }

        $assessment = Get-TargetAssessmentByKey $analysis $targetKey
        if ($null -eq $assessment) {
            [System.Windows.Forms.MessageBox]::Show("没有配置这个 release 目标。", "快速 merge", "OK", "Information") | Out-Null
            return
        }

        if ($assessment.flowDone) {
            [System.Windows.Forms.MessageBox]::Show("这个单的 $($assessment.target.name) release 已检测到提交。请点击【打开单子】手动流转流程。", "快速 merge", "OK", "Information") | Out-Null
            return
        }

        $confirm = [System.Windows.Forms.MessageBox]::Show("将检查 $($assessment.target.name) SVN log 是否已经包含单子 #$($analysis.ticket.id)。`r`n`r`n检查通过后仅标记为【已提交】，流程请点击【打开单子】手动流转。`r`n`r`n继续吗？", "检查提交", "YesNo", "Question")
        if ($confirm -ne [System.Windows.Forms.DialogResult]::Yes) {
            return
        }

        try {
            $form.Cursor = [System.Windows.Forms.Cursors]::WaitCursor
            $grid.Enabled = $false
            $status.Text = "正在校验 $($assessment.target.name) SVN 提交：#$($analysis.ticket.id) ..."
            [System.Windows.Forms.Application]::DoEvents()

            $commitInfo = Assert-TargetCommitted $analysis $assessment
            $assessment.commitRevision = [int]$commitInfo.revision

            $assessment.flowDone = $true
            $assessment.state = "Submitted"
            $assessment.riskText = "已提交：$($assessment.target.name) release r$($commitInfo.revision)。请点击【打开单子】手动流转流程。"
            & $reloadGrid
            $status.Text = "已提交：#$($analysis.ticket.id) $($assessment.target.name) release r$($commitInfo.revision)。请点击【打开单子】手动流转流程。"
            [System.Windows.Forms.MessageBox]::Show("已检测到 release 提交。`r`n`r`n$($assessment.target.name) release：r$($commitInfo.revision)`r`n`r`n请点击【打开单子】手动流转流程。", "已提交", "OK", "Information") | Out-Null
        }
        catch {
            $status.Text = "检查提交失败：#$($analysis.ticket.id)"
            if ($_.Exception.Message -like "*还有未提交状态*") {
                $assessment.commitRequested = $false
                $assessment.riskText = "仍有未提交状态；点击【提交】重新打开 $($assessment.target.name) SVN 提交窗口。"
                & $reloadGrid
            }
            [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "检查提交失败", "OK", "Error") | Out-Null
        }
        finally {
            $grid.Enabled = $true
            $form.Cursor = [System.Windows.Forms.Cursors]::Default
        }
    }

    $runMerge = {
        param($analysis, [string]$targetKey)

        if ($null -eq $analysis) {
            return
        }

        $assessment = Get-TargetAssessmentByKey $analysis $targetKey
        if ($null -eq $assessment) {
            [System.Windows.Forms.MessageBox]::Show("没有配置这个 release 目标。", "快速 merge", "OK", "Information") | Out-Null
            return
        }

        $targetPath = [string]$assessment.target.uiRoot
        if ([string]::IsNullOrWhiteSpace($targetPath) -or -not (Test-Path -LiteralPath $targetPath -PathType Container)) {
            $assessment = & $ensureTargetPath $analysis $targetKey
            if ($null -eq $assessment) {
                return
            }
        }

        $targetName = [string]$assessment.target.name

        if ($assessment.flowDone) {
            [System.Windows.Forms.MessageBox]::Show("这个单的 $targetName release 已检测到提交。请点击【打开单子】手动流转流程。", "快速 merge", "OK", "Information") | Out-Null
            return
        }

        if ($assessment.commitRequested) {
            & $runCheckSubmitted $analysis $targetKey
            return
        }

        if ($assessment.merged) {
            & $runCommit $analysis $targetKey
            return
        }

        if ($assessment.state -eq "Blocked") {
            [System.Windows.Forms.MessageBox]::Show($assessment.riskText, "已阻止 merge", "OK", "Warning") | Out-Null
            return
        }

        if ($assessment.state -eq "Warning") {
            $confirm = [System.Windows.Forms.MessageBox]::Show("这个单合入 $targetName 有风险提示：`r`n`r`n$($assessment.riskText)`r`n`r`n将只合入 trunk UI 路径，继续吗？", "确认 merge", "YesNo", "Warning")
            if ($confirm -ne [System.Windows.Forms.DialogResult]::Yes) {
                return
            }
        }

        try {
            $form.Cursor = [System.Windows.Forms.Cursors]::WaitCursor
            $grid.Enabled = $false
            $status.Text = "正在 dry-run 到 $targetName：#$($analysis.ticket.id) ..."
            [System.Windows.Forms.Application]::DoEvents()
            $dryRunOutput = Invoke-MergeDryRun $assessment -AllowConflicts
            $acceptTrunkConflicts = $false
            if (Test-DryRunOutputHasConflict $dryRunOutput) {
                $form.Cursor = [System.Windows.Forms.Cursors]::Default
                $grid.Enabled = $true
                $conflictText = Limit-MessageText $dryRunOutput
                $confirmConflict = [System.Windows.Forms.MessageBox]::Show("dry-run 发现 SVN 冲突。`r`n`r`n如果继续，将对本次 merge 使用 trunk 版本覆盖冲突点；普通冲突会使用 svn merge --accept theirs-full，tree conflict 会从 trunk 导出对应文件/目录后标记 resolved。`r`n`r`n这可能覆盖 release 上同一文件/目录的改动，请确认你确实要以 trunk 为准。`r`n`r`n冲突摘要：`r`n$conflictText`r`n`r`n继续吗？", "用 trunk 覆盖冲突？", "YesNo", "Warning")
                if ($confirmConflict -ne [System.Windows.Forms.DialogResult]::Yes) {
                    $status.Text = "已取消：#$($analysis.ticket.id) dry-run 发现冲突。"
                    return
                }
                $grid.Enabled = $false
                $form.Cursor = [System.Windows.Forms.Cursors]::WaitCursor
                $acceptTrunkConflicts = $true
            }

            if ($acceptTrunkConflicts) {
                $status.Text = "正在用 trunk 覆盖冲突并 merge 到 $targetName：#$($analysis.ticket.id) ..."
            }
            else {
                $status.Text = "dry-run 通过，正在 merge 到 $targetName：#$($analysis.ticket.id) ..."
            }
            [System.Windows.Forms.Application]::DoEvents()
            $mergeOutput = Invoke-ActualMerge $assessment -AcceptTheirsFull:$acceptTrunkConflicts
            $mergeMode = "MERGE"
            if ($acceptTrunkConflicts) {
                $mergeMode = "MERGE --accept theirs-full + trunk export for tree conflicts"
            }
            $logPath = Save-ToolLog $Config $analysis.ticket.id $assessment.target.key ("TARGET $targetName`r`nDRY RUN`r`n$dryRunOutput`r`n`r`n$mergeMode`r`n$mergeOutput")

            $assessment.merged = $true
            $assessment.state = "Merged"
            if ($acceptTrunkConflicts) {
                $assessment.riskText = "已用 trunk 覆盖冲突并合入 $targetName 工作副本，等待 SVN Commit。日志：$logPath"
            }
            else {
                $assessment.riskText = "已合入 $targetName 工作副本，等待 SVN Commit。日志：$logPath"
            }
            $commitMessage = Get-CommitMessage $analysis.ticket
            $copyResult = Copy-TextToClipboard $commitMessage
            $copyText = "提交信息已复制：$commitMessage"
            if (-not $copyResult.ok) {
                $copyText = "提交信息复制失败：$($copyResult.error)`r`n提交信息：$commitMessage"
            }
            & $reloadGrid
            $status.Text = "完成：#$($analysis.ticket.id) 已 merge 到 $targetName 工作副本。$copyText"
            $detail.Text = "$copyText`r`n`r`nDRY RUN`r`n$dryRunOutput`r`n`r`n$mergeMode`r`n$mergeOutput`r`n`r`n日志：$logPath"
            $finishMessage = "已 merge 到 $targetName 工作副本，尚未 SVN Commit。"
            if ($acceptTrunkConflicts) {
                $finishMessage = "已用 trunk 覆盖冲突并 merge 到 $targetName 工作副本，尚未 SVN Commit。"
            }
            [System.Windows.Forms.MessageBox]::Show("$finishMessage`r`n`r`n$copyText`r`n`r`n点击该行【提交】可打开 TortoiseSVN 提交窗口。", "完成", "OK", "Information") | Out-Null
        }
        catch {
            $status.Text = "失败：#$($analysis.ticket.id)"
            [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "merge 失败", "OK", "Error") | Out-Null
        }
        finally {
            $grid.Enabled = $true
            $form.Cursor = [System.Windows.Forms.Cursors]::Default
        }
    }

    $grid.Add_CellContentClick({
        param($sender, $event)

        if ($event.RowIndex -lt 0) { return }
        $columnName = $grid.Columns[$event.ColumnIndex].Name
        if ($columnName -ne "merge_cn" -and $columnName -ne "merge_na") { return }
        $analysis = $grid.Rows[$event.RowIndex].Tag
        $targetKey = "cn"
        if ($columnName -eq "merge_na") {
            $targetKey = "na"
        }
        & $runMerge $analysis $targetKey
    })

    $openTicket.Add_Click({
        if ($grid.SelectedRows.Count -eq 0) {
            [System.Windows.Forms.MessageBox]::Show("先选一行。", "未选择单子", "OK", "Information") | Out-Null
            return
        }
        Start-Process (Get-TicketUrl $grid.SelectedRows[0].Tag.ticket)
    })

    $refresh.Add_Click({
        $form.DialogResult = [System.Windows.Forms.DialogResult]::Retry
        $form.Close()
    })

    $close.Add_Click({
        $form.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
        $form.Close()
    })

    $applyLayout = {
        $margin = 12
        $buttonWidth = 120
        $buttonHeight = 36
        $buttonGap = 16
        $bottom = 14
        $right = $form.ClientSize.Width - $margin
        $buttonY = $form.ClientSize.Height - $bottom - $buttonHeight
        $statusY = $buttonY - 32
        $detailHeight = 82
        $detailY = $statusY - $detailHeight - 10
        $gridBottom = $detailY - 12

        $summary.Width = $form.ClientSize.Width - ($margin * 2)
        $search.Width = $form.ClientSize.Width - ($margin * 2)
        $grid.Width = $form.ClientSize.Width - ($margin * 2)
        $grid.Height = [math]::Max(240, $gridBottom - $grid.Top)
        $detail.Top = $detailY
        $detail.Width = $form.ClientSize.Width - ($margin * 2)
        $detail.Height = $detailHeight
        $status.Top = $statusY
        $status.Width = $form.ClientSize.Width - ($margin * 2)

        $close.Left = $right - $buttonWidth
        $close.Top = $buttonY
        $refresh.Left = $close.Left - $buttonGap - $buttonWidth
        $refresh.Top = $buttonY
        $openTicket.Left = $refresh.Left - $buttonGap - $buttonWidth
        $openTicket.Top = $buttonY
    }

    $form.Add_Shown($applyLayout)
    $form.Add_Resize($applyLayout)
    & $reloadGrid

    if ($SmokeOnly) {
        $rowCount = $grid.Rows.Count
        $form.Dispose()
        return [pscustomobject]@{
            ok = $true
            rowCount = $rowCount
            detailText = [string]$detail.Text
        }
    }

    $dialogResult = $form.ShowDialog()
    & $saveUiSettings

    return $dialogResult
}

function Show-NoTicketRetryDialog {
    param($TicketResult)

    Initialize-WinForms

    $message = "没有找到目标状态单子：$($TicketResult.targetNode)`r`n`r`n来源：$($TicketResult.source)`r`n实时刷新：$($TicketResult.forceTicketRefresh)    缓存兜底：$($TicketResult.allowCacheFallback)`r`n用户：$($TicketResult.identity.windowsUser) / $($TicketResult.identity.email)"
    if (-not [string]::IsNullOrWhiteSpace([string]$TicketResult.errorMessage)) {
        $message = "$message`r`n错误：$($TicketResult.errorMessage)"
    }
    $message = "$message`r`n`r`n点击 Retry 会重新拉取单据；点击 Cancel 关闭。"
    return [System.Windows.Forms.MessageBox]::Show($message, "快速 merge", "RetryCancel", "Information")
}

function Show-StartupLoadingWindow {
    Initialize-WinForms
    Add-Type -AssemblyName System.Drawing

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "UI 快速 merge"
    $form.StartPosition = "CenterScreen"
    $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $form.ShowInTaskbar = $false
    $form.ClientSize = New-Object System.Drawing.Size(430, 104)
    $form.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", 10)

    $label = New-Object System.Windows.Forms.Label
    $label.AutoSize = $false
    $label.Location = New-Object System.Drawing.Point(18, 16)
    $label.Size = New-Object System.Drawing.Size(394, 26)
    $label.Text = "正在加载待 merge 单和 SVN 记录..."
    $form.Controls.Add($label)

    $progress = New-Object System.Windows.Forms.ProgressBar
    $progress.Location = New-Object System.Drawing.Point(18, 56)
    $progress.Size = New-Object System.Drawing.Size(394, 18)
    $progress.Style = [System.Windows.Forms.ProgressBarStyle]::Marquee
    $progress.MarqueeAnimationSpeed = 28
    $form.Controls.Add($progress)

    $form.Show()
    $form.Refresh()
    [System.Windows.Forms.Application]::DoEvents()
    return $form
}

function Load-Analyses {
    param($Config, $TicketResult)

    $analyses = @()
    $maxTickets = 0
    if ($Config.PSObject.Properties.Name -contains "maxTickets") {
        $maxTickets = [int]$Config.maxTickets
    }

    $tickets = @($TicketResult.tickets)
    if ($maxTickets -gt 0) {
        $tickets = @($tickets | Select-Object -First $maxTickets)
    }

    $script:ReleaseStatusCache = @{}
    $svnEntryMap = $null
    try {
        $svnEntryMap = Get-TicketsSvnEntryMap -Tickets $tickets -Config $Config
    }
    catch {
        # Preserve the previous per-ticket behavior if a batch query is rejected by an older SVN server.
        $svnEntryMap = $null
    }

    if ($null -ne $svnEntryMap) {
        $allTargetPaths = @()
        foreach ($ticket in $tickets) {
            foreach ($entry in @($svnEntryMap[[string]$ticket.id])) {
                foreach ($path in @($entry.paths)) {
                    $relativePath = Get-RelativeUiPath ([string]$path.path) $Config
                    if ($null -eq $relativePath) { continue }
                    $allTargetPaths += [string]$relativePath
                    $allTargetPaths += [string](Get-MergeParentRelativePath ([string]$relativePath))
                }
            }
        }
        $allTargetPaths = @($allTargetPaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
        foreach ($target in @(Get-ReleaseTargets $Config)) {
            Initialize-ReleaseStatusCache -RelativePaths $allTargetPaths -Target $target
        }
    }

    foreach ($ticket in $tickets) {
        if ($null -ne $svnEntryMap) {
            $ticketEntries = @($svnEntryMap[[string]$ticket.id])
            $analyses += Get-TicketAnalysis -Ticket $ticket -Config $Config -SvnEntries $ticketEntries
        }
        else {
            $analyses += Get-TicketAnalysis $ticket $Config
        }
    }

    return @($analyses)
}

function Get-QuickMergeData {
    param($Config, [string]$OnlyTicketId, [switch]$ShowFeedback)

    $loadingForm = $null
    if ($ShowFeedback) {
        $loadingForm = Show-StartupLoadingWindow
    }

    try {
        $ticketResult = Get-Tickets $Config
        if (-not [string]::IsNullOrWhiteSpace($OnlyTicketId)) {
            $ticketResult.tickets = @($ticketResult.tickets | Where-Object { $_.id -eq $OnlyTicketId })
        }
        $analyses = @(Load-Analyses $Config $ticketResult)
        return [pscustomobject]@{
            ticketResult = $ticketResult
            analyses = @($analyses)
        }
    }
    finally {
        if ($null -ne $loadingForm) {
            $loadingForm.Close()
            $loadingForm.Dispose()
        }
    }
}

try {
    $config = Merge-Config
    Assert-StartPathAllowed $config

    $loadResult = Get-QuickMergeData -Config $config -OnlyTicketId $TestTicketId -ShowFeedback:(-not $ListTickets -and -not $SmokeUi)
    $ticketResult = $loadResult.ticketResult
    $analyses = @($loadResult.analyses)
    if ($ListTickets) {
        $analyses | ConvertTo-Json -Depth 12
        exit 0
    }

    while ($analyses.Count -eq 0) {
        $retryResult = Show-NoTicketRetryDialog $ticketResult
        if ($retryResult -ne [System.Windows.Forms.DialogResult]::Retry) {
            exit 0
        }
        $loadResult = Get-QuickMergeData -Config $config -OnlyTicketId $TestTicketId -ShowFeedback
        $ticketResult = $loadResult.ticketResult
        $analyses = @($loadResult.analyses)
    }

    do {
        $result = Show-QuickMergeDialog $config $ticketResult $analyses -SmokeOnly:$SmokeUi
        if ($SmokeUi) {
            $result | ConvertTo-Json -Depth 4
            exit 0
        }
        if ($result -eq [System.Windows.Forms.DialogResult]::Retry) {
            $loadResult = Get-QuickMergeData -Config $config -OnlyTicketId $TestTicketId -ShowFeedback
            $ticketResult = $loadResult.ticketResult
            $analyses = @($loadResult.analyses)
        }
    } while ($result -eq [System.Windows.Forms.DialogResult]::Retry)
}
catch {
    if ($ListTickets -or $SmokeUi) {
        Write-Error "$($_.Exception.Message)`n$($_.ScriptStackTrace)"
    }
    else {
        Initialize-WinForms
        [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "快速 merge 错误", "OK", "Error") | Out-Null
    }
    exit 1
}
