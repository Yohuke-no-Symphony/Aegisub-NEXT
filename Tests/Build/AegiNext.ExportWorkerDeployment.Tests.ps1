#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $targets = Join-Path $repository 'scripts/build/AegiNext.Companions.targets'
    $closureTargets = Join-Path $repository 'scripts/build/AegiNext.CompanionFiles.targets'
    $dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
    Import-Module (Join-Path $repository 'scripts/publish/AegiNext.Publish.psm1') -Force
    $formatOutput = Join-Path $TestDrive 'format library'
    $isolationProps = Join-Path $TestDrive 'isolated.props'
    [IO.File]::WriteAllText($isolationProps, @"
<Project>
  <Import Project="$(Join-Path $repository 'Directory.Build.props')" />
  <PropertyGroup>
    <BaseIntermediateOutputPath>$TestDrive/obj/`$(MSBuildProjectName)/</BaseIntermediateOutputPath>
  </PropertyGroup>
</Project>
"@)
    $formatProject = Join-Path $repository 'src/AegiNext.SubtitleFormats/AegiNext.SubtitleFormats.csproj'
    $output = & $dotnet build $formatProject -c Release "-p:DirectoryBuildPropsPath=$isolationProps" '-p:RuntimeIdentifier=' -o $formatOutput --verbosity minimal 2>&1
    $LASTEXITCODE | Should -Be 0 -Because ($output | Out-String)

    function New-CompanionFixture
    {
        param([string] $Root, [string] $SharedContent = 'identical shared content')
        foreach ($name in @('main', 'worker', 'cli', 'support'))
        {
            [IO.Directory]::CreateDirectory((Join-Path $Root $name)) | Out-Null
        }
        $workerProject = Join-Path $Root 'worker/worker.csproj'
        $cliProject = Join-Path $Root 'cli/cli.csproj'
        $supportProject = Join-Path $Root 'support/support.csproj'
        [IO.File]::WriteAllText($workerProject, @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><AssemblyName>aegn-exporter</AssemblyName></PropertyGroup>
  <Import Project="$closureTargets" />
</Project>
"@)
        [IO.File]::WriteAllText((Join-Path $Root 'worker/Program.cs'), 'System.Console.WriteLine("fixture worker");')
        [IO.File]::WriteAllText($supportProject, @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>Fixture.CliSupport</AssemblyName><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup>
    <Reference Include="AegiNext.SubtitleFormats"><HintPath>$(Join-Path $formatOutput 'AegiNext.SubtitleFormats.dll')</HintPath></Reference>
    <Reference Include="AegiNext.Core"><HintPath>$(Join-Path $formatOutput 'AegiNext.Core.dll')</HintPath></Reference>
  </ItemGroup>
</Project>
"@)
        [IO.File]::WriteAllText((Join-Path $Root 'support/SubtitleConverter.cs'), @'
using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

public static class SubtitleConverter
{
    public static void Convert(string input, string output)
    {
        var lines = SubtitleTextFormat.ParseSrt(File.ReadAllText(input));
        var document = new ProjectDocument
        {
            Subtitles = lines,
            Layers = lines.Select(line => new ProjectLayer
            {
                Id = line.Id,
                Kind = LayerKind.SUBTITLE,
                SubtitleId = line.Id,
                Start = line.Start,
                End = line.End
            }).ToImmutableArray()
        };
        File.WriteAllText(output, AssSubtitleFormat.Write(document).Text);
    }
}
'@)
        [IO.File]::WriteAllText($cliProject, @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><AssemblyName>aegnc</AssemblyName><Version>0.1.0</Version><InformationalVersion>0.1.0</InformationalVersion><AegiNextWorkerProject>$workerProject</AegiNextWorkerProject></PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$supportProject" />
    <Content Include="private.txt" Link="cli-data/nested/private.txt" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
    <Content Include="shared.txt" Link="data/shared.txt" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
    <Content Include="native.txt" Link="runtimes/fixture/native/native.txt" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
  </ItemGroup>
  <Import Project="$targets" />
</Project>
"@)
        [IO.File]::WriteAllText((Join-Path $Root 'cli/private.txt'), 'CLI nested content')
        [IO.File]::WriteAllText((Join-Path $Root 'cli/shared.txt'), $SharedContent)
        [IO.File]::WriteAllText((Join-Path $Root 'cli/native.txt'), 'nested native asset')
        [IO.File]::WriteAllText((Join-Path $Root 'cli/Program.cs'), @'
if (args.Length == 1 && args[0] == "--help")
{
    System.Console.WriteLine("convert export");
    return;
}
if (args.Length == 1 && args[0] == "--version")
{
    var attributes = System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
    System.Console.WriteLine(((System.Reflection.AssemblyInformationalVersionAttribute)attributes[0]).InformationalVersion);
    return;
}
SubtitleConverter.Convert(args[System.Array.IndexOf(args, "-i") + 1], args[System.Array.IndexOf(args, "-o") + 1]);
'@)
        $mainProject = Join-Path $Root 'main/main.csproj'
        [IO.File]::WriteAllText($mainProject, @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><AssemblyName>aegi-next</AssemblyName><AegiNextWorkerProject>$workerProject</AegiNextWorkerProject><AegiNextCliProject>$cliProject</AegiNextCliProject></PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$supportProject" />
    <Content Include="shared.txt" Link="data/shared.txt" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
  </ItemGroup>
  <Import Project="$targets" />
</Project>
"@)
        [IO.File]::WriteAllText((Join-Path $Root 'main/shared.txt'), 'identical shared content')
        [IO.File]::WriteAllText((Join-Path $Root 'main/Program.cs'), 'System.Console.WriteLine("fixture desktop");')
        return $mainProject
    }
}

Describe 'Adjacent companion deployment' {
    It 'resolves production companion projects absolutely without assembly references (<Project>)' -TestCases @(
        @{ Project = 'AegiNext.Desktop'; Companions = @('AegiNext.ExportWorker', 'AegiNext.Cli') }
        @{ Project = 'AegiNext.Cli'; Companions = @('AegiNext.ExportWorker') }
    ) {
        param($Project, $Companions)
        $projectPath = Join-Path $repository "src/$Project/$Project.csproj"
        $output = & $dotnet msbuild $projectPath -nologo '-getItem:ProjectReference'
        $LASTEXITCODE | Should -Be 0
        $references = ($output | Out-String | ConvertFrom-Json).Items.ProjectReference
        foreach ($companion in $Companions)
        {
            $reference = @($references | Where-Object Identity -eq (Join-Path $repository "src/$companion/$companion.csproj"))
            $reference.Count | Should -Be 1
            $reference[0].ReferenceOutputAssembly | Should -Be 'false'
            $reference[0].Private | Should -Be 'false'
        }
    }

    It 'builds and publishes complete executable closures with the real shared targets' {
        $root = Join-Path $TestDrive 'three entry deployment'
        $mainProject = New-CompanionFixture $root
        $suffix = if ($IsWindows) { '.exe' } else { '' }
        $output = & $dotnet build $mainProject -c Release --verbosity minimal 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output | Out-String)
        $buildDirectory = Join-Path $root 'main/bin/Release/net10.0'
        foreach ($directory in @('worker', 'cli'))
        {
            foreach ($stale in @('publish', 'osx-arm64', 'win-x64'))
            {
                $staleDirectory = Join-Path $root "$directory/bin/Release/net10.0/$stale"
                [IO.Directory]::CreateDirectory($staleDirectory) | Out-Null
                [IO.File]::WriteAllText((Join-Path $staleDirectory 'stale.dll'), 'previous deployment')
            }
        }
        $output = & $dotnet build $mainProject -c Release --no-restore --verbosity minimal 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output | Out-String)
        $payload = Join-Path $root 'publish'
        $output = & $dotnet publish $mainProject -c Release --no-restore -o $payload --verbosity minimal 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output | Out-String)
        ($output | Out-String) | Should -Not -Match 'NETSDK1152|warning '
        foreach ($directory in @($buildDirectory, $payload))
        {
            foreach ($name in @('aegi-next', 'aegn-exporter', 'aegnc'))
            {
                foreach ($file in @("$name$suffix", "$name.dll", "$name.deps.json", "$name.runtimeconfig.json"))
                {
                    Test-Path -LiteralPath (Join-Path $directory $file) -PathType Leaf | Should -BeTrue
                }
            }
            Test-Path -LiteralPath (Join-Path $directory 'Fixture.CliSupport.dll') -PathType Leaf | Should -BeTrue
            Get-Content -LiteralPath (Join-Path $directory 'cli-data/nested/private.txt') -Raw | Should -Be 'CLI nested content'
            Get-Content -LiteralPath (Join-Path $directory 'data/shared.txt') -Raw | Should -Be 'identical shared content'
            Get-Content -LiteralPath (Join-Path $directory 'runtimes/fixture/native/native.txt') -Raw | Should -Be 'nested native asset'
            @(Get-ChildItem -LiteralPath $directory -File -Recurse | Where-Object Name -eq 'stale.dll').Count | Should -Be 0
            $workerOutput = & (Join-Path $directory "aegn-exporter$suffix")
            $LASTEXITCODE | Should -Be 0
            ($workerOutput | Out-String).Trim() | Should -Be 'fixture worker'
        }
        $desktopOutput = & (Join-Path $payload "aegi-next$suffix")
        $LASTEXITCODE | Should -Be 0
        ($desktopOutput | Out-String).Trim() | Should -Be 'fixture desktop'
        $cliDirectory = Join-Path $root 'cli/bin/Release/net10.0'
        Test-Path -LiteralPath (Join-Path $cliDirectory "aegn-exporter$suffix") -PathType Leaf | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $cliDirectory 'aegn-exporter.runtimeconfig.json') -PathType Leaf | Should -BeTrue
        InModuleScope AegiNext.Publish -Parameters @{ Payload = $payload; RuntimeIdentifier = $(if ($IsWindows) { 'win-x64' } else { 'osx-arm64' }) } {
            param($Payload, $RuntimeIdentifier)
            Test-AegiNextPublishedCli -Payload $Payload -RuntimeIdentifier $RuntimeIdentifier -ProductVersion '0.1.0'
        }
        Remove-Item -LiteralPath (Join-Path $payload 'Fixture.CliSupport.dll')
        $output = & (Join-Path $payload "aegnc$suffix") convert -i missing.srt -o missing.ass 2>&1
        $LASTEXITCODE | Should -Not -Be 0
        ($output | Out-String) | Should -Match 'Fixture.CliSupport'
    }

    It 'preserves shared dependencies under a unified RID, self-contained and product version contract' {
        $root = Join-Path $TestDrive 'RID deployment'
        $mainProject = New-CompanionFixture $root
        $rid = [Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
        $arguments = @('-c', 'Release', '-r', $rid, '--self-contained', 'true', '-p:Version=2.7.3', '-p:InformationalVersion=2.7.3', '-p:AssemblyVersion=2.7.3.0', '-p:FileVersion=2.7.3.0', '-p:IncludeSourceRevisionInInformationalVersion=false')
        $output = & $dotnet build $mainProject @arguments --verbosity minimal 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output | Out-String)
        $mainOutput = Join-Path $root "main/bin/Release/net10.0/$rid"
        $cliOutput = Join-Path $root "cli/bin/Release/net10.0/$rid"
        (Get-FileHash -LiteralPath (Join-Path $mainOutput 'Fixture.CliSupport.dll')).Hash |
            Should -Be (Get-FileHash -LiteralPath (Join-Path $cliOutput 'Fixture.CliSupport.dll')).Hash
        $payload = Join-Path $root 'publish'
        $output = & $dotnet publish $mainProject @arguments --no-restore -o $payload --verbosity minimal 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output | Out-String)
        ($output | Out-String) | Should -Not -Match 'NETSDK1152|warning '
        InModuleScope AegiNext.Publish -Parameters @{ Payload = $payload; RuntimeIdentifier = $rid } {
            param($Payload, $RuntimeIdentifier)
            @(Get-AegiNextPublishedRuntimeFramework $Payload).Application | Should -Be @('aegi-next', 'aegn-exporter', 'aegnc')
            Test-AegiNextPublishedCli -Payload $Payload -RuntimeIdentifier $RuntimeIdentifier -ProductVersion '2.7.3'
        }
    }

    It 'rejects a different companion file at the same relative content path during <Operation>' -TestCases @(
        @{ Operation = 'Build' }
        @{ Operation = 'Publish' }
    ) {
        param($Operation)
        $root = Join-Path $TestDrive "collision deployment $Operation"
        $mainProject = New-CompanionFixture $root $(if ($Operation -eq 'Build') { 'different companion content' } else { 'identical shared content' })
        if ($Operation -eq 'Build')
        {
            $output = & $dotnet build $mainProject -c Release --verbosity minimal 2>&1
        }
        else
        {
            $output = & $dotnet build $mainProject -c Release --verbosity minimal 2>&1
            $LASTEXITCODE | Should -Be 0 -Because ($output | Out-String)
            [IO.File]::WriteAllText((Join-Path $root 'cli/shared.txt'), 'different companion content')
            $output = & $dotnet publish $mainProject -c Release --no-build -o (Join-Path $root 'publish') --verbosity minimal 2>&1
        }
        $LASTEXITCODE | Should -Not -Be 0
        ($output | Out-String) | Should -Match 'Companion dependency collision.*data[/\\]shared.txt'
    }
}
