#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    Import-Module (Join-Path $sourceRoot 'scripts/build/AegiNext.Build.psm1') -Force
}

Describe 'Locked dependency manifest' {
    It 'matches the media and rendering version contracts and locks every source' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $sourceRoot } {
            param($Repository)
            $manifest = Get-AegiNextDependencyManifest
            $ffmpeg = Get-Content (Join-Path $Repository 'src/AegiNext.Media/Probing/ffmpeg-toolchain.json') -Raw | ConvertFrom-Json
            $render = Get-Content (Join-Path $Repository 'native/dependencies.json') -Raw | ConvertFrom-Json
            $sdl = Get-Content (Join-Path $Repository 'scripts/build/scoop/aeginext-sdl3.json') -Raw | ConvertFrom-Json
            $manifest.packages.ffmpeg.version | Should -Be $ffmpeg.version
            $manifest.packages.sdl3.version | Should -Be $sdl.version
            $manifest.packages.libplacebo.version | Should -Be $render.directDependencies.libplacebo.version
            $manifest.packages['molten-vk'].version | Should -Be $render.directDependencies.MoltenVK.version
            $manifest.packages['vulkan-headers'].version | Should -Be $render.directDependencies.'Vulkan-Headers'.version
            foreach ($source in $manifest.sources.Values)
            {
                $source.url | Should -Match '^https://'
                $source.sha256 | Should -Match '^[a-f0-9]{64}$'
            }
        }
    }

    It 'resolves macOS dependencies first while Windows uses complete binary SDKs' {
        InModuleScope AegiNext.Build {
            $manifest = Get-AegiNextDependencyManifest
            $order = @(Get-AegiNextSdkInstallOrder $manifest @('ffmpeg', 'libplacebo', 'molten-vk', 'sdl3') 'MacOS')
            [array]::IndexOf($order, 'x264') | Should -BeLessThan ([array]::IndexOf($order, 'ffmpeg'))
            [array]::IndexOf($order, 'dav1d') | Should -BeGreaterOrEqual 0
            [array]::IndexOf($order, 'dav1d') | Should -BeLessThan ([array]::IndexOf($order, 'ffmpeg'))
            foreach ($encoder in @('libvpx', 'svt-av1', 'opus'))
            {
                [array]::IndexOf($order, $encoder) | Should -BeGreaterOrEqual 0
                [array]::IndexOf($order, $encoder) | Should -BeLessThan ([array]::IndexOf($order, 'ffmpeg'))
            }
            [array]::IndexOf($order, 'shaderc') | Should -BeLessThan ([array]::IndexOf($order, 'libplacebo'))
            [array]::IndexOf($order, 'vulkan-headers') | Should -BeLessThan ([array]::IndexOf($order, 'vulkan-loader'))
            @($order | Select-Object -Unique).Count | Should -Be $order.Count
            @(Get-AegiNextSdkInstallOrder $manifest @('ffmpeg', 'sdl3') 'Windows') | Should -Be @('ffmpeg', 'sdl3')
            { Get-AegiNextSdkInstallOrder $manifest @('libplacebo') 'Windows' } | Should -Throw '*does not support Windows*'
            $manifest.packages.ffmpeg.windows.source | Should -Be 'ffmpeg-windows'
            $manifest.packages.sdl3.windows.source | Should -Be 'sdl3-windows'
        }
    }

    It 'locks the software AV1 decoder independently and invalidates FFmpeg when its source changes' {
        InModuleScope AegiNext.Build {
            $manifest = Get-AegiNextDependencyManifest
            $manifest.packages.dav1d.version | Should -Be '1.5.4'
            $manifest.packages.dav1d.recipe | Should -Be 'dav1d'
            $manifest.sources.dav1d.url | Should -Be 'https://download.videolan.org/pub/videolan/dav1d/1.5.4/dav1d-1.5.4.tar.xz'
            $manifest.sources.dav1d.sha256 | Should -Be '686616b7c69eb88d44459391ab25cac13b6647a3b288835c5784e71c1514a5c5'
            $manifest.packages.ffmpeg.configureOptions | Should -Contain '--enable-libdav1d'
            $manifest.packages.dav1d.requiredFiles | Should -Contain 'lib/pkgconfig/dav1d.pc'
            $first = Get-AegiNextSdkFingerprint $manifest 'ffmpeg' 'MacOS'
            $manifest.sources.dav1d.sha256 = 'a' * 64
            Get-AegiNextSdkFingerprint $manifest 'ffmpeg' 'MacOS' | Should -Not -Be $first
        }
    }

    It 'rejects dependency cycles and fingerprints dependency source changes deterministically' {
        InModuleScope AegiNext.Build {
            $manifest = Get-AegiNextDependencyManifest
            $first = Get-AegiNextSdkFingerprint $manifest 'ffmpeg' 'MacOS'
            $source = $manifest.sources.x264
            $manifest.sources.x264 = @{ archive = $source.archive; sha256 = $source.sha256; url = $source.url }
            Get-AegiNextSdkFingerprint $manifest 'ffmpeg' 'MacOS' | Should -Be $first
            $manifest.sources.x264.sha256 = 'a' * 64
            Get-AegiNextSdkFingerprint $manifest 'ffmpeg' 'MacOS' | Should -Not -Be $first
            $manifest.packages.x264.dependencies = @('ffmpeg')
            { Get-AegiNextSdkInstallOrder $manifest @('ffmpeg') 'MacOS' } | Should -Throw '*cyclic*'
        }
    }

    It 'invalidates FFmpeg when any new export library source changes and requires upstream notices' {
        InModuleScope AegiNext.Build {
            foreach ($name in @('libvpx', 'svt-av1', 'opus'))
            {
                $manifest = Get-AegiNextDependencyManifest
                $first = Get-AegiNextSdkFingerprint $manifest 'ffmpeg' 'MacOS'
                $manifest.sources[$name].sha256 = 'a' * 64
                Get-AegiNextSdkFingerprint $manifest 'ffmpeg' 'MacOS' | Should -Not -Be $first
                @($manifest.packages[$name].requiredFiles | Where-Object { $_ -like 'share/licenses/*' }).Count | Should -BeGreaterThan 0
            }
            $manifest = Get-AegiNextDependencyManifest
            $manifest.packages.libvpx.requiredFiles | Should -Contain 'share/licenses/libvpx/PATENTS'
            $manifest.packages['svt-av1'].requiredFiles | Should -Contain 'share/licenses/svt-av1/PATENTS.md'
        }
    }
}

