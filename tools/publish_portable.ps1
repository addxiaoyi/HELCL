[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('win-x64','linux-x64','linux-arm64','osx-x64','osx-arm64')][string]$Runtime,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$BuildProperties = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../src/HelL.Desktop/HelL.Desktop.csproj'
$target = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $target) { throw "Publish directory already exists: $target" }
$arguments = @('publish', $project, '-c', 'Release', '-r', $Runtime, '--self-contained', 'true',
    '-p:UseAppHost=true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-o', $target, '--nologo')
if ($BuildProperties) { $arguments += "-p:CustomBeforeDirectoryBuildProps=$([IO.Path]::GetFullPath($BuildProperties))" }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Portable publish failed ($LASTEXITCODE)." }
[xml]$definition = Get-Content -LiteralPath $project -Raw
$version = [string]$definition.Project.PropertyGroup.Version
$metadata = [ordered]@{ schema = 1; version = $version; runtime = $Runtime; selfContained = $true }
[IO.File]::WriteAllText((Join-Path $target 'portable-build.json'), ($metadata | ConvertTo-Json))
Write-Output "Published $version for $Runtime at $target"
