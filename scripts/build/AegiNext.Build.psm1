#Requires -Version 7.2
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'AegiNext.Dependencies.ps1')
. (Join-Path $PSScriptRoot 'AegiNext.SdkInstall.ps1')
. (Join-Path $PSScriptRoot 'AegiNext.SdkRecipes.ps1')
. (Join-Path $PSScriptRoot 'AegiNext.SdkMacPaths.ps1')
. (Join-Path $PSScriptRoot 'AegiNext.Decoder.ps1')
. (Join-Path $PSScriptRoot 'AegiNext.Audio.ps1')
. (Join-Path $PSScriptRoot 'AegiNext.Export.ps1')

function Get-AegiNextHost
{
    $platform = if ($IsMacOS) { 'MacOS' } elseif ($IsWindows) { 'Windows' } elseif ($IsLinux) { 'Linux' } else { 'Unknown' }
    [pscustomobject]@{
        Platform = $platform
        Architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        ProcessArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    }
}

function Get-AegiNextRuntimeIdentifier
{
    param([object] $HostInfo = (Get-AegiNextHost), [string] $RuntimeIdentifier)
    $expected = if ($HostInfo.Platform -eq 'Windows') { 'win-x64' }
        elseif ($HostInfo.Platform -eq 'MacOS') { "osx-$($HostInfo.Architecture.ToLowerInvariant())" }
        else { "linux-$($HostInfo.Architecture.ToLowerInvariant())" }
    if ($RuntimeIdentifier -and $RuntimeIdentifier -ne $expected)
    {
        throw "Target RID $RuntimeIdentifier does not match this build host. Use $expected; Windows ARM64 hosts build the x64 target for emulation."
    }
    return $expected
}

function Find-AegiNextCommand
{
    param([Parameter(Mandatory)][string] $Name)
    $command = Get-Command $Name -CommandType Application, ExternalScript -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command)
    {
        $command.Path
    }
}

function Invoke-AegiNextCommand
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $FilePath,
        [string[]] $Arguments = [string[]]@(),
        [Parameter(Mandatory)][string] $WorkingDirectory,
        [hashtable] $Environment = @{},
        [switch] $StreamOutput
    )
    $savedEnvironment = @{}
    $previousExitCode = Get-Variable LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
    $previousExitValue = if ($previousExitCode) { $previousExitCode.Value } else { $null }
    $PSNativeCommandUseErrorActionPreference = $false
    $ErrorActionPreference = 'Continue'
    Push-Location -LiteralPath $WorkingDirectory -ErrorAction Stop
    try
    {
        foreach ($key in $Environment.Keys)
        {
            $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
            [Environment]::SetEnvironmentVariable($key, $Environment[$key], 'Process')
        }

        $global:LASTEXITCODE = 0
        $output = @(& $FilePath @Arguments 2>&1 | ForEach-Object {
            if ($StreamOutput)
            {
                Write-Information -MessageData $_.ToString() -InformationAction Continue
            }
            $_
        })
        $succeeded = $?
        $code = $global:LASTEXITCODE
        if (!$succeeded -and $code -eq 0)
        {
            $code = 1
        }

        [pscustomobject]@{ ExitCode = $code; Output = ($output | Out-String).TrimEnd() }
    }
    finally
    {
        foreach ($key in $savedEnvironment.Keys)
        {
            [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], 'Process')
        }

        if ($previousExitCode)
        {
            $global:LASTEXITCODE = $previousExitValue
        }
        else
        {
            Remove-Variable LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
        }

        Pop-Location
    }
}

function Test-AegiNextSdkVersion
{
    param([string] $SelectedVersion, [string] $RequiredVersion)
    if ($SelectedVersion -notmatch '^\d+\.\d+\.\d+$' -or $RequiredVersion -notmatch '^\d+\.\d+\.\d+$')
    {
        return $false
    }

    $selected = $null
    $required = $null
    if (![version]::TryParse($SelectedVersion, [ref]$selected) -or ![version]::TryParse($RequiredVersion, [ref]$required))
    {
        return $false
    }
    return $selected.Major -eq $required.Major -and $selected.Minor -eq $required.Minor -and
        [math]::Floor($selected.Build / 100) -eq [math]::Floor($required.Build / 100) -and $selected -ge $required
}

function Get-AegiNextCheck
{
    param([string] $Id, [string] $Status, [string] $Detail, [string] $Package = '', [string] $Manager = '')
    [pscustomobject]@{ Id = $Id; Status = $Status; Detail = $Detail; Package = $Package; Manager = $Manager }
}