Describe 'Verified SDK downloads' {
    It 'reuses only checksum-matching archives and rejects bad downloads before extraction' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $TestDrive } {
            param($Repository)
            $content = [Text.Encoding]::UTF8.GetBytes('verified archive')
            $sha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($content)).ToLowerInvariant()
            $source = @{ url = 'https://example.invalid/sdk.tar.gz'; sha256 = $sha; archive = 'sdk.tar.gz' }
            $directory = Join-Path $Repository '.dependencies/downloads'
            [IO.Directory]::CreateDirectory($directory) | Out-Null
            $path = Join-Path $directory "$sha-sdk.tar.gz"
            [IO.File]::WriteAllBytes($path, $content)
            Mock Invoke-WebRequest { throw 'A verified cache must not access the network.' }
            Get-AegiNextSdkArchive $source $Repository | Should -Be $path
            Should -Invoke Invoke-WebRequest -Times 0 -Exactly
            Set-Content -LiteralPath $path -Value 'corrupt archive'
            Mock Invoke-WebRequest { Set-Content -LiteralPath $OutFile -Value 'untrusted bytes' }
            { Get-AegiNextSdkArchive $source $Repository } | Should -Throw '*SHA256 mismatch*'
            Test-Path -LiteralPath $path | Should -BeFalse
            @(Get-ChildItem -LiteralPath $directory -File).Count | Should -Be 0
        }
    }

    It 'removes partial downloads on network failures and refuses unlocked URLs' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $TestDrive } {
            param($Repository)
            $source = @{ url = 'https://example.invalid/sdk.zip'; sha256 = 'a' * 64; archive = 'sdk.zip' }
            Mock Invoke-WebRequest {
                Set-Content -LiteralPath $OutFile -Value 'partial'
                throw 'network failure'
            }
            { Get-AegiNextSdkArchive $source $Repository } | Should -Throw '*network failure*'
            @(Get-ChildItem -LiteralPath (Join-Path $Repository '.dependencies/downloads') -File).Count | Should -Be 0
            $source.url = 'http://example.invalid/sdk.zip'
            { Get-AegiNextSdkArchive $source $Repository } | Should -Throw '*HTTPS*'
        }
    }
}

