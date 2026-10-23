#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    Import-Module (Join-Path $repository 'scripts/build/AegiNext.Build.psm1') -Force
}

Describe 'Workbench and Application selection' {
    It 'forwards one explicit model directory to every managed restore build and test step' {
        $hostInfo = [pscustomobject]@{ Platform = 'MacOS'; Architecture = 'Arm64'; ProcessArchitecture = 'Arm64' }
        $source = Join-Path $TestDrive 'manually supplied model'
        $plan = @(Get-AegiNextBuildPlan -RepositoryRoot $repository -Target Managed -HostInfo $hostInfo -RunTests -TestProjects Application -KoumoriModelDirectory $source)
        foreach ($step in $plan)
        {
            $step.Arguments | Should -Contain "-p:AegiNextKoumoriModelDirectory=$source"
        }
    }

    It 'plans Decoder then Audio then Export then Managed on <Platform>' -TestCases @(
        @{ Platform = 'MacOS'; Architecture = 'Arm64' }
        @{ Platform = 'Windows'; Architecture = 'X64' }
    ) {
        param($Platform, $Architecture)
        $hostInfo = [pscustomobject]@{ Platform = $Platform; Architecture = $Architecture; ProcessArchitecture = $Architecture }
        $prefixes = @{ ffmpeg = '/ffmpeg sdk'; sdl3 = '/sdl sdk'; cc = 'cc'; cxx = 'cxx'; cmake = 'cmake'; ninja = 'ninja'; ctest = 'ctest' }
        $planningRepository = Join-Path $TestDrive 'workbench plan with spaces'
        $plan = @(Get-AegiNextBuildPlan -RepositoryRoot $planningRepository -Target Workbench -HostInfo $hostInfo -NativePrefixes $prefixes -RunTests -TestProjects Application)
        @($plan | Where-Object { $_.Label -like 'Build *' } | ForEach-Object Label) | Should -Be @('Build decoder', 'Build audio', 'Build export', 'Build managed')
        @($plan | Where-Object { $_.Label -like 'Test *' } | ForEach-Object Label) | Should -Be @('Test decoder', 'Test audio', 'Test export', 'Test Application')
        $plan[-1].Arguments | Should -Contain (Join-Path $planningRepository 'Tests/AegiNext.Application.Tests/AegiNext.Application.Tests.csproj')
        @($plan | Where-Object { $_.Arguments -match 'libplacebo|MOLTENVK' }).Count | Should -Be 0
    }

    It 'allows only Application tests without any native plan' {
        $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64'; ProcessArchitecture = 'X64' }
        $plan = @(Get-AegiNextBuildPlan -RepositoryRoot $repository -Target Managed -HostInfo $hostInfo -RunTests -TestProjects Application)
        $plan.Count | Should -Be 4
        $plan[-1].Label | Should -Be 'Test Application'
        $plan[-1].Arguments | Should -Not -Contain '--no-build'
        $plan[-1].Arguments | Should -Contain '--no-restore'
    }

    It 'does not silently degrade Workbench to a managed-only build on Linux' {
        $hostInfo = [pscustomobject]@{ Platform = 'Linux'; Architecture = 'X64'; ProcessArchitecture = 'X64' }
        { Get-AegiNextBuildPlan -RepositoryRoot $repository -Target Workbench -HostInfo $hostInfo } | Should -Throw '*deferred*'
    }

    It 'merges native SDL export and managed checks and fails when SDL is missing' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $repository } {
            param($Repository)
            Mock Find-AegiNextCommand { param($Name) "fixture-$Name" }
            Mock Invoke-AegiNextCommand { [pscustomobject]@{ ExitCode = 0; Output = '10.0.401' } }
            Mock Get-AegiNextDecoderEnvironment {
                [pscustomobject]@{
                    Checks = @((Get-AegiNextCheck 'FfmpegSdk' 'Ready' 'Includes pinned swresample'))
                    NativePrefixes = @{ ffmpeg = '/selected sdk' }
                }
            }
            Mock Get-AegiNextSdlEnvironment {
                [pscustomobject]@{ Check = (Get-AegiNextCheck 'SdlSdk' 'Missing' 'SDK missing' 'sdl-package' 'scoop'); Root = $null }
            }
            Mock Get-AegiNextExportCapabilityCheck { Get-AegiNextCheck 'ExportCapabilities' 'Ready' 'fixed capabilities' }
            Mock Get-AegiNextSdkArchitectureCheck { Get-AegiNextCheck 'SdkArchitecture' 'Ready' 'win-x64' }
            $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64'; ProcessArchitecture = 'X64' }
            $report = Get-AegiNextEnvironment -RepositoryRoot $Repository -Target Workbench -HostInfo $hostInfo
            $report.Ready | Should -BeFalse
            foreach ($id in @('FfmpegSdk', 'SdlSdk', 'ExportCapabilities', 'DotNetSdk', 'SdkArchitecture'))
            {
                @($report.Checks | Where-Object Id -eq $id).Count | Should -Be 1
            }
            Should -Invoke Get-AegiNextDecoderEnvironment -Times 1 -Exactly
            Should -Invoke Get-AegiNextSdlEnvironment -Times 1 -Exactly
        }
    }
}

