#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $query = Join-Path $repository 'scripts/build/AegiNext.Version.proj'
    $git = (Get-Command git -CommandType Application | Select-Object -First 1).Source
    $dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
    Import-Module (Join-Path $repository 'scripts/build/AegiNext.Build.psm1') -Force

    function Invoke-VersionGit
    {
        param([string] $Root, [string[]] $Arguments)
        $output = & $git -C $Root @Arguments 2>&1
        if ($LASTEXITCODE -ne 0) { throw ($output | Out-String) }
    }

    function New-VersionRepository
    {
        param([string] $Root, [int] $Commits = 2)
        [IO.Directory]::CreateDirectory($Root) | Out-Null
        Invoke-VersionGit $Root @('init', '--quiet')
        for ($index = 0; $index -lt $Commits; $index++)
        {
            Invoke-VersionGit $Root @('-c', 'user.name=Version Tests', '-c', 'user.email=version@example.invalid', 'commit', '--allow-empty', '--quiet', '-m', "Fixture $index")
        }
        return $Root
    }

    function Invoke-VersionQuery
    {
        param([string] $Root, [string] $Tag, [switch] $RequireRelease)
        $arguments = @('msbuild', $query, '-nologo', '-m:1', '-nr:false', '-target:ResolveAegiNextVersion',
            "-p:AegiNextRepositoryRoot=$Root", "-p:AegiNextRequestedTag=$Tag", "-p:AegiNextVersionRequireRelease=$([bool]$RequireRelease)",
            '-getProperty:AegiNextProductVersion,AegiNextBaseVersion,AegiNextReleaseChannel,AegiNextReleaseSequence,AegiNextBuildNumber,AegiNextReleaseTag,AssemblyVersion,FileVersion')
        $output = & $dotnet @arguments 2>&1
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
    }
}