function Get-AegiNextTargetProblem
{
    param([string] $Platform, [string] $Target, [switch] $RunTests, [string[]] $TestProjects)
    if ($Platform -notin @('MacOS', 'Windows', 'Linux'))
    {
        return "Unsupported host platform: $Platform."
    }

    if ($Target -in @('Decoder', 'Audio', 'Export', 'Workbench') -and $Platform -eq 'Linux')
    {
        return 'The Linux decoder build is deferred. The Decoder target currently supports macOS and Windows x64.'
    }

    if ($Target -in @('All', 'Native') -and $Platform -ne 'MacOS')
    {
        return "The $Platform native backend is not implemented. Use -Target Managed explicitly; this does not enable HDR on $Platform."
    }

    if ($Platform -eq 'Linux' -and $Target -in @('Managed', 'All') -and $RunTests -and ('Rendering' -in $TestProjects -or 'Desktop.Ui' -in $TestProjects))
    {
        return 'Linux Rendering/Desktop.Ui tests are not supported by the current native asset packages. Select Core/Media explicitly; no tests will be silently skipped.'
    }
}

function Get-AegiNextSdkArchitectureCheck
{
    param([string] $RepositoryRoot, [string] $Dotnet, [object] $HostInfo)
    $expectedRid = if ($HostInfo.Platform -eq 'Windows') { 'win-x64' } else { "osx-$($HostInfo.Architecture.ToLowerInvariant())" }
    $project = Join-Path $RepositoryRoot 'src/AegiNext.Media/AegiNext.Media.csproj'
    $result = Invoke-AegiNextCommand $Dotnet @('msbuild', $project, '-nologo', '-getProperty:NETCoreSdkRuntimeIdentifier') $RepositoryRoot
    $actualRid = $result.Output.Trim()
    if ($HostInfo.Platform -eq 'Windows' -and $HostInfo.Architecture -eq 'Arm64' -and $result.ExitCode -eq 0 -and $actualRid -eq 'win-arm64')
    {
        return Get-AegiNextCheck 'SdkArchitecture' 'Ready' 'win-arm64 SDK host; managed build/publish explicitly targets win-x64, and MinGW compiler identity separately verifies x64 native output.'
    }
    if ($result.ExitCode -ne 0 -or $actualRid -ne $expectedRid)
    {
        return Get-AegiNextCheck 'SdkArchitecture' 'Invalid' "The native build needs SDK RID $expectedRid, but dotnet resolved '$actualRid'. Use the matching native dotnet SDK on PATH."
    }

    Get-AegiNextCheck 'SdkArchitecture' 'Ready' $actualRid
}

function Get-AegiNextMediaToolCheck
{
    param(
        [string] $Name,
        [string] $RepositoryRoot,
        [object] $Toolchain,
        [string] $Package,
        [string] $Manager,
        [string] $FilePath
    )
    $command = if ($FilePath) { $FilePath } else { Find-AegiNextCommand $Name }
    if (!$command)
    {
        return Get-AegiNextCheck $Name 'Missing' "Requires $Name $($Toolchain.version) on PATH." $Package $Manager
    }

    try
    {
        $result = Invoke-AegiNextCommand $command @('-version') $RepositoryRoot
    }
    catch
    {
        return Get-AegiNextCheck $Name 'Invalid' "Could not execute $command. $($_.Exception.Message)"
    }

    if ($result.ExitCode -ne 0)
    {
        return Get-AegiNextCheck $Name 'Invalid' "$command -version failed (exit $($result.ExitCode)). $($result.Output)"
    }

    $programVersion = [regex]::Match($result.Output, "(?m)^\s*$([regex]::Escape($Name))\s+version\s+(\S+)").Groups[1].Value
    if ($programVersion -cnotin $Toolchain.acceptedVersionStrings)
    {
        return Get-AegiNextCheck $Name 'Invalid' "Requires an accepted $Name $($Toolchain.version) build from the toolchain manifest; found '$programVersion' via $command. Existing tools are not automatically upgraded or downgraded."
    }

    foreach ($library in $Toolchain.libraries.PSObject.Properties)
    {
        $version = [regex]::Match($result.Output,
            "(?m)^\s*$([regex]::Escape($library.Name))\s+(\d+)\.\s*(\d+)\.\s*(\d+)\s*/\s*(\d+)\.\s*(\d+)\.\s*(\d+)\s*$")
        if (!$version.Success)
        {
            return Get-AegiNextCheck $Name 'Invalid' "$command did not report compiled/runtime $($library.Name) versions."
        }

        $compiled = "$($version.Groups[1].Value).$($version.Groups[2].Value).$($version.Groups[3].Value)"
        $runtime = "$($version.Groups[4].Value).$($version.Groups[5].Value).$($version.Groups[6].Value)"
        if ($compiled -cne $library.Value -or $runtime -cne $library.Value)
        {
            return Get-AegiNextCheck $Name 'Invalid' "$command requires $($library.Name) $($library.Value); compiled $compiled, runtime $runtime. Existing tools are not automatically upgraded or downgraded."
        }
    }

    Get-AegiNextCheck $Name 'Ready' "$programVersion with locked library versions via $command"
}

