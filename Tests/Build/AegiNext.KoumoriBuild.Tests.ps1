#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $targets = Join-Path $repository 'scripts/build/AegiNext.Koumori.targets'
    . (Join-Path $PSScriptRoot 'Fixtures/KoumoriPackage.ps1')

    function New-KoumoriBuildFixture
    {
        param([string] $Root)
        [IO.Directory]::CreateDirectory($Root) | Out-Null
        $project = Join-Path $Root 'fixture.csproj'
        [IO.File]::WriteAllText($project, @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AegiNextKoumoriModelDirectory>$Root/models/koumori</AegiNextKoumoriModelDirectory>
  </PropertyGroup>
  <Import Project="$targets" />
</Project>
"@)
        return $project
    }

    function Invoke-KoumoriFixtureBuild
    {
        param([string] $Project, [string] $Verb = 'build', [string[]] $Extra = @())
        $output = & dotnet $Verb $Project --nologo --verbosity quiet @Extra 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output | Out-String)
    }
}

Describe 'Project-local Koumori runtime resources' {
    It 'preserves UTF-8 manifests with a BOM and escaped property names' {
        $root = Join-Path $TestDrive 'escaped manifest'
        $project = New-KoumoriBuildFixture $root
        $source = Join-Path $root 'models/koumori'
        New-KoumoriTestPackage $source
        $path = Join-Path $source 'manifest.json'
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        $manifest.modelId = "`u{6A21}`u{578B}-`u{30B3}`u{30A6}`u{30E2}`u{30EA}"
        $json = ($manifest | ConvertTo-Json -Depth 5).Replace('"modelId"', '"model\u0049d"')
        [IO.File]::WriteAllText($path, $json, [Text.UTF8Encoding]::new($true))
        Invoke-KoumoriFixtureBuild $project
        (Get-FileHash -LiteralPath (Join-Path $root 'bin/Debug/net10.0/models/koumori/manifest.json')).Hash |
            Should -BeExactly (Get-FileHash -LiteralPath $path).Hash
    }

    It 'builds schema1 packages with absent null or correct frontend integers: <State>' -TestCases @(
        @{ State = 'absent' }, @{ State = 'null' }, @{ State = 'integer' }) {
        param($State)
        $root = Join-Path $TestDrive "schema1 shape $State"
        $project = New-KoumoriBuildFixture $root
        $source = Join-Path $root 'models/koumori'
        New-KoumoriTestPackage $source
        $path = Join-Path $source 'manifest.json'
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        if ($State -ne 'absent')
        {
            $manifest.featureCount = if ($State -eq 'integer') { 82 } else { $null }
            $manifest.sampleRate = if ($State -eq 'integer') { 16000 } else { $null }
        }
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path
        Invoke-KoumoriFixtureBuild $project
    }

    It 'rejects an incompatible frontend integer before deploying schema <Schema>, <Field>, <Fault>' -TestCases @(
        foreach ($schema in 1..3)
        {
            foreach ($field in @('featureCount', 'sampleRate'))
            {
                foreach ($fault in @('missing', 'null', 'string', 'float', 'value'))
                {
                    if ($schema -eq 1 -and $fault -in @('missing', 'null')) { continue }
                    @{ Schema = $schema; Field = $field; Fault = $fault }
                }
            }
        }) {
        param($Schema, $Field, $Fault)
        $root = Join-Path $TestDrive "frontend $Schema $Field $Fault"
        $project = New-KoumoriBuildFixture $root
        $source = Join-Path $root 'models/koumori'
        New-KoumoriTestPackage $source -Contextual ($Schema -eq 2) -GenerationSnap ($Schema -eq 3)
        $path = Join-Path $source 'manifest.json'
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        $manifest.featureCount = 82
        $manifest.sampleRate = 16000
        $expected = $manifest[$Field]
        switch ($Fault)
        {
            'missing' { $manifest.Remove($Field) }
            'null' { $manifest[$Field] = $null }
            'string' { $manifest[$Field] = [string]$expected }
            'float' { $manifest[$Field] = [double]$expected }
            'value' { $manifest[$Field] = $expected - 1 }
        }
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path
        $publish = Join-Path $root 'published output'
        $output = & dotnet msbuild $project -nologo -t:PublishAegiNextKoumoriResources "-p:PublishDir=$publish" 2>&1
        $LASTEXITCODE | Should -Not -Be 0
        ($output | Out-String) | Should -Match $Field
        Test-Path -LiteralPath (Join-Path $publish 'models/koumori') | Should -BeFalse
    }

    It 'rejects source and output paths that differ only by case before cleanup' -Skip:($IsLinux) {
        $root = Join-Path $TestDrive 'case alias model'
        $project = New-KoumoriBuildFixture $root
        $source = Join-Path $root 'models/koumori'
        New-KoumoriTestPackage $source
        $before = (Get-FileHash -LiteralPath (Join-Path $source 'manifest.json')).Hash
        $output = & dotnet msbuild $project -nologo -t:CleanAegiNextKoumoriBuildResources "-p:OutDir=$root" "-p:AegiNextKoumoriModelDirectory=$root/MODELS/KOUMORI" 2>&1
        $LASTEXITCODE | Should -Not -Be 0
        ($output | Out-String) | Should -Match 'must not overlap'
        (Get-FileHash -LiteralPath (Join-Path $source 'manifest.json')).Hash | Should -BeExactly $before
    }

    It 'builds and publishes only manifest runtime files from paths with spaces for schema <Schema>' -TestCases @(
        @{ Schema = 1; Count = 4 }, @{ Schema = 2; Count = 7 }, @{ Schema = 3; Count = 8 }) {
        param($Schema, $Count)
        $root = Join-Path $TestDrive "schema $Schema fixture"
        $project = New-KoumoriBuildFixture $root
        $source = Join-Path $root 'models/koumori'
        New-KoumoriTestPackage $source -Contextual ($Schema -eq 2) -GenerationSnap ($Schema -eq 3)
        [IO.File]::WriteAllText((Join-Path $source 'training-private.json'), 'not deployed')
        [IO.File]::WriteAllText((Join-Path $source 'epoch.pt'), 'not deployed')
        Push-Location $repository
        try
        {
            Invoke-KoumoriFixtureBuild $project
            $buildOutput = Join-Path $root 'bin/Debug/net10.0/models/koumori'
            @(Get-ChildItem -LiteralPath $buildOutput -File).Count | Should -Be $Count
            $publish = Join-Path $root 'published output'
            Invoke-KoumoriFixtureBuild $project 'publish' @('-o', $publish)
            @(Get-ChildItem -LiteralPath (Join-Path $publish 'models/koumori') -File).Count | Should -Be $Count
            (Get-FileHash -LiteralPath (Join-Path $publish 'models/koumori/manifest.json')).Hash |
                Should -BeExactly (Get-FileHash -LiteralPath (Join-Path $source 'manifest.json')).Hash
        }
        finally
        {
            Pop-Location
        }
    }

    It 'removes stale runtime files when changing from schema3 to schema1 and when removing the source package' {
        $root = Join-Path $TestDrive 'replaced model'
        $project = New-KoumoriBuildFixture $root
        $source = Join-Path $root 'models/koumori'
        $publish = Join-Path $root 'published output'
        New-KoumoriTestPackage $source -GenerationSnap $true
        Invoke-KoumoriFixtureBuild $project 'publish' @('-o', $publish)
        Remove-Item -LiteralPath $source -Recurse -Force
        New-KoumoriTestPackage $source
        Invoke-KoumoriFixtureBuild $project 'publish' @('-o', $publish)
        foreach ($target in @((Join-Path $root 'bin/Release/net10.0/models/koumori'), (Join-Path $publish 'models/koumori')))
        {
            @(Get-ChildItem -LiteralPath $target -File).Count | Should -Be 4
            Test-Path -LiteralPath (Join-Path $target 'snap-model.onnx') | Should -BeFalse
            Test-Path -LiteralPath (Join-Path $target 'encoder.onnx') | Should -BeFalse
        }
        Remove-Item -LiteralPath $source -Recurse -Force
        Invoke-KoumoriFixtureBuild $project 'publish' @('-o', $publish)
        Test-Path -LiteralPath (Join-Path $root 'bin/Release/net10.0/models/koumori') | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $publish 'models/koumori') | Should -BeFalse
    }

    It 'allows a checkout containing only the model directory readme' {
        $root = Join-Path $TestDrive 'unpopulated checkout'
        $project = New-KoumoriBuildFixture $root
        $source = Join-Path $root 'models/koumori'
        [IO.Directory]::CreateDirectory($source) | Out-Null
        [IO.File]::WriteAllText((Join-Path $source 'README.md'), 'manual model placement')
        Invoke-KoumoriFixtureBuild $project
        Test-Path -LiteralPath (Join-Path $root 'bin/Debug/net10.0/models/koumori') | Should -BeFalse
    }

    It 'fails clearly for incomplete, altered or unsafe runtime packages: <Fault>' -TestCases @(
        @{ Fault = 'missing-manifest' }, @{ Fault = 'missing-file' }, @{ Fault = 'hash' },
        @{ Fault = 'duplicate-json' }, @{ Fault = 'traversal' }, @{ Fault = 'drive-path' },
        @{ Fault = 'aliased-file' }, @{ Fault = 'linked-file' }) {
        param($Fault)
        $root = Join-Path $TestDrive "broken $Fault"
        $project = New-KoumoriBuildFixture $root
        $source = Join-Path $root 'models/koumori'
        New-KoumoriTestPackage $source
        $path = Join-Path $source 'manifest.json'
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        switch ($Fault)
        {
            'missing-manifest' { Remove-Item -LiteralPath $path }
            'missing-file' { Remove-Item -LiteralPath (Join-Path $source 'model.onnx') }
            'hash' { [IO.File]::AppendAllText((Join-Path $source 'model.onnx'), 'changed') }
            'duplicate-json' { $content = (Get-Content -LiteralPath $path -Raw).TrimEnd(); [IO.File]::WriteAllText($path, $content.Substring(0, $content.Length - 1) + ', "MODELFILE": "model.onnx" }') }
            'traversal' { $manifest.modelFile = '../model.onnx' }
            'drive-path' { $manifest.modelFile = 'C:model.onnx' }
            'aliased-file' { $manifest.frontendFile = 'model.onnx'; $manifest.frontendSha256 = $manifest.modelSha256 }
            'linked-file' {
                $outside = Join-Path $root 'outside.onnx'
                Move-Item -LiteralPath (Join-Path $source 'model.onnx') -Destination $outside
                try
                {
                    New-Item -ItemType SymbolicLink -Path (Join-Path $source 'model.onnx') -Target $outside -ErrorAction Stop | Out-Null
                }
                catch [System.UnauthorizedAccessException]
                {
                    Set-ItResult -Skipped -Because 'Creating symbolic links requires administrator privileges or Windows Developer Mode.'
                    return
                }
            }
        }
        if ($Fault -in @('traversal', 'drive-path', 'aliased-file'))
        {
            $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path
        }
        $output = & dotnet build $project --nologo --verbosity quiet 2>&1
        $LASTEXITCODE | Should -Not -Be 0
        ($output | Out-String) | Should -Match 'Koumori'
    }
}