Describe 'Windows development SDK extraction' {
    It 'copies only the selected architecture and retains upstream licenses from the archive' {
        $archiveRoot = Join-Path $TestDrive 'archive/SDL3-3.4.16'
        foreach ($entry in @('x86_64-w64-mingw32/bin/SDL3.dll', 'x86_64-w64-mingw32/lib/libSDL3.dll.a', 'i686-w64-mingw32/bin/SDL3.dll', 'LICENSE.txt'))
        {
            $path = Join-Path $archiveRoot $entry
            [IO.Directory]::CreateDirectory((Split-Path $path)) | Out-Null
            Set-Content -LiteralPath $path -Value $entry
        }
        $archive = Join-Path $TestDrive 'SDL SDK [archive].zip'
        Compress-Archive -LiteralPath $archiveRoot -DestinationPath $archive
        InModuleScope AegiNext.Build -Parameters @{ Archive = $archive; Repository = $TestDrive } {
            param($Archive, $Repository)
            $work = Join-Path $Repository 'work [selected]'
            $stage = Join-Path $work 'sdk'
            [IO.Directory]::CreateDirectory($stage) | Out-Null
            $script:fixtureSdkArchivePath = $Archive
            Mock Get-AegiNextSdkArchive { return $script:fixtureSdkArchivePath }
            Invoke-AegiNextSdkRecipe $Repository ([pscustomobject]@{ Platform = 'Windows' }) (Get-AegiNextDependencyManifest) sdl3 $work $stage 2
            (Get-Content -LiteralPath (Join-Path $stage 'bin/SDL3.dll') -Raw).Trim() | Should -Be 'x86_64-w64-mingw32/bin/SDL3.dll'
            Test-Path -LiteralPath (Join-Path $stage 'lib/libSDL3.dll.a') | Should -BeTrue
            Test-Path -LiteralPath (Join-Path $stage 'i686-w64-mingw32') | Should -BeFalse
            @(Get-ChildItem -LiteralPath (Join-Path $stage 'share/licenses') -Filter 'LICENSE.txt' -File -Recurse).Count | Should -BeGreaterThan 0
        }
    }
}

