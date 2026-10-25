#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $desktop = Join-Path $repository 'src/AegiNext.Desktop/AegiNext.Desktop.csproj'

    function Get-ManualModelSnapshot
    {
        param([string] $Directory)

        return (@(Get-ChildItem -LiteralPath $Directory -File -Recurse | Sort-Object FullName | ForEach-Object {
            '{0}:{1}' -f [IO.Path]::GetRelativePath($Directory, $_.FullName), (Get-FileHash -LiteralPath $_.FullName).Hash
        }) -join "`n")
    }

    function New-ManualModelDirectory
    {
        param([string] $Directory, [string] $Marker)

        [IO.Directory]::CreateDirectory($Directory) | Out-Null
        Set-Content -LiteralPath (Join-Path $Directory 'manifest.json') -Value 'invalid JSON is checked only when loading the model'
        Set-Content -LiteralPath (Join-Path $Directory "$Marker.onnx") -Value $Marker
        return Get-ManualModelSnapshot $Directory
    }
}

Describe 'Manual Koumori model deployment' {
    It 'leaves source and manually placed output models unchanged through the actual desktop build publish and clean' {
        $root = Join-Path $TestDrive 'manual models with spaces'
        $source = Join-Path $root 'source model'
        $artifacts = Join-Path $root 'artifacts'
        $publish = Join-Path $root 'publish'
        $intermediateProps = Join-Path $root 'intermediate.props'
        [IO.Directory]::CreateDirectory($root) | Out-Null
        $repositoryProps = [Security.SecurityElement]::Escape((Join-Path $repository 'Directory.Build.props'))
        $escapedArtifacts = [Security.SecurityElement]::Escape($artifacts)
        [IO.File]::WriteAllText($intermediateProps, @"
<Project>
  <Import Project="$repositoryProps" />
  <PropertyGroup>
    <BaseIntermediateOutputPath>$escapedArtifacts/obj/`$(MSBuildProjectName)/</BaseIntermediateOutputPath>
    <MSBuildProjectExtensionsPath>`$(BaseIntermediateOutputPath)</MSBuildProjectExtensionsPath>
  </PropertyGroup>
</Project>
"@)
        $properties = @(
            "-p:ArtifactsPath=$artifacts"
            '-p:UseArtifactsOutput=true'
            "-p:DirectoryBuildPropsPath=$intermediateProps"
            "-p:PublishDir=$publish/"
            "-p:AegiNextKoumoriModelDirectory=$source"
            '-p:AegiNextPublishCompanionsSeparately=true'
        )
        $output = & dotnet msbuild $desktop -nologo '-getProperty:OutDir,BaseIntermediateOutputPath,MSBuildProjectExtensionsPath' '-p:Configuration=Release' @properties 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output | Out-String)
        $resolved = ($output | Out-String | ConvertFrom-Json).Properties
        [IO.Path]::GetFullPath($resolved.BaseIntermediateOutputPath).StartsWith([IO.Path]::GetFullPath($artifacts) + [IO.Path]::DirectorySeparatorChar) | Should -BeTrue
        [IO.Path]::GetFullPath($resolved.MSBuildProjectExtensionsPath) | Should -Be ([IO.Path]::GetFullPath($resolved.BaseIntermediateOutputPath))
        $buildModels = Join-Path $resolved.OutDir 'models/koumori'
        $publishModels = Join-Path $publish 'models/koumori'
        $sourceSnapshot = New-ManualModelDirectory $source 'source-only'
        $buildSnapshot = New-ManualModelDirectory $buildModels 'manually-placed-build'
        $publishSnapshot = New-ManualModelDirectory $publishModels 'manually-placed-publish'
        $commands = @(
            @{ Verb = 'build'; Arguments = @('--disable-build-servers') }
            @{ Verb = 'publish'; Arguments = @('--no-build', '--no-restore') }
            @{ Verb = 'clean'; Arguments = @() }
        )
        foreach ($command in $commands)
        {
            $verb = $command.Verb
            $arguments = @($verb, $desktop, '--configuration', 'Release', '--nologo', '--verbosity', 'quiet') + $command.Arguments + $properties
            $output = & dotnet @arguments 2>&1
            $LASTEXITCODE | Should -Be 0 -Because ("dotnet $verb failed: " + ($output | Out-String))
            (Get-ManualModelSnapshot $source) | Should -BeExactly $sourceSnapshot
            (Get-ManualModelSnapshot $buildModels) | Should -BeExactly $buildSnapshot
            (Get-ManualModelSnapshot $publishModels) | Should -BeExactly $publishSnapshot
        }
    }
}
