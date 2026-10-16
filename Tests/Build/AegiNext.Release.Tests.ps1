#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    Import-Module (Join-Path $repository 'scripts/publish/AegiNext.Release.psm1') -Force
}

Describe 'Automatic platform release' {
    It 'creates the native package for <Platform> <Architecture>' -ForEach @(
        @{ Platform = 'Windows'; Architecture = 'X64'; Rid = 'win-x64'; Installer = $true; Dmg = $false }
        @{ Platform = 'Windows'; Architecture = 'Arm64'; Rid = 'win-x64'; Installer = $true; Dmg = $false }
        @{ Platform = 'MacOS'; Architecture = 'Arm64'; Rid = 'osx-arm64'; Installer = $false; Dmg = $true }
        @{ Platform = 'MacOS'; Architecture = 'X64'; Rid = 'osx-x64'; Installer = $false; Dmg = $true }
    ) {
        InModuleScope AegiNext.Release -Parameters @{
            Fixture = $TestDrive; Platform = $Platform; Architecture = $Architecture
            Rid = $Rid; Installer = $Installer; Dmg = $Dmg
        } {
            Mock Get-AegiNextHost { [pscustomobject]@{ Platform = $Platform; Architecture = $Architecture } }
            Mock Invoke-AegiNextPublish {}

            Invoke-AegiNextRelease -RepositoryRoot $Fixture

            Should -Invoke Invoke-AegiNextPublish -Times 1 -Exactly -ParameterFilter {
                $RepositoryRoot -eq $Fixture -and $RuntimeIdentifier -eq $Rid -and
                [bool]$CreateInstaller -eq $Installer -and [bool]$CreateDmg -eq $Dmg -and
                $Configuration -eq 'Release'
            }
        }
    }

    It 'places separate releases under the repository artifacts directory regardless of the working directory' {
        InModuleScope AegiNext.Release -Parameters @{ Fixture = $TestDrive } {
            $root = Join-Path $Fixture "repo `u{4e2d}`u{6587} with spaces"
            [IO.Directory]::CreateDirectory($root) | Out-Null
            $script:releaseOutputs = [Collections.Generic.List[string]]::new()
            Mock Get-AegiNextHost { [pscustomobject]@{ Platform = 'MacOS'; Architecture = 'Arm64' } }
            Mock Invoke-AegiNextPublish { $script:releaseOutputs.Add($OutputDirectory) }

            Push-Location -LiteralPath $Fixture
            try
            {
                Invoke-AegiNextRelease -RepositoryRoot $root
                Invoke-AegiNextRelease -RepositoryRoot $root
            }
            finally { Pop-Location }

            $script:releaseOutputs.Count | Should -Be 2
            $script:releaseOutputs[0] | Should -Not -Be $script:releaseOutputs[1]
            foreach ($output in $script:releaseOutputs)
            {
                [IO.Path]::IsPathFullyQualified($output) | Should -BeTrue
                [IO.Path]::GetRelativePath($root, $output).Replace('\', '/') |
                    Should -Match '^artifacts/releases/osx-arm64/\d{8}-\d{9}Z-[a-f0-9]{8}$'
                Test-Path -LiteralPath $output | Should -BeFalse
            }
        }
    }

    It 'forwards explicit publishing options and uses the requested output directory' {
        InModuleScope AegiNext.Release -Parameters @{ Fixture = $TestDrive } {
            Mock Get-AegiNextHost { [pscustomobject]@{ Platform = 'Windows'; Architecture = 'Arm64' } }
            Mock Invoke-AegiNextPublish {}
            $output = Join-Path $Fixture 'custom package'

            Invoke-AegiNextRelease -RepositoryRoot $Fixture -RuntimeIdentifier win-x64 -Configuration Debug `
                -Tag 'rel/stable/1.2.3.1' -Version 1.2.3.1 -OutputDirectory $output -FfmpegRoot 'sdk/ffmpeg' -SdlRoot 'sdk/sdl' `
                -LicenseDirectory 'notices' -RuntimeDependencyDirectory @('runtime/one', 'runtime/two') `
                -SigningIdentity 'test identity' -SkipBuild -NsisPath 'tools/makensis.exe' -Jobs 4

            Should -Invoke Invoke-AegiNextPublish -Times 1 -Exactly -ParameterFilter {
                $Configuration -eq 'Debug' -and $Tag -eq 'rel/stable/1.2.3.1' -and $Version -eq [version]'1.2.3.1' -and
                $OutputDirectory -eq $output -and $FfmpegRoot -eq 'sdk/ffmpeg' -and $SdlRoot -eq 'sdk/sdl' -and
                $LicenseDirectory -eq 'notices' -and $RuntimeDependencyDirectory.Count -eq 2 -and
                $RuntimeDependencyDirectory[0] -eq 'runtime/one' -and $RuntimeDependencyDirectory[1] -eq 'runtime/two' -and
                $SigningIdentity -eq 'test identity' -and $SkipBuild -and $NsisPath -eq 'tools/makensis.exe' -and $Jobs -eq 4
            }
        }
    }

    It 'translates Force=<Force> into the internal test packaging option' -TestCases @(
        @{ Force = $true }
        @{ Force = $false }
    ) {
        param($Force)
        InModuleScope AegiNext.Release -Parameters @{ Fixture = $TestDrive; Force = $Force } {
            param($Fixture, $Force)
            Mock Get-AegiNextHost { [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64' } }
            Mock Invoke-AegiNextPublish { $script:forwardedReleaseArguments = @{} + $PesterBoundParameters }

            Invoke-AegiNextRelease -RepositoryRoot $Fixture -Tag 'rel/beta/1.2.3.99' -Force:$Force

            $script:forwardedReleaseArguments.Tag | Should -Be 'rel/beta/1.2.3.99'
            $script:forwardedReleaseArguments.ContainsKey('Force') | Should -BeFalse
            $script:forwardedReleaseArguments.ContainsKey('AllowMissingTag') | Should -Be $Force
            if ($Force)
            {
                $script:forwardedReleaseArguments.AllowMissingTag | Should -BeTrue
            }
        }
    }

    It 'rejects unsupported <Platform> hosts before publishing' -ForEach @(@{ Platform = 'Linux' }, @{ Platform = 'Unknown' }) {
        InModuleScope AegiNext.Release -Parameters @{ Fixture = $TestDrive; Platform = $Platform } {
            Mock Get-AegiNextHost { [pscustomobject]@{ Platform = $Platform; Architecture = 'X64' } }
            Mock Invoke-AegiNextPublish {}

            { Invoke-AegiNextRelease -RepositoryRoot $Fixture } | Should -Throw '*Unsupported release host*'
            Should -Invoke Invoke-AegiNextPublish -Times 0 -Exactly
            Test-Path -LiteralPath (Join-Path $Fixture 'artifacts') | Should -BeFalse
        }
    }

    It 'rejects cross-platform targets before publishing' {
        InModuleScope AegiNext.Release -Parameters @{ Fixture = $TestDrive } {
            Mock Get-AegiNextHost { [pscustomobject]@{ Platform = 'MacOS'; Architecture = 'Arm64' } }
            Mock Invoke-AegiNextPublish {}

            { Invoke-AegiNextRelease -RepositoryRoot $Fixture -RuntimeIdentifier win-x64 } |
                Should -Throw '*does not match this build host*'
            Should -Invoke Invoke-AegiNextPublish -Times 0 -Exactly
        }
    }

    It 'does not hide packaging failures' {
        InModuleScope AegiNext.Release -Parameters @{ Fixture = $TestDrive } {
            Mock Get-AegiNextHost { [pscustomobject]@{ Platform = 'MacOS'; Architecture = 'Arm64' } }
            Mock Invoke-AegiNextPublish { throw 'disk image failed' }

            { Invoke-AegiNextRelease -RepositoryRoot $Fixture } | Should -Throw '*disk image failed*'
        }
    }
}

Describe 'Release command line entry point' {
    BeforeEach {
        $entryRoot = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $moduleDirectory = Join-Path $entryRoot 'scripts/publish'
        [IO.Directory]::CreateDirectory($moduleDirectory) | Out-Null
        Copy-Item -LiteralPath (Join-Path $repository 'release.ps1') -Destination $entryRoot
        @'
function Invoke-AegiNextRelease
{
    param($RepositoryRoot, [string] $Tag, [version] $Version, [switch] $Force, [switch] $SkipBuild, $NsisPath, $OutputDirectory)
    if ($NsisPath -eq 'fail') { throw 'release fixture failure' }
    $record = @{} + $PSBoundParameters
    if ($PSBoundParameters.ContainsKey('Version')) { $record.Version = $Version.ToString() }
    if ($PSBoundParameters.ContainsKey('Force')) { $record.Force = [bool]$Force }
    $record.SkipBuild = [bool]$SkipBuild
    $record | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $RepositoryRoot 'arguments.json')
}
Export-ModuleMember -Function Invoke-AegiNextRelease
'@ | Set-Content -LiteralPath (Join-Path $moduleDirectory 'AegiNext.Release.psm1') -Encoding utf8NoBOM
        $script:releasePwsh = (Get-Process -Id $PID).Path
    }

    It 'runs from another directory and forwards the root, version and switches' {
        $null = & $script:releasePwsh -NoProfile -File (Join-Path $entryRoot 'release.ps1') -Tag 'rel/beta/2.3.4.0' -Version 2.3.4.0 -SkipBuild
        $LASTEXITCODE | Should -Be 0
        $arguments = Get-Content -LiteralPath (Join-Path $entryRoot 'arguments.json') -Raw | ConvertFrom-Json
        $arguments.RepositoryRoot | Should -Be $entryRoot
        $arguments.Tag | Should -Be 'rel/beta/2.3.4.0'
        $arguments.Version | Should -Be '2.3.4.0'
        $arguments.SkipBuild | Should -BeTrue
        $arguments.PSObject.Properties.Name | Should -Not -Contain 'Force'
    }

    It 'accepts native <ForceArgument> for a test package with an explicit tag' -TestCases @(
        @{ ForceArgument = '--force' }
        @{ ForceArgument = '-Force' }
    ) {
        param($ForceArgument)
        $output = & $script:releasePwsh -NoProfile -File (Join-Path $entryRoot 'release.ps1') $ForceArgument -Tag 'rel/beta/1.2.3.99' -SkipBuild 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output | Out-String)
        $arguments = Get-Content -LiteralPath (Join-Path $entryRoot 'arguments.json') -Raw | ConvertFrom-Json
        $arguments.Force | Should -BeTrue
        $arguments.Tag | Should -Be 'rel/beta/1.2.3.99'
        $arguments.SkipBuild | Should -BeTrue
    }

    It 'returns a nonzero exit code and reports the packaging error' {
        $PSNativeCommandUseErrorActionPreference = $false
        $output = & $script:releasePwsh -NoProfile -File (Join-Path $entryRoot 'release.ps1') -NsisPath fail 2>&1
        $LASTEXITCODE | Should -Be 1
        ($output | Out-String) | Should -Match 'release fixture failure'
    }
}
