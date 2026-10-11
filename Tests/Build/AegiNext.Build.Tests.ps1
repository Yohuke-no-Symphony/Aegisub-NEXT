#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    Import-Module (Join-Path $sourceRoot 'scripts/build/AegiNext.Build.psm1') -Force

    function New-BuildTestRepository
    {
        param([string] $Root)

        [IO.Directory]::CreateDirectory((Join-Path $Root 'native')) | Out-Null
        Copy-Item -LiteralPath (Join-Path $sourceRoot 'global.json') -Destination $Root
        Copy-Item -LiteralPath (Join-Path $sourceRoot 'AegiNext.Product.slnf') -Destination $Root
        Copy-Item -LiteralPath (Join-Path $sourceRoot 'native/dependencies.json') -Destination (Join-Path $Root 'native')
        Set-Content -LiteralPath (Join-Path $Root 'AegiNext.sln') -Value ''
    }

    function New-BuildTestHost
    {
        param([string] $Platform = 'MacOS', [string] $Architecture = 'Arm64')

        [pscustomobject]@{
            Platform = $Platform
            Architecture = $Architecture
            ProcessArchitecture = $Architecture
        }
    }

    function Get-BuildTestExecutableName
    {
        param([object] $Step)

        [IO.Path]::GetFileNameWithoutExtension($Step.FilePath).ToLowerInvariant()
    }
}

Describe 'SDK selection follows the repository stable latestPatch contract' {
    It 'classifies selected SDK <SelectedVersion> as <Expected>' -TestCases @(
        @{ SelectedVersion = '10.0.401'; Expected = $true }
        @{ SelectedVersion = '10.0.402'; Expected = $true }
        @{ SelectedVersion = '10.0.499'; Expected = $true }
        @{ SelectedVersion = '10.0.400'; Expected = $false }
        @{ SelectedVersion = '10.0.500'; Expected = $false }
        @{ SelectedVersion = '10.1.401'; Expected = $false }
        @{ SelectedVersion = '9.0.499'; Expected = $false }
        @{ SelectedVersion = '11.0.401'; Expected = $false }
        @{ SelectedVersion = '10.0.401-preview.1'; Expected = $false }
        @{ SelectedVersion = '10.0.401.1'; Expected = $false }
        @{ SelectedVersion = '10.0.401+metadata'; Expected = $false }
        @{ SelectedVersion = 'not-a-version'; Expected = $false }
        @{ SelectedVersion = ''; Expected = $false }
    ) {
        param($SelectedVersion, $Expected)

        Test-AegiNextSdkVersion -SelectedVersion $SelectedVersion -RequiredVersion '10.0.401' |
            Should -Be $Expected
    }

    It 'uses the supplied requirement instead of a hard-coded feature band' {
        Test-AegiNextSdkVersion -SelectedVersion '11.2.306' -RequiredVersion '11.2.305' | Should -BeTrue
        Test-AegiNextSdkVersion -SelectedVersion '11.2.304' -RequiredVersion '11.2.305' | Should -BeFalse
        Test-AegiNextSdkVersion -SelectedVersion '11.2.405' -RequiredVersion '11.2.305' | Should -BeFalse
    }
}