Describe 'Software AV1 SDK preparation' {
    It 'checks the prepared dav1d version and requires its packaged upstream license' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $TestDrive } {
            param($Repository)
            $manifest = Get-AegiNextDependencyManifest
            $root = Join-Path $Repository 'prepared dav1d SDK'
            foreach ($file in $manifest.packages.dav1d.requiredFiles)
            {
                $path = Join-Path $root $file
                [IO.Directory]::CreateDirectory((Split-Path $path)) | Out-Null
                Set-Content -LiteralPath $path -Value 'prepared SDK file'
            }
            Mock Assert-AegiNextSdkReceipt {}
            Set-Content -LiteralPath (Join-Path $root 'lib/pkgconfig/dav1d.pc') -Value 'Version: 1.5.4'
            { Test-AegiNextPreparedSdk $root $Repository ([pscustomobject]@{ Platform = 'MacOS' }) $manifest dav1d } | Should -Not -Throw
            Set-Content -LiteralPath (Join-Path $root 'lib/pkgconfig/dav1d.pc') -Value 'Version: 1.5.3'
            { Test-AegiNextPreparedSdk $root $Repository ([pscustomobject]@{ Platform = 'MacOS' }) $manifest dav1d } | Should -Throw '*locked version*'
            Remove-Item -LiteralPath (Join-Path $root 'share/licenses/dav1d/COPYING')
            { Test-AegiNextPreparedSdk $root $Repository ([pscustomobject]@{ Platform = 'MacOS' }) $manifest dav1d } | Should -Throw '*COPYING*'
        }
    }

    It 'builds both dav1d bitdepth families without tools or network wraps and preserves its license' {
        $source = Join-Path $TestDrive 'dav1d source [locked]'
        [IO.Directory]::CreateDirectory($source) | Out-Null
        Set-Content -LiteralPath (Join-Path $source 'COPYING') -Value 'dav1d upstream BSD license'
        InModuleScope AegiNext.Build -Parameters @{ Source = $source; Repository = $TestDrive } {
            param($Source, $Repository)
            $script:dav1dSource = $Source
            $work = Join-Path $Repository 'dav1d work [staging]'
            $stage = Join-Path $work 'sdk'
            $build = Join-Path $work 'build'
            Mock Get-AegiNextSdkArchive { 'verified-archive' }
            Mock Expand-AegiNextSdkArchive { $script:dav1dSource }
            Mock Get-AegiNextSdkBuildEnvironment { @{ CC = 'apple-clang'; CFLAGS = '-mmacosx-version-min=14.0' } }
            Mock Find-AegiNextCommand { "fixture-$Name" }
            Mock Invoke-AegiNextSdkCommand {}
            Invoke-AegiNextSdkRecipe $Repository ([pscustomobject]@{ Platform = 'MacOS'; Architecture = 'Arm64' }) (Get-AegiNextDependencyManifest) dav1d $work $stage 3
            Should -Invoke Invoke-AegiNextSdkCommand -Times 1 -Exactly -ParameterFilter {
                $FilePath -eq 'fixture-meson' -and $Arguments[0] -eq 'setup' -and
                $Arguments[1] -eq $build -and $Arguments[2] -eq $Source -and
                "--prefix=$stage" -in $Arguments -and '--wrap-mode=nodownload' -in $Arguments -and
                '-Ddefault_library=shared' -in $Arguments -and '-Dbitdepths=8,16' -in $Arguments -and
                '-Denable_asm=true' -in $Arguments -and '-Denable_tools=false' -in $Arguments -and
                '-Denable_tests=false' -in $Arguments -and '-Denable_examples=false' -in $Arguments -and
                '-Denable_docs=false' -in $Arguments -and '-Dxxhash_muxer=disabled' -in $Arguments -and
                $Environment.CFLAGS -eq '-mmacosx-version-min=14.0'
            }
            Should -Invoke Invoke-AegiNextSdkCommand -Times 1 -Exactly -ParameterFilter {
                $Arguments[0] -eq 'compile' -and $Arguments[2] -eq $build -and $Arguments[4] -eq '3'
            }
            Should -Invoke Invoke-AegiNextSdkCommand -Times 1 -Exactly -ParameterFilter {
                $Arguments[0] -eq 'install' -and $Arguments[2] -eq $build -and '--no-rebuild' -in $Arguments
            }
            (Get-Content -LiteralPath (Join-Path $stage 'share/licenses/dav1d/COPYING') -Raw).Trim() | Should -Be 'dav1d upstream BSD license'
        }
    }

    It 'checks all transitive build tools while installing only the missing Meson tool on <Architecture>' -TestCases @(
        @{ Architecture = 'Arm64' }
        @{ Architecture = 'X64' }
    ) {
        param($Architecture)
        InModuleScope AegiNext.Build -Parameters @{ Repository = $TestDrive; Architecture = $Architecture } {
            param($Repository, $Architecture)
            $script:mesonInstalled = $false
            Mock Find-AegiNextCommand {
                if ($Name -eq 'meson' -and !$script:mesonInstalled) { return $null }
                return "fixture-$Name"
            }
            Mock Invoke-AegiNextSdkCommand {
                $script:mesonInstalled = $true
            }
            Install-AegiNextSdkTool ([pscustomobject]@{ Platform = 'MacOS'; Architecture = $Architecture }) @('x264', 'x265', 'dav1d', 'ffmpeg') $Repository
            Should -Invoke Find-AegiNextCommand -Times 1 -Exactly -ParameterFilter { $Name -eq 'python3' }
            $nasmCalls = if ($Architecture -eq 'X64') { 1 } else { 0 }
            Should -Invoke Find-AegiNextCommand -Times $nasmCalls -Exactly -ParameterFilter { $Name -eq 'nasm' }
            Should -Invoke Invoke-AegiNextSdkCommand -Times 1 -Exactly -ParameterFilter {
                $FilePath -eq 'fixture-brew' -and $Arguments[0] -eq 'install' -and $Arguments[1] -eq 'meson'
            }
        }
    }
}

