#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $pwsh = (Get-Process -Id $PID).Path

    function Invoke-DebugNativeFixture
    {
        param([hashtable] $Parameters = @{}, [int] $ExitCode = 0)

        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'build-debug-native.ps1') -Destination $fixtureDirectory
        Set-Content -LiteralPath (Join-Path $fixtureDirectory 'parameters.json') -Value ($Parameters | ConvertTo-Json -Depth 8)
        Set-Content -LiteralPath (Join-Path $fixtureDirectory 'exit-code.txt') -Value $ExitCode
        Push-Location -LiteralPath $callerDirectory
        try
        {
            $output = & $pwsh -NoProfile -File (Join-Path $fixtureDirectory 'invoke.ps1') 2>&1
            $resultCode = $LASTEXITCODE
        }
        finally
        {
            Pop-Location
        }
        $capture = Join-Path $fixtureDirectory 'actual.json'
        $actual = if (Test-Path -LiteralPath $capture) { Get-Content -LiteralPath $capture -Raw | ConvertFrom-Json }
        [pscustomobject]@{ ExitCode = $resultCode; Actual = $actual; Output = ($output | Out-String) }
    }
}

Describe 'Debug native build entry point' {
    BeforeEach {
        $fixtureDirectory = Join-Path $TestDrive "repository with spaces [debug] $([Guid]::NewGuid())"
        $callerDirectory = Join-Path $TestDrive 'another working directory'
        [IO.Directory]::CreateDirectory($fixtureDirectory) | Out-Null
        [IO.Directory]::CreateDirectory($callerDirectory) | Out-Null
        Set-Content -LiteralPath (Join-Path $fixtureDirectory 'invoke.ps1') -Value @'
$ErrorActionPreference = 'Stop'
$parameters = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'parameters.json') -Raw | ConvertFrom-Json -AsHashtable
& (Join-Path $PSScriptRoot 'build-debug-native.ps1') @parameters
exit $LASTEXITCODE
'@
        Set-Content -LiteralPath (Join-Path $fixtureDirectory 'build.ps1') -Value @'
[CmdletBinding()]
param(
    [string] $Target = 'Managed',
    [string] $Configuration = 'Release',
    [switch] $CheckEnvironment,
    [switch] $InstallDependencies,
    [string] $FfmpegRoot,
    [string] $SdlRoot,
    [string] $RuntimeIdentifier,
    [switch] $RunTests,
    [string[]] $TestProjects = @('Core', 'Application', 'SubtitleFormats', 'Rendering', 'Media', 'Cli', 'Desktop'),
    [int] $Jobs = 2,
    [string] $ReportPath
)
$actual = @{
    Target = $Target; Configuration = $Configuration; WorkingDirectory = (Get-Location).Path
    CheckEnvironment = [bool]$CheckEnvironment; InstallDependencies = [bool]$InstallDependencies
    FfmpegRoot = $FfmpegRoot; SdlRoot = $SdlRoot; RuntimeIdentifier = $RuntimeIdentifier
    RunTests = [bool]$RunTests; TestProjects = $TestProjects; Jobs = $Jobs; ReportPath = $ReportPath
}
Set-Content -LiteralPath (Join-Path $PSScriptRoot 'actual.json') -Value ($actual | ConvertTo-Json -Depth 8)
exit ([int](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'exit-code.txt')))
'@
    }

    It 'builds the Debug workbench from another directory without enabling installation or tests' {
        $result = Invoke-DebugNativeFixture
        $result.ExitCode | Should -Be 0 -Because $result.Output
        $result.Actual.Target | Should -Be 'Workbench'
        $result.Actual.Configuration | Should -Be 'Debug'
        $result.Actual.WorkingDirectory | Should -Be $callerDirectory
        $result.Actual.RuntimeIdentifier | Should -BeNullOrEmpty
        $result.Actual.InstallDependencies | Should -BeFalse
        $result.Actual.RunTests | Should -BeFalse
    }

    It 'preserves explicit SDK paths RID test selections switches and report paths as separate arguments' {
        $parameters = @{
            FfmpegRoot = Join-Path $TestDrive 'FFmpeg SDK [selected]'
            SdlRoot = Join-Path $TestDrive 'SDL SDK [selected]'
            RuntimeIdentifier = 'win-x64'
            InstallDependencies = $true
            RunTests = $true
            TestProjects = @('Media', 'Desktop')
            Jobs = 3
            ReportPath = 'reports with spaces/environment [debug].json'
        }
        $result = Invoke-DebugNativeFixture -Parameters $parameters
        $result.ExitCode | Should -Be 0 -Because $result.Output
        foreach ($name in @('FfmpegRoot', 'SdlRoot', 'RuntimeIdentifier', 'InstallDependencies', 'RunTests', 'Jobs', 'ReportPath'))
        {
            $result.Actual.$name | Should -Be $parameters[$name]
        }
        $result.Actual.TestProjects | Should -Be @('Media', 'Desktop')
    }

    It 'forwards only <Project> tests to the Debug workbench' -TestCases @(@{ Project = 'SubtitleFormats' }, @{ Project = 'Cli' }) {
        param($Project)
        $result = Invoke-DebugNativeFixture -Parameters @{ RunTests = $true; TestProjects = @($Project) }

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $result.Actual.Target | Should -Be 'Workbench'
        $result.Actual.Configuration | Should -Be 'Debug'
        $result.Actual.RunTests | Should -BeTrue
        @($result.Actual.TestProjects) | Should -Be @($Project)
    }

    It 'forwards environment-only checks without enabling installation or tests' {
        $result = Invoke-DebugNativeFixture -Parameters @{ CheckEnvironment = $true }
        $result.ExitCode | Should -Be 0 -Because $result.Output
        $result.Actual.CheckEnvironment | Should -BeTrue
        $result.Actual.InstallDependencies | Should -BeFalse
        $result.Actual.RunTests | Should -BeFalse
    }

    It 'preserves a build or environment exit code of <ExitCode>' -TestCases @(
        @{ ExitCode = 2 }, @{ ExitCode = 23 }
    ) {
        param($ExitCode)
        (Invoke-DebugNativeFixture -ExitCode $ExitCode).ExitCode | Should -Be $ExitCode
    }

    It 'rejects <Name> before the delegated build starts' -TestCases @(
        @{ Name = 'Configuration'; Value = 'Release' }
        @{ Name = 'Target'; Value = 'Native' }
        @{ Name = 'RuntimeIdentifier'; Value = 'linux-x64' }
        @{ Name = 'TestProjects'; Value = @('Unknown') }
        @{ Name = 'Jobs'; Value = 0 }
    ) {
        param($Name, $Value)
        $result = Invoke-DebugNativeFixture -Parameters @{ $Name = $Value }
        $result.ExitCode | Should -Not -Be 0
        $result.Actual | Should -BeNullOrEmpty
    }
}
