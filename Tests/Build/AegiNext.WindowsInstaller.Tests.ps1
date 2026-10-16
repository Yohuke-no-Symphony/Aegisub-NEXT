#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    Import-Module (Join-Path $repository 'scripts/publish/AegiNext.Publish.psm1') -Force
}

Describe 'Windows installer publishing boundaries' {
    BeforeEach {
        Mock Get-AegiNextBuildVersion -ModuleName AegiNext.Publish { [pscustomobject]@{ ProductVersion = '0.1.0' } }
    }
    It 'rejects installer creation on macOS before inspecting the build environment' {
        InModuleScope AegiNext.Publish {
            Mock Get-AegiNextHost { [pscustomobject]@{ Platform = 'MacOS'; Architecture = 'Arm64' } }
            Mock Get-AegiNextEnvironment { throw 'Must not inspect the build environment.' }
            { Invoke-AegiNextPublish -RepositoryRoot '.' -CreateInstaller } | Should -Throw '*CreateInstaller is only supported on Windows*'
            Should -Invoke Get-AegiNextEnvironment -Times 0 -Exactly
        }
    }

    It 'requires CreateInstaller when an explicit NSIS path is supplied' {
        InModuleScope AegiNext.Publish {
            { Invoke-AegiNextPublish -RepositoryRoot '.' -NsisPath 'makensis.exe' } | Should -Throw '*NsisPath requires -CreateInstaller*'
        }
    }

    It 'fails NSIS preflight before building or writing a publish directory' {
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive } {
            param($Fixture)
            Mock Get-AegiNextHost { [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64' } }
            Mock Get-AegiNextNsisCompiler { throw 'Missing NSIS compiler fixture.' }
            Mock Get-AegiNextEnvironment { throw 'Must not inspect the build environment.' }
            Mock Invoke-AegiNextBuild { throw 'Must not build.' }
            $output = Join-Path $Fixture 'no partial publish'
            { Invoke-AegiNextPublish -RepositoryRoot '.' -CreateInstaller -OutputDirectory $output } | Should -Throw '*Missing NSIS*'
            Test-Path -LiteralPath $output | Should -BeFalse
            Should -Invoke Get-AegiNextEnvironment -Times 0 -Exactly
            Should -Invoke Invoke-AegiNextBuild -Times 0 -Exactly
        }
    }
}

Describe 'NSIS compiler discovery' {
    It 'reports a missing compiler on PATH and in standard installation directories' {
        InModuleScope AegiNext.Publish {
            Mock Find-AegiNextCommand { $null }
            Mock Test-Path { $false }
            { Get-AegiNextNsisCompiler -RepositoryRoot '.' } | Should -Throw '*Missing NSIS compiler*'
        }
    }

    It 'propagates a failed compiler version query' {
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive } {
            param($Fixture)
            $compiler = Join-Path $Fixture 'broken compiler.exe'
            Set-Content -LiteralPath $compiler -Value 'fixture'
            Mock Invoke-AegiNextPublishCommand { throw 'Fixture NSIS version query failed.' }
            { Get-AegiNextNsisCompiler -RepositoryRoot '.' -NsisPath $compiler } | Should -Throw '*version query failed*'
        }
    }

    It 'validates an explicit compiler path containing spaces' {
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive } {
            param($Fixture)
            $compiler = Join-Path $Fixture 'NSIS tools/makensis.exe'
            [IO.Directory]::CreateDirectory((Split-Path $compiler)) | Out-Null
            Set-Content -LiteralPath $compiler -Value 'compiler fixture'
            Mock Find-AegiNextCommand { throw 'Explicit paths must not fall back to PATH.' }
            Mock Invoke-AegiNextPublishCommand { 'v3.13' }
            $result = Get-AegiNextNsisCompiler -RepositoryRoot $Fixture -NsisPath $compiler
            $result.Path | Should -Be $compiler
            $result.Version | Should -Be '3.13'
            Should -Invoke Invoke-AegiNextPublishCommand -Times 1 -Exactly -ParameterFilter {
                $FilePath -eq $compiler -and $Arguments.Count -eq 1 -and $Arguments[0] -eq '/VERSION'
            }
        }
    }

    It 'rejects a missing explicit compiler without falling back' {
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive } {
            param($Fixture)
            Mock Find-AegiNextCommand { throw 'Must not fall back.' }
            $missing = Join-Path $Fixture 'missing.exe'
            { Get-AegiNextNsisCompiler -RepositoryRoot '.' -NsisPath $missing } | Should -Throw '*Missing NSIS compiler*'
        }
    }

    It 'rejects unsupported or unrecognizable version <Version>' -TestCases @(
        @{ Version = 'v2.51' }
        @{ Version = 'v3.10' }
        @{ Version = 'unexpected output' }
    ) {
        param($Version)
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive; Version = $Version } {
            param($Fixture, $Version)
            $compiler = Join-Path $Fixture 'old-makensis.exe'
            Set-Content -LiteralPath $compiler -Value 'compiler fixture'
            $script:nsisVersionFixture = $Version
            Mock Invoke-AegiNextPublishCommand { $script:nsisVersionFixture }
            { Get-AegiNextNsisCompiler -RepositoryRoot $Fixture -NsisPath $compiler } | Should -Throw '*Requires NSIS 3.11 or newer*'
        }
    }
}

