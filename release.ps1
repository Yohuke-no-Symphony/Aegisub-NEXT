#Requires -Version 7.2
<#
.SYNOPSIS
Builds and publishes a Windows NSIS installer or macOS DMG into a fresh artifacts/releases directory.
#>
[CmdletBinding()]
param(
    [ValidateSet('osx-arm64', 'osx-x64', 'win-x64')][string] $RuntimeIdentifier,
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [string] $Tag,
    [Alias('-force')][switch] $Force,
    [version] $Version,
    [string] $FfmpegRoot,
    [string] $SdlRoot,
    [string] $OutputDirectory,
    [string] $LicenseDirectory,
    [string[]] $RuntimeDependencyDirectory = @(),
    [string] $SigningIdentity = '-',
    [switch] $SkipBuild,
    [string] $NsisPath,
    [ValidateRange(1, 128)][int] $Jobs = 2
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try
{
    Import-Module (Join-Path $PSScriptRoot 'scripts/publish/AegiNext.Release.psm1') -Force
    $arguments = @{} + $PSBoundParameters
    $arguments.RepositoryRoot = $PSScriptRoot
    Invoke-AegiNextRelease @arguments
    exit 0
}
catch
{
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