function Get-AegiNextEnvironment
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [ValidateSet('All', 'Managed', 'Native', 'Decoder', 'Audio', 'Export', 'Workbench')][string] $Target = 'Managed',
        [object] $HostInfo = (Get-AegiNextHost),
        [switch] $WithMediaTools,
        [string] $FfmpegRoot,
        [string] $SdlRoot,
        [switch] $RunTests,
        [ValidateSet('Core', 'Application', 'Rendering', 'Media', 'Export', 'Desktop', 'Desktop.Ui')][string[]] $TestProjects = @('Core', 'Application', 'Rendering', 'Media', 'Export', 'Desktop')
    )
    $checks = [System.Collections.Generic.List[object]]::new()
    $prefixes = @{}
    $problem = Get-AegiNextTargetProblem $HostInfo.Platform $Target -RunTests:$RunTests -TestProjects $TestProjects
    if ($problem)
    {
        $checks.Add((Get-AegiNextCheck 'Target' 'Unsupported' $problem))
    }
    else
    {
        $checks.Add((Get-AegiNextCheck 'Target' 'Ready' "$($HostInfo.Platform) / $Target"))
    }

    $manager = switch ($HostInfo.Platform) { 'MacOS' { 'brew' } 'Windows' { 'scoop' } default { '' } }
    $managerCommand = if ($manager) { Find-AegiNextCommand $manager } else { $null }
    if ($managerCommand)
    {
        $checks.Add((Get-AegiNextCheck 'PackageManager' 'Ready' "$managerCommand"))
    }
    else
    {
        $hint = switch ($HostInfo.Platform)
        {
            'MacOS' { 'Homebrew not found. Install from https://brew.sh and load brew shellenv.' }
            'Windows' { 'Scoop not found. Install from https://scoop.sh and reopen PowerShell.' }
            default { 'Automatic dependency installation is only supported with macOS Homebrew or Windows Scoop.' }
        }
        $checks.Add((Get-AegiNextCheck 'PackageManager' 'Optional' $hint))
    }

    if ($Target -in @('Decoder', 'Audio', 'Export', 'Workbench') -and !$problem)
    {
        $decoder = Get-AegiNextDecoderEnvironment -RepositoryRoot $RepositoryRoot -HostInfo $HostInfo -ManagerCommand $managerCommand -FfmpegRoot $FfmpegRoot -RunTests:$RunTests
        foreach ($check in $decoder.Checks)
        {
            $checks.Add($check)
        }
        $prefixes = $decoder.NativePrefixes
        if ($Target -in @('Audio', 'Workbench'))
        {
            $sdl = Get-AegiNextSdlEnvironment -RepositoryRoot $RepositoryRoot -HostInfo $HostInfo -ManagerCommand $managerCommand -SdlRoot $SdlRoot
            $checks.Add($sdl.Check)
            if ($sdl.Root)
            {
                $prefixes['sdl3'] = $sdl.Root
            }
        }
        if ($Target -in @('Export', 'Workbench') -and $prefixes.ContainsKey('ffmpeg'))
        {
            $checks.Add((Get-AegiNextExportCapabilityCheck -RepositoryRoot $RepositoryRoot -HostInfo $HostInfo -FfmpegRoot $prefixes['ffmpeg']))
        }
    }

    if ($WithMediaTools -and $Target -notin @('Decoder', 'Audio', 'Export', 'Workbench'))
    {
        $toolchainPath = Join-Path $RepositoryRoot 'src/AegiNext.Media/Probing/ffmpeg-toolchain.json'
        $toolchain = Get-Content -LiteralPath $toolchainPath -Raw -ErrorAction Stop | ConvertFrom-Json
        $package = switch ($HostInfo.Platform) { 'MacOS' { 'ffmpeg' } 'Windows' { 'main/ffmpeg' } default { '' } }
        foreach ($tool in @('ffprobe', 'ffmpeg'))
        {
            $checks.Add((Get-AegiNextMediaToolCheck $tool $RepositoryRoot $toolchain $package $manager))
        }
    }

    if ($Target -in @('Managed', 'All', 'Workbench'))
    {
        $sdk = (Get-Content -LiteralPath (Join-Path $RepositoryRoot 'global.json') -Raw | ConvertFrom-Json).sdk
        if ($sdk.rollForward -ne 'latestPatch' -or $sdk.allowPrerelease)
        {
            $checks.Add((Get-AegiNextCheck 'DotNetSdk' 'Invalid' 'The SDK check currently requires global.json latestPatch with allowPrerelease=false.'))
        }
        else
        {
            $dotnet = Find-AegiNextCommand 'dotnet'
            $package = if ($HostInfo.Platform -eq 'Windows') { 'main/dotnet-sdk' } elseif ($HostInfo.Platform -eq 'MacOS') { 'dotnet' } else { '' }
            if (!$dotnet)
            {
                $checks.Add((Get-AegiNextCheck 'DotNetSdk' 'Missing' "Requires SDK $($sdk.version) (same feature-band stable patch)." $package $manager))
            }
            else
            {
                $selected = Invoke-AegiNextCommand $dotnet @('--version') $RepositoryRoot
                if ($selected.ExitCode -eq 0 -and (Test-AegiNextSdkVersion $selected.Output.Trim() $sdk.version))
                {
                    $checks.Add((Get-AegiNextCheck 'DotNetSdk' 'Ready' "$($selected.Output.Trim()) via $dotnet"))
                }
                else
                {
                    $checks.Add((Get-AegiNextCheck 'DotNetSdk' 'Invalid' "Repository SDK resolution failed; requires $($sdk.version) in its feature band. $($selected.Output) Install the matching SDK; installed tools are not automatically upgraded or downgraded."))
                }
            }
        }
    }

    if ($Target -eq 'Workbench' -and !$problem -and @($checks | Where-Object { $_.Id -eq 'DotNetSdk' -and $_.Status -eq 'Ready' }).Count)
    {
        $checks.Add((Get-AegiNextSdkArchitectureCheck $RepositoryRoot $dotnet $HostInfo))
    }

    if ($HostInfo.Platform -eq 'MacOS' -and $Target -in @('Native', 'All'))
    {
        if ($HostInfo.Architecture -ne $HostInfo.ProcessArchitecture -or $HostInfo.Architecture -notin @('Arm64', 'X64'))
        {
            $checks.Add((Get-AegiNextCheck 'Architecture' 'Unsupported' 'Run a native arm64/x64 PowerShell matching this Mac; mixed Rosetta and native dependencies are unsupported.'))
        }
        else
        {
            $checks.Add((Get-AegiNextCheck 'Architecture' 'Ready' $HostInfo.Architecture))
        }

        if ($Target -eq 'All' -and @($checks | Where-Object { $_.Id -eq 'DotNetSdk' -and $_.Status -eq 'Ready' }).Count)
        {
            $checks.Add((Get-AegiNextSdkArchitectureCheck $RepositoryRoot $dotnet $HostInfo))
        }

        $os = Invoke-AegiNextCommand '/usr/bin/sw_vers' @('-productVersion') $RepositoryRoot
        if ($os.ExitCode -eq 0 -and $os.Output.Trim() -match '^\d+\.\d+(\.\d+)?$' -and [version]$os.Output.Trim() -ge [version]'14.0')
        {
            $checks.Add((Get-AegiNextCheck 'MacOS' 'Ready' $os.Output.Trim()))
        }
        else
        {
            $checks.Add((Get-AegiNextCheck 'MacOS' 'Unsupported' 'Native preview requires macOS 14 or newer.'))
        }

        $cmakeSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'native/CMakeLists.txt') -Raw
        $minimumCmake = [regex]::Match($cmakeSource, 'cmake_minimum_required\(VERSION ([\d.]+)\)').Groups[1].Value
        foreach ($tool in @(
            @{ Id = 'CMake'; Command = 'cmake'; Package = 'cmake'; Minimum = $minimumCmake },
            @{ Id = 'Ninja'; Command = 'ninja'; Package = 'ninja'; Minimum = '1.10' },
            @{ Id = 'PkgConfig'; Command = 'pkg-config'; Package = 'pkgconf'; Minimum = '1.8' }
        ))
        {
            $command = Find-AegiNextCommand $tool.Command
            if (!$command)
            {
                $checks.Add((Get-AegiNextCheck $tool.Id 'Missing' "Missing $($tool.Command) >= $($tool.Minimum)." $tool.Package 'brew'))
                continue
            }

            $result = Invoke-AegiNextCommand $command @('--version') $RepositoryRoot
            $versionText = [regex]::Match($result.Output, '\d+\.\d+(\.\d+)?').Value
            if ($result.ExitCode -eq 0 -and $versionText -and [version]$versionText -ge [version]$tool.Minimum)
            {
                $checks.Add((Get-AegiNextCheck $tool.Id 'Ready' "$versionText via $command"))
            }
            else
            {
                $checks.Add((Get-AegiNextCheck $tool.Id 'Invalid' "Requires $($tool.Minimum) or newer. $($result.Output)"))
            }
        }

        if ($RunTests -and !(Find-AegiNextCommand 'ctest'))
        {
            $checks.Add((Get-AegiNextCheck 'CTest' 'Missing' 'CTest is required for -RunTests; repair the CMake package or PATH.' 'cmake' 'brew'))
        }

        $xcrun = Find-AegiNextCommand 'xcrun'
        $compilerReady = $false
        if ($xcrun)
        {
            $compiler = Invoke-AegiNextCommand $xcrun @('--find', 'clang') $RepositoryRoot
            $sdkPath = Invoke-AegiNextCommand $xcrun @('--sdk', 'macosx', '--show-sdk-path') $RepositoryRoot
            $compilerReady = $compiler.ExitCode -eq 0 -and $sdkPath.ExitCode -eq 0 -and
                (Test-Path -LiteralPath $compiler.Output.Trim() -PathType Leaf) -and
                (Test-Path -LiteralPath $sdkPath.Output.Trim() -PathType Container)
        }
        if ($compilerReady)
        {
            $checks.Add((Get-AegiNextCheck 'AppleToolchain' 'Ready' $sdkPath.Output.Trim()))
        }
        else
        {
            $checks.Add((Get-AegiNextCheck 'AppleToolchain' 'Missing' 'Install/select Xcode or Command Line Tools, then resolve any license/setup prompt. Homebrew cannot replace the Apple SDK.'))
        }

        $dependencies = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'native/dependencies.json') -Raw | ConvertFrom-Json
        $manifest = Get-AegiNextDependencyManifest
        foreach ($name in @('libplacebo', 'molten-vk', 'vulkan-headers'))
        {
            $root = Find-AegiNextProjectSdk $RepositoryRoot $HostInfo $name
            if (!$root -and $managerCommand)
            {
                $prefix = Invoke-AegiNextCommand $managerCommand @('--prefix', $name) $RepositoryRoot
                if ($prefix.ExitCode -eq 0 -and ![string]::IsNullOrWhiteSpace($prefix.Output))
                {
                    $root = $prefix.Output.Trim()
                }
            }
            if (!$root -or !(Test-Path -LiteralPath $root -PathType Container))
            {
                $checks.Add((Get-AegiNextCheck $name 'Missing' "Requires locked project SDK $($manifest.packages[$name].version); use -InstallDependencies." $name 'project'))
                continue
            }
            try
            {
                Test-AegiNextPreparedSdk $root $RepositoryRoot $HostInfo $manifest $name
                $prefixes[$name] = $root
                $checks.Add((Get-AegiNextCheck $name 'Ready' "$($manifest.packages[$name].version) at $root"))
            }
            catch
            {
                $checks.Add((Get-AegiNextCheck $name 'Invalid' $_.Exception.Message $name 'project'))
            }
        }

        $pkgConfig = Find-AegiNextCommand 'pkg-config'
        if ($pkgConfig -and $prefixes.ContainsKey('libplacebo'))
        {
            $pkgEnvironment = Get-AegiNextNativePkgEnvironment $RepositoryRoot $HostInfo $prefixes
            $version = Invoke-AegiNextCommand $pkgConfig @('--modversion', 'libplacebo') $RepositoryRoot $pkgEnvironment
            $features = $dependencies.directDependencies.libplacebo.requiredFeatures
            $invalid = $version.ExitCode -ne 0 -or $version.Output.Trim() -ne $dependencies.directDependencies.libplacebo.version
            foreach ($feature in $features)
            {
                $value = Invoke-AegiNextCommand $pkgConfig @("--variable=pl_has_$feature", 'libplacebo') $RepositoryRoot $pkgEnvironment
                $invalid = $invalid -or $value.ExitCode -ne 0 -or $value.Output.Trim() -ne '1'
            }

            $checks.Add((Get-AegiNextCheck 'LibplaceboCapabilities' $(if ($invalid) { 'Invalid' } else { 'Ready' }) 'Requires the locked active libplacebo with Vulkan, vk_proc_addr and shaderc.'))
        }
    }

    [pscustomobject]@{
        HostInfo = $HostInfo
        Target = $Target
        WithMediaTools = [bool]$WithMediaTools
        Ready = @($checks | Where-Object Status -NotIn @('Ready', 'Optional')).Count -eq 0
        Checks = $checks.ToArray()
        NativePrefixes = $prefixes
    }
}