Describe 'Software export SDK preparation' {
    It 'refuses missing licenses and incorrect prepared versions for <Name>' -TestCases @(
        @{ Name = 'libvpx'; Module = 'vpx'; License = 'LICENSE' }
        @{ Name = 'svt-av1'; Module = 'SvtAv1Enc'; License = 'PATENTS.md' }
        @{ Name = 'opus'; Module = 'opus'; License = 'COPYING' }
    ) {
        param($Name, $Module, $License)
        InModuleScope AegiNext.Build -Parameters @{ Repository = $TestDrive; Name = $Name; Module = $Module; License = $License } {
            param($Repository, $Name, $Module, $License)
            $manifest = Get-AegiNextDependencyManifest
            $root = Join-Path $Repository "prepared $Name"
            foreach ($file in $manifest.packages[$Name].requiredFiles)
            {
                $path = Join-Path $root $file
                [IO.Directory]::CreateDirectory((Split-Path $path)) | Out-Null
                Set-Content -LiteralPath $path -Value 'prepared SDK file'
            }
            Mock Assert-AegiNextSdkReceipt {}
            $pc = Join-Path $root "lib/pkgconfig/$Module.pc"
            Set-Content -LiteralPath $pc -Value "Version: $($manifest.packages[$Name].version)"
            { Test-AegiNextPreparedSdk $root $Repository ([pscustomobject]@{ Platform = 'MacOS' }) $manifest $Name } | Should -Not -Throw
            Set-Content -LiteralPath $pc -Value 'Version: 0.0.0'
            { Test-AegiNextPreparedSdk $root $Repository ([pscustomobject]@{ Platform = 'MacOS' }) $manifest $Name } | Should -Throw '*locked version*'
            Remove-Item -LiteralPath (Join-Path $root "share/licenses/$Name/$License")
            { Test-AegiNextPreparedSdk $root $Repository ([pscustomobject]@{ Platform = 'MacOS' }) $manifest $Name } | Should -Throw "*$License*"
        }
    }

    It 'copies upstream patent notices alongside copyright licenses' {
        $source = Join-Path $TestDrive 'codec source'
        [IO.Directory]::CreateDirectory($source) | Out-Null
        foreach ($name in @('LICENSE.md', 'PATENTS.md', 'unrelated.md'))
        {
            Set-Content -LiteralPath (Join-Path $source $name) -Value $name
        }
        InModuleScope AegiNext.Build -Parameters @{ Source = $source; Stage = (Join-Path $TestDrive 'sdk') } {
            param($Source, $Stage)
            Copy-AegiNextSdkLicense $Source $Stage 'svt-av1'
            Test-Path -LiteralPath (Join-Path $Stage 'share/licenses/svt-av1/LICENSE.md') | Should -BeTrue
            Test-Path -LiteralPath (Join-Path $Stage 'share/licenses/svt-av1/PATENTS.md') | Should -BeTrue
            Test-Path -LiteralPath (Join-Path $Stage 'share/licenses/svt-av1/unrelated.md') | Should -BeFalse
        }
    }
}

