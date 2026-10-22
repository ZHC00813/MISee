[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'Source/GreenFolder.csproj'
$xml = [xml](Get-Content -LiteralPath $project -Raw)
$version = [string]$xml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid project version.' }
$publish = Join-Path $repo "artifacts/publish/$version"
$packages = Join-Path $repo 'artifacts/packages'
New-Item -ItemType Directory -Path $packages -Force | Out-Null
dotnet publish $project --configuration Release --runtime win-x64 --self-contained false --output $publish -p:DebugSymbols=false -p:DebugType=None --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $repo 'README.md') -Destination $publish
Copy-Item -LiteralPath (Join-Path $repo 'docs') -Destination $publish -Recurse -Force
$archive = Join-Path $packages "MISee-v$version-win-x64.zip"
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $archive -Force
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
"$hash  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath ($archive + '.sha256') -Encoding ascii
Write-Host "Package: $archive"