function Install-AegiNextDependency
{
    [CmdletBinding()]
    param([Parameter(Mandatory)][object] $Report, [Parameter(Mandatory)][string] $RepositoryRoot,
        [string] $FfmpegRoot, [string] $SdlRoot, [ValidateRange(1, 128)][int] $Jobs = 2)
    $projectNames = [Collections.Generic.List[string]]::new()
    foreach ($check in $Report.Checks)
    {
        switch ($check.Id)
        {
            'FfmpegSdk' { if (!$FfmpegRoot) { $projectNames.Add('ffmpeg') } }
            'SdlSdk' { if (!$SdlRoot) { $projectNames.Add('sdl3') } }
            'libplacebo' { $projectNames.Add('libplacebo') }
            'molten-vk' { $projectNames.Add('molten-vk') }
            'vulkan-headers' { $projectNames.Add('vulkan-headers') }
        }
    }
    $sdkIds = @('FfmpegSdk', 'SdlSdk', 'libplacebo', 'molten-vk', 'vulkan-headers')
    $missing = @($Report.Checks | Where-Object { $_.Status -eq 'Missing' -and $_.Package -and $_.Id -notin $sdkIds })
    foreach ($dependency in ($missing | Sort-Object Manager, Package -Unique))
    {
        $manager = Find-AegiNextCommand $dependency.Manager
        if (!$manager)
        {
            throw "Cannot install $($dependency.Package): $($dependency.Manager) is unavailable. Follow the package-manager setup instructions and reopen your terminal."
        }

        Write-Information -InformationAction Continue -MessageData "Installing missing dependency: $($dependency.Package)"
        $result = Invoke-AegiNextCommand $manager @('install', $dependency.Package) $RepositoryRoot
        if ($result.Output)
        {
            Write-Information -InformationAction Continue -MessageData $result.Output
        }
        if ($result.ExitCode -ne 0)
        {
            $commandFailure = [InvalidOperationException]::new("$($dependency.Manager) failed to install $($dependency.Package) (exit $($result.ExitCode)).")
            $commandFailure.Data['ExitCode'] = $result.ExitCode
            throw $commandFailure
        }
    }
    if ($projectNames.Count)
    {
        Install-AegiNextProjectSdk -RepositoryRoot $RepositoryRoot -HostInfo $Report.HostInfo -Names ($projectNames.ToArray() | Select-Object -Unique) -Jobs $Jobs
    }
}

