#Requires -Version 7.2

function Assert-AegiNextVersionRequest
{
    param([string] $Tag, [version] $Version)
    if ($null -ne $Version -and ($Version.Revision -lt 0 -or
        @($Version.Major, $Version.Minor, $Version.Build, $Version.Revision | Where-Object { $_ -gt 65534 }).Count))
    {
        throw "Invalid product version '$Version'. Use four numeric components between 0 and 65534."
    }
    if ($Tag -and $Tag -cnotmatch '^rel/(beta|stable)/(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')
    {
        throw "Invalid release tag '$Tag'. Use rel/beta/x.y.z.n or rel/stable/x.y.z.n."
    }
    if ($Tag)
    {
        $tagVersion = $null
        if (![version]::TryParse(($Tag -split '/')[2], [ref]$tagVersion) -or
            @($tagVersion.Major, $tagVersion.Minor, $tagVersion.Build, $tagVersion.Revision | Where-Object { $_ -gt 65534 }).Count)
        {
            throw "Invalid release tag '$Tag'. Components must be between 0 and 65534."
        }
    }
}

function Get-AegiNextBuildVersion
{
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $RepositoryRoot, [string] $Tag, [version] $Version, [switch] $RequireRelease,
        [switch] $AllowMissingTag)
    Assert-AegiNextVersionRequest -Tag $Tag -Version $Version
    if ($AllowMissingTag -and !$Tag) { throw 'Test packaging requires an explicit -Tag.' }
    $properties = @('AegiNextProductVersion', 'AegiNextBaseVersion', 'AegiNextReleaseTag', 'AegiNextReleaseChannel',
        'AegiNextReleaseSequence', 'AegiNextBuildNumber', 'AssemblyVersion', 'FileVersion', 'AegiNextVersionGitSha')
    $arguments = @('msbuild', (Join-Path $PSScriptRoot 'AegiNext.Version.proj'), '-nologo', '-m:1', '-nr:false', '-target:ResolveAegiNextVersion',
        "-p:AegiNextRepositoryRoot=$([IO.Path]::GetFullPath($RepositoryRoot))", "-p:AegiNextVersionRequireRelease=$([bool]$RequireRelease)",
        "-p:AegiNextVersionAllowMissingTag=$([bool]$AllowMissingTag)",
        "-getProperty:$($properties -join ',')")
    if ($Tag) { $arguments += "-p:AegiNextRequestedTag=$Tag" }
    if ($null -ne $Version) { $arguments += "-p:AegiNextRequestedVersion=$Version" }
    $dotnet = Find-AegiNextCommand 'dotnet'
    if (!$dotnet) { throw 'The .NET SDK is required to resolve build versions.' }
    $result = Invoke-AegiNextCommand -FilePath $dotnet -Arguments $arguments -WorkingDirectory $RepositoryRoot
    if ($result.ExitCode -ne 0) { throw "Version resolution failed: $($result.Output)" }
    $resolved = ($result.Output | ConvertFrom-Json).Properties
    return [pscustomobject]@{
        ReleaseTag = $resolved.AegiNextReleaseTag
        NumericVersion = if ($resolved.AegiNextReleaseTag) { ($resolved.AegiNextReleaseTag -split '/')[2] } else { "$($resolved.AegiNextBaseVersion).0" }
        BaseVersion = $resolved.AegiNextBaseVersion
        Channel = $resolved.AegiNextReleaseChannel
        ReleaseSequence = [int]$resolved.AegiNextReleaseSequence
        BuildNumber = [int]$resolved.AegiNextBuildNumber
        ProductVersion = $resolved.AegiNextProductVersion
        AssemblyVersion = $resolved.AssemblyVersion
        FileVersion = $resolved.FileVersion
        GitSha = $resolved.AegiNextVersionGitSha
    }
}

function Get-AegiNextVersionArgument
{
    param([Parameter(Mandatory)][object] $BuildVersion)
    return [string[]]@(
        '-p:AegiNextVersionResolved=true', "-p:AegiNextProductVersion=$($BuildVersion.ProductVersion)",
        "-p:Version=$($BuildVersion.ProductVersion)", "-p:InformationalVersion=$($BuildVersion.ProductVersion)",
        "-p:AssemblyVersion=$($BuildVersion.AssemblyVersion)", "-p:FileVersion=$($BuildVersion.FileVersion)",
        "-p:AegiNextBaseVersion=$($BuildVersion.BaseVersion)", "-p:AegiNextReleaseTag=$($BuildVersion.ReleaseTag)",
        "-p:AegiNextReleaseChannel=$($BuildVersion.Channel)", "-p:AegiNextReleaseSequence=$($BuildVersion.ReleaseSequence)",
        "-p:AegiNextBuildNumber=$($BuildVersion.BuildNumber)", "-p:AegiNextVersionGitSha=$($BuildVersion.GitSha)"
    )
}
