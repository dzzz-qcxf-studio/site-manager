[CmdletBinding()]
param(
    [string]$ServerHost = "47.86.89.203",
    [string]$PublicBaseUrl = "http://47.86.89.203/s/",
    [string]$Username = "sitepublisher",
    [string]$PrivateKeyPath = (Join-Path $env:USERPROFILE ".ssh\site_manager_ed25519")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-Condition {
    param([bool]$Condition, [string]$Message)

    if (-not $Condition) {
        throw "Assertion failed: $Message"
    }
}

function ConvertTo-Base64Url {
    param([string]$Text)

    return [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Text)).TrimEnd("=").Replace("+", "-").Replace("/", "_")
}

function Invoke-SiteManagerCtl {
    param([string[]]$Arguments)

    $command = "site-managerctl " + ($Arguments -join " ")
    $result = @(& ssh @script:SshOptions "$script:Username@$script:ServerHost" $command 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Remote command failed ($LASTEXITCODE): $($result -join [Environment]::NewLine)"
    }

    $envelope = (($result | Select-Object -Last 1) | Out-String).Trim() | ConvertFrom-Json
    if (-not $envelope.ok) {
        throw "Remote protocol error [$($envelope.error.code)]: $($envelope.error.message)"
    }

    return $envelope.data
}

function Get-HttpStatus {
    param([string]$Url, [string]$ResponsePath)

    $status = (& curl.exe --silent --show-error --output $ResponsePath --write-out "%{http_code}" --max-time 20 $Url).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "HTTP request failed for $Url"
    }

    return [int]$status
}

function Publish-Archive {
    param([string]$ArchivePath, [string]$Mode, [string]$SiteId = "")

    $hash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $size = (Get-Item -LiteralPath $ArchivePath).Length
    $prepareArguments = @("prepare", "--request-id", ([Guid]::NewGuid().ToString("D")), "--mode", $Mode, "--size", $size, "--sha256", $hash)
    if ($Mode -eq "update") {
        $prepareArguments += @("--site-id", $SiteId)
    }

    $session = Invoke-SiteManagerCtl -Arguments $prepareArguments
    $remotePath = [string]$session.remotePath
    Assert-Condition ($remotePath -match "^/srv/site-manager/staging/[0-9a-f-]{36}/payload\.tar\.gz\.partial$") "Server returned an unsafe upload path."

    & scp @script:ScpOptions $ArchivePath ("{0}@{1}:{2}" -f $script:Username, $script:ServerHost, $remotePath)
    if ($LASTEXITCODE -ne 0) {
        throw "SFTP upload failed with exit code $LASTEXITCODE."
    }

    return Invoke-SiteManagerCtl -Arguments @(
        "publish", "--request-id", ([Guid]::NewGuid().ToString("D")), "--upload-id", ([string]$session.uploadId),
        "--name-b64", (ConvertTo-Base64Url "Site Manager E2E"), "--note-b64", (ConvertTo-Base64Url "automated smoke test")
    )
}

Assert-Condition ($ServerHost -match "^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$") "ServerHost must be a hostname or IPv4 address."
Assert-Condition ($Username -match "^[a-z_][a-z0-9_-]{0,31}$") "Username is invalid."
Assert-Condition (Test-Path -LiteralPath $PrivateKeyPath -PathType Leaf) "Private key file does not exist: $PrivateKeyPath"
Assert-Condition ($PublicBaseUrl -match "^http://[^/?#]+/s/$") "PublicBaseUrl must use the HTTP /s/ root."

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testSitePath = Join-Path $repositoryRoot "tests\e2e\TestSite"
$runRoot = Join-Path ([IO.Path]::GetTempPath()) ("site-manager-e2e-" + [Guid]::NewGuid().ToString("N"))
$responsePath = Join-Path $runRoot "response.html"
$archiveV1 = Join-Path $runRoot "site-v1.tar.gz"
$archiveV2 = Join-Path $runRoot "site-v2.tar.gz"
$updatedSitePath = Join-Path $runRoot "updated-site"
$script:ServerHost = $ServerHost
$script:Username = $Username
$script:SshOptions = @("-i", $PrivateKeyPath, "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes")
$script:ScpOptions = @("-i", $PrivateKeyPath, "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes")
$siteId = $null

