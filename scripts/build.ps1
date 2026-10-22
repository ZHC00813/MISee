[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne 'Win32NT') { throw 'MISee requires Windows.' }
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'Source/GreenFolder.csproj'
$build = Join-Path $repo 'artifacts/build'
dotnet build $project --configuration $Configuration --output $build --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