function Get-AegiNextBuildPlan
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [ValidateSet('All', 'Managed', 'Native', 'Decoder', 'Audio', 'Export', 'Workbench')][string] $Target = 'Managed',
        [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
        [object] $HostInfo = (Get-AegiNextHost),
        [switch] $RunTests,
        [ValidateSet('Core', 'Application', 'Rendering', 'Media', 'Export', 'Desktop', 'Desktop.Ui')][string[]] $TestProjects = @('Core', 'Application', 'Rendering', 'Media', 'Export', 'Desktop'),
        [ValidateRange(1, 128)][int] $Jobs = 2,
        [ValidateSet('osx-arm64', 'osx-x64', 'win-x64')][string] $RuntimeIdentifier,
        [hashtable] $NativePrefixes = @{}
    )
    $rid = Get-AegiNextRuntimeIdentifier -HostInfo $HostInfo -RuntimeIdentifier $RuntimeIdentifier
    $problem = Get-AegiNextTargetProblem $HostInfo.Platform $Target -RunTests:$RunTests -TestProjects $TestProjects
    if ($problem)
    {
        throw $problem
    }

    if ($Target -in @('Decoder', 'Audio', 'Export'))
    {
        return Get-AegiNextDecoderBuildPlan -RepositoryRoot $RepositoryRoot -HostInfo $HostInfo -Configuration $Configuration -RunTests:$RunTests -Jobs $Jobs -NativePrefixes $NativePrefixes -Target $Target
    }

    $plan = [System.Collections.Generic.List[object]]::new()
    if ($Target -eq 'Workbench')
    {
        foreach ($component in @('Decoder', 'Audio', 'Export'))
        {
            foreach ($step in (Get-AegiNextDecoderBuildPlan -RepositoryRoot $RepositoryRoot -HostInfo $HostInfo -Configuration $Configuration -RunTests:$RunTests -Jobs $Jobs -NativePrefixes $NativePrefixes -Target $component))
            {
                $plan.Add($step)
            }
        }
    }
    if ($Target -in @('Native', 'All'))
    {
        foreach ($package in @('libplacebo', 'molten-vk', 'vulkan-headers'))
        {
            if (!$NativePrefixes.ContainsKey($package))
            {
                throw "Native dependency prefix is missing: $package. Run the environment check first."
            }
        }
        $architecture = $HostInfo.Architecture.ToLowerInvariant()
        $cmakeArchitecture = if ($architecture -eq 'arm64') { 'arm64' } else { 'x86_64' }
        $directory = Join-Path $RepositoryRoot "artifacts/native/build-macos-$architecture-$($Configuration.ToLowerInvariant())"
        $nativeOutput = Join-Path $RepositoryRoot "artifacts/native/osx-$architecture/$Configuration"
        $environment = Get-AegiNextNativePkgEnvironment $RepositoryRoot $HostInfo $NativePrefixes
        $plan.Add([pscustomobject]@{
            Label = 'Configure native'; FilePath = 'cmake'; WorkingDirectory = $RepositoryRoot; Environment = $environment
            Arguments = [string[]]@('-S', (Join-Path $RepositoryRoot 'native'), '-B', $directory, '-G', 'Ninja',
                "-DCMAKE_BUILD_TYPE=$Configuration", '-DCMAKE_OSX_DEPLOYMENT_TARGET=14.0', "-DCMAKE_OSX_ARCHITECTURES=$cmakeArchitecture",
                "-DMOLTENVK_ROOT=$($NativePrefixes['molten-vk'])", "-DVULKAN_HEADERS_ROOT=$($NativePrefixes['vulkan-headers'])",
                "-DAEGINEXT_NATIVE_OUTPUT_DIR=$nativeOutput", '-DBUILD_TESTING=ON')
        })
        $plan.Add([pscustomobject]@{
            Label = 'Build native'; FilePath = 'cmake'; WorkingDirectory = $RepositoryRoot; Environment = @{}
            Arguments = [string[]]@('--build', $directory, '--config', $Configuration, '--parallel', "$Jobs")
        })
        if ($RunTests)
        {
            $plan.Add([pscustomobject]@{
                Label = 'Test native'; FilePath = 'ctest'; WorkingDirectory = $RepositoryRoot; Environment = @{}
                Arguments = [string[]]@('--test-dir', $directory, '--build-config', $Configuration, '--output-on-failure')
            })
        }
    }

    if ($Target -in @('Managed', 'All', 'Workbench'))
    {
        if ($RunTests -and $TestProjects.Count -eq 0)
        {
            throw '-RunTests requires at least one managed test project.'
        }
        $isRelease = $Configuration -eq 'Release'
        $solution = Join-Path $RepositoryRoot $(if ($isRelease) { 'AegiNext.Product.slnf' } else { 'AegiNext.sln' })
        $restoreRuntimeArguments = [string[]]@()
        $useRuntime = $RuntimeIdentifier -or $HostInfo.Platform -eq 'Windows' -or $Target -eq 'Workbench'
        if ($useRuntime)
        {
            $restoreRuntimeArguments = [string[]]@('-r', $rid, "-p:AegiNextRuntimeIdentifier=$rid")
        }
        $plan.Add([pscustomobject]@{
            Label = 'Restore managed'; FilePath = 'dotnet'; WorkingDirectory = $RepositoryRoot; Environment = @{}
            Arguments = [string[]](@('restore', $solution, '--disable-build-servers') + $restoreRuntimeArguments)
        })
        $plan.Add([pscustomobject]@{
            Label = 'Build managed'; FilePath = 'dotnet'; WorkingDirectory = $RepositoryRoot; Environment = @{}
            Arguments = [string[]](@('build', $solution, '--configuration', $Configuration, '--no-restore', '--disable-build-servers') + $(if ($useRuntime) { @("-p:AegiNextRuntimeIdentifier=$rid") } else { @() }))
        })
        if ($RunTests)
        {
            foreach ($project in ($TestProjects | Select-Object -Unique))
            {
                $testProject = Join-Path $RepositoryRoot "Tests/AegiNext.$project.Tests/AegiNext.$project.Tests.csproj"
                if ($isRelease)
                {
                    $plan.Add([pscustomobject]@{
                        Label = "Restore test $project"; FilePath = 'dotnet'; WorkingDirectory = $RepositoryRoot; Environment = @{}
                        Arguments = [string[]](@('restore', $testProject, '--disable-build-servers') + $restoreRuntimeArguments)
                    })
                }
                $testEnvironment = @{}
                if ($Target -eq 'Workbench' -and $HostInfo.Platform -eq 'Windows' -and $project -eq 'Media')
                {
                    $testEnvironment.AEGINEXT_RUN_NATIVE_RUNTIME_TESTS = '1'
                }
                $plan.Add([pscustomobject]@{
                    Label = "Test $project"; FilePath = 'dotnet'; WorkingDirectory = $RepositoryRoot; Environment = $testEnvironment
                    Arguments = [string[]](@('test', $testProject,
                        '--configuration', $Configuration, '--no-restore', '--disable-build-servers') + $(if ($useRuntime) { @('-r', $rid, "-p:AegiNextRuntimeIdentifier=$rid") } else { @() }))
                })
            }
        }
    }
    $plan.ToArray()
}