Describe 'Build plans are pure and preserve target boundaries' {
    BeforeEach {
        $repository = Join-Path $TestDrive 'repository with spaces [plan]'
        $prefixes = @{
            libplacebo = (Join-Path $TestDrive 'packages with spaces/libplacebo')
            'molten-vk' = (Join-Path $TestDrive 'packages with spaces/molten-vk')
            'vulkan-headers' = (Join-Path $TestDrive 'packages with spaces/vulkan-headers')
        }
        $parameters = @{
            RepositoryRoot = $repository
            Target = 'All'
            Configuration = 'Release'
            HostInfo = (New-BuildTestHost)
            NativePrefixes = $prefixes
            Jobs = 2
        }
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build { throw 'Planning must not execute tools.' }
    }

    It 'plans native before managed and does not create an output directory' {
        $plan = @(Get-AegiNextBuildPlan @parameters)

        $plan.Count | Should -Be 4
        @(foreach ($step in $plan) { Get-BuildTestExecutableName $step }) | Should -Be @('cmake', 'cmake', 'dotnet', 'dotnet')
        $plan[2].Arguments[0] | Should -Be 'restore'
        $plan[2].Arguments | Should -Not -Contain '--locked-mode'
        $plan[3].Arguments[0] | Should -Be 'build'
        $plan[3].Arguments | Should -Contain '--no-restore'
        $plan[3].Arguments | Should -Contain 'Release'
        Test-Path -LiteralPath $repository | Should -BeFalse
        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 0 -Exactly
    }

    It 'defaults to Managed so ordinary development does not require the optional HDR backend' {
        $plan = @(Get-AegiNextBuildPlan -RepositoryRoot $repository -HostInfo (New-BuildTestHost 'Windows' 'X64'))

        $plan.Count | Should -Be 2
        @(foreach ($step in $plan) { Get-BuildTestExecutableName $step }) | Should -Be @('dotnet', 'dotnet')
    }

    It 'restores and builds only product projects for Release <Target> on <Platform>' -TestCases @(
        @{ Target = 'Managed'; Platform = 'MacOS'; Architecture = 'Arm64' }
        @{ Target = 'Managed'; Platform = 'Windows'; Architecture = 'X64' }
        @{ Target = 'All'; Platform = 'MacOS'; Architecture = 'Arm64' }
        @{ Target = 'Workbench'; Platform = 'MacOS'; Architecture = 'Arm64' }
        @{ Target = 'Workbench'; Platform = 'Windows'; Architecture = 'Arm64' }
    ) {
        param($Target, $Platform, $Architecture)

        $parameters.Target = $Target
        $parameters.HostInfo = New-BuildTestHost $Platform $Architecture
        $parameters.NativePrefixes += @{ ffmpeg = '/ffmpeg'; sdl3 = '/sdl'; cc = 'cc'; cxx = 'c++'; cmake = 'cmake'; ninja = 'ninja' }
        $plan = @(Get-AegiNextBuildPlan @parameters)
        $managedSteps = @($plan | Where-Object { (Get-BuildTestExecutableName $_) -eq 'dotnet' })

        $managedSteps.Count | Should -Be 2
        @($managedSteps.Label) | Should -Be @('Restore managed', 'Build managed')
        foreach ($step in $managedSteps)
        {
            $step.Arguments | Should -Contain (Join-Path $repository 'AegiNext.Product.slnf')
            $step.Arguments | Should -Not -Contain (Join-Path $repository 'AegiNext.sln')
            @($step.Arguments | Where-Object { $_ -like '*Tests*' }).Count | Should -Be 0
        }
        $managedSteps[0].Arguments | Should -Not -Contain '--locked-mode'
        $managedSteps[1].Arguments | Should -Contain '--no-restore'
        $managedSteps[1].Arguments | Should -Contain 'Release'
        Test-Path -LiteralPath $repository | Should -BeFalse
    }

    It 'preserves the full solution entry point for Debug with RunTests <RunTests>' -TestCases @(
        @{ RunTests = $false; ExpectedSteps = 2 }
        @{ RunTests = $true; ExpectedSteps = 3 }
    ) {
        param($RunTests, $ExpectedSteps)

        $plan = @(Get-AegiNextBuildPlan -RepositoryRoot $repository -Configuration Debug -HostInfo (New-BuildTestHost) -RunTests:$RunTests -TestProjects Media)

        $plan.Count | Should -Be $ExpectedSteps
        foreach ($step in $plan[0..1])
        {
            $step.Arguments | Should -Contain (Join-Path $repository 'AegiNext.sln')
            $step.Arguments | Should -Not -Contain (Join-Path $repository 'AegiNext.Product.slnf')
        }
        $plan[0].Arguments | Should -Not -Contain '--locked-mode'
        $plan[1].Arguments | Should -Contain '--no-restore'
        $plan[1].Arguments | Should -Contain 'Debug'
        if ($RunTests)
        {
            $plan[2].Label | Should -Be 'Test Media'
            $plan[2].Arguments | Should -Contain '--no-restore'
        }
    }

    It 'restores and runs only unique selected Release test projects' {
        $plan = @(Get-AegiNextBuildPlan -RepositoryRoot $repository -HostInfo (New-BuildTestHost 'Windows' 'Arm64') -RunTests -TestProjects Core,Media,Core)

        @($plan.Label) | Should -Be @('Restore managed', 'Build managed', 'Restore test Core', 'Test Core', 'Restore test Media', 'Test Media')
        foreach ($index in @(2, 4))
        {
            $restore = $plan[$index]
            $test = $plan[$index + 1]
            $restore.Arguments[0] | Should -Be 'restore'
            $restore.Arguments[1] | Should -Be $test.Arguments[1]
            $restore.Arguments | Should -Not -Contain '--locked-mode'
            $restore.Arguments | Should -Contain '-r'
            $restore.Arguments | Should -Contain 'win-x64'
            $restore.Arguments | Should -Contain '-p:AegiNextRuntimeIdentifier=win-x64'
            $test.Arguments[0] | Should -Be 'test'
            $test.Arguments | Should -Contain '--no-restore'
            $test.Arguments | Should -Not -Contain '--no-build'
        }
        @($plan | Where-Object { $_.Arguments -contains (Join-Path $repository 'Tests/AegiNext.Desktop.Tests/AegiNext.Desktop.Tests.csproj') }).Count | Should -Be 0
    }

    It 'rejects an empty explicit test selection for <Configuration>' -TestCases @(
        @{ Configuration = 'Debug' }
        @{ Configuration = 'Release' }
    ) {
        param($Configuration)

        $parameters.Configuration = $Configuration
        $parameters.Target = 'Managed'
        { Get-AegiNextBuildPlan @parameters -RunTests -TestProjects @() } |
            Should -Throw '*at least one managed test project*'
    }

    It 'sets the early RID consistently for restore build and tests on <RuntimeIdentifier>' -TestCases @(
        @{ Platform = 'MacOS'; Architecture = 'Arm64'; RuntimeIdentifier = 'osx-arm64' }
        @{ Platform = 'MacOS'; Architecture = 'X64'; RuntimeIdentifier = 'osx-x64' }
        @{ Platform = 'Windows'; Architecture = 'Arm64'; RuntimeIdentifier = 'win-x64' }
    ) {
        param($Platform, $Architecture, $RuntimeIdentifier)
        $plan = @(Get-AegiNextBuildPlan -RepositoryRoot $repository -Target Managed -HostInfo (New-BuildTestHost $Platform $Architecture) -RuntimeIdentifier $RuntimeIdentifier -RunTests -TestProjects Media)
        foreach ($step in $plan)
        {
            ,$step.Arguments | Should -BeOfType [string[]]
            $step.Arguments | Should -Contain "-p:AegiNextRuntimeIdentifier=$RuntimeIdentifier"
        }
        $plan[0].Arguments | Should -Contain '-r'
        $plan[0].Arguments | Should -Contain $RuntimeIdentifier
        $plan[0].Arguments | Should -Not -Contain '--locked-mode'
        $plan[1].Arguments | Should -Not -Contain '-r'
        $plan[2].Arguments | Should -Contain '-r'
        $plan[2].Arguments | Should -Contain $RuntimeIdentifier
        $plan[2].Arguments | Should -Not -Contain '--locked-mode'
        $plan[3].Arguments | Should -Contain '-r'
        $plan[3].Arguments | Should -Contain $RuntimeIdentifier
        $plan[3].Arguments | Should -Contain '--no-restore'
    }

    It 'preserves portable Mac restore without assigning an implicit RID with RunTests <RunTests>' -TestCases @(
        @{ RunTests = $false }
        @{ RunTests = $true }
    ) {
        param($RunTests)

        $plan = @(Get-AegiNextBuildPlan -RepositoryRoot $repository -Target Managed -HostInfo (New-BuildTestHost) -RunTests:$RunTests -TestProjects Core)
        foreach ($step in $plan)
        {
            $step.Arguments | Should -Not -Contain '-r'
            @($step.Arguments | Where-Object { $_ -like '-p:AegiNextRuntimeIdentifier=*' }).Count | Should -Be 0
            @($step.Arguments | Where-Object { [string]::IsNullOrEmpty($_) }).Count | Should -Be 0
        }
    }

    It 'keeps native configuration, architecture, job count and prefixes as individual arguments' {
        $parameters.Configuration = 'Debug'
        $parameters.Jobs = 3
        $plan = @(Get-AegiNextBuildPlan @parameters)
        $buildDirectory = Join-Path $repository 'artifacts/native/build-macos-arm64-debug'

        $plan[0].Arguments | Should -Contain $buildDirectory
        $plan[0].Arguments | Should -Contain '-DCMAKE_BUILD_TYPE=Debug'
        $plan[0].Arguments | Should -Contain '-DCMAKE_OSX_ARCHITECTURES=arm64'
        $plan[0].Arguments | Should -Contain "-DMOLTENVK_ROOT=$($prefixes['molten-vk'])"
        $plan[0].Arguments | Should -Contain "-DVULKAN_HEADERS_ROOT=$($prefixes['vulkan-headers'])"
        $plan[0].Arguments | Should -Contain "-DAEGINEXT_NATIVE_OUTPUT_DIR=$(Join-Path $repository 'artifacts/native/osx-arm64/Debug')"
        $plan[1].Arguments | Should -Contain $buildDirectory
        $plan[1].Arguments | Should -Contain '--parallel'
        $plan[1].Arguments | Should -Contain '3'
        $plan[3].Arguments | Should -Contain 'Debug'
        foreach ($step in $plan)
        {
            $step.Label | Should -Not -BeNullOrEmpty
            ,$step.Arguments | Should -BeOfType [string[]]
            $step.WorkingDirectory | Should -Be $repository
            $step.Environment | Should -BeOfType [hashtable]
        }
    }

    It 'isolates Debug and Release native outputs for <Architecture> and keeps each plan consistent' -TestCases @(
        @{ Architecture = 'Arm64'; RuntimeIdentifier = 'osx-arm64' }
        @{ Architecture = 'X64'; RuntimeIdentifier = 'osx-x64' }
    ) {
        param($Architecture, $RuntimeIdentifier)

        $parameters.HostInfo = New-BuildTestHost 'MacOS' $Architecture
        $outputArguments = [Collections.Generic.List[string]]::new()
        foreach ($configuration in @('Debug', 'Release'))
        {
            $parameters.Configuration = $configuration
            $plan = @(Get-AegiNextBuildPlan @parameters)
            $expectedOutput = Join-Path $repository "artifacts/native/$RuntimeIdentifier/$configuration"
            $expectedBuild = Join-Path $repository "artifacts/native/build-macos-$($Architecture.ToLowerInvariant())-$($configuration.ToLowerInvariant())"
            $actualOutputs = @($plan[0].Arguments | Where-Object { $_ -like '-DAEGINEXT_NATIVE_OUTPUT_DIR=*' })

            $actualOutputs.Count | Should -Be 1
            $actualOutputs[0] | Should -Be "-DAEGINEXT_NATIVE_OUTPUT_DIR=$expectedOutput"
            $plan[0].Arguments | Should -Contain "-DCMAKE_BUILD_TYPE=$configuration"
            $plan[0].Arguments | Should -Contain $expectedBuild
            $plan[1].Arguments | Should -Contain $expectedBuild
            $plan[1].Arguments | Should -Contain $configuration
            $plan[3].Arguments | Should -Contain $configuration
            $outputArguments.Add($actualOutputs[0])
        }

        $outputArguments[0] | Should -Not -Be $outputArguments[1]
    }

    It 'runs CTest before only the requested managed test project' {
        $plan = @(Get-AegiNextBuildPlan @parameters -RunTests -TestProjects @('Media'))

        $plan.Count | Should -Be 7
        Get-BuildTestExecutableName $plan[2] | Should -Be 'ctest'
        $plan[2].Arguments | Should -Contain '--output-on-failure'
        $plan[5].Arguments[0] | Should -Be 'restore'
        $plan[5].Arguments | Should -Not -Contain '--locked-mode'
        $plan[6].Arguments[0] | Should -Be 'test'
        $plan[6].Arguments | Should -Contain (Join-Path $repository 'Tests/AegiNext.Media.Tests/AegiNext.Media.Tests.csproj')
        $plan[6].Arguments | Should -Not -Contain '--no-build'
        $plan[6].Arguments | Should -Contain '--no-restore'
        $plan[6].Arguments | Should -Contain 'Release'
    }

    It 'defaults RunTests to the six managed projects including export and desktop lifecycle tests' {
        $parameters.Target = 'Managed'
        $plan = @(Get-AegiNextBuildPlan @parameters -RunTests)

        $plan.Count | Should -Be 14
        foreach ($name in @('Core', 'Application', 'Rendering', 'Media', 'Export', 'Desktop'))
        {
            @($plan | Where-Object { $_.Arguments -contains (Join-Path $repository "Tests/AegiNext.$name.Tests/AegiNext.$name.Tests.csproj") }).Count |
                Should -Be 2
        }
    }

    It 'selects only desktop tests when explicitly requested' {
        $parameters.Target = 'Managed'
        $plan = @(Get-AegiNextBuildPlan @parameters -RunTests -TestProjects @('Desktop'))

        $plan.Count | Should -Be 4
        $plan[2].Arguments | Should -Contain (Join-Path $repository 'Tests/AegiNext.Desktop.Tests/AegiNext.Desktop.Tests.csproj')
        $plan[2].Arguments | Should -Not -Contain '--locked-mode'
        $plan[3].Arguments | Should -Contain (Join-Path $repository 'Tests/AegiNext.Desktop.Tests/AegiNext.Desktop.Tests.csproj')
        $plan[3].Arguments | Should -Not -Contain '--no-build'
        $plan[3].Arguments | Should -Contain '--no-restore'
    }

    It 'keeps Native independent from dotnet and xUnit' {
        $parameters.Target = 'Native'
        $plan = @(Get-AegiNextBuildPlan @parameters -RunTests)

        @(foreach ($step in $plan) { Get-BuildTestExecutableName $step }) | Should -Be @('cmake', 'cmake', 'ctest')
    }

    It 'rejects <Platform> <Target> instead of silently falling back' -TestCases @(
        @{ Platform = 'Windows'; Target = 'All' }
        @{ Platform = 'Windows'; Target = 'Native' }
        @{ Platform = 'Linux'; Target = 'All' }
        @{ Platform = 'Linux'; Target = 'Native' }
    ) {
        param($Platform, $Target)

        $parameters.HostInfo = New-BuildTestHost $Platform 'X64'
        $parameters.Target = $Target

        { Get-AegiNextBuildPlan @parameters } | Should -Throw
        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 0 -Exactly
    }

    It 'supports Managed compilation on <Platform> without native prefixes' -TestCases @(
        @{ Platform = 'Windows' }
        @{ Platform = 'Linux' }
    ) {
        param($Platform)

        $parameters.HostInfo = New-BuildTestHost $Platform 'X64'
        $parameters.Target = 'Managed'
        $parameters.NativePrefixes = @{}
        $plan = @(Get-AegiNextBuildPlan @parameters)

        $plan.Count | Should -Be 2
        @(foreach ($step in $plan) { Get-BuildTestExecutableName $step }) | Should -Be @('dotnet', 'dotnet')
    }

    It 'rejects Rendering tests on Linux but permits Core and Media' {
        $parameters.HostInfo = New-BuildTestHost 'Linux' 'X64'
        $parameters.Target = 'Managed'

        { Get-AegiNextBuildPlan @parameters -RunTests -TestProjects @('Rendering') } | Should -Throw
        $plan = @(Get-AegiNextBuildPlan @parameters -RunTests -TestProjects @('Core', 'Media'))
        $plan.Count | Should -Be 6
        @($plan | Where-Object { $_.Arguments[0] -eq 'test' }).Count | Should -Be 2
    }
}

