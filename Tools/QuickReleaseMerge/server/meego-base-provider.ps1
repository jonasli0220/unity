param(
    [string]$BaseUrl = "https://fastgatecraft.sgra.woa.com/meego-base",
    [string]$ProjectKey = "dragon_heir",
    [string[]]$WorkItemTypeKeys = @("story", "6745be52ca5bd28affaa7241"),
    [string[]]$OpenStorySubStages = @("started", "qdm4u_x1d", "sub_stage_3", "linshihong_3331583827552071", "36pl0to30", "sub_stage_1651205593210", "oke8rkmep", "sub_stage_2", "sub_stage_1660535137631", "53c2802s2"),
    [string[]]$OpenBugStateKeys = @("started", "9cR44p7mQ", "TbEabtpyH", "53c2802s2"),
    [string[]]$ExcludedNodeKeywords = @(),
    [int]$PageSize = 200,
    [int]$MaxPages = 0,
    [string]$ApiKey = "",
    [string]$UserKey = "",
    [string]$Email = ""
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = [string]$env:MEEGO_BASE_API_KEY
}
if ([string]::IsNullOrWhiteSpace($UserKey)) {
    $UserKey = [string]$env:SVN_FEISHU_USER_KEY
}
if ([string]::IsNullOrWhiteSpace($Email)) {
    $Email = [string]$env:SVN_FEISHU_EMAIL
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "Missing API key. Set MEEGO_BASE_API_KEY before starting ticket-service."
}

if ($ExcludedNodeKeywords.Count -eq 0) {
    $ExcludedNodeKeywords = @((-join ([char[]]@(22806, 25918, 23436, 27605))))
}

$BaseUrl = $BaseUrl.TrimEnd("/")
$headers = @{
    Authorization = "Bearer $ApiKey"
    "Content-Type" = "application/json; charset=utf-8"
    "Cache-Control" = "no-cache"
}

function Invoke-MeegoApi {
    param(
        [ValidateSet("GET", "POST")]
        [string]$Method,
        [string]$Path,
        $Body = $null
    )

    $uri = "$BaseUrl$Path"
    $args = @{
        Method = $Method
        Uri = $uri
        Headers = $headers
        TimeoutSec = 30
    }
    if ($null -ne $Body) {
        $args.Body = ($Body | ConvertTo-Json -Depth 12)
    }

    return Invoke-RestMethod @args
}

function Resolve-MeegoUserKey {
    if (-not [string]::IsNullOrWhiteSpace($UserKey)) {
        return $UserKey
    }
    if ([string]::IsNullOrWhiteSpace($Email)) {
        throw "Missing user identity. Provide userKey or email."
    }

    $body = @{ emails = @($Email) }
    $result = Invoke-MeegoApi -Method POST -Path "/api/v1/meego/users/query" -Body $body
    if ($result.code -ne 0) {
        throw "User query failed: $($result.message)"
    }

    $user = @($result.data) | Select-Object -First 1
    if ($null -eq $user -or [string]::IsNullOrWhiteSpace([string]$user.user_key)) {
        throw "Cannot resolve user_key for email: $Email"
    }

    return [string]$user.user_key
}

function Get-StatusText {
    param($Status)

    if ($null -eq $Status) { return "" }
    if ($Status -is [string]) { return Repair-Mojibake $Status }

    foreach ($name in @("label", "name", "state_name", "state_key", "key")) {
        if ($Status.PSObject.Properties.Name -contains $name) {
            $value = [string]$Status.$name
            if (-not [string]::IsNullOrWhiteSpace($value)) { return Repair-Mojibake $value }
        }
    }

    return ""
}

function Repair-Mojibake {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $Text
    }

    try {
        $bytes = New-Object byte[] $Text.Length
        for ($i = 0; $i -lt $Text.Length; $i += 1) {
            $code = [int][char]$Text[$i]
            if ($code -gt 255) {
                return $Text
            }
            $bytes[$i] = [byte]$code
        }
        return [System.Text.Encoding]::UTF8.GetString($bytes)
    }
    catch {
        return $Text
    }
}

