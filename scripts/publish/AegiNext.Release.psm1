#Requires -Version 7.2
Set-StrictMode -Version Latest
Import-Module ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../build/AegiNext.Build.psm1'))) -Force
Import-Module (Join-Path $PSScriptRoot 'AegiNext.Publish.psm1') -Force

function Invoke-AegiNextRelease
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [ValidateSet('osx-arm64', 'osx-x64', 'win-x64')][string] $RuntimeIdentifier,
        [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
        [string] $Tag,
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

    $hostInfo = Get-AegiNextHost
    if ($hostInfo.Platform -notin @('MacOS', 'Windows'))
    {
        throw "Unsupported release host: $($hostInfo.Platform). Use macOS for DMG or Windows for NSIS installers."
    }

    $rid = Get-AegiNextRuntimeIdentifier -HostInfo $hostInfo -RuntimeIdentifier $RuntimeIdentifier
    $arguments = @{} + $PSBoundParameters
    $arguments.RuntimeIdentifier = $rid
    $arguments.Configuration = $Configuration
    $arguments.CreateInstaller = $hostInfo.Platform -eq 'Windows'
    $arguments.CreateDmg = $hostInfo.Platform -eq 'MacOS'
    if (!$OutputDirectory)
    {
        $releaseId = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmssfff'Z'", [Globalization.CultureInfo]::InvariantCulture) + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
        $arguments.OutputDirectory = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot "artifacts/releases/$rid/$releaseId"))
    }

    Invoke-AegiNextPublish @arguments
}

Export-ModuleMember -Function Invoke-AegiNextRelease
