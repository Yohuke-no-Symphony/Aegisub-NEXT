#Requires -Version 7.2

function Get-AegiNextSdkBuildEnvironment
{
    param([string] $RepositoryRoot, [object] $HostInfo, [hashtable] $Manifest, [string] $Name, [string] $Work)
    $prefixes = @($Manifest.packages[$Name].dependencies | ForEach-Object { Get-AegiNextProjectSdkRoot $RepositoryRoot $HostInfo $_ })
    $pkgDirectories = @($prefixes | ForEach-Object { Join-Path $_ 'lib/pkgconfig'; Join-Path $_ 'share/pkgconfig' })
    $empty = Join-Path $Work 'empty-pkgconfig'
    [IO.Directory]::CreateDirectory($empty) | Out-Null
    $xcrun = Find-AegiNextCommand 'xcrun'
    $cc = Invoke-AegiNextCommand $xcrun @('--find', 'clang') $RepositoryRoot
    $cxx = Invoke-AegiNextCommand $xcrun @('--find', 'clang++') $RepositoryRoot
    $sdk = Invoke-AegiNextCommand $xcrun @('--sdk', 'macosx', '--show-sdk-path') $RepositoryRoot
    if ($cc.ExitCode -ne 0 -or $cxx.ExitCode -ne 0 -or $sdk.ExitCode -ne 0)
    {
        throw 'The selected Apple toolchain cannot resolve clang/clang++.'
    }
    return @{
        CC = $cc.Output.Trim(); CXX = $cxx.Output.Trim(); SDKROOT = $sdk.Output.Trim(); MACOSX_DEPLOYMENT_TARGET = $Manifest.minimumMacOS
        PKG_CONFIG = Find-AegiNextCommand 'pkg-config'; PKG_CONFIG_PATH = ''
        PKG_CONFIG_SYSROOT_DIR = ''; DYLD_LIBRARY_PATH = ''; DYLD_FALLBACK_LIBRARY_PATH = ''
        PKG_CONFIG_LIBDIR = (@($empty) + $pkgDirectories) -join [IO.Path]::PathSeparator
        CMAKE_PREFIX_PATH = $prefixes -join [IO.Path]::PathSeparator
        CPATH = ''; LIBRARY_PATH = ''; CFLAGS = "-mmacosx-version-min=$($Manifest.minimumMacOS)"
        CXXFLAGS = "-mmacosx-version-min=$($Manifest.minimumMacOS)"; LDFLAGS = "-mmacosx-version-min=$($Manifest.minimumMacOS)"
    }
}

function Invoke-AegiNextSdkCmake
{
    param([string] $Source, [string] $Build, [string] $Stage, [string[]] $Options, [hashtable] $Environment, [object] $HostInfo, [hashtable] $Manifest, [int] $Jobs,
        [switch] $SkipInstall)
    $cmake = Find-AegiNextCommand 'cmake'
    $architecture = if ($HostInfo.Architecture -eq 'Arm64') { 'arm64' } else { 'x86_64' }
    $prefixes = $Environment.CMAKE_PREFIX_PATH -split [IO.Path]::PathSeparator
    $arguments = @('-S', $Source, '-B', $Build, '-G', 'Ninja', '-DCMAKE_BUILD_TYPE=Release',
        "-DCMAKE_MAKE_PROGRAM=$(Find-AegiNextCommand 'ninja')", "-DCMAKE_C_COMPILER=$($Environment.CC)", "-DCMAKE_CXX_COMPILER=$($Environment.CXX)",
        "-DCMAKE_INSTALL_PREFIX=$Stage", '-DCMAKE_INSTALL_LIBDIR=lib', "-DCMAKE_OSX_DEPLOYMENT_TARGET=$($Manifest.minimumMacOS)",
        "-DCMAKE_OSX_ARCHITECTURES=$architecture", '-DCMAKE_POSITION_INDEPENDENT_CODE=ON', '-DCMAKE_POLICY_VERSION_MINIMUM=3.5',
        '-DCMAKE_FIND_USE_PACKAGE_REGISTRY=OFF', '-DCMAKE_FIND_USE_SYSTEM_PACKAGE_REGISTRY=OFF',
        '-DCMAKE_FIND_USE_CMAKE_ENVIRONMENT_PATH=OFF', "-DCMAKE_PREFIX_PATH=$($prefixes -join ';')") + $Options
    Invoke-AegiNextSdkCommand $cmake $arguments $Source $Environment
    Invoke-AegiNextSdkCommand $cmake @('--build', $Build, '--parallel', "$Jobs") $Source $Environment
    if (!$SkipInstall)
    {
        Invoke-AegiNextSdkCommand $cmake @('--install', $Build) $Source $Environment
    }
}