Describe 'Product solution filter preserves the production dependency graph' {
    It 'includes every source project exactly once and excludes test projects' {
        $filter = Get-Content -LiteralPath (Join-Path $sourceRoot 'AegiNext.Product.slnf') -Raw | ConvertFrom-Json
        $filter.solution.path | Should -Be 'AegiNext.sln'
        $projects = @($filter.solution.projects | ForEach-Object { $_.Replace('\', '/') })
        $sourceProjects = @(Get-ChildItem -Path (Join-Path $sourceRoot 'src/*/*.csproj') -File |
            ForEach-Object { [IO.Path]::GetRelativePath($sourceRoot, $_.FullName).Replace('\', '/') })

        $projects.Count | Should -BeGreaterThan 0
        @($projects | Sort-Object) | Should -Be @($sourceProjects | Sort-Object)
        @($projects | Sort-Object -Unique).Count | Should -Be $projects.Count
        $solution = Get-Content -LiteralPath (Join-Path $sourceRoot $filter.solution.path) -Raw
        foreach ($project in $filter.solution.projects)
        {
            $solution | Should -Match ([regex]::Escape('"' + $project + '"'))
        }
    }

    It 'keeps every production project reference within the product filter' {
        $filter = Get-Content -LiteralPath (Join-Path $sourceRoot 'AegiNext.Product.slnf') -Raw | ConvertFrom-Json
        $projects = @($filter.solution.projects | ForEach-Object { $_.Replace('\', '/') })
        foreach ($project in $projects)
        {
            $projectPath = Join-Path $sourceRoot $project
            $document = [xml](Get-Content -LiteralPath $projectPath -Raw)
            foreach ($reference in $document.SelectNodes('/Project/ItemGroup/ProjectReference'))
            {
                $referencePath = [IO.Path]::GetFullPath((Join-Path (Split-Path $projectPath) $reference.Include))
                $projects | Should -Contain ([IO.Path]::GetRelativePath($sourceRoot, $referencePath).Replace('\', '/'))
            }
        }
    }
}

Describe 'Environment diagnosis resolves the SDK inside the repository' {
    BeforeEach {
        $repository = Join-Path $TestDrive 'repository with spaces [environment]'
        New-BuildTestRepository $repository
        $hostInfo = New-BuildTestHost 'Windows' 'X64'
        Mock Find-AegiNextCommand -ModuleName AegiNext.Build { return $null }
        Mock Find-AegiNextCommand -ModuleName AegiNext.Build { return 'dotnet' } -ParameterFilter { $Name -eq 'dotnet' }
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build { throw 'Unexpected environment command.' }
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build {
            [pscustomobject]@{ ExitCode = 0; Output = '10.0.402' }
        } -ParameterFilter { $FilePath -eq 'dotnet' -and $Arguments -contains '--version' }
    }

    It 'accepts the SDK selected from global.json without requiring a package manager' {
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Managed -HostInfo $hostInfo

        $report.Ready | Should -BeTrue
        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 1 -Exactly -ParameterFilter {
            $FilePath -eq 'dotnet' -and $Arguments -contains '--version' -and $WorkingDirectory -eq $repository
        }
        Test-Path -LiteralPath (Join-Path $repository 'artifacts') | Should -BeFalse
    }

    It 'reports a missing SDK command without trying to install it' {
        Mock Find-AegiNextCommand -ModuleName AegiNext.Build { return $null } -ParameterFilter { $Name -eq 'dotnet' }
        Mock Install-AegiNextDependency -ModuleName AegiNext.Build { throw 'Diagnosis must not install.' }

        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Managed -HostInfo $hostInfo

        $report.Ready | Should -BeFalse
        @($report.Checks | Where-Object Status -eq 'Missing').Count | Should -BeGreaterThan 0
        Should -Invoke Install-AegiNextDependency -ModuleName AegiNext.Build -Times 0 -Exactly
    }

    It 'rejects a resolver error even when its output contains the requested SDK version' {
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build {
            [pscustomobject]@{ ExitCode = 145; Output = 'A compatible .NET SDK was not found. Requested SDK version: 10.0.401' }
        } -ParameterFilter { $FilePath -eq 'dotnet' -and $Arguments -contains '--version' }

        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Managed -HostInfo $hostInfo

        $report.Ready | Should -BeFalse
        @($report.Checks | Where-Object Status -eq 'Invalid').Count | Should -BeGreaterThan 0
    }
}

Describe 'SDK architecture matches the native output runtime identifier' {
    It 'classifies <Architecture> with SDK <SdkOutput> and exit <CommandExitCode> as <ExpectedStatus>' -TestCases @(
        @{ Architecture = 'Arm64'; SdkOutput = 'osx-arm64'; CommandExitCode = 0; ExpectedStatus = 'Ready' }
        @{ Architecture = 'X64'; SdkOutput = 'osx-x64'; CommandExitCode = 0; ExpectedStatus = 'Ready' }
        @{ Architecture = 'Arm64'; SdkOutput = 'osx-x64'; CommandExitCode = 0; ExpectedStatus = 'Invalid' }
        @{ Architecture = 'X64'; SdkOutput = 'osx-arm64'; CommandExitCode = 0; ExpectedStatus = 'Invalid' }
        @{ Architecture = 'Arm64'; SdkOutput = 'osx-arm64'; CommandExitCode = 1; ExpectedStatus = 'Invalid' }
        @{ Architecture = 'X64'; SdkOutput = 'osx-x64'; CommandExitCode = 1; ExpectedStatus = 'Invalid' }
    ) {
        param($Architecture, $SdkOutput, $CommandExitCode, $ExpectedStatus)

        InModuleScope AegiNext.Build -Parameters @{
            RepositoryRoot = (Join-Path $TestDrive 'repository with spaces [sdk rid]')
            Architecture = $Architecture
            SdkOutput = $SdkOutput
            CommandExitCode = $CommandExitCode
            ExpectedStatus = $ExpectedStatus
        } {
            param($RepositoryRoot, $Architecture, $SdkOutput, $CommandExitCode, $ExpectedStatus)

            $commandResult = [pscustomobject]@{ ExitCode = $CommandExitCode; Output = $SdkOutput }
            Mock Invoke-AegiNextCommand { $commandResult }
            $hostInfo = [pscustomobject]@{
                Platform = 'MacOS'
                Architecture = $Architecture
                ProcessArchitecture = $Architecture
            }

            $check = Get-AegiNextSdkArchitectureCheck -RepositoryRoot $RepositoryRoot -Dotnet 'test-dotnet' -HostInfo $hostInfo

            $check.Id | Should -Be 'SdkArchitecture'
            $check.Status | Should -Be $ExpectedStatus
            $check.Detail | Should -Not -BeNullOrEmpty
            Should -Invoke Invoke-AegiNextCommand -Times 1 -Exactly -ParameterFilter {
                $FilePath -eq 'test-dotnet' -and $WorkingDirectory -eq $RepositoryRoot -and
                $Arguments.Count -eq 4 -and $Arguments[0] -eq 'msbuild' -and
                $Arguments[1] -eq (Join-Path $RepositoryRoot 'src/AegiNext.Media/AegiNext.Media.csproj') -and
                $Arguments[2] -eq '-nologo' -and $Arguments[3] -eq '-getProperty:NETCoreSdkRuntimeIdentifier'
            }
        }
    }
}

Describe 'Build orchestration fails closed and rechecks installed dependencies' {
    BeforeEach {
        $repository = Join-Path $TestDrive 'repository with spaces [orchestration]'
        New-BuildTestRepository $repository
        $script:probeCount = 0
        $script:buildCalls = [Collections.Generic.List[string]]::new()
        Mock Get-AegiNextHost -ModuleName AegiNext.Build {
            [pscustomobject]@{ Platform = 'MacOS'; Architecture = 'Arm64'; ProcessArchitecture = 'Arm64' }
        }
        Mock Get-AegiNextEnvironment -ModuleName AegiNext.Build {
            [pscustomobject]@{ HostInfo = $HostInfo; Target = $Target; Ready = $true; Checks = @(); NativePrefixes = @{} }
        }
        Mock Install-AegiNextDependency -ModuleName AegiNext.Build { }
        Mock Get-AegiNextBuildPlan -ModuleName AegiNext.Build {
            @(
                [pscustomobject]@{ Label = 'first'; FilePath = 'first'; Arguments = @('argument with spaces'); WorkingDirectory = $RepositoryRoot; Environment = @{ AEGINEXT_TEST_MARKER = 'first' } }
                [pscustomobject]@{ Label = 'second'; FilePath = 'second'; Arguments = @('second-argument'); WorkingDirectory = $RepositoryRoot; Environment = @{} }
            )
        }
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build {
            $script:buildCalls.Add($FilePath)
            [pscustomobject]@{ ExitCode = 0; Output = '' }
        }
    }

    It 'refuses diagnostic and installation switches together before any mutation' {
        { Invoke-AegiNextBuild -RepositoryRoot $repository -CheckEnvironment -InstallDependencies } | Should -Throw

        Should -Invoke Install-AegiNextDependency -ModuleName AegiNext.Build -Times 0 -Exactly
        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 0 -Exactly
    }

    It 'returns zero for ready diagnostics without generating or executing a plan' {
        Invoke-AegiNextBuild -RepositoryRoot $repository -Target Managed -CheckEnvironment | Should -Be 0

        Should -Invoke Get-AegiNextBuildPlan -ModuleName AegiNext.Build -Times 0 -Exactly
        Should -Invoke Install-AegiNextDependency -ModuleName AegiNext.Build -Times 0 -Exactly
        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 0 -Exactly
    }

    It 'returns two for incomplete diagnostics without attempting a build' {
        Mock Get-AegiNextEnvironment -ModuleName AegiNext.Build {
            [pscustomobject]@{
                HostInfo = $HostInfo; Target = $Target; Ready = $false; NativePrefixes = @{}
                Checks = @([pscustomobject]@{ Id = 'sdk'; Status = 'Missing'; Detail = 'SDK missing'; Package = 'dotnet-sdk'; Manager = 'brew' })
            }
        }

        Invoke-AegiNextBuild -RepositoryRoot $repository -CheckEnvironment | Should -Be 2
        Should -Invoke Get-AegiNextBuildPlan -ModuleName AegiNext.Build -Times 0 -Exactly
        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 0 -Exactly
    }

    It 'never installs dependencies for an unsupported target' {
        Mock Get-AegiNextEnvironment -ModuleName AegiNext.Build {
            [pscustomobject]@{
                HostInfo = $HostInfo; Target = $Target; Ready = $false; NativePrefixes = @{}
                Checks = @([pscustomobject]@{ Id = 'native'; Status = 'Unsupported'; Detail = 'No backend'; Package = ''; Manager = '' })
            }
        }

        Invoke-AegiNextBuild -RepositoryRoot $repository -InstallDependencies | Should -Be 2
        Should -Invoke Install-AegiNextDependency -ModuleName AegiNext.Build -Times 0 -Exactly
        Should -Invoke Get-AegiNextBuildPlan -ModuleName AegiNext.Build -Times 0 -Exactly
    }

    It 'reprobes after installation and builds only once the report becomes ready' {
        Mock Get-AegiNextEnvironment -ModuleName AegiNext.Build {
            $script:probeCount++
            [pscustomobject]@{
                HostInfo = $HostInfo; Target = $Target; Ready = ($script:probeCount -gt 1); NativePrefixes = @{}
                Checks = @([pscustomobject]@{ Id = 'sdk'; Status = 'Missing'; Detail = 'SDK missing'; Package = 'dotnet-sdk'; Manager = 'brew' })
            }
        }

        Invoke-AegiNextBuild -RepositoryRoot $repository -InstallDependencies | Should -Be 0

        Should -Invoke Get-AegiNextEnvironment -ModuleName AegiNext.Build -Times 2 -Exactly
        Should -Invoke Install-AegiNextDependency -ModuleName AegiNext.Build -Times 1 -Exactly
        $script:buildCalls.ToArray() | Should -Be @('first', 'second')
    }

    It 'does not build when installation returns but the environment remains invalid' {
        Mock Get-AegiNextEnvironment -ModuleName AegiNext.Build {
            [pscustomobject]@{
                HostInfo = $HostInfo; Target = $Target; Ready = $false; NativePrefixes = @{}
                Checks = @([pscustomobject]@{ Id = 'sdk'; Status = 'Invalid'; Detail = 'Wrong SDK band'; Package = 'dotnet-sdk'; Manager = 'brew' })
            }
        }

        Invoke-AegiNextBuild -RepositoryRoot $repository -InstallDependencies | Should -Be 2

        Should -Invoke Get-AegiNextEnvironment -ModuleName AegiNext.Build -Times 2 -Exactly
        Should -Invoke Get-AegiNextBuildPlan -ModuleName AegiNext.Build -Times 0 -Exactly
        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 0 -Exactly
    }

    It 'preserves a failing tool exit code and stops before the next step' {
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build {
            [pscustomobject]@{ ExitCode = 17; Output = 'fixture failure' }
        } -ParameterFilter { $FilePath -eq 'first' }

        $failure = $null
        try
        {
            Invoke-AegiNextBuild -RepositoryRoot $repository
        }
        catch
        {
            $failure = $_.Exception
        }

        $failure | Should -Not -BeNullOrEmpty
        $failure.Data['ExitCode'] | Should -Be 17
        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 0 -Exactly -ParameterFilter { $FilePath -eq 'second' }
    }

    It 'passes test selection, configuration and isolated command arguments unchanged' {
        Invoke-AegiNextBuild -RepositoryRoot $repository -Target Managed -Configuration Debug -RunTests -TestProjects @('Media') -Jobs 3 |
            Should -Be 0

        Should -Invoke Get-AegiNextBuildPlan -ModuleName AegiNext.Build -Times 1 -Exactly -ParameterFilter {
            $Target -eq 'Managed' -and $Configuration -eq 'Debug' -and $RunTests -and
            $TestProjects.Count -eq 1 -and $TestProjects[0] -eq 'Media' -and $Jobs -eq 3
        }
        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 1 -Exactly -ParameterFilter {
            $FilePath -eq 'first' -and $Arguments.Count -eq 1 -and $Arguments[0] -eq 'argument with spaces' -and
            $Environment['AEGINEXT_TEST_MARKER'] -eq 'first'
        }
    }

    It 'uses Managed by default and does not request tests implicitly' {
        Invoke-AegiNextBuild -RepositoryRoot $repository | Should -Be 0

        Should -Invoke Get-AegiNextBuildPlan -ModuleName AegiNext.Build -Times 1 -Exactly -ParameterFilter {
            $Target -eq 'Managed' -and !$RunTests
        }
        Should -Invoke Install-AegiNextDependency -ModuleName AegiNext.Build -Times 0 -Exactly
    }
}

Describe 'Dependency installation only acts on missing installable entries' {
    BeforeEach {
        $repository = Join-Path $TestDrive 'repository with spaces [install]'
        New-BuildTestRepository $repository
        Mock Find-AegiNextCommand -ModuleName AegiNext.Build { return $Name }
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build { [pscustomobject]@{ ExitCode = 0; Output = '' } }
    }

    It 'ignores ready, invalid and unpackageable checks instead of upgrading them' {
        $report = [pscustomobject]@{
            HostInfo = (New-BuildTestHost); Target = 'All'; Ready = $false; NativePrefixes = @{}
            Checks = @(
                [pscustomobject]@{ Id = 'missing'; Status = 'Missing'; Detail = ''; Package = 'ninja'; Manager = 'brew' }
                [pscustomobject]@{ Id = 'duplicate'; Status = 'Missing'; Detail = ''; Package = 'ninja'; Manager = 'brew' }
                [pscustomobject]@{ Id = 'ready'; Status = 'Ready'; Detail = ''; Package = 'cmake'; Manager = 'brew' }
                [pscustomobject]@{ Id = 'invalid'; Status = 'Invalid'; Detail = ''; Package = 'libplacebo'; Manager = 'brew' }
                [pscustomobject]@{ Id = 'manual'; Status = 'Missing'; Detail = ''; Package = ''; Manager = '' }
            )
        }

        Install-AegiNextDependency -Report $report -RepositoryRoot $repository

        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 1 -Exactly -ParameterFilter {
            $FilePath -eq 'brew' -and $Arguments -contains 'install' -and $Arguments -contains 'ninja'
        }
        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 0 -Exactly -ParameterFilter {
            $Arguments -contains 'upgrade' -or $Arguments -contains 'cmake' -or $Arguments -contains 'libplacebo'
        }
    }

    It 'reports an unavailable package manager without running a bootstrap command' {
        Mock Find-AegiNextCommand -ModuleName AegiNext.Build { return $null }
        $report = [pscustomobject]@{
            HostInfo = (New-BuildTestHost); Target = 'Native'; Ready = $false; NativePrefixes = @{}
            Checks = @([pscustomobject]@{ Id = 'ninja'; Status = 'Missing'; Detail = ''; Package = 'ninja'; Manager = 'brew' })
        }

        { Install-AegiNextDependency -Report $report -RepositoryRoot $repository } | Should -Throw

        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 0 -Exactly
    }

    It 'preserves a package manager failure and stops installing later dependencies' {
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build {
            [pscustomobject]@{ ExitCode = 23; Output = 'installation failed' }
        }
        $report = [pscustomobject]@{
            HostInfo = (New-BuildTestHost); Target = 'Native'; Ready = $false; NativePrefixes = @{}
            Checks = @(
                [pscustomobject]@{ Id = 'cmake'; Status = 'Missing'; Detail = ''; Package = 'cmake'; Manager = 'brew' }
                [pscustomobject]@{ Id = 'ninja'; Status = 'Missing'; Detail = ''; Package = 'ninja'; Manager = 'brew' }
            )
        }
        $failure = $null
        try
        {
            Install-AegiNextDependency -Report $report -RepositoryRoot $repository
        }
        catch
        {
            $failure = $_.Exception
        }

        $failure | Should -Not -BeNullOrEmpty
        $failure.Data['ExitCode'] | Should -Be 23
        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 1 -Exactly
    }

    It 'invokes an existing Scoop script with a package argument array' {
        Mock Find-AegiNextCommand -ModuleName AegiNext.Build { return 'scoop.ps1' } -ParameterFilter { $Name -eq 'scoop' }
        $report = [pscustomobject]@{
            HostInfo = (New-BuildTestHost 'Windows' 'X64'); Target = 'Managed'; Ready = $false; NativePrefixes = @{}
            Checks = @([pscustomobject]@{ Id = 'sdk'; Status = 'Missing'; Detail = ''; Package = 'main/dotnet-sdk'; Manager = 'scoop' })
        }

        Install-AegiNextDependency -Report $report -RepositoryRoot $repository

        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 1 -Exactly -ParameterFilter {
            $FilePath -eq 'scoop.ps1' -and $Arguments.Count -eq 2 -and
            $Arguments[0] -eq 'install' -and $Arguments[1] -eq 'main/dotnet-sdk'
        }
    }
}

Describe 'Native command execution preserves caller state and literal arguments' -Tag Command {
    BeforeAll {
        $pwshPath = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
    }

    BeforeEach {
        $workingDirectory = Join-Path $TestDrive 'working directory [literal]'
        [IO.Directory]::CreateDirectory($workingDirectory) | Out-Null
        $fixturePath = Join-Path $TestDrive 'command fixture.ps1'
        Set-Content -LiteralPath $fixturePath -Value @'
param([string] $Value, [int] $Code)
[pscustomobject]@{
    Directory = (Get-Location).ProviderPath
    Marker = [Environment]::GetEnvironmentVariable('AEGINEXT_BUILD_TEST_EXISTING')
    Added = [Environment]::GetEnvironmentVariable('AEGINEXT_BUILD_TEST_ADDED')
    Value = $Value
} | ConvertTo-Json -Compress
exit $Code
'@
        $originalDirectory = (Get-Location).ProviderPath
        $savedExisting = [Environment]::GetEnvironmentVariable('AEGINEXT_BUILD_TEST_EXISTING')
        $savedAdded = [Environment]::GetEnvironmentVariable('AEGINEXT_BUILD_TEST_ADDED')
        [Environment]::SetEnvironmentVariable('AEGINEXT_BUILD_TEST_EXISTING', 'caller value')
        [Environment]::SetEnvironmentVariable('AEGINEXT_BUILD_TEST_ADDED', $null)
    }

    AfterEach {
        Set-Location -LiteralPath $originalDirectory
        [Environment]::SetEnvironmentVariable('AEGINEXT_BUILD_TEST_EXISTING', $savedExisting)
        [Environment]::SetEnvironmentVariable('AEGINEXT_BUILD_TEST_ADDED', $savedAdded)
    }

    It 'restores environment and location after exit code <Code>' -TestCases @(
        @{ Code = 0 }
        @{ Code = 7 }
    ) {
        param($Code)

        $literalValue = 'space "quote"; $value $(never-execute) [brackets]'
        $result = Invoke-AegiNextCommand -FilePath $pwshPath -Arguments @(
            '-NoLogo', '-NoProfile', '-File', $fixturePath, '-Value', $literalValue, '-Code', [string]$Code
        ) -WorkingDirectory $workingDirectory -Environment @{
            AEGINEXT_BUILD_TEST_EXISTING = 'child value'
            AEGINEXT_BUILD_TEST_ADDED = 'temporary value'
        }

        $result.ExitCode | Should -Be $Code
        $child = ($result.Output -join "`n") | ConvertFrom-Json
        $child.Directory | Should -Be $workingDirectory
        $child.Marker | Should -Be 'child value'
        $child.Added | Should -Be 'temporary value'
        $child.Value | Should -Be $literalValue
        (Get-Location).ProviderPath | Should -Be $originalDirectory
        [Environment]::GetEnvironmentVariable('AEGINEXT_BUILD_TEST_EXISTING') | Should -Be 'caller value'
        [Environment]::GetEnvironmentVariable('AEGINEXT_BUILD_TEST_ADDED') | Should -BeNullOrEmpty
    }

    It 'streams stdout and stderr before completion while retaining output and the exit code' {
        $signal = Join-Path $TestDrive 'output received.signal'
        $streamFixture = Join-Path $TestDrive 'stream fixture.ps1'
        Set-Content -LiteralPath $streamFixture -Value @'
param([string] $Signal)
[Console]::Out.WriteLine('fixture ready')
[Console]::Error.WriteLine('fixture diagnostic')
for ($attempt = 0; $attempt -lt 50; $attempt++)
{
    if ([IO.File]::Exists($Signal))
    {
        [Console]::Out.WriteLine('fixture finished')
        exit 7
    }
    Start-Sleep -Milliseconds 100
}
exit 91
'@
        $messages = [Collections.Generic.List[string]]::new()
        $result = $null
        Invoke-AegiNextCommand -FilePath $pwshPath -Arguments @('-NoProfile', '-File', $streamFixture, '-Signal', $signal) `
            -WorkingDirectory $workingDirectory -StreamOutput 6>&1 | ForEach-Object {
                if ($_ -is [Management.Automation.InformationRecord])
                {
                    $message = $_.MessageData.ToString()
                    $messages.Add($message)
                    if ($message -eq 'fixture ready') { [IO.File]::WriteAllText($signal, 'received before process exit') }
                }
                else { $result = $_ }
            }

        $result.ExitCode | Should -Be 7
        $messages | Should -Contain 'fixture ready'
        $messages | Should -Contain 'fixture diagnostic'
        $messages | Should -Contain 'fixture finished'
        $result.Output | Should -Match 'fixture ready'
        $result.Output | Should -Match 'fixture diagnostic'
        $result.Output | Should -Match 'fixture finished'
        (Get-Location).ProviderPath | Should -Be $originalDirectory
    }

    It 'also restores state when the process cannot be started (<StreamOutput>)' -TestCases @(
        @{ StreamOutput = $false }
        @{ StreamOutput = $true }
    ) {
        param($StreamOutput)
        $failed = $false
        try
        {
            $result = Invoke-AegiNextCommand -FilePath (Join-Path $TestDrive 'missing executable') -Arguments @() -WorkingDirectory $workingDirectory -StreamOutput:$StreamOutput -Environment @{
                AEGINEXT_BUILD_TEST_EXISTING = 'changed'
                AEGINEXT_BUILD_TEST_ADDED = 'temporary'
            }
            $failed = $result.ExitCode -ne 0
        }
        catch
        {
            $failed = $true
        }

        $failed | Should -BeTrue
        (Get-Location).ProviderPath | Should -Be $originalDirectory
        [Environment]::GetEnvironmentVariable('AEGINEXT_BUILD_TEST_EXISTING') | Should -Be 'caller value'
        [Environment]::GetEnvironmentVariable('AEGINEXT_BUILD_TEST_ADDED') | Should -BeNullOrEmpty
    }
}
