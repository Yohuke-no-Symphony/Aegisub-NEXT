#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    Import-Module (Join-Path $sourceRoot 'scripts/build/AegiNext.Build.psm1') -Force
}

Describe 'Managed MSVC runtime SDK' {
    It 'identifies a PE runtime machine <Machine> with optional header <OptionalMagic> as <Expected>' -TestCases @(
        @{ Machine = 0x8664; OptionalMagic = 0x20b; Expected = 'x64' }
        @{ Machine = 0x014c; OptionalMagic = 0x10b; Expected = 'PE-332' }
        @{ Machine = 0xaa64; OptionalMagic = 0x20b; Expected = 'PE-43620' }
        @{ Machine = 0x8664; OptionalMagic = 0x10b; Expected = 'PE-34404' }
    ) {
        param($Machine, $OptionalMagic, $Expected)
        InModuleScope AegiNext.Build -Parameters @{ Root = $TestDrive; Machine = $Machine; OptionalMagic = $OptionalMagic; Expected = $Expected } {
            param($Root, $Machine, $OptionalMagic, $Expected)
            $bytes = [byte[]]::new(128)
            $writer = [IO.BinaryWriter]::new([IO.MemoryStream]::new($bytes, $true))
            try
            {
                $writer.Write([uint16]0x5a4d)
                $writer.BaseStream.Position = 60
                $writer.Write([uint32]64)
                $writer.Write([uint32]0x00004550)
                $writer.Write([uint16]$Machine)
                $writer.BaseStream.Position = 88
                $writer.Write([uint16]$OptionalMagic)
            }
            finally
            {
                $writer.Dispose()
            }
            $path = Join-Path $Root 'runtime.dll'
            [IO.File]::WriteAllBytes($path, $bytes)
            (Get-AegiNextMsvcRuntimeBinaryInfo $path).Architecture | Should -Be $Expected
            [IO.File]::WriteAllBytes($path, [byte[]]::new(10))
            { Get-AegiNextMsvcRuntimeBinaryInfo $path } | Should -Throw '*Invalid Windows runtime PE*'
        }
    }

    It 'isolates the locked x64 runtime even on a Windows ARM64 host' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $TestDrive } {
            param($Repository)
            $manifest = Get-AegiNextDependencyManifest
            $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'Arm64' }
            @(Get-AegiNextSdkInstallOrder $manifest @('msvc-runtime') 'Windows') | Should -Be @('msvc-runtime')
            Get-AegiNextProjectSdkRoot $Repository $hostInfo 'msvc-runtime' |
                Should -Be (Join-Path $Repository '.dependencies/win-x64/msvc-runtime/14.51.36247')
            $source = $manifest.sources[$manifest.packages['msvc-runtime'].windows.source]
            $source.url | Should -Match '^https://download\.visualstudio\.microsoft\.com/'
            $source.sha256 | Should -Match '^[a-f0-9]{64}$'
            $originalMacFingerprint = Get-AegiNextSdkFingerprint $manifest 'ffmpeg' 'MacOS'
            $originalRuntimeFingerprint = Get-AegiNextSdkFingerprint $manifest 'msvc-runtime' 'Windows'
            $source.sha256 = 'a' * 64
            Get-AegiNextSdkFingerprint $manifest 'msvc-runtime' 'Windows' | Should -Not -Be $originalRuntimeFingerprint
            Get-AegiNextSdkFingerprint $manifest 'ffmpeg' 'MacOS' | Should -Be $originalMacFingerprint
        }
    }

    It 'rejects a Windows runtime request on macOS <Architecture> before tool installation' -TestCases @(
        @{ Architecture = 'Arm64' }
        @{ Architecture = 'X64' }
    ) {
        param($Architecture)
        InModuleScope AegiNext.Build -Parameters @{ Repository = $TestDrive; Architecture = $Architecture } {
            param($Repository, $Architecture)
            $hostInfo = [pscustomobject]@{ Platform = 'MacOS'; Architecture = $Architecture }
            Mock Install-AegiNextSdkTool { throw 'macOS must not prepare Windows CRT tools.' }
            Mock Get-AegiNextSdkArchive { throw 'macOS must not download Windows CRT.' }
            { Install-AegiNextProjectSdk $Repository $hostInfo @('msvc-runtime') } | Should -Throw '*does not support MacOS*'
            Should -Invoke Install-AegiNextSdkTool -Times 0 -Exactly
            Should -Invoke Get-AegiNextSdkArchive -Times 0 -Exactly
            Test-Path -LiteralPath (Join-Path $Repository '.dependencies') | Should -BeFalse
        }
    }

    It 'extracts only desktop x64 DLLs and retains notices beside the managed runtime' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $TestDrive } {
            param($Repository)
            $manifest = Get-AegiNextDependencyManifest
            $package = $manifest.packages['msvc-runtime']
            $archiveDirectory = Join-Path $Repository 'upstream'
            $selected = Join-Path $archiveDirectory $package.windows.archiveRoot
            [IO.Directory]::CreateDirectory($selected) | Out-Null
            foreach ($name in @('vcruntime140.dll', 'msvcp140.dll'))
            {
                Set-Content -LiteralPath (Join-Path $selected $name) -Value "desktop x64 $name"
            }
            $other = Join-Path $archiveDirectory 'Contents/VC/Redist/MSVC/14.51.36231/x86/Microsoft.VC145.CRT'
            [IO.Directory]::CreateDirectory($other) | Out-Null
            Set-Content -LiteralPath (Join-Path $other 'vcruntime140.dll') -Value 'wrong architecture'
            $archive = Join-Path $Repository 'runtime [archive].zip'
            [IO.Compression.ZipFile]::CreateFromDirectory($archiveDirectory, $archive)
            $script:msvcFixtureArchive = $archive
            Mock Get-AegiNextSdkArchive { $script:msvcFixtureArchive }
            $stage = Join-Path $Repository 'stage'
            [IO.Directory]::CreateDirectory($stage) | Out-Null

            Invoke-AegiNextSdkRecipe $Repository ([pscustomobject]@{ Platform = 'Windows' }) $manifest 'msvc-runtime' (Join-Path $Repository 'work') $stage 2

            (Get-Content -LiteralPath (Join-Path $stage 'bin/vcruntime140.dll') -Raw).Trim() | Should -Be 'desktop x64 vcruntime140.dll'
            @(Get-ChildItem -LiteralPath (Join-Path $stage 'bin') -File).Count | Should -Be 2
            foreach ($name in @('LICENSE', 'NOTICE'))
            {
                Test-Path -LiteralPath (Join-Path $stage "share/licenses/msvc-runtime/$name-Microsoft-Visual-Cpp-Runtime.txt") | Should -BeTrue
            }
            Test-Path -LiteralPath (Join-Path $stage 'Contents') | Should -BeFalse
        }
    }

    It 'requires matching architecture, locked file versions and notices before accepting a runtime' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $TestDrive } {
            param($Repository)
            $manifest = Get-AegiNextDependencyManifest
            $root = Join-Path $Repository 'runtime'
            $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64' }
            foreach ($relative in $manifest.packages['msvc-runtime'].windows.requiredFiles)
            {
                $path = Join-Path $root $relative
                [IO.Directory]::CreateDirectory((Split-Path $path)) | Out-Null
                Set-Content -LiteralPath $path -Value 'runtime fixture'
            }
            Mock Get-AegiNextMsvcRuntimeBinaryInfo { [pscustomobject]@{ Architecture = 'x64'; Version = '14.51.36247.0' } }
            { Test-AegiNextPreparedSdk $root $Repository $hostInfo $manifest 'msvc-runtime' } | Should -Not -Throw
            Mock Get-AegiNextMsvcRuntimeBinaryInfo { [pscustomobject]@{ Architecture = 'PE-332'; Version = '14.51.36247.0' } }
            { Test-AegiNextPreparedSdk $root $Repository $hostInfo $manifest 'msvc-runtime' } | Should -Throw '*requires x64 version*'
            Mock Get-AegiNextMsvcRuntimeBinaryInfo { [pscustomobject]@{ Architecture = 'x64'; Version = '14.50.0.0' } }
            { Test-AegiNextPreparedSdk $root $Repository $hostInfo $manifest 'msvc-runtime' } | Should -Throw '*requires x64 version*'
            Mock Get-AegiNextMsvcRuntimeBinaryInfo { [pscustomobject]@{ Architecture = 'x64'; Version = '14.51.36247.0' } }
            Remove-Item -LiteralPath (Join-Path $root 'share/licenses/msvc-runtime/LICENSE-Microsoft-Visual-Cpp-Runtime.txt')
            { Test-AegiNextPreparedSdk $root $Repository $hostInfo $manifest 'msvc-runtime' } | Should -Throw '*missing*LICENSE*'
        }
    }
}

