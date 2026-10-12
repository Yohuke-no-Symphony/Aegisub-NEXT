#Requires -Version 7.2
<#
.SYNOPSIS
Builds Debug media native libraries and the matching managed workbench.
.DESCRIPTION
Runs build.ps1 with Workbench / Debug so Decoder, Audio and Export are built before
the managed build copies their libraries into the application output directory.
.EXAMPLE
pwsh -NoProfile -File ./build-debug-native.ps1
.EXAMPLE
pwsh -NoProfile -File ./build-debug-native.ps1 -CheckEnvironment
.EXAMPLE
pwsh -NoProfile -File ./build-debug-native.ps1 -RunTests -TestProjects Media
#>
[CmdletBinding()]
param(
    [switch] $CheckEnvironment,
    [switch] $InstallDependencies,
    [string] $FfmpegRoot,
    [string] $SdlRoot,
    [ValidateSet('osx-arm64', 'osx-x64', 'win-x64')]
    [string] $RuntimeIdentifier,
    [switch] $RunTests,
    [ValidateSet('Core', 'Application', 'SubtitleFormats', 'Rendering', 'Media', 'Cli', 'Desktop', 'Desktop.Ui')]
    [string[]] $TestProjects = @('Core', 'Application', 'SubtitleFormats', 'Rendering', 'Media', 'Cli', 'Desktop'),
    [ValidateRange(1, 128)]
    [int] $Jobs = 2,
    [string] $ReportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$parameters = @{} + $PSBoundParameters
$parameters.Target = 'Workbench'
$parameters.Configuration = 'Debug'
& (Join-Path $PSScriptRoot 'build.ps1') @parameters
exit $LASTEXITCODE