Describe 'Project SDK installation transactions' {
    BeforeEach {
        $repository = Join-Path $TestDrive ([guid]::NewGuid().ToString('N') + ' project [sdk]')
    }

    It 'passes the full dependency order to tool preparation when FFmpeg is the requested root' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $repository } {
            param($Repository)
            $hostInfo = [pscustomobject]@{ Platform = 'MacOS'; Architecture = 'Arm64' }
            Mock Install-AegiNextSdkTool {}
            Mock Invoke-AegiNextSdkRecipe { Set-Content -LiteralPath (Join-Path $Stage 'runtime.dylib') -Value 'complete SDK' }
            Mock Repair-AegiNextSdkPath {}
            Mock Test-AegiNextPreparedSdk {}
            Install-AegiNextProjectSdk $Repository $hostInfo @('ffmpeg')
            Should -Invoke Install-AegiNextSdkTool -Times 1 -Exactly -ParameterFilter {
                'dav1d' -in $Names -and 'ffmpeg' -in $Names -and
                [array]::IndexOf($Names, 'dav1d') -lt [array]::IndexOf($Names, 'ffmpeg')
            }
        }
    }

    It 'installs, verifies and reuses a complete SDK without rebuilding or installing tools twice' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $repository } {
            param($Repository)
            $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'Arm64' }
            Mock Install-AegiNextSdkTool {}
            Mock Invoke-AegiNextSdkRecipe { Set-Content -LiteralPath (Join-Path $Stage 'runtime.dll') -Value 'complete SDK' }
            Mock Test-AegiNextPreparedSdk {
                if (!(Test-Path -LiteralPath (Join-Path $Root 'runtime.dll')))
                {
                    throw 'SDK fixture is incomplete'
                }
            }
            Install-AegiNextProjectSdk $Repository $hostInfo @('sdl3')
            $root = Get-AegiNextProjectSdkRoot $Repository $hostInfo 'sdl3'
            Test-AegiNextInstalledSdk $root (Get-AegiNextDependencyManifest) 'sdl3' $hostInfo | Should -BeTrue
            Install-AegiNextProjectSdk $Repository $hostInfo @('sdl3')
            Should -Invoke Invoke-AegiNextSdkRecipe -Times 1 -Exactly
            Should -Invoke Install-AegiNextSdkTool -Times 1 -Exactly
            Set-Content -LiteralPath (Join-Path $root 'runtime.dll') -Value 'damaged'
            Test-AegiNextInstalledSdk $root (Get-AegiNextDependencyManifest) 'sdl3' $hostInfo | Should -BeFalse
            Install-AegiNextProjectSdk $Repository $hostInfo @('sdl3')
            Test-AegiNextInstalledSdk $root (Get-AegiNextDependencyManifest) 'sdl3' $hostInfo | Should -BeTrue
            Should -Invoke Invoke-AegiNextSdkRecipe -Times 2 -Exactly
        }
    }

    It 'restores the previous SDK when validation fails after promotion and retains failure logs' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $repository } {
            param($Repository)
            $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64' }
            Mock Install-AegiNextSdkTool {}
            Mock Invoke-AegiNextSdkRecipe { Set-Content -LiteralPath (Join-Path $Stage 'runtime.dll') -Value 'original' }
            Mock Test-AegiNextPreparedSdk {}
            Install-AegiNextProjectSdk $Repository $hostInfo @('sdl3')
            $root = Get-AegiNextProjectSdkRoot $Repository $hostInfo 'sdl3'
            Set-Content -LiteralPath (Join-Path $root 'runtime.dll') -Value 'previous bytes'
            Mock Invoke-AegiNextSdkRecipe { Set-Content -LiteralPath (Join-Path $Stage 'runtime.dll') -Value 'replacement' }
            Mock Test-AegiNextPreparedSdk { throw 'runtime verification failure' } -ParameterFilter { !$SkipRuntime }
            { Install-AegiNextProjectSdk $Repository $hostInfo @('sdl3') } | Should -Throw '*runtime verification failure*'
            (Get-Content -LiteralPath (Join-Path $root 'runtime.dll') -Raw).Trim() | Should -Be 'previous bytes'
            Test-Path -LiteralPath "$root.previous" | Should -BeFalse
            @(Get-ChildItem -LiteralPath (Join-Path $Repository '.dependencies/.work') -Directory).Count | Should -Be 1
        }
    }

    It 'preserves unmanaged SDKs and serializes concurrent installations' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $repository } {
            param($Repository)
            $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64' }
            $root = Get-AegiNextProjectSdkRoot $Repository $hostInfo 'sdl3'
            [IO.Directory]::CreateDirectory($root) | Out-Null
            Set-Content -LiteralPath (Join-Path $root 'user-file') -Value 'keep'
            Mock Install-AegiNextSdkTool {}
            Mock Invoke-AegiNextSdkRecipe { throw 'Unmanaged SDKs must be preserved' }
            { Install-AegiNextProjectSdk $Repository $hostInfo @('sdl3') } | Should -Throw '*unmanaged SDK*'
            Test-Path -LiteralPath (Join-Path $root 'user-file') | Should -BeTrue
            Should -Invoke Invoke-AegiNextSdkRecipe -Times 0 -Exactly
            $lock = [IO.File]::Open((Join-Path $Repository '.dependencies/.install.lock'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            try
            {
                { Install-AegiNextProjectSdk $Repository $hostInfo @('sdl3') } | Should -Throw '*Another project SDK installation*'
            }
            finally
            {
                $lock.Dispose()
            }
        }
    }

    It 'recovers a managed SDK when an interrupted promotion left only its backup' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $repository } {
            param($Repository)
            $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64' }
            Mock Install-AegiNextSdkTool {}
            Mock Invoke-AegiNextSdkRecipe { Set-Content -LiteralPath (Join-Path $Stage 'runtime.dll') -Value 'complete' }
            Mock Test-AegiNextPreparedSdk {}
            Install-AegiNextProjectSdk $Repository $hostInfo @('sdl3')
            $root = Get-AegiNextProjectSdkRoot $Repository $hostInfo 'sdl3'
            Move-Item -LiteralPath $root -Destination "$root.previous"
            Install-AegiNextProjectSdk $Repository $hostInfo @('sdl3')
            Test-AegiNextInstalledSdk $root (Get-AegiNextDependencyManifest) 'sdl3' $hostInfo | Should -BeTrue
            Test-Path -LiteralPath "$root.previous" | Should -BeFalse
            Should -Invoke Invoke-AegiNextSdkRecipe -Times 1 -Exactly
        }
    }
}