Describe 'Runtime discovery preserves selected sources' {
    BeforeEach {
        $runtimeRepository = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
    }

    It 'prefers a verified project SDK and refuses corrupt receipts without falling back to Visual Studio' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $runtimeRepository } {
            param($Repository)
            $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64' }
            Mock Install-AegiNextSdkTool {}
            Mock Invoke-AegiNextSdkRecipe {
                foreach ($relative in $Manifest.packages['msvc-runtime'].windows.requiredFiles)
                {
                    $path = Join-Path $Stage $relative
                    [IO.Directory]::CreateDirectory((Split-Path $path)) | Out-Null
                    Set-Content -LiteralPath $path -Value "runtime fixture $relative"
                }
            }
            Mock Get-AegiNextMsvcRuntimeBinaryInfo { [pscustomobject]@{ Architecture = 'x64'; Version = '14.51.36247.0' } }
            Install-AegiNextProjectSdk $Repository $hostInfo @('msvc-runtime')
            Mock Find-AegiNextCommand { throw 'A selected project SDK must not search installed applications.' }

            $runtime = Get-AegiNextMsvcRuntime -RepositoryRoot $Repository
            $runtime.Source | Should -Be 'ProjectSdk'
            $root = Get-AegiNextProjectSdkRoot $Repository $hostInfo 'msvc-runtime'
            $runtime.RuntimeDirectory | Should -Be (Join-Path $root 'bin')
            $runtime.LicenseRoot | Should -Be $root
            $runtime.Version | Should -Be '14.51.36247.0'
            Set-Content -LiteralPath (Join-Path $root 'bin/vcruntime140.dll') -Value 'corrupt runtime'
            { Get-AegiNextMsvcRuntime -RepositoryRoot $Repository } | Should -Throw
            Should -Invoke Find-AegiNextCommand -Times 0 -Exactly
        }
    }

    It 'honors an <SearchMode> explicit runtime and rejects its wrong architecture without probing other sources' -TestCases @(
        @{ SearchMode = 'Absolute' }
        @{ SearchMode = 'Relative' }
    ) {
        param($SearchMode)
        InModuleScope AegiNext.Build -Parameters @{ Repository = $runtimeRepository; SearchMode = $SearchMode } {
            param($Repository, $SearchMode)
            $directory = Join-Path $Repository 'custom runtime'
            [IO.Directory]::CreateDirectory($directory) | Out-Null
            Set-Content -LiteralPath (Join-Path $directory 'vcruntime140.dll') -Value 'explicit fixture'
            Mock Get-AegiNextMsvcRuntimeBinaryInfo { [pscustomobject]@{ Architecture = 'x64'; Version = '14.51.36247.0' } }
            Mock Find-AegiNextCommand { throw 'Explicit runtime selection must not search installed applications.' }

            $selectedDirectory = $directory
            $locationChanged = $false
            try
            {
                if ($SearchMode -eq 'Relative')
                {
                    Push-Location -LiteralPath $Repository
                    $locationChanged = $true
                    $selectedDirectory = [IO.Path]::GetRelativePath($Repository, $directory)
                    [IO.Path]::IsPathFullyQualified($selectedDirectory) | Should -BeFalse
                }
                $runtime = Get-AegiNextMsvcRuntime -RepositoryRoot $Repository -RuntimeDependencyDirectory @($selectedDirectory)
                $runtime.Source | Should -Be 'Explicit'
                $runtime.RuntimeDirectory | Should -Be $directory
                $runtime.LicenseRoot | Should -Be $directory
                Mock Get-AegiNextMsvcRuntimeBinaryInfo { [pscustomobject]@{ Architecture = 'PE-332'; Version = '14.51.36247.0' } }
                { Get-AegiNextMsvcRuntime -RepositoryRoot $Repository -RuntimeDependencyDirectory @($selectedDirectory) } | Should -Throw
                Should -Invoke Find-AegiNextCommand -Times 0 -Exactly
            }
            finally
            {
                if ($locationChanged)
                {
                    Pop-Location
                }
            }
        }
    }

    It 'discovers only release x64 CRT redistributables from Visual Studio and ignores unrelated explicit roots' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $runtimeRepository } {
            param($Repository)
            $installation = Join-Path $Repository 'Visual Studio'
            $redist = Join-Path $installation 'VC/Redist/MSVC/14.51.36231'
            $expected = Join-Path $redist 'x64/Microsoft.VC145.CRT'
            foreach ($relative in @('x64/Microsoft.VC145.CRT', 'x86/Microsoft.VC145.CRT', 'onecore/x64/Microsoft.VC145.CRT', 'debug_nonredist/x64/Microsoft.VC145.DebugCRT'))
            {
                $directory = Join-Path $redist $relative
                [IO.Directory]::CreateDirectory($directory) | Out-Null
                Set-Content -LiteralPath (Join-Path $directory 'vcruntime140.dll') -Value 'fixture runtime'
            }
            Mock Find-AegiNextCommand { 'fixture-vswhere' }
            Mock Invoke-AegiNextCommand { [pscustomobject]@{ ExitCode = 0; Output = (@(@{ installationPath = $installation }) | ConvertTo-Json -AsArray) } }
            Mock Get-AegiNextMsvcRuntimeBinaryInfo { [pscustomobject]@{ Architecture = 'x64'; Version = '14.51.36247.0' } }
            $previous = $env:VCToolsRedistDir
            try
            {
                $env:VCToolsRedistDir = $null
                $runtime = Get-AegiNextMsvcRuntime -RepositoryRoot $Repository -RuntimeDependencyDirectory @((Join-Path $Repository 'unrelated'))
                $runtime.RuntimeDirectory | Should -Be $expected
                $runtime.Source | Should -Be 'VisualStudio'
                $runtime.LicenseRoot | Should -Not -BeNullOrEmpty
                Should -Invoke Get-AegiNextMsvcRuntimeBinaryInfo -Times 1 -Exactly
                Mock Get-AegiNextMsvcRuntimeBinaryInfo { [pscustomobject]@{ Architecture = 'PE-332'; Version = '14.51.36247.0' } }
                Get-AegiNextMsvcRuntime -RepositoryRoot $Repository | Should -BeNullOrEmpty
            }
            finally
            {
                $env:VCToolsRedistDir = $previous
            }
        }
    }
}