try {
    New-Item -ItemType Directory -Path $runRoot | Out-Null
    $status = Invoke-SiteManagerCtl -Arguments @("status", "--request-id", ([Guid]::NewGuid().ToString("D")))
    Assert-Condition ($null -ne $status.serverTime) "Status response did not contain serverTime."

    & tar.exe -czf $archiveV1 -C $testSitePath .
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create the v1 test archive."
    }

    $created = Publish-Archive -ArchivePath $archiveV1 -Mode "create"
    $siteId = [string]$created.id
    $url = [string]$created.url
    Assert-Condition ($url.StartsWith($PublicBaseUrl, [StringComparison]::Ordinal)) "Published URL did not use the requested public base URL."
    Assert-Condition ((Get-HttpStatus -Url $url -ResponsePath $responsePath) -eq 200) "Published v1 site was not public."
    Assert-Condition ((Get-Content -LiteralPath $responsePath -Raw) -match "site-manager-e2e-v1") "Published v1 content marker was missing."

    Copy-Item -LiteralPath $testSitePath -Destination $updatedSitePath -Recurse
    $updatedIndexPath = Join-Path $updatedSitePath "index.html"
    $updatedIndex = (Get-Content -LiteralPath $updatedIndexPath -Raw).Replace("site-manager-e2e-v1", "site-manager-e2e-v2")
    [IO.File]::WriteAllText($updatedIndexPath, $updatedIndex, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $updatedSitePath "assets\version.txt"), "site-manager-e2e-v2`n", [Text.UTF8Encoding]::new($false))
    & tar.exe -czf $archiveV2 -C $updatedSitePath .
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create the v2 test archive."
    }

    $updated = Publish-Archive -ArchivePath $archiveV2 -Mode "update" -SiteId $siteId
    Assert-Condition ([string]$updated.url -eq $url) "Update changed the public URL."
    Assert-Condition ((Get-HttpStatus -Url $url -ResponsePath $responsePath) -eq 200) "Updated site was not public."
    Assert-Condition ((Get-Content -LiteralPath $responsePath -Raw) -match "site-manager-e2e-v2") "Updated content marker was missing."

    Invoke-SiteManagerCtl -Arguments @("trash", "--request-id", ([Guid]::NewGuid().ToString("D")), "--site-id", $siteId) | Out-Null
    Assert-Condition ((Get-HttpStatus -Url $url -ResponsePath $responsePath) -eq 404) "Trashed site still returned HTTP 200."

    Invoke-SiteManagerCtl -Arguments @("restore", "--request-id", ([Guid]::NewGuid().ToString("D")), "--site-id", $siteId) | Out-Null
    Assert-Condition ((Get-HttpStatus -Url $url -ResponsePath $responsePath) -eq 200) "Restored site was not public."

    Invoke-SiteManagerCtl -Arguments @("trash", "--request-id", ([Guid]::NewGuid().ToString("D")), "--site-id", $siteId) | Out-Null
    Invoke-SiteManagerCtl -Arguments @("purge", "--request-id", ([Guid]::NewGuid().ToString("D")), "--site-id", $siteId) | Out-Null
    $remaining = Invoke-SiteManagerCtl -Arguments @("list", "--request-id", ([Guid]::NewGuid().ToString("D")), "--status", "all")
    Assert-Condition (@($remaining.sites | Where-Object { [string]$_.id -eq $siteId }).Count -eq 0) "Purge left the smoke-test site in the registry."
    $siteId = $null
    Write-Host "PASS: status, create, update, trash, restore, purge, and public HTTP checks completed."
}
finally {
    if ($siteId) {
        try {
            Invoke-SiteManagerCtl -Arguments @("trash", "--request-id", ([Guid]::NewGuid().ToString("D")), "--site-id", $siteId) | Out-Null
            Invoke-SiteManagerCtl -Arguments @("purge", "--request-id", ([Guid]::NewGuid().ToString("D")), "--site-id", $siteId) | Out-Null
        }
        catch {
            Write-Warning "Could not clean up smoke-test site ${siteId}: $($_.Exception.Message)"
        }
    }

    if (Test-Path -LiteralPath $runRoot) {
        [IO.Directory]::Delete($runRoot, $true)
    }
}