function Copy-AegiNextSdkLicense
{
    param([string] $Source, [string] $Stage, [string] $Name)
    $destination = Join-Path $Stage "share/licenses/$Name"
    foreach ($file in Get-ChildItem -LiteralPath $Source -File -Recurse | Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING|COPYRIGHT|NOTICE|PATENTS)([._-].*)?$' })
    {
        $relative = [IO.Path]::GetRelativePath($Source, $file.FullName)
        $target = Join-Path $destination $relative
        [IO.Directory]::CreateDirectory((Split-Path $target)) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}

function Invoke-AegiNextSdkRecipe
{
    param([string] $RepositoryRoot, [object] $HostInfo, [hashtable] $Manifest, [string] $Name, [string] $Work, [string] $Stage, [int] $Jobs)
    $package = $Manifest.packages[$Name]
    if ($HostInfo.Platform -eq 'Windows')
    {
        $source = $Manifest.sources[$package.windows.source]
        $archive = Get-AegiNextSdkArchive $source $RepositoryRoot
        $root = Expand-AegiNextSdkArchive $archive (Join-Path $Work 'extracted') $package.windows.archiveRoot
        foreach ($entry in Get-ChildItem -LiteralPath $root -Force)
        {
            Copy-Item -LiteralPath $entry.FullName -Destination $Stage -Recurse -Force
        }
        Copy-AegiNextSdkLicense (Join-Path $Work 'extracted') $Stage $package.windows.source
        return
    }

    $sources = @{}
    foreach ($id in $package.sources)
    {
        $archive = Get-AegiNextSdkArchive $Manifest.sources[$id] $RepositoryRoot
        $sources[$id] = Expand-AegiNextSdkArchive $archive (Join-Path $Work "sources/$id")
    }
    $source = $sources[$Name]
    $environment = Get-AegiNextSdkBuildEnvironment $RepositoryRoot $HostInfo $Manifest $Name $Work
    $build = Join-Path $Work 'build'
    switch ($package.recipe)
    {
        'configure'
        {
            $options = @("--prefix=$Stage", '--enable-shared')
            if ($Name -eq 'x264')
            {
                $options += '--disable-cli'
            }
            else
            {
                $options += '--disable-static'
            }
            Invoke-AegiNextSdkCommand (Find-AegiNextCommand 'bash') (@((Join-Path $source 'configure')) + $options) $source $environment
            Invoke-AegiNextSdkCommand (Find-AegiNextCommand 'make') @("-j$Jobs") $source $environment
            Invoke-AegiNextSdkCommand (Find-AegiNextCommand 'make') @('install') $source $environment
        }
        'libvpx'
        {
            $target = if ($HostInfo.Architecture -eq 'Arm64') { 'arm64-darwin23-gcc' } else { 'x86_64-darwin23-gcc' }
            $options = @("--prefix=$Stage", "--target=$target", '--enable-shared', '--disable-static', '--enable-pic',
                '--enable-vp8', '--enable-vp9', '--enable-vp9-highbitdepth', '--disable-examples', '--disable-tools',
                '--disable-docs', '--disable-unit-tests', '--disable-install-srcs')
            Invoke-AegiNextSdkCommand (Find-AegiNextCommand 'bash') (@((Join-Path $source 'configure')) + $options) $source $environment
            Invoke-AegiNextSdkCommand (Find-AegiNextCommand 'make') @("-j$Jobs") $source $environment
            Invoke-AegiNextSdkCommand (Find-AegiNextCommand 'make') @('install') $source $environment
        }
        'x265'
        {
            $high = Join-Path $Work 'build-10bit'
            $main = Join-Path $Work 'build-8bit'
            $cmakeSource = Join-Path $source 'source'
            Invoke-AegiNextSdkCmake $cmakeSource $high $Stage @('-DHIGH_BIT_DEPTH=ON', '-DEXPORT_C_API=OFF', '-DENABLE_SHARED=OFF', '-DENABLE_CLI=OFF') $environment $HostInfo $Manifest $Jobs -SkipInstall
            [IO.Directory]::CreateDirectory($main) | Out-Null
            Copy-Item -LiteralPath (Join-Path $high 'libx265.a') -Destination (Join-Path $main 'libx265_main10.a')
            Invoke-AegiNextSdkCmake $cmakeSource $main $Stage @('-DENABLE_SHARED=ON', '-DENABLE_CLI=OFF', '-DLINKED_10BIT=ON',
                '-DEXTRA_LIB=x265_main10.a', "-DEXTRA_LINK_FLAGS=-L$main") $environment $HostInfo $Manifest $Jobs
        }
        'ffmpeg'
        {
            $options = @("--prefix=$Stage") + $package.configureOptions + @(
                "--cc=$($environment.CC)", "--cxx=$($environment.CXX)", "--pkg-config=$($environment.PKG_CONFIG)",
                "--extra-cflags=-mmacosx-version-min=$($Manifest.minimumMacOS)", "--extra-ldflags=-mmacosx-version-min=$($Manifest.minimumMacOS)")
            Invoke-AegiNextSdkCommand (Find-AegiNextCommand 'bash') (@((Join-Path $source 'configure')) + $options) $source $environment
            Invoke-AegiNextSdkCommand (Find-AegiNextCommand 'make') @("-j$Jobs") $source $environment
            Invoke-AegiNextSdkCommand (Find-AegiNextCommand 'make') @('install') $source $environment
        }
        'dav1d'
        {
            $meson = Find-AegiNextCommand 'meson'
            $options = @('setup', $build, $source, "--prefix=$Stage", '--libdir=lib', '--buildtype=release', '--wrap-mode=nodownload',
                '-Ddefault_library=shared', '-Dbitdepths=8,16', '-Denable_asm=true', '-Denable_tools=false',
                '-Denable_tests=false', '-Denable_examples=false', '-Denable_docs=false', '-Dxxhash_muxer=disabled')
            Invoke-AegiNextSdkCommand $meson $options $source $environment
            Invoke-AegiNextSdkCommand $meson @('compile', '-C', $build, '-j', "$Jobs") $source $environment
            Invoke-AegiNextSdkCommand $meson @('install', '-C', $build, '--no-rebuild') $source $environment
        }
        'cmake'
        {
            $options = switch ($Name)
            {
                'sdl3' { @('-DSDL_SHARED=ON', '-DSDL_STATIC=OFF', '-DSDL_TESTS=OFF', '-DSDL_TEST_LIBRARY=OFF') }
                'svt-av1' { @('-DBUILD_SHARED_LIBS=ON', '-DBUILD_APPS=OFF', '-DBUILD_TESTING=OFF', '-DNATIVE=OFF',
                    '-DREPRODUCIBLE_BUILDS=ON') }
                'opus' { @('-DOPUS_BUILD_SHARED_LIBRARY=ON', '-DOPUS_BUILD_TESTING=OFF', '-DOPUS_BUILD_PROGRAMS=OFF',
                    '-DOPUS_INSTALL_PKG_CONFIG_MODULE=ON', '-DOPUS_INSTALL_CMAKE_CONFIG_MODULE=ON') }
                'vulkan-headers' { @('-DVULKAN_HEADERS_ENABLE_TESTS=OFF') }
                'vulkan-loader' { @('-DBUILD_TESTS=OFF', '-DBUILD_WERROR=OFF', '-DUPDATE_DEPS=OFF',
                    "-DVULKAN_HEADERS_INSTALL_DIR=$(Get-AegiNextProjectSdkRoot $RepositoryRoot $HostInfo 'vulkan-headers')") }
                default { throw "Unsupported CMake SDK: $Name." }
            }
            Invoke-AegiNextSdkCmake $source $build $Stage $options $environment $HostInfo $Manifest $Jobs
            if ($Name -eq 'vulkan-loader')
            {
                $pcPath = Join-Path $Stage 'lib/pkgconfig/vulkan.pc'
                $headerRoot = Get-AegiNextProjectSdkRoot $RepositoryRoot $HostInfo $package.headerPackage
                $pcDirectory = Join-Path (Get-AegiNextProjectSdkRoot $RepositoryRoot $HostInfo $Name) 'lib/pkgconfig'
                $relative = [IO.Path]::GetRelativePath($pcDirectory, (Join-Path $headerRoot 'include')).Replace('\', '/')
                $pc = Get-Content -LiteralPath $pcPath -Raw
                $pc = [regex]::Replace($pc, '(?m)^includedir=.*$', ('includedir=${pcfiledir}/' + $relative))
                [IO.File]::WriteAllText($pcPath, $pc, [Text.UTF8Encoding]::new($false))
            }
        }
        'shaderc'
        {
            foreach ($id in @('glslang', 'spirv-headers', 'spirv-tools'))
            {
                $target = Join-Path $source "third_party/$id"
                if (Test-Path -LiteralPath $target)
                {
                    Remove-Item -LiteralPath $target -Recurse -Force
                }
                Copy-Item -LiteralPath $sources[$id] -Destination $target -Recurse
            }
            Invoke-AegiNextSdkCmake $source $build $Stage @('-DSHADERC_SKIP_TESTS=ON', '-DSHADERC_SKIP_EXAMPLES=ON',
                '-DSHADERC_SKIP_COPYRIGHT_CHECK=ON', '-DSHADERC_ENABLE_WGSL_OUTPUT=OFF', '-DSPIRV_SKIP_TESTS=ON',
                '-DGLSLANG_TESTS=OFF', '-DENABLE_GLSLANG_BINARIES=OFF', "-DPython3_EXECUTABLE=$(Find-AegiNextCommand 'python3')") $environment $HostInfo $Manifest $Jobs
        }
        'meson'
        {
            $headersRoot = Get-AegiNextProjectSdkRoot $RepositoryRoot $HostInfo 'vulkan-headers'
            $vendoredHeaders = Join-Path $source '3rdparty/Vulkan-Headers'
            [IO.Directory]::CreateDirectory($vendoredHeaders) | Out-Null
            Copy-Item -LiteralPath (Join-Path $headersRoot 'include') -Destination (Join-Path $vendoredHeaders 'include') -Recurse
            Copy-Item -LiteralPath (Join-Path $headersRoot 'share/vulkan/registry') -Destination (Join-Path $vendoredHeaders 'registry') -Recurse
            foreach ($entry in @(@{ Id = 'fast-float'; Directory = 'fast_float' }, @{ Id = 'jinja2'; Directory = 'jinja' }, @{ Id = 'markupsafe'; Directory = 'markupsafe' }))
            {
                $target = Join-Path $source "3rdparty/$($entry.Directory)"
                if (Test-Path -LiteralPath $target)
                {
                    Remove-Item -LiteralPath $target -Recurse -Force
                }
                Copy-Item -LiteralPath $sources[$entry.Id] -Destination $target -Recurse
            }
            $meson = Find-AegiNextCommand 'meson'
            $options = @('setup', $build, $source, "--prefix=$Stage", '--libdir=lib', '--buildtype=release', '--wrap-mode=nodownload',
                '-Ddefault_library=shared', '-Dvulkan=enabled', '-Dvk-proc-addr=enabled', '-Dshaderc=enabled', '-Dlcms=enabled',
                '-Dglslang=disabled', '-Dopengl=disabled', '-Dd3d11=disabled', '-Dlibdovi=disabled', '-Dunwind=disabled', '-Dxxhash=disabled',
                '-Ddemos=false', '-Dtests=false', '-Dbench=false', '-Dfuzz=false',
                "-Dvulkan-registry=$(Join-Path (Get-AegiNextProjectSdkRoot $RepositoryRoot $HostInfo 'vulkan-headers') 'share/vulkan/registry/vk.xml')")
            Invoke-AegiNextSdkCommand $meson $options $source $environment
            Invoke-AegiNextSdkCommand $meson @('compile', '-C', $build, '-j', "$Jobs") $source $environment
            Invoke-AegiNextSdkCommand $meson @('install', '-C', $build, '--no-rebuild') $source $environment
        }
        'molten-vk'
        {
            $sdk = Join-Path $source 'MoltenVK'
            Copy-Item -LiteralPath (Join-Path $sdk 'include') -Destination (Join-Path $Stage 'include') -Recurse
            [IO.Directory]::CreateDirectory((Join-Path $Stage 'lib')) | Out-Null
            Copy-Item -LiteralPath (Join-Path $sdk 'dynamic/dylib/macOS/libMoltenVK.dylib') -Destination (Join-Path $Stage 'lib/libMoltenVK.dylib')
        }
        default { throw "Unknown SDK recipe: $($package.recipe)." }
    }
    foreach ($id in $sources.Keys)
    {
        Copy-AegiNextSdkLicense $sources[$id] $Stage $id
    }
}

function Test-AegiNextPreparedSdk
{
    param([string] $Root, [string] $RepositoryRoot, [object] $HostInfo, [hashtable] $Manifest, [string] $Name, [switch] $SkipRuntime)
    $package = $Manifest.packages[$Name]
    Assert-AegiNextSdkReceipt $Root $RepositoryRoot $HostInfo $Name
    $required = if ($HostInfo.Platform -eq 'Windows') { $package.windows.requiredFiles } else { $package.requiredFiles }
    foreach ($file in $required)
    {
        if (!(Test-Path -LiteralPath (Join-Path $Root $file) -PathType Leaf))
        {
            throw "Incomplete prepared SDK $Name`: missing $file."
        }
    }
    if ($Name -eq 'ffmpeg' -and !$SkipRuntime)
    {
        $toolchain = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'src/AegiNext.Media/Probing/ffmpeg-toolchain.json') -Raw | ConvertFrom-Json
        if ($toolchain.version -ne $package.version)
        {
            throw 'The FFmpeg SDK lock and media toolchain manifest disagree.'
        }
        $check = Get-AegiNextFfmpegSdkCheck $Root $RepositoryRoot $HostInfo.Platform $toolchain
        if ($check.Status -ne 'Ready')
        {
            throw $check.Detail
        }
        $check = Get-AegiNextExportCapabilityCheck $RepositoryRoot $HostInfo $Root
        if ($check.Status -ne 'Ready')
        {
            throw $check.Detail
        }
    }
    elseif ($Name -eq 'sdl3')
    {
        $check = Get-AegiNextSdlEnvironment -RepositoryRoot $RepositoryRoot -HostInfo $HostInfo -SdlRoot $Root
        if ($check.Check.Status -ne 'Ready')
        {
            throw $check.Check.Detail
        }
    }
    elseif ($Name -in @('dav1d', 'libvpx', 'svt-av1', 'opus'))
    {
        $module = switch ($Name) { 'libvpx' { 'vpx' } 'svt-av1' { 'SvtAv1Enc' } default { $Name } }
        $pc = Get-Content -LiteralPath (Join-Path $Root "lib/pkgconfig/$module.pc") -Raw
        if ($pc -notmatch ('(?m)^Version:\s*' + [regex]::Escape($package.version) + '\s*$'))
        {
            throw "Prepared $Name does not match the locked version."
        }
    }
    elseif ($Name -eq 'vulkan-headers')
    {
        $header = Get-Content -LiteralPath (Join-Path $Root 'include/vulkan/vulkan_core.h') -Raw
        $patch = ($package.version -split '\.')[2]
        if ($header -notmatch "(?m)^#define VK_HEADER_VERSION\s+$patch\s*$")
        {
            throw 'Prepared Vulkan headers do not match the locked version.'
        }
    }
    elseif ($Name -eq 'molten-vk')
    {
        $header = Get-Content -LiteralPath (Join-Path $Root 'include/MoltenVK/mvk_private_api.h') -Raw
        $parts = foreach ($part in @('MAJOR', 'MINOR', 'PATCH'))
        {
            [regex]::Match($header, "(?m)^#define MVK_VERSION_$part\s+(\d+)").Groups[1].Value
        }
        if (($parts -join '.') -ne $package.version)
        {
            throw 'Prepared MoltenVK headers do not match the locked version.'
        }
    }
    elseif ($Name -eq 'libplacebo')
    {
        $header = Get-Content -LiteralPath (Join-Path $Root 'include/libplacebo/config.h') -Raw
        foreach ($feature in @('VULKAN', 'VK_PROC_ADDR', 'SHADERC', 'LCMS'))
        {
            if ($header -notmatch "(?m)^#define PL_HAVE_$feature\s+1\s*$")
            {
                throw "Prepared libplacebo is missing $feature."
            }
        }
        $pc = Get-Content -LiteralPath (Join-Path $Root 'lib/pkgconfig/libplacebo.pc') -Raw
        if ($pc -notmatch ('(?m)^Version:\s*' + [regex]::Escape($package.version) + '\s*$'))
        {
            throw 'Prepared libplacebo does not match the locked version.'
        }
    }
}