Describe 'Fixed export capabilities' {
    It 'requires the selected SDK encoder bit depths audio formats and three muxers without using PATH' {
        InModuleScope AegiNext.Build {
            Mock Invoke-AegiNextCommand {
                param($FilePath, $Arguments)
                $FilePath | Should -Be (Join-Path '/chosen sdk' 'bin/ffmpeg.exe')
                $output = switch ($Arguments[-1]) {
                    'encoder=libx264' { "Encoder libx264 [H264]:`nSupported pixel formats: yuv420p yuv420p10le" }
                    'encoder=libx265' { "Encoder libx265 [HEVC]:`nSupported pixel formats: yuv420p yuv420p10le" }
                    'encoder=libvpx' { "Encoder libvpx [VP8]:`nSupported pixel formats: yuv420p yuva420p" }
                    'encoder=libvpx-vp9' { "Encoder libvpx-vp9 [VP9]:`nSupported pixel formats: yuv420p yuv420p10le yuv420p12le" }
                    'encoder=libsvtav1' { "Encoder libsvtav1 [SVT-AV1]:`nSupported pixel formats: yuv420p yuv420p10le" }
                    'encoder=aac' { "Encoder aac [AAC]:`nSupported sample formats: fltp" }
                    'encoder=libopus' { "Encoder libopus [Opus]:`nSupported sample formats: s16 flt" }
                    'muxer=mp4' { 'Muxer mp4 [MP4]:' }
                    'muxer=matroska' { 'Muxer matroska [Matroska]:' }
                    'muxer=webm' { 'Muxer webm [WebM]:' }
                    default { throw 'Unexpected capability probe.' }
                }
                [pscustomobject]@{ ExitCode = 0; Output = $output }
            }
            $check = Get-AegiNextExportCapabilityCheck -RepositoryRoot '/repo' -HostInfo ([pscustomobject]@{ Platform = 'Windows' }) -FfmpegRoot '/chosen sdk'
            $check.Status | Should -Be 'Ready'
            Should -Invoke Invoke-AegiNextCommand -Times 12 -Exactly
        }
    }

    It 'rejects a listed encoder without the required ten-bit pixel format' {
        InModuleScope AegiNext.Build {
            Mock Invoke-AegiNextCommand {
                param($Arguments)
                $name = $Arguments[-1].Split('=')[1]
                [pscustomobject]@{ ExitCode = 0; Output = "Encoder $name [fixture]:`nSupported pixel formats: yuv420p" }
            }
            $check = Get-AegiNextExportCapabilityCheck -RepositoryRoot '/repo' -HostInfo ([pscustomobject]@{ Platform = 'MacOS' }) -FfmpegRoot '/chosen sdk'
            $check.Status | Should -Be 'Invalid'
            $check.Detail | Should -BeLike '*libx265*yuv420p10le*'
            Should -Invoke Invoke-AegiNextCommand -Times 2 -Exactly
        }
    }

    It 'rejects an incomplete VP9 AV1 Opus or WebM capability in the selected SDK' -TestCases @(
        @{ Missing = 'encoder=libvpx-vp9'; Detail = 'libvpx-vp9'; Output = "Encoder libvpx-vp9 [VP9]:`nSupported pixel formats: yuv420p" }
        @{ Missing = 'encoder=libsvtav1'; Detail = 'libsvtav1'; Output = "Encoder libsvtav1 [AV1]:`nSupported pixel formats: yuv420p10le" }
        @{ Missing = 'encoder=libopus'; Detail = 'libopus'; Output = "Encoder libopus [Opus]:`nSupported sample formats: s16" }
        @{ Missing = 'muxer=webm'; Detail = 'webm'; Output = "Unknown format 'webm'." }
    ) {
        param($Missing, $Detail, $Output)
        InModuleScope AegiNext.Build -Parameters @{ Missing = $Missing; Detail = $Detail; Output = $Output } {
            param($Missing, $Detail, $Output)
            $script:missingCapability = $Missing
            $script:missingCapabilityOutput = $Output
            Mock Invoke-AegiNextCommand {
                param($Arguments)
                $capability = $Arguments[-1]
                $kind, $name = $capability.Split('=')
                $output = if ($capability -eq $script:missingCapability)
                {
                    $script:missingCapabilityOutput
                }
                elseif ($kind -eq 'muxer')
                {
                    "Muxer $name [fixture]:"
                }
                else
                {
                    "Encoder $name [fixture]:`nSupported pixel formats: yuv420p yuv420p10le`nSupported sample formats: fltp flt"
                }
                [pscustomobject]@{ ExitCode = 0; Output = $output }
            }
            $check = Get-AegiNextExportCapabilityCheck '/repo' ([pscustomobject]@{ Platform = 'MacOS' }) '/chosen sdk'
            $check.Status | Should -Be 'Invalid'
            $check.Detail | Should -BeLike "*$Detail*"
        }
    }
}
