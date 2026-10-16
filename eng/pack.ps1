param(
    [string]$Configuration = 'Release',
    [string]$Version,
    [string]$DotNet = 'dotnet',
    [string]$Python = 'python',
    [string]$ParserOsxX64 = $env:BGCS_OSX_X64_PACKAGE_RUNTIME_DIR,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$packArguments = @((Join-Path $PSScriptRoot 'pack.py'), '--configuration', $Configuration, '--dotnet', $DotNet)
if ($Version) { $packArguments += @('--version', $Version) }
if ($ParserOsxX64) { $packArguments += @('--parser-osx-x64', $ParserOsxX64) }
if ($NoBuild) { $packArguments += '--no-build' }
& $Python @packArguments
if ($LASTEXITCODE -ne 0) { throw 'BGCS product packaging failed.' }