Describe 'Git release version contract' {
    It 'separates product, channel sequence and system build number for <Tag>' -ForEach @(
        @{ Tag = 'rel/beta/1.2.3.999'; Product = '1.2.3-beta.999'; Channel = 'beta'; Sequence = '999' }
        @{ Tag = 'rel/stable/1.2.3.1'; Product = '1.2.3'; Channel = 'stable'; Sequence = '1' }
        @{ Tag = 'rel/beta/0.8.0.0'; Product = '0.8.0-beta.0'; Channel = 'beta'; Sequence = '0' }
    ) {
        $root = New-VersionRepository (Join-Path $TestDrive ([guid]::NewGuid().ToString('N')))
        Invoke-VersionGit $root @('tag', $Tag)
        $result = Invoke-VersionQuery $root -RequireRelease
        $result.ExitCode | Should -Be 0 -Because $result.Output
        $properties = ($result.Output | ConvertFrom-Json).Properties
        $properties.AegiNextProductVersion | Should -Be $Product
        $properties.AegiNextReleaseChannel | Should -Be $Channel
        $properties.AegiNextReleaseSequence | Should -Be $Sequence
        $properties.AegiNextReleaseTag | Should -Be $Tag
        $properties.AegiNextBuildNumber | Should -Be '2'
        $properties.AssemblyVersion | Should -Be "$($properties.AegiNextBaseVersion).0"
        $properties.FileVersion | Should -Be "$($properties.AegiNextBaseVersion).2"
        (Invoke-VersionQuery $root -RequireRelease).Output | Should -Be $result.Output
    }

    It 'requires an explicit tag when HEAD has different release identities' {
        $root = New-VersionRepository (Join-Path $TestDrive 'multiple tags')
        Invoke-VersionGit $root @('tag', 'rel/beta/1.2.3.1')
        Invoke-VersionGit $root @('tag', 'rel/stable/1.2.3.1')
        (Invoke-VersionQuery $root -RequireRelease).ExitCode | Should -Not -Be 0
        $result = Invoke-VersionQuery $root -Tag 'rel/stable/1.2.3.1' -RequireRelease
        $result.ExitCode | Should -Be 0 -Because $result.Output
        ($result.Output | ConvertFrom-Json).Properties.AegiNextReleaseChannel | Should -Be 'stable'
    }

    It 'does not select a tag from another commit' {
        $root = New-VersionRepository (Join-Path $TestDrive 'different commit')
        Invoke-VersionGit $root @('tag', 'rel/beta/1.2.3.1', 'HEAD~1')
        (Invoke-VersionQuery $root -Tag 'rel/beta/1.2.3.1' -RequireRelease).ExitCode | Should -Not -Be 0
    }

    It 'allows untagged development builds but refuses to publish them' {
        $root = New-VersionRepository (Join-Path $TestDrive 'development')
        $result = Invoke-VersionQuery $root
        $result.ExitCode | Should -Be 0 -Because $result.Output
        ($result.Output | ConvertFrom-Json).Properties.AegiNextProductVersion | Should -Be '0.1.0-dev'
        (Invoke-VersionQuery $root -RequireRelease).ExitCode | Should -Not -Be 0
    }

    It 'shares the configured development base with ordinary SDK builds' {
        $root = New-VersionRepository (Join-Path $TestDrive 'configured development')
        Set-Content (Join-Path $root 'Directory.Build.props') '<Project><PropertyGroup><AegiNextDevelopmentBaseVersion>2.3.4</AegiNextDevelopmentBaseVersion></PropertyGroup></Project>'
        $result = Invoke-VersionQuery $root
        $result.ExitCode | Should -Be 0 -Because $result.Output
        ($result.Output | ConvertFrom-Json).Properties.AegiNextProductVersion | Should -Be '2.3.4-dev'
    }

    It 'uses an explicit development identity for source archives' {
        $root = Join-Path $TestDrive 'source archive'
        [IO.Directory]::CreateDirectory($root) | Out-Null
        $result = Invoke-VersionQuery $root
        $result.ExitCode | Should -Be 0 -Because $result.Output
        ($result.Output | ConvertFrom-Json).Properties.AegiNextBuildNumber | Should -Be '0'
        (Invoke-VersionQuery $root -RequireRelease).ExitCode | Should -Not -Be 0
    }

    It 'rejects oversized tag components' {
        $root = New-VersionRepository (Join-Path $TestDrive 'oversized')
        Invoke-VersionGit $root @('tag', 'rel/beta/1.2.3.65535')
        (Invoke-VersionQuery $root -Tag 'rel/beta/1.2.3.65535' -RequireRelease).ExitCode | Should -Not -Be 0
    }

    It 'validates a four-part assertion against the selected channel tag' {
        $root = New-VersionRepository (Join-Path $TestDrive 'PowerShell snapshot')
        Invoke-VersionGit $root @('tag', 'rel/stable/1.2.3.7')
        $snapshot = Get-AegiNextBuildVersion -RepositoryRoot $root -Tag 'rel/stable/1.2.3.7' -Version 1.2.3.7 -RequireRelease
        $snapshot.ProductVersion | Should -Be '1.2.3'
        $snapshot.NumericVersion | Should -Be '1.2.3.7'
        $snapshot.ReleaseSequence | Should -Be 7
        $snapshot.BuildNumber | Should -Be 2
        { Get-AegiNextBuildVersion -RepositoryRoot $root -Tag 'rel/stable/1.2.3.7' -Version 1.2.3.8 -RequireRelease } | Should -Throw
        { Get-AegiNextBuildVersion -RepositoryRoot $root -Version 1.2.3 -RequireRelease } | Should -Throw '*four numeric*'
    }

    It 'rejects noncanonical release tags' {
        $root = New-VersionRepository (Join-Path $TestDrive 'noncanonical')
        Invoke-VersionGit $root @('tag', 'rel/beta/01.2.3.1')
        (Invoke-VersionQuery $root -Tag 'rel/beta/01.2.3.1' -RequireRelease).ExitCode | Should -Not -Be 0
    }

    It 'rejects shallow history rather than using a truncated published build number' {
        $origin = New-VersionRepository (Join-Path $TestDrive 'origin')
        Invoke-VersionGit $origin @('tag', 'rel/beta/1.2.3.1')
        $clone = Join-Path $TestDrive 'shallow'
        $originUri = [UriBuilder]::new([Uri]::UriSchemeFile, '', -1, $origin)
        $output = & $git clone --quiet --depth 1 $originUri.Uri.AbsoluteUri $clone 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output | Out-String)
        (Invoke-VersionQuery $clone -RequireRelease).ExitCode | Should -Not -Be 0
        $result = Invoke-VersionQuery $clone
        $result.ExitCode | Should -Be 0 -Because $result.Output
        ($result.Output | ConvertFrom-Json).Properties.AegiNextBuildNumber | Should -Be '0'
    }

    It 'writes the resolved identity before SDK assembly attributes are generated' {
        $root = New-VersionRepository (Join-Path $TestDrive 'metadata project')
        Invoke-VersionGit $root @('tag', 'rel/stable/1.2.3.7')
        $project = Join-Path $root 'Metadata.csproj'
        $targets = [Security.SecurityElement]::Escape((Join-Path $repository 'scripts/build/AegiNext.Version.targets'))
        Set-Content $project @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion><AegiNextRepositoryRoot>`$(MSBuildProjectDirectory)</AegiNextRepositoryRoot></PropertyGroup>
  <Import Project="$targets" />
</Project>
"@
        Set-Content (Join-Path $root 'nuget.config') '<configuration><packageSources><clear /></packageSources></configuration>'
        Set-Content (Join-Path $root 'Marker.cs') 'public sealed class Marker { }'
        $output = & $dotnet build $project --disable-build-servers -m:1 '-p:UseSharedCompilation=false' --verbosity quiet 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output | Out-String)
        $attributes = Get-Content (Join-Path $root 'obj/Debug/net10.0/Metadata.AssemblyInfo.cs') -Raw
        $attributes | Should -Match 'AssemblyInformationalVersionAttribute\("1.2.3"\)'
        $attributes | Should -Match 'AssemblyFileVersionAttribute\("1.2.3.2"\)'
        $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'bin/Debug/net10.0/Metadata.dll')))
        $metadata = @{}
        foreach ($attribute in $assembly.GetCustomAttributesData())
        {
            if ($attribute.AttributeType.FullName -eq 'System.Reflection.AssemblyMetadataAttribute')
            {
                $metadata[$attribute.ConstructorArguments[0].Value] = $attribute.ConstructorArguments[1].Value
            }
        }
        $metadata.AegiNextReleaseSequence | Should -Be '7'
        $metadata.AegiNextReleaseChannel | Should -Be 'stable'
        $metadata.AegiNextBuildNumber | Should -Be '2'
    }
}