Describe 'Runtime dependency checks are platform specific' {
    BeforeEach {
        $repository = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        [IO.Directory]::CreateDirectory($repository) | Out-Null
        Copy-Item -LiteralPath (Join-Path $sourceRoot 'global.json') -Destination $repository
        Mock Find-AegiNextCommand -ModuleName AegiNext.Build { $null }
        Mock Find-AegiNextCommand -ModuleName AegiNext.Build { 'fixture-dotnet' } -ParameterFilter { $Name -eq 'dotnet' }
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build { [pscustomobject]@{ ExitCode = 0; Output = '10.0.402' } }
        Mock Get-AegiNextDecoderEnvironment -ModuleName AegiNext.Build { [pscustomobject]@{ Checks = @(); NativePrefixes = @{} } }
        Mock Get-AegiNextSdlEnvironment -ModuleName AegiNext.Build { [pscustomobject]@{ Check = [pscustomobject]@{ Id = 'SdlSdk'; Status = 'Ready'; Detail = ''; Package = ''; Manager = '' }; Root = $null } }
        Mock Get-AegiNextSdkArchitectureCheck -ModuleName AegiNext.Build { [pscustomobject]@{ Id = 'SdkArchitecture'; Status = 'Ready'; Detail = ''; Package = ''; Manager = '' } }
        Mock Get-AegiNextMsvcRuntime -ModuleName AegiNext.Build { $null }
    }

    It 'reports a missing runtime for Windows <Target> and routes installation to the project SDK' -TestCases @(
        @{ Target = 'Managed' }
        @{ Target = 'Workbench' }
    ) {
        param($Target)
        $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'Arm64'; ProcessArchitecture = 'Arm64' }
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target $Target -HostInfo $hostInfo
        $check = @($report.Checks | Where-Object Id -eq 'MsvcRuntime')
        $check.Count | Should -Be 1
        $check[0].Status | Should -Be 'Missing'
        $check[0].Package | Should -Be 'msvc-runtime'
        $check[0].Manager | Should -Be 'project'
        $report.Ready | Should -BeFalse
        Mock Install-AegiNextProjectSdk -ModuleName AegiNext.Build {}
        Install-AegiNextDependency -Report $report -RepositoryRoot $repository
        Should -Invoke Install-AegiNextProjectSdk -ModuleName AegiNext.Build -Times 1 -Exactly -ParameterFilter { 'msvc-runtime' -in $Names }
    }

    It 'reports a corrupt runtime as repairable and publishes the selected runtime prefix when ready' {
        $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64'; ProcessArchitecture = 'X64' }
        Mock Get-AegiNextMsvcRuntime -ModuleName AegiNext.Build { throw 'Corrupt runtime receipt.' }
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Managed -HostInfo $hostInfo
        $check = @($report.Checks | Where-Object Id -eq 'MsvcRuntime')[0]
        $check.Status | Should -Be 'Invalid'
        $check.Package | Should -Be 'msvc-runtime'
        $check.Manager | Should -Be 'project'
        $runtimeDirectory = Join-Path $repository 'runtime/bin'
        Mock Get-AegiNextMsvcRuntime -ModuleName AegiNext.Build { [pscustomobject]@{ RuntimeDirectory = $runtimeDirectory; LicenseRoot = $repository; Version = '14.51.36247.0'; Source = 'ProjectSdk' } }
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Managed -HostInfo $hostInfo
        @($report.Checks | Where-Object Id -eq 'MsvcRuntime')[0].Status | Should -Be 'Ready'
        $report.NativePrefixes['msvc-runtime'] | Should -Be $runtimeDirectory
        $report.Ready | Should -BeTrue
    }

    It 'does not check or install Windows CRT for macOS <Architecture> <Target>' -TestCases @(
        @{ Architecture = 'Arm64'; Target = 'Managed' }
        @{ Architecture = 'X64'; Target = 'Managed' }
        @{ Architecture = 'Arm64'; Target = 'Workbench' }
        @{ Architecture = 'X64'; Target = 'Workbench' }
    ) {
        param($Architecture, $Target)
        $hostInfo = [pscustomobject]@{ Platform = 'MacOS'; Architecture = $Architecture; ProcessArchitecture = $Architecture }
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target $Target -HostInfo $hostInfo
        @($report.Checks | Where-Object Id -eq 'MsvcRuntime').Count | Should -Be 0
        Should -Invoke Get-AegiNextMsvcRuntime -ModuleName AegiNext.Build -Times 0 -Exactly
        Mock Install-AegiNextProjectSdk -ModuleName AegiNext.Build {}
        Install-AegiNextDependency -Report $report -RepositoryRoot $repository
        Should -Invoke Install-AegiNextProjectSdk -ModuleName AegiNext.Build -Times 0 -Exactly -ParameterFilter { 'msvc-runtime' -in $Names }
    }
}
