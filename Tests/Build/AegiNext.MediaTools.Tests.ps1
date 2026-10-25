#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    Import-Module (Join-Path $sourceRoot 'scripts/build/AegiNext.Build.psm1') -Force

    function Get-MediaToolTestOutput
    {
        param([string] $Name, [string] $Version = '9.0.2')

        @"
$Name version $Version Copyright (c) FFmpeg developers
built with a fixture compiler
libavutil      61.  1.102 / 61.  1.102
libavcodec     63.  1.102 / 63.  1.102
libavformat    63.  1.102 / 63.  1.102
libswscale     10.  1.102 / 10.  1.102
libswresample   7.  1.102 /  7.  1.102
"@
    }
}

Describe 'Media tool checks are explicit and follow the shared manifest' {
    BeforeEach {
        $repository = Join-Path $TestDrive 'repository with spaces [media tools]'
        $manifestDirectory = Join-Path $repository 'src/AegiNext.Media/Probing'
        [IO.Directory]::CreateDirectory($manifestDirectory) | Out-Null
        Copy-Item -LiteralPath (Join-Path $sourceRoot 'global.json') -Destination $repository
        $manifestPath = Join-Path $manifestDirectory 'ffmpeg-toolchain.json'
        Set-Content -LiteralPath $manifestPath -Value @'
{
  "version": "9.0.2",
  "acceptedVersionStrings": ["9.0.2", "9.0.2-full_build-www.gyan.dev"],
  "libraries": {
    "libavutil": "61.1.102",
    "libavcodec": "63.1.102",
    "libavformat": "63.1.102",
    "libswscale": "10.1.102",
    "libswresample": "7.1.102"
  }
}
'@
        $hostInfo = [pscustomobject]@{ Platform = 'MacOS'; Architecture = 'Arm64'; ProcessArchitecture = 'Arm64' }
        $script:mediaCommandResults = @{
            'fixture-ffprobe' = [pscustomobject]@{ ExitCode = 0; Output = (Get-MediaToolTestOutput 'ffprobe') }
            'fixture-ffmpeg' = [pscustomobject]@{ ExitCode = 0; Output = (Get-MediaToolTestOutput 'ffmpeg') }
        }
        Mock Get-AegiNextHost -ModuleName AegiNext.Build {
            [pscustomobject]@{ Platform = 'MacOS'; Architecture = 'Arm64'; ProcessArchitecture = 'Arm64' }
        }
        Mock Find-AegiNextCommand -ModuleName AegiNext.Build {
            if ($Name -in @('dotnet', 'ffprobe', 'ffmpeg'))
            {
                return "fixture-$Name"
            }
            return $null
        }
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build {
            if ($FilePath -eq 'fixture-dotnet' -and $Arguments.Count -eq 1 -and $Arguments[0] -eq '--version')
            {
                return [pscustomobject]@{ ExitCode = 0; Output = '10.0.401' }
            }
            if ($script:mediaCommandResults.ContainsKey($FilePath))
            {
                return $script:mediaCommandResults[$FilePath]
            }
            throw "Unexpected tool invocation: $FilePath"
        }
        Mock Install-AegiNextDependency -ModuleName AegiNext.Build { throw 'Installation was not requested.' }
        Mock Get-AegiNextBuildPlan -ModuleName AegiNext.Build { throw 'A build was not requested.' }
    }

    It 'does not read the manifest or locate media tools for ordinary Managed builds' {
        Remove-Item -LiteralPath $manifestPath

        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Managed -HostInfo $hostInfo

        $report.Ready | Should -BeTrue
        $report.WithMediaTools | Should -BeFalse
        Should -Invoke Find-AegiNextCommand -ModuleName AegiNext.Build -Times 0 -Exactly -ParameterFilter {
            $Name -in @('ffprobe', 'ffmpeg')
        }
    }

    It 'checks both selected PATH tools and all pinned libraries on <Platform>' -TestCases @(
        @{ Platform = 'MacOS' }
        @{ Platform = 'Windows' }
        @{ Platform = 'Linux' }
    ) {
        param($Platform)

        $hostInfo.Platform = $Platform
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Managed -HostInfo $hostInfo -WithMediaTools

        $report.Ready | Should -BeTrue
        $report.WithMediaTools | Should -BeTrue
        @($report.Checks | Where-Object { $_.Id -in @('ffprobe', 'ffmpeg') -and $_.Status -eq 'Ready' }).Count | Should -Be 2
        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 2 -Exactly -ParameterFilter {
            $FilePath -in @('fixture-ffprobe', 'fixture-ffmpeg') -and $Arguments.Count -eq 1 -and
            $Arguments[0] -eq '-version' -and $WorkingDirectory -eq $repository
        }
        Should -Invoke Install-AegiNextDependency -ModuleName AegiNext.Build -Times 0 -Exactly
        Test-Path -LiteralPath (Join-Path $repository 'artifacts') | Should -BeFalse
    }

    It 'reports missing tools with the correct installation boundary on <Platform>' -TestCases @(
        @{ Platform = 'MacOS'; ExpectedPackage = 'ffmpeg'; ExpectedManager = 'brew' }
        @{ Platform = 'Windows'; ExpectedPackage = 'main/ffmpeg'; ExpectedManager = 'scoop' }
        @{ Platform = 'Linux'; ExpectedPackage = ''; ExpectedManager = '' }
    ) {
        param($Platform, $ExpectedPackage, $ExpectedManager)

        $hostInfo.Platform = $Platform
        Mock Find-AegiNextCommand -ModuleName AegiNext.Build { return $null } -ParameterFilter { $Name -in @('ffprobe', 'ffmpeg') }

        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Managed -HostInfo $hostInfo -WithMediaTools

        $report.Ready | Should -BeFalse
        $mediaChecks = @($report.Checks | Where-Object { $_.Id -in @('ffprobe', 'ffmpeg') })
        $mediaChecks.Count | Should -Be 2
        foreach ($check in $mediaChecks)
        {
            $check.Status | Should -Be 'Missing'
            $check.Package | Should -Be $ExpectedPackage
            $check.Manager | Should -Be $ExpectedManager
        }
        Should -Invoke Install-AegiNextDependency -ModuleName AegiNext.Build -Times 0 -Exactly
    }

    It 'accepts only explicitly allowed release strings, including the Windows distribution suffix' {
        $hostInfo.Platform = 'Windows'
        $script:mediaCommandResults['fixture-ffprobe'].Output = Get-MediaToolTestOutput 'ffprobe' '9.0.2-full_build-www.gyan.dev'
        $script:mediaCommandResults['fixture-ffmpeg'].Output = (Get-MediaToolTestOutput 'ffmpeg' '9.0.2-full_build-www.gyan.dev') -replace "`n", "`r`n"

        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Managed -HostInfo $hostInfo -WithMediaTools

        $report.Ready | Should -BeTrue
    }

    It 'rejects an unlisted <Version> build for <Tool>' -TestCases @(
        @{ Tool = 'ffprobe'; Version = '9.0.1' }
        @{ Tool = 'ffmpeg'; Version = '9.0.3' }
        @{ Tool = 'ffprobe'; Version = '9.0.2-dev' }
        @{ Tool = 'ffmpeg'; Version = '9.0.2-full_build-unlisted.example' }
    ) {
        param($Tool, $Version)

        $script:mediaCommandResults["fixture-$Tool"].Output = Get-MediaToolTestOutput $Tool $Version

        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Managed -HostInfo $hostInfo -WithMediaTools

        $report.Ready | Should -BeFalse
        $check = $report.Checks | Where-Object Id -eq $Tool
        $check.Status | Should -Be 'Invalid'
        $check.Package | Should -BeNullOrEmpty
        Should -Invoke Install-AegiNextDependency -ModuleName AegiNext.Build -Times 0 -Exactly
    }

    It 'rejects mismatched compiled or runtime <Library> versions' -TestCases @(
        @{ Library = 'libavutil'; Line = 'libavutil 61.2.102 / 61.1.102' }
        @{ Library = 'libavutil'; Line = 'libavutil 61.1.102 / 61.2.102' }
        @{ Library = 'libavcodec'; Line = 'libavcodec 63.2.102 / 63.1.102' }
        @{ Library = 'libavcodec'; Line = 'libavcodec 63.1.102 / 63.2.102' }
        @{ Library = 'libavformat'; Line = 'libavformat 63.2.102 / 63.1.102' }
        @{ Library = 'libavformat'; Line = 'libavformat 63.1.102 / 63.2.102' }
        @{ Library = 'libswscale'; Line = 'libswscale 10.2.102 / 10.1.102' }
        @{ Library = 'libswscale'; Line = 'libswscale 10.1.102 / 10.2.102' }
    ) {
        param($Library, $Line)

        $script:mediaCommandResults['fixture-ffprobe'].Output = (Get-MediaToolTestOutput 'ffprobe') -replace "(?m)^$Library[^\r\n]*", $Line

        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Managed -HostInfo $hostInfo -WithMediaTools

        $report.Ready | Should -BeFalse
        ($report.Checks | Where-Object Id -eq 'ffprobe').Status | Should -Be 'Invalid'
    }

    It 'rejects a missing library version instead of accepting only the program banner' {
        $script:mediaCommandResults['fixture-ffmpeg'].Output = 'ffmpeg version 9.0.2'

        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Managed -HostInfo $hostInfo -WithMediaTools

        $report.Ready | Should -BeFalse
        ($report.Checks | Where-Object Id -eq 'ffmpeg').Status | Should -Be 'Invalid'
    }

    It 'uses the manifest values instead of hard-coded program or library versions' {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $manifest.version = '10.1.2'
        $manifest.acceptedVersionStrings = @('10.1.2')
        $manifest.libraries.libavcodec = '64.3.104'
        $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath
        foreach ($tool in @('ffprobe', 'ffmpeg'))
        {
            $script:mediaCommandResults["fixture-$tool"].Output = (Get-MediaToolTestOutput $tool '10.1.2') -replace '(?m)^libavcodec[^\r\n]*', 'libavcodec 64.3.104 / 64.3.104'
        }

        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Managed -HostInfo $hostInfo -WithMediaTools

        $report.Ready | Should -BeTrue
    }

    It 'rejects nonzero exit even when the output reports all correct versions' {
        $script:mediaCommandResults['fixture-ffprobe'].ExitCode = 7

        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Managed -HostInfo $hostInfo -WithMediaTools

        $report.Ready | Should -BeFalse
        ($report.Checks | Where-Object Id -eq 'ffprobe').Status | Should -Be 'Invalid'
    }

    It 'reports a tool that fails to start and continues checking the other tool' {
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build { throw 'The selected executable could not be started.' } -ParameterFilter { $FilePath -eq 'fixture-ffprobe' }

        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Managed -HostInfo $hostInfo -WithMediaTools

        ($report.Checks | Where-Object Id -eq 'ffprobe').Status | Should -Be 'Invalid'
        ($report.Checks | Where-Object Id -eq 'ffmpeg').Status | Should -Be 'Ready'
    }

    It 'propagates WithMediaTools through diagnostic orchestration without installing or building' {
        Invoke-AegiNextBuild -RepositoryRoot $repository -WithMediaTools -CheckEnvironment | Should -Be 0

        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 2 -Exactly -ParameterFilter {
            $FilePath -in @('fixture-ffprobe', 'fixture-ffmpeg')
        }
        Should -Invoke Get-AegiNextBuildPlan -ModuleName AegiNext.Build -Times 0 -Exactly
        Should -Invoke Install-AegiNextDependency -ModuleName AegiNext.Build -Times 0 -Exactly
    }

    It 'returns an incomplete diagnostic result for missing tools without an implicit install' {
        Mock Find-AegiNextCommand -ModuleName AegiNext.Build { return $null } -ParameterFilter { $Name -in @('ffprobe', 'ffmpeg') }

        Invoke-AegiNextBuild -RepositoryRoot $repository -WithMediaTools -CheckEnvironment | Should -Be 2

        Should -Invoke Get-AegiNextBuildPlan -ModuleName AegiNext.Build -Times 0 -Exactly
        Should -Invoke Install-AegiNextDependency -ModuleName AegiNext.Build -Times 0 -Exactly
    }

    It 'rechecks both tools after explicit installation before generating a build plan' {
        $script:mediaToolsInstalled = $false
        Mock Find-AegiNextCommand -ModuleName AegiNext.Build {
            if ($script:mediaToolsInstalled)
            {
                return "fixture-$Name"
            }
            return $null
        } -ParameterFilter { $Name -in @('ffprobe', 'ffmpeg') }
        Mock Install-AegiNextDependency -ModuleName AegiNext.Build { $script:mediaToolsInstalled = $true }
        Mock Get-AegiNextBuildVersion -ModuleName AegiNext.Build {
            [pscustomobject]@{
                ReleaseTag = ''; NumericVersion = '0.1.0.0'; BaseVersion = '0.1.0'; Channel = 'dev'; ReleaseSequence = 0
                BuildNumber = 2; ProductVersion = '0.1.0-dev'; AssemblyVersion = '0.1.0.0'; FileVersion = '0.1.0.2'; GitSha = 'fixture'
            }
        }
        Mock Get-AegiNextBuildPlan -ModuleName AegiNext.Build { return @() }

        Invoke-AegiNextBuild -RepositoryRoot $repository -WithMediaTools -InstallDependencies | Should -Be 0

        Should -Invoke Install-AegiNextDependency -ModuleName AegiNext.Build -Times 1 -Exactly -ParameterFilter {
            $Report.WithMediaTools -and @($Report.Checks | Where-Object { $_.Id -in @('ffprobe', 'ffmpeg') -and $_.Status -eq 'Missing' }).Count -eq 2
        }
        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 2 -Exactly -ParameterFilter {
            $FilePath -in @('fixture-ffprobe', 'fixture-ffmpeg')
        }
        Should -Invoke Get-AegiNextBuildPlan -ModuleName AegiNext.Build -Times 1 -Exactly
    }

    It 'also checks media tools for the macOS Native target without requiring dotnet' {
        [IO.Directory]::CreateDirectory((Join-Path $repository 'native')) | Out-Null
        Copy-Item -LiteralPath (Join-Path $sourceRoot 'native/CMakeLists.txt') -Destination (Join-Path $repository 'native')
        Copy-Item -LiteralPath (Join-Path $sourceRoot 'native/dependencies.json') -Destination (Join-Path $repository 'native')
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build {
            [pscustomobject]@{ ExitCode = 0; Output = '14.0' }
        } -ParameterFilter { $FilePath -eq '/usr/bin/sw_vers' }

        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Native -HostInfo $hostInfo -WithMediaTools

        @($report.Checks | Where-Object { $_.Id -in @('ffprobe', 'ffmpeg') -and $_.Status -eq 'Ready' }).Count | Should -Be 2
        @($report.Checks | Where-Object Status -eq 'Unsupported').Count | Should -Be 0
        Should -Invoke Find-AegiNextCommand -ModuleName AegiNext.Build -Times 0 -Exactly -ParameterFilter { $Name -eq 'dotnet' }
    }

    It 'does not enable unsupported native targets when media tools are requested' {
        Mock Get-AegiNextHost -ModuleName AegiNext.Build {
            [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64'; ProcessArchitecture = 'X64' }
        }

        Invoke-AegiNextBuild -RepositoryRoot $repository -Target Native -WithMediaTools -InstallDependencies | Should -Be 2

        Should -Invoke Get-AegiNextBuildPlan -ModuleName AegiNext.Build -Times 0 -Exactly
        Should -Invoke Install-AegiNextDependency -ModuleName AegiNext.Build -Times 0 -Exactly
    }
}

Describe 'Media package installation preserves the existing missing-only policy' {
    BeforeEach {
        Mock Find-AegiNextCommand -ModuleName AegiNext.Build { return "fixture-$Name" }
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build { [pscustomobject]@{ ExitCode = 0; Output = '' } }
    }

    It 'installs one <Package> package for two missing media executables' -TestCases @(
        @{ Manager = 'brew'; Package = 'ffmpeg' }
        @{ Manager = 'scoop'; Package = 'main/ffmpeg' }
    ) {
        param($Manager, $Package)

        $report = [pscustomobject]@{
            Checks = @(
                [pscustomobject]@{ Id = 'ffprobe'; Status = 'Missing'; Package = $Package; Manager = $Manager }
                [pscustomobject]@{ Id = 'ffmpeg'; Status = 'Missing'; Package = $Package; Manager = $Manager }
            )
        }

        Install-AegiNextDependency -Report $report -RepositoryRoot $TestDrive

        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 1 -Exactly -ParameterFilter {
            $FilePath -eq "fixture-$Manager" -and $Arguments.Count -eq 2 -and $Arguments[0] -eq 'install' -and $Arguments[1] -eq $Package
        }
    }

    It 'never upgrades or downgrades existing incompatible media tools' {
        $report = [pscustomobject]@{
            Checks = @(
                [pscustomobject]@{ Id = 'ffprobe'; Status = 'Invalid'; Package = 'ffmpeg'; Manager = 'brew' }
                [pscustomobject]@{ Id = 'ffmpeg'; Status = 'Invalid'; Package = 'ffmpeg'; Manager = 'brew' }
            )
        }

        Install-AegiNextDependency -Report $report -RepositoryRoot $TestDrive

        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 0 -Exactly
    }
}
