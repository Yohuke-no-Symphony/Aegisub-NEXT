#Requires -Version 7.2

function Get-AegiNextSourceIdentity
{
    param([string] $RepositoryRoot)
    $git = Find-AegiNextCommand 'git'
    if (!$git) { return [pscustomobject]@{ GitSha = $null; WorkingTreeDirty = $null; Status = 'GitUnavailable' } }
    $configuration = @('-c', "safe.directory=$RepositoryRoot")
    $revision = Invoke-AegiNextCommand $git ($configuration + @('rev-parse', '--verify', 'HEAD')) $RepositoryRoot
    $status = Invoke-AegiNextCommand $git ($configuration + @('status', '--porcelain=v1', '--untracked-files=normal')) $RepositoryRoot
    if ($revision.ExitCode -ne 0 -or $status.ExitCode -ne 0 -or $revision.Output.Trim() -notmatch '^[0-9a-fA-F]{40,64}$')
    {
        return [pscustomobject]@{ GitSha = $null; WorkingTreeDirty = $null; Status = 'GitMetadataUnavailable' }
    }
    return [pscustomobject]@{ GitSha = $revision.Output.Trim().ToLowerInvariant(); WorkingTreeDirty = ![string]::IsNullOrWhiteSpace($status.Output); Status = 'Recorded' }
}

function Get-AegiNextPublishedToolVersion
{
    param([string] $Payload, [string] $RuntimeIdentifier)
    $suffix = if ($RuntimeIdentifier -eq 'win-x64') { '.exe' } else { '' }
    foreach ($name in @('ffmpeg', 'ffprobe'))
    {
        $relativePath = "tools/$name$suffix"
        $output = Invoke-AegiNextPublishCommand (Join-Path $Payload $relativePath) @('-version') $Payload
        $line = ($output -split '\r?\n', 2)[0].Trim()
        if ($line -notmatch "^$name version (\S+)(?:\s|$)") { throw "Unexpected packaged $name version output: $line" }
        [pscustomobject]@{ Name = $name; Path = $relativePath; Version = $Matches[1]; VersionLine = $line }
    }
}

function Get-AegiNextPublishedRuntimeFramework
{
    param([string] $Payload)
    foreach ($name in @('aegi-next', 'aegn-exporter', 'aegnc'))
    {
        $filename = "$name.runtimeconfig.json"
        $configuration = Get-Content -LiteralPath (Join-Path $Payload $filename) -Raw | ConvertFrom-Json -AsHashtable
        $options = $configuration.runtimeOptions
        if ($options.ContainsKey('framework') -or $options.ContainsKey('frameworks') -or !$options.ContainsKey('includedFrameworks'))
        {
            throw "$filename is not a self-contained runtime configuration."
        }
        $frameworks = @($options.includedFrameworks | ForEach-Object { [pscustomobject]@{ Name = $_.name; Version = $_.version } })
        if (!@($frameworks | Where-Object { $_.Name -eq 'Microsoft.NETCore.App' -and ([version]$_.Version).Major -eq 10 }).Count)
        {
            throw "$filename does not include the required .NET 10 runtime."
        }
        [pscustomobject]@{ Application = $name; RuntimeConfig = $filename; TargetFramework = $options.tfm; IncludedFrameworks = $frameworks }
    }
}

function Test-AegiNextPublishedCli
{
    param([string] $Payload, [string] $RuntimeIdentifier, [string] $ProductVersion)
    $executable = Join-Path $Payload $(if ($RuntimeIdentifier -eq 'win-x64') { 'aegnc.exe' } else { 'aegnc' })
    $help = Invoke-AegiNextPublishCommand $executable @('--help') $Payload
    if ($help -notmatch '\bconvert\b' -or $help -notmatch '\bexport\b')
    {
        throw 'Packaged aegnc help does not expose convert and export commands.'
    }
    $version = Invoke-AegiNextPublishCommand $executable @('--version') $Payload
    if ($version.Trim() -cne $ProductVersion) { throw "Packaged aegnc version differs from $ProductVersion`: $version" }
    $directory = [IO.Directory]::CreateTempSubdirectory('aeginext-cli-smoke-').FullName
    try
    {
        $inputPath = Join-Path $directory 'smoke.srt'
        $outputPath = Join-Path $directory 'smoke.ass'
        [IO.File]::WriteAllText($inputPath, "1`n00:00:00,000 --> 00:00:01,000`nSmoke subtitle`n`n", [Text.UTF8Encoding]::new($false))
        $null = Invoke-AegiNextPublishCommand $executable @('convert', '-i', $inputPath, '--target', 'ass', '-o', $outputPath, '--accept-loss', '--non-interactive') $Payload
        if (!(Test-Path -LiteralPath $outputPath -PathType Leaf)) { throw 'Packaged aegnc did not create the ASS smoke output.' }
        $text = [IO.File]::ReadAllText($outputPath)
        if ($text -notmatch '\[Events\]' -or $text -notmatch 'Dialogue:' -or $text -notmatch 'Smoke subtitle')
        {
            throw 'Packaged aegnc did not preserve the SRT smoke subtitle in ASS output.'
        }
    }
    finally { Remove-Item -LiteralPath $directory -Recurse -Force }
}

function Get-AegiNextRuntimeOperatingSystemPolicy
{
    param([object] $HostInfo)
    $policy = [ordered]@{
        Platform = $HostInfo.Platform
        BuildHostDescription = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        BuildHostVersion = [Environment]::OSVersion.Version.ToString()
        BuildHostArchitecture = $HostInfo.Architecture
        ProductMinimumOSVerified = $false
        RequiredRuntimePolicy = $null
    }
    if ($HostInfo.Platform -eq 'Windows')
    {
        $policy.RequiredRuntimePolicy = [ordered]@{
            RuntimeFamily = '.NET 10'
            Source = 'https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md'
            PolicyVerifiedOn = '2026-10-04'
            PolicySourceUpdatedOn = '2026-09-28'
            ClientReleases = @('11 26H1', '11 25H2', '11 24H2 (IoT)', '11 24H2 (E)', '11 24H2', '11 23H2 (E)', '10 21H2 (E)', '10 21H2 (IoT)', '10 1809 (E)', '10 1607 (E)')
            LifecycleRequirement = 'Only editions and releases still supported by the OS publisher are supported by .NET. E denotes Enterprise/Education; listed Windows 10 editions are LTSC/Enterprise.'
            TargetArchitecture = 'x64'
            Arm64Execution = 'Windows 11 supports x64 emulation; this package remains x64.'
            ProductValidation = 'Runtime support policy does not establish the minimum OS of the complete native application. Build host metadata is not a test result.'
        }
    }
    return $policy
}