function Invoke-AegiNextBuild
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [ValidateSet('All', 'Managed', 'Native', 'Decoder', 'Audio', 'Export', 'Workbench')][string] $Target = 'Managed',
        [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
        [switch] $CheckEnvironment,
        [switch] $InstallDependencies,
        [switch] $WithMediaTools,
        [string] $FfmpegRoot,
        [string] $SdlRoot,
        [switch] $RunTests,
        [ValidateSet('Core', 'Application', 'Rendering', 'Media', 'Export', 'Desktop', 'Desktop.Ui')][string[]] $TestProjects = @('Core', 'Application', 'Rendering', 'Media', 'Export', 'Desktop'),
        [ValidateRange(1, 128)][int] $Jobs = 2,
        [ValidateSet('osx-arm64', 'osx-x64', 'win-x64')][string] $RuntimeIdentifier,
        [string] $ReportPath
    )
    if ($CheckEnvironment -and $InstallDependencies)
    {
        throw '-CheckEnvironment is read-only and cannot be combined with -InstallDependencies.'
    }

    if ($FfmpegRoot -and $Target -notin @('Decoder', 'Audio', 'Export', 'Workbench'))
    {
        throw '-FfmpegRoot applies only to -Target Decoder, Audio, Export or Workbench.'
    }
    if ($SdlRoot -and $Target -notin @('Audio', 'Workbench'))
    {
        throw '-SdlRoot applies only to -Target Audio or Workbench.'
    }

    $RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    $hostInfo = Get-AegiNextHost
    $null = Get-AegiNextRuntimeIdentifier -HostInfo $hostInfo -RuntimeIdentifier $RuntimeIdentifier
    $environmentArguments = @{ RepositoryRoot = $RepositoryRoot; Target = $Target; HostInfo = $hostInfo; WithMediaTools = $WithMediaTools; FfmpegRoot = $FfmpegRoot; SdlRoot = $SdlRoot; RunTests = $RunTests; TestProjects = $TestProjects }
    $report = Get-AegiNextEnvironment @environmentArguments
    if ($InstallDependencies -and !@($report.Checks | Where-Object Status -EQ 'Unsupported').Count)
    {
        Install-AegiNextDependency -Report $report -RepositoryRoot $RepositoryRoot -FfmpegRoot $FfmpegRoot -SdlRoot $SdlRoot -Jobs $Jobs
        $report = Get-AegiNextEnvironment @environmentArguments
    }

    $report.Checks | Format-Table Id, Status, Detail -Wrap | Out-Host
    if ($ReportPath)
    {
        $reportFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ReportPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportFile)) | Out-Null
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportFile -Encoding utf8
    }

    if (!$report.Ready)
    {
        Write-Information -InformationAction Continue -MessageData 'Environment is not ready. Resolve the listed items; -InstallDependencies prepares locked project SDKs and installs missing tools. Explicit SDK roots are never replaced.'
        return 2
    }

    if ($CheckEnvironment)
    {
        Write-Information -InformationAction Continue -MessageData 'Environment check passed. No dependencies were installed and no build was started.'
        return 0
    }

    $planArguments = @{ RepositoryRoot = $RepositoryRoot; Target = $Target; Configuration = $Configuration; HostInfo = $hostInfo; RunTests = $RunTests; TestProjects = $TestProjects; Jobs = $Jobs; NativePrefixes = $report.NativePrefixes }
    if ($RuntimeIdentifier)
    {
        $planArguments.RuntimeIdentifier = $RuntimeIdentifier
    }
    $plan = @(Get-AegiNextBuildPlan @planArguments)
    foreach ($step in $plan)
    {
        Write-Information -InformationAction Continue -MessageData "[$($step.Label)]"
        $result = Invoke-AegiNextCommand -FilePath $step.FilePath -Arguments $step.Arguments -WorkingDirectory $step.WorkingDirectory -Environment $step.Environment -StreamOutput
        if ($result.ExitCode -ne 0)
        {
            $commandFailure = [InvalidOperationException]::new("$($step.Label) failed (exit $($result.ExitCode)).")
            $commandFailure.Data['ExitCode'] = $result.ExitCode
            throw $commandFailure
        }
    }

    Write-Information -InformationAction Continue -MessageData "Build completed: $($hostInfo.Platform) / $Target / $Configuration."
    if (!$RunTests)
    {
        Write-Information -InformationAction Continue -MessageData 'Tests were not requested. Use -RunTests and optionally -TestProjects Media.'
    }
    return 0
}

Export-ModuleMember -Function Get-AegiNextHost, Get-AegiNextRuntimeIdentifier, Find-AegiNextCommand, Invoke-AegiNextCommand, Test-AegiNextSdkVersion,
    Get-AegiNextEnvironment, Install-AegiNextDependency, Get-AegiNextBuildPlan, Invoke-AegiNextBuild