Describe 'NSIS package compilation' {
    It 'rejects an out-of-range Windows file version <FileVersion> before invoking the compiler' -TestCases @(
        @{ FileVersion = '65535.1.0.271' }
        @{ FileVersion = '0.65535.0.271' }
        @{ FileVersion = '0.1.65535.271' }
        @{ FileVersion = '0.1.0.65535' }
    ) {
        param($FileVersion)
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive; Repository = $repository; FileVersion = $FileVersion } {
            param($Fixture, $Repository, $FileVersion)
            $payload = Join-Path $Fixture "invalid version payload $FileVersion"
            [IO.Directory]::CreateDirectory($payload) | Out-Null
            foreach ($name in @('aegi-next.exe', 'aegn-exporter.exe', 'aegnc.exe'))
            {
                Set-Content -LiteralPath (Join-Path $payload $name) -Value 'fixture'
            }
            $installerArguments = @{
                RepositoryRoot = $Repository; PayloadDirectory = $payload; PublishDirectory = $Fixture
                ProductVersion = '0.1.0'; FileVersion = $FileVersion; RuntimeIdentifier = 'win-x64'; NsisCompiler = 'fixture'
            }
            Mock Invoke-AegiNextPublishCommand { throw 'Compiler must not be invoked.' }
            { New-AegiNextWindowsInstaller @installerArguments -WhatIf } |
                Should -Throw '*Invalid Windows file version*'
            Should -Invoke Invoke-AegiNextPublishCommand -Times 0 -Exactly
        }
    }

    It 'requires the CLI executable before invoking the installer compiler' {
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive; Repository = $repository } {
            param($Fixture, $Repository)
            $payload = Join-Path $Fixture 'missing CLI payload'
            [IO.Directory]::CreateDirectory($payload) | Out-Null
            foreach ($name in @('aegi-next.exe', 'aegn-exporter.exe'))
            {
                Set-Content -LiteralPath (Join-Path $payload $name) -Value 'fixture'
            }
            Mock Invoke-AegiNextPublishCommand { throw 'Compiler must not be invoked.' }
            $arguments = @{ RepositoryRoot = $Repository; PayloadDirectory = $payload; PublishDirectory = $Fixture; ProductVersion = '0.1.0'; FileVersion = '0.1.0.271'; RuntimeIdentifier = 'win-x64'; NsisCompiler = 'fixture' }
            { New-AegiNextWindowsInstaller @arguments } |
                Should -Throw '*aegnc.exe*'
            Should -Invoke Invoke-AegiNextPublishCommand -Times 0 -Exactly
        }
    }

    It 'compiles explicit file ownership lists and cleans staging (<Outcome>)' -TestCases @(
        @{ Outcome = 'Success' }
        @{ Outcome = 'CompilerFailure' }
        @{ Outcome = 'MissingOutput' }
        @{ Outcome = 'EmptyOutput' }
    ) {
        param($Outcome)
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive; Repository = $repository; Outcome = $Outcome } {
            param($Fixture, $Repository, $Outcome)
            $publish = Join-Path $Fixture "publish with spaces $Outcome"
            $payload = Join-Path $publish 'AegiNext'
            [IO.Directory]::CreateDirectory((Join-Path $payload 'tools/nested')) | Out-Null
            foreach ($name in @('aegi-next.exe', 'aegn-exporter.exe', 'aegnc.exe', 'tools/nested/dollar$sign.txt', "tools/nested/quote's.txt"))
            {
                Set-Content -LiteralPath (Join-Path $payload $name) -Value 'published fixture'
            }
            Set-Content -LiteralPath (Join-Path $publish 'package-manifest.json') -Value 'must not be installed'
            $script:installerStaging = $null
            $script:installerIncludes = $null
            Mock Invoke-AegiNextPublishCommand {
                param($FilePath, $Arguments)
                $FilePath | Should -Be 'fixture-makensis'
                $Arguments | Should -Contain '/WX'
                $Arguments | Should -Contain '/INPUTCHARSET'
                $Arguments | Should -Contain 'UTF8'
                $configPath = ($Arguments | Where-Object { $_.StartsWith('/DAEGINEXT_CONFIG_FILE=') }).Substring('/DAEGINEXT_CONFIG_FILE='.Length)
                $script:installerStaging = Split-Path $configPath
                $config = Get-Content -LiteralPath $configPath -Raw
                $config | Should -Match 'AEGINEXT_PRODUCT_VERSION "0.1.0-beta.0"'
                $config | Should -Match 'AEGINEXT_FILE_VERSION "0.1.0.271"'
                $includePath = Join-Path $script:installerStaging 'payload.nsh'
                $script:installerIncludes = Get-Content -LiteralPath $includePath -Raw
                if ($Outcome -eq 'CompilerFailure') { throw 'Fixture compiler failure.' }
                if ($Outcome -ne 'MissingOutput')
                {
                    $outputPath = Join-Path $script:installerStaging 'AegiNext-0.1.0-beta.0-win-x64-setup.exe'
                    [IO.File]::WriteAllBytes($outputPath, $(if ($Outcome -eq 'EmptyOutput') { [byte[]]@() } else { [Text.Encoding]::UTF8.GetBytes('installer fixture') }))
                }
                return 'compiled fixture'
            }
            $arguments = @{ RepositoryRoot = $Repository; PayloadDirectory = $payload; PublishDirectory = $publish; ProductVersion = '0.1.0-beta.0'; FileVersion = '0.1.0.271'; RuntimeIdentifier = 'win-x64'; NsisCompiler = 'fixture-makensis' }
            $invoke = { New-AegiNextWindowsInstaller @arguments }
            $installer = Join-Path $publish 'AegiNext-0.1.0-beta.0-win-x64-setup.exe'
            if ($Outcome -eq 'Success')
            {
                & $invoke | Should -Be $installer
                Test-Path -LiteralPath $installer | Should -BeTrue
            }
            else
            {
                $invoke | Should -Throw
                Test-Path -LiteralPath $installer | Should -BeFalse
            }
            Test-Path -LiteralPath $script:installerStaging | Should -BeFalse
            $script:installerIncludes | Should -Match 'Delete "\$INSTDIR\\tools\\nested\\dollar\$\$sign.txt"'
            $script:installerIncludes | Should -Match 'AegiNextCheckFile "\$INSTDIR\\aegn-exporter.exe"'
            $script:installerIncludes | Should -Match 'AegiNextCheckFile "\$INSTDIR\\aegnc.exe"'
            $script:installerIncludes | Should -Not -Match 'package-manifest.json|RMDir /r'
            $script:installerIncludes.IndexOf('RMDir "$INSTDIR\tools\nested"') | Should -BeLessThan $script:installerIncludes.IndexOf('RMDir "$INSTDIR\tools"')
        }
    }

    It 'rejects payload files reserved for installer metadata' {
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive; Repository = $repository } {
            param($Fixture, $Repository)
            $payload = Join-Path $Fixture 'reserved payload'
            [IO.Directory]::CreateDirectory($payload) | Out-Null
            foreach ($name in @('aegi-next.exe', 'aegn-exporter.exe', 'aegnc.exe', 'install-state.ini'))
            {
                Set-Content -LiteralPath (Join-Path $payload $name) -Value 'fixture'
            }
            $arguments = @{ RepositoryRoot = $Repository; PayloadDirectory = $payload; PublishDirectory = $Fixture; ProductVersion = '0.1.0'; FileVersion = '0.1.0.271'; RuntimeIdentifier = 'win-x64'; NsisCompiler = 'fixture' }
            { New-AegiNextWindowsInstaller @arguments } | Should -Throw '*Reserved installer path*'
        }
    }

    It 'rejects control characters in generated NSIS strings' {
        InModuleScope AegiNext.Publish {
            { ConvertTo-AegiNextNsisLiteral "file`nSection injected" } | Should -Throw '*control characters*'
            ConvertTo-AegiNextNsisLiteral 'dollar$sign"quote' | Should -Be 'dollar$$sign$\"quote'
            ConvertTo-AegiNextNsisLiteral 'dollar$sign.txt' -CompileTime | Should -Be 'dollar$sign.txt'
            { ConvertTo-AegiNextNsisLiteral '${AEGINEXT_INJECTED}' -CompileTime } | Should -Throw '*preprocessor references*'
        }
    }
}