function Get-CurrentNodeText {
    param($Item)

    $nodes = @($Item.current_nodes)
    if ($nodes.Count -eq 0) { return "" }

    $names = foreach ($node in $nodes) {
        foreach ($name in @("name", "node_name", "state_name", "state_key", "key")) {
            if ($node.PSObject.Properties.Name -contains $name) {
                $value = [string]$node.$name
                if (-not [string]::IsNullOrWhiteSpace($value)) {
                    Repair-Mojibake $value
                    break
                }
            }
        }
    }

    return (@($names) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique) -join ", "
}

function Convert-ToTicket {
    param($Item, [string]$TypeKey)

    $id = [string]$Item.id
    if ([string]::IsNullOrWhiteSpace($id)) { $id = [string]$Item.work_item_id }

    $title = Repair-Mojibake ([string]$Item.name)
    if ([string]::IsNullOrWhiteSpace($title)) { $title = Repair-Mojibake ([string]$Item.title) }

    $type = [string]$Item.work_item_type_key
    if ([string]::IsNullOrWhiteSpace($type)) { $type = $TypeKey }
    if ($type -eq "6745be52ca5bd28affaa7241") { $type = "bug" }

    $status = Get-StatusText $Item.work_item_status
    $node = Get-CurrentNodeText $Item
    if ([string]::IsNullOrWhiteSpace($node)) { $node = $status }

    [pscustomobject]@{
        id = $id
        type = $type
        title = $title
        status = "open"
        node = $node
    }
}

function Test-ExcludedTicket {
    param($Ticket)

    foreach ($keyword in $ExcludedNodeKeywords) {
        if ([string]::IsNullOrWhiteSpace($keyword)) { continue }
        if ([string]$Ticket.node -like "*$keyword*") {
            return $true
        }
    }

    return $false
}

$resolvedUserKey = Resolve-MeegoUserKey
$tickets = @()

foreach ($typeKey in $WorkItemTypeKeys) {
    $pageNum = 1
    do {
        $body = @{
            work_item_type_keys = @($typeKey)
            user_keys = @($resolvedUserKey)
            page_num = $pageNum
            page_size = $PageSize
            expand = @{
                need_workflow = $true
                need_user_detail = $false
            }
        }
        if ($typeKey -eq "story" -and $OpenStorySubStages.Count -gt 0) {
            $body.sub_stages = @($OpenStorySubStages)
        }
        elseif ($typeKey -eq "6745be52ca5bd28affaa7241" -and $OpenBugStateKeys.Count -gt 0) {
            $body.work_item_status = @($OpenBugStateKeys | ForEach-Object { @{ state_key = $_ } })
        }

        $result = Invoke-MeegoApi -Method POST -Path "/api/v1/meego/work-items/$ProjectKey/filter" -Body $body
        if ($result.code -ne 0) {
            throw "Work item filter failed for ${typeKey}: $($result.message)"
        }

        foreach ($item in @($result.data)) {
            if ($null -ne $item) {
                $tickets += Convert-ToTicket $item $typeKey
            }
        }

        $hasMore = $false
        if ($null -ne $result.pagination -and $result.pagination.has_more) {
            $hasMore = $true
            $pageNum += 1
        }
        if ($MaxPages -gt 0 -and $pageNum -gt $MaxPages) {
            $hasMore = $false
        }
    } while ($hasMore)
}

$json = $tickets |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_.id) -and -not [string]::IsNullOrWhiteSpace($_.title) } |
    Where-Object { -not (Test-ExcludedTicket $_) } |
    Sort-Object @{ Expression = { [int64]$_.id }; Descending = $true } |
    ConvertTo-Json -Depth 8

Write-Output (Repair-Mojibake $json)