Describe 'Build dependency installation routing' {
    It 'prepares project SDKs even when system SDKs are ready, without replacing explicit roots' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $TestDrive } {
            param($Repository)
            $originalFfmpeg = $env:FFMPEG_DIR
            $originalSdl = $env:SDL3_DIR
            try
            {
                $env:FFMPEG_DIR = ''; $env:SDL3_DIR = ''
                $report = [pscustomobject]@{
                    HostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64' }
                    Checks = @((Get-AegiNextCheck 'FfmpegSdk' 'Ready' 'system FFmpeg'), (Get-AegiNextCheck 'SdlSdk' 'Invalid' 'wrong system SDL'))
                }
                Mock Install-AegiNextProjectSdk {}
                Mock Invoke-AegiNextCommand { throw 'System libraries must not be reinstalled' }
                Install-AegiNextDependency $report $Repository -Jobs 3
                Should -Invoke Install-AegiNextProjectSdk -Times 1 -Exactly -ParameterFilter { 'ffmpeg' -in $Names -and 'sdl3' -in $Names -and $Jobs -eq 3 }
                $env:FFMPEG_DIR = 'package-manager-ffmpeg'
                $env:SDL3_DIR = 'package-manager-sdl'
                Install-AegiNextDependency $report $Repository
                Should -Invoke Install-AegiNextProjectSdk -Times 2 -Exactly
                Install-AegiNextDependency $report $Repository -FfmpegRoot 'explicit-ffmpeg' -SdlRoot 'explicit-sdl'
                Should -Invoke Install-AegiNextProjectSdk -Times 2 -Exactly
            }
            finally
            {
                $env:FFMPEG_DIR = $originalFfmpeg; $env:SDL3_DIR = $originalSdl
            }
        }
    }

    It 'keeps the default Workbench restore, build and tests on the native SDK RID for <Platform>' -TestCases @(
        @{ Platform = 'MacOS'; Architecture = 'Arm64'; Rid = 'osx-arm64' }
        @{ Platform = 'MacOS'; Architecture = 'X64'; Rid = 'osx-x64' }
        @{ Platform = 'Windows'; Architecture = 'Arm64'; Rid = 'win-x64' }
    ) {
        param($Platform, $Architecture, $Rid)
        $prefixes = @{ ffmpeg = '/sdk/ffmpeg'; sdl3 = '/sdk/sdl'; cc = 'cc'; cxx = 'c++'; cmake = 'cmake'; ninja = 'ninja'; ctest = 'ctest' }
        $hostInfo = [pscustomobject]@{ Platform = $Platform; Architecture = $Architecture; ProcessArchitecture = $Architecture }
        $plan = @(Get-AegiNextBuildPlan -RepositoryRoot $TestDrive -HostInfo $hostInfo -Target Workbench -Configuration Debug -NativePrefixes $prefixes -RunTests -TestProjects Media)
        $restore = $plan | Where-Object Label -eq 'Restore managed'
        $build = $plan | Where-Object Label -eq 'Build managed'
        $test = $plan | Where-Object Label -eq 'Test Media'
        $restore.Arguments | Should -Contain '-r'
        $restore.Arguments | Should -Contain $Rid
        foreach ($step in @($restore, $build, $test))
        {
            $step.Arguments | Should -Contain "-p:AegiNextRuntimeIdentifier=$Rid"
        }
    }
}
