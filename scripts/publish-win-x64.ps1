[CmdletBinding()]
param([switch]$Overwrite)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Remove-PreviousPackage {
    param([string]$Path, [string]$ArtifactsRoot)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    if (-not $Overwrite) {
        throw "Release output already exists: $Path. Run again with -Overwrite to replace only this package."
    }

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $resolvedArtifactsRoot = [IO.Path]::GetFullPath($ArtifactsRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($resolvedArtifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside artifacts: $resolvedPath"
    }

    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\SiteManager.App\SiteManager.App.csproj"
$readmePath = Join-Path $repositoryRoot "README.md"
$settingsTemplatePath = Join-Path $repositoryRoot "config\settings.example.json"
$artifactsRoot = Join-Path $repositoryRoot "artifacts"
$publishDirectory = Join-Path $artifactsRoot "SiteManager-win-x64"
$zipPath = Join-Path $artifactsRoot "SiteManager-win-x64.zip"
$hashPath = Join-Path $artifactsRoot "SiteManager-win-x64.zip.sha256"

& dotnet test -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Release tests failed. No package was created."
}

if (-not (Test-Path -LiteralPath $readmePath -PathType Leaf) -or -not (Test-Path -LiteralPath $settingsTemplatePath -PathType Leaf)) {
    throw "Package documentation or settings template is missing."
}

Remove-PreviousPackage -Path $publishDirectory -ArtifactsRoot $artifactsRoot
Remove-PreviousPackage -Path $zipPath -ArtifactsRoot $artifactsRoot
Remove-PreviousPackage -Path $hashPath -ArtifactsRoot $artifactsRoot
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

& dotnet publish $projectPath -c Release -r win-x64 --self-contained true --no-restore -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Windows x64 publish failed."
}

$applicationPath = Join-Path $publishDirectory "SiteManager.App.exe"
if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
    throw "Published application is missing: $applicationPath"
}

Copy-Item -LiteralPath $readmePath -Destination (Join-Path $publishDirectory "README.md")
Copy-Item -LiteralPath $settingsTemplatePath -Destination (Join-Path $publishDirectory "settings.example.json")
Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $zipPath -CompressionLevel Optimal
if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
    throw "ZIP package was not created."
}

$sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($hashPath, "$sha256  SiteManager-win-x64.zip`n", [Text.UTF8Encoding]::new($false))
Write-Host "Created: $zipPath"
Write-Host "SHA-256: $sha256"
