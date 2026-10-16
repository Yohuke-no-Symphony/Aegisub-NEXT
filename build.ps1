<#
.SYNOPSIS
Checks the selected platform environment and builds AegiNext.
.EXAMPLE
pwsh -NoProfile -File ./build.ps1 -CheckEnvironment
.EXAMPLE
pwsh -NoProfile -File ./build.ps1 -Target Managed -InstallDependencies -RunTests -TestProjects Media
.EXAMPLE
pwsh -NoProfile -File ./build.ps1 -WithMediaTools -CheckEnvironment
.EXAMPLE
pwsh -NoProfile -File ./build.ps1 -Target Decoder -RunTests
.EXAMPLE
pwsh -NoProfile -File ./build.ps1 -Target Workbench -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('All', 'Managed', 'Native', 'Decoder', 'Audio', 'Export', 'Workbench')]
    [string] $Target = 'Managed',
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $CheckEnvironment,
    [switch] $InstallDependencies,
    [switch] $WithMediaTools,
    [string] $FfmpegRoot,
    [string] $SdlRoot,
    [ValidateSet('osx-arm64', 'osx-x64', 'win-x64')]
    [string] $RuntimeIdentifier,
    [switch] $RunTests,
    [ValidateSet('Core', 'Application', 'SubtitleFormats', 'Rendering', 'Media', 'Export', 'Cli', 'Desktop', 'Desktop.Ui')]
    [string[]] $TestProjects = @('Core', 'Application', 'SubtitleFormats', 'Rendering', 'Media', 'Export', 'Cli', 'Desktop'),
    [ValidateRange(1, 128)]
    [int] $Jobs = 2,
    [string] $ReportPath,
    [string] $Tag,
    [version] $Version
)

if ($PSVersionTable.PSVersion -lt [version]'7.2')
{
    [Console]::Error.WriteLine('PowerShell 7.2+ is required. Windows: scoop install main/pwsh; macOS: brew install powershell. Run this script with pwsh.')
    exit 2
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try
{
    Import-Module (Join-Path $PSScriptRoot 'scripts/build/AegiNext.Build.psm1') -Force
    $parameters = @{} + $PSBoundParameters
    $parameters.RepositoryRoot = $PSScriptRoot
    $exitCode = Invoke-AegiNextBuild @parameters
    exit $exitCode
}
catch
{
    [Console]::Error.WriteLine($_.Exception.Message)
    if ($_.Exception.Data.Contains('ExitCode'))
    {
        exit ([int]$_.Exception.Data['ExitCode'])
    }

    exit 1
}
