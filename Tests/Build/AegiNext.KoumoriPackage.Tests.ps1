#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    . (Join-Path $repository 'scripts/publish/AegiNext.KoumoriPackage.ps1')

    . (Join-Path $PSScriptRoot 'Fixtures/KoumoriPackage.ps1')
}

Describe 'Versioned Koumori release packages' {
    It 'allows schema1 frontend integers to be absent null or explicitly correct: <State>' -TestCases @(
        @{ State = 'absent' }, @{ State = 'null' }, @{ State = 'integer' }) {
        param($State)
        $source = Join-Path $TestDrive "schema1 frontend $State"
        New-KoumoriTestPackage $source
        $path = Join-Path $source 'manifest.json'
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        if ($State -ne 'absent')
        {
            $manifest.featureCount = if ($State -eq 'integer') { 82 } else { $null }
            $manifest.sampleRate = if ($State -eq 'integer') { 16000 } else { $null }
        }
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path
        (Get-AegiNextKoumoriPackage $source 'win-x64').Files.Count | Should -Be 4
    }

    It 'rejects an incompatible frontend integer for schema <Schema>, <Field>, <Fault>' -TestCases @(
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
        $source = Join-Path $TestDrive "frontend $Schema $Field $Fault"
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
        { Get-AegiNextKoumoriPackage $source 'win-x64' } | Should -Throw "*$Field*"
    }

    It 'resolves only the repository model directory unless an explicit source is supplied' {
        $root = Join-Path $TestDrive 'default package source'
        $source = Join-Path $root 'models/koumori'
        Resolve-AegiNextKoumoriModelDirectory $root | Should -BeNullOrEmpty
        [IO.Directory]::CreateDirectory($source) | Out-Null
        Set-Content -LiteralPath (Join-Path $source 'README.md') -Value 'place runtime files here'
        Resolve-AegiNextKoumoriModelDirectory $root | Should -BeNullOrEmpty
        New-KoumoriTestPackage $source
        Resolve-AegiNextKoumoriModelDirectory $root | Should -BeExactly $source
        $manual = Join-Path $TestDrive 'manual model'
        Resolve-AegiNextKoumoriModelDirectory $root $manual | Should -BeExactly $manual
    }

    It 'reports incomplete default sources instead of silently omitting the model' {
        $root = Join-Path $TestDrive 'incomplete default source'
        $source = Join-Path $root 'models/koumori'
        [IO.Directory]::CreateDirectory($source) | Out-Null
        Set-Content -LiteralPath (Join-Path $source 'model.onnx') -Value 'partial runtime'
        { Resolve-AegiNextKoumoriModelDirectory $root } | Should -Throw '*Missing Koumori model manifest*'
    }

    It 'validates an already published package and rejects unexpected payloads or a changed source identity' {
        $source = Join-Path $TestDrive 'published metadata source'
        $payload = Join-Path $TestDrive 'published metadata payload'
        New-KoumoriTestPackage $source
        $expected = Get-AegiNextKoumoriPackage $source 'win-x64'
        Copy-AegiNextKoumoriPackage $source $payload 'win-x64' | Out-Null
        (Get-AegiNextPublishedKoumoriMetadata $payload 'win-x64' $expected).ModelId | Should -BeExactly $expected.ModelId
        $expected.ModelId = 'different-source'
        { Get-AegiNextPublishedKoumoriMetadata $payload 'win-x64' $expected } | Should -Throw '*does not match its source*'
        Set-Content -LiteralPath (Join-Path $payload 'models/koumori/epoch.pt') -Value 'unexpected training file'
        { Get-AegiNextPublishedKoumoriMetadata $payload 'win-x64' } | Should -Throw '*unexpected files*'
    }

    It 'copies exactly eight verified generation and snap package files for <Rid>' -TestCases @(@{ Rid = 'win-x64' }, @{ Rid = 'osx-arm64' }) {
        param($Rid)
        $source = Join-Path $TestDrive "generation-snap-source-$Rid"
        $payload = Join-Path $TestDrive "generation-snap-payload-$Rid"
        New-KoumoriTestPackage -Directory $source -GenerationSnap $true
        [IO.File]::WriteAllText((Join-Path $source 'training-private.json'), 'not deployed')
        $package = Get-AegiNextKoumoriPackage $source $Rid
        $package.Files.Count | Should -Be 8
        $package.Files | Should -Contain 'snap-model.onnx'
        Copy-AegiNextKoumoriPackage $source $payload $Rid | Out-Null
        $target = Join-Path $payload 'models/koumori'
        @(Get-ChildItem -LiteralPath $target -File).Count | Should -Be 8
        (@(Get-ChildItem -LiteralPath $target -File -Name | Sort-Object) -join ',') |
            Should -BeExactly (@($package.Files | Sort-Object) -join ',')
        foreach ($filename in $package.Files)
        {
            (Get-FileHash -LiteralPath (Join-Path $target $filename)).Hash |
                Should -BeExactly (Get-FileHash -LiteralPath (Join-Path $source $filename)).Hash
        }
    }

    It 'rejects incomplete or changed snap bindings before publication: <Fault>' -TestCases @(
        @{ Fault = 'missing-file' }, @{ Fault = 'missing-binding' }, @{ Fault = 'missing-hash' },
        @{ Fault = 'hash' }, @{ Fault = 'architecture' }, @{ Fault = 'alias' },
        @{ Fault = 'traversal' }, @{ Fault = 'drive-path' }) {
        param($Fault)
        $source = Join-Path $TestDrive "snap-invalid-$Fault"
        $payload = Join-Path $TestDrive "snap-invalid-payload-$Fault"
        New-KoumoriTestPackage -Directory $source -GenerationSnap $true
        $path = Join-Path $source 'manifest.json'
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        switch ($Fault)
        {
            'missing-file' { Remove-Item -LiteralPath (Join-Path $source 'snap-model.onnx') }
            'missing-binding' { $manifest.Remove('snapModelFile') }
            'missing-hash' { $manifest.Remove('snapModelSha256') }
            'hash' { [IO.File]::AppendAllText((Join-Path $source 'snap-model.onnx'), 'changed') }
            'architecture' { $manifest.architecture = 'whisper-small-local-fusion-v1' }
            'alias' { $manifest.snapModelFile = 'model.onnx'; $manifest.snapModelSha256 = $manifest.modelSha256 }
            'traversal' { $manifest.snapModelFile = '../snap-model.onnx' }
            'drive-path' { $manifest.snapModelFile = 'C:snap-model.onnx' }
        }
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path
        { Copy-AegiNextKoumoriPackage $source $payload 'win-x64' } | Should -Throw
        Test-Path -LiteralPath $payload | Should -BeFalse
    }

    It 'never ignores a snap declaration on legacy schema <Schema> for <Field>' -TestCases @(
        @{ Schema = 1; Field = 'snapModelFile' }, @{ Schema = 1; Field = 'snapModelSha256' },
        @{ Schema = 2; Field = 'snapModelFile' }, @{ Schema = 2; Field = 'snapModelSha256' }) {
        param($Schema, $Field)
        $source = Join-Path $TestDrive "legacy-snap-$Schema-$Field"
        New-KoumoriTestPackage -Directory $source -Contextual ($Schema -eq 2)
        $path = Join-Path $source 'manifest.json'
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        $manifest[$Field] = $null
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path
        { Get-AegiNextKoumoriPackage $source 'osx-arm64' } | Should -Throw '*require schemaVersion 3*'
    }

    It 'rejects linked manifest, snap payload or package roots: <Kind>' -TestCases @(
        @{ Kind = 'manifest' }, @{ Kind = 'snap' }, @{ Kind = 'hardlink' }, @{ Kind = 'root' }) {
        param($Kind)
        $source = Join-Path $TestDrive "snap-linked-$Kind"
        New-KoumoriTestPackage -Directory $source -GenerationSnap $true
        if ($Kind -eq 'root')
        {
            $alias = Join-Path $TestDrive 'snap-root-link'
            New-Item -ItemType SymbolicLink -Path $alias -Target $source | Out-Null
            $source = $alias
        }
        elseif ($Kind -eq 'hardlink')
        {
            Remove-Item -LiteralPath (Join-Path $source 'snap-model.onnx')
            New-Item -ItemType HardLink -Path (Join-Path $source 'snap-model.onnx') -Target (Join-Path $source 'model.onnx') | Out-Null
        }
        else
        {
            $filename = if ($Kind -eq 'manifest') { 'manifest.json' } else { 'snap-model.onnx' }
            $outside = Join-Path $TestDrive "snap-linked-outside-$Kind"
            Move-Item -LiteralPath (Join-Path $source $filename) -Destination $outside
            New-Item -ItemType SymbolicLink -Path (Join-Path $source $filename) -Target $outside | Out-Null
        }
        { Get-AegiNextKoumoriPackage $source 'win-x64' } | Should -Throw '*linked*'
    }

    It 'rejects duplicate and case-aliased manifest fields' {
        $source = Join-Path $TestDrive 'snap-duplicate-json'
        New-KoumoriTestPackage -Directory $source -GenerationSnap $true
        $path = Join-Path $source 'manifest.json'
        $content = (Get-Content -LiteralPath $path -Raw).TrimEnd()
        [IO.File]::WriteAllText($path, $content.Substring(0, $content.Length - 1) + ', "SNAPMODELFILE": "snap-model.onnx" }')
        { Get-AegiNextKoumoriPackage $source 'osx-arm64' } | Should -Throw '*Duplicate or aliased*'
    }

    It 'copies schema3 runtime packages independently of training quality metadata for <Rid>' -TestCases @(@{ Rid = 'win-x64' }, @{ Rid = 'osx-arm64' }) {
        param($Rid)
        $source = Join-Path $TestDrive "snap-not-met-$Rid"
        $payload = Join-Path $TestDrive "snap-not-met-payload-$Rid"
        New-KoumoriTestPackage -Directory $source -GenerationSnap $true
        $path = Join-Path $source 'manifest.json'
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        $manifest.training.qualityAcceptance.status = 'not_met'
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path
        Copy-AegiNextKoumoriPackage $source $payload $Rid | Out-Null
        @(Get-ChildItem -LiteralPath (Join-Path $payload 'models/koumori') -File).Count | Should -Be 8
    }

    It 'copies and verifies contextual payloads and the encoder license for <Rid>' -TestCases @(@{ Rid = 'win-x64' }, @{ Rid = 'osx-arm64' }) {
        param($Rid)
        $source = Join-Path $TestDrive "context-source-$Rid"
        $payload = Join-Path $TestDrive "context-payload-$Rid"
        New-KoumoriTestPackage -Directory $source -Contextual $true
        Copy-AegiNextKoumoriPackage -ModelDirectory $source -PayloadDirectory $payload -RuntimeIdentifier $Rid | Out-Null
        @(Get-ChildItem -LiteralPath (Join-Path $payload 'models/koumori') -File).Count | Should -Be 7
        Test-Path -LiteralPath (Join-Path $payload 'models/koumori/encoder.onnx') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $payload 'models/koumori/encoder-frontend.json') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $payload 'models/koumori/WHISPER-LICENSE.txt') | Should -BeTrue
    }

    It 'rejects a corrupt contextual encoder before creating the destination' {
        $source = Join-Path $TestDrive 'context-corrupt-source'
        $payload = Join-Path $TestDrive 'context-corrupt-payload'
        New-KoumoriTestPackage -Directory $source -Contextual $true
        [IO.File]::AppendAllText((Join-Path $source 'encoder.onnx'), 'changed')
        { Copy-AegiNextKoumoriPackage $source $payload 'osx-arm64' } | Should -Throw '*hash mismatch*'
        Test-Path -LiteralPath $payload | Should -BeFalse
    }

    It 'rejects contextual packages with unknown tensor contracts' {
        $source = Join-Path $TestDrive 'context-invalid-source'
        New-KoumoriTestPackage -Directory $source -Contextual $true
        $path = Join-Path $source 'manifest.json'
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        $manifest.contextInputName = 'wrong'
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path
        { Get-AegiNextKoumoriPackage $source 'win-x64' } | Should -Throw '*contextual model contract*'
    }

    It 'copies only verified runtime files into the default model directory for <Rid>' -TestCases @(@{ Rid = 'win-x64' }, @{ Rid = 'osx-arm64' }) {
        param($Rid)
        $source = Join-Path $TestDrive "source-$Rid"
        $payload = Join-Path $TestDrive "payload-$Rid"
        New-KoumoriTestPackage $source
        [IO.File]::WriteAllText((Join-Path $source 'training-private.json'), 'not deployed')
        $result = Copy-AegiNextKoumoriPackage -ModelDirectory $source -PayloadDirectory $payload -RuntimeIdentifier $Rid
        $result.ModelId | Should -BeExactly 'koumori-publish-test'
        @(Get-ChildItem -LiteralPath (Join-Path $payload 'models/koumori') -File).Count | Should -Be 4
        Test-Path -LiteralPath (Join-Path $payload 'models/koumori/training-private.json') | Should -BeFalse
    }

    It 'rejects a changed file before creating the destination' {
        $source = Join-Path $TestDrive 'corrupt-source'
        $payload = Join-Path $TestDrive 'corrupt-payload'
        New-KoumoriTestPackage $source
        [IO.File]::AppendAllText((Join-Path $source 'model.onnx'), 'changed')
        { Copy-AegiNextKoumoriPackage $source $payload 'win-x64' } | Should -Throw '*hash mismatch*'
        Test-Path -LiteralPath $payload | Should -BeFalse
    }

    It 'validates runtime files without using training metadata as a release gate' {
        $source = Join-Path $TestDrive 'fixture-source'
        New-KoumoriTestPackage $source $false
        (Get-AegiNextKoumoriPackage $source 'osx-arm64').Files.Count | Should -Be 4
        $path = Join-Path $source 'manifest.json'
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        $manifest.Remove('training')
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path
        (Get-AegiNextKoumoriPackage $source 'osx-arm64').Files.Count | Should -Be 4
    }

    It 'rejects path traversal and unsupported architectures' {
        $source = Join-Path $TestDrive 'invalid-source'
        New-KoumoriTestPackage $source
        $path = Join-Path $source 'manifest.json'
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        $manifest.modelFile = '../model.onnx'
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path
        { Get-AegiNextKoumoriPackage $source 'win-x64' } | Should -Throw '*Invalid Koumori model file*'
        { Get-AegiNextKoumoriPackage $source 'osx-x64' } | Should -Throw '*not supported*'
    }

    It 'does not rewrite training quality metadata when validating a runtime package' {
        $source = Join-Path $TestDrive 'experimental-source'
        New-KoumoriTestPackage $source
        $path = Join-Path $source 'manifest.json'
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        $manifest.training.qualityAcceptance.status = 'not_met'
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path
        $before = (Get-FileHash -LiteralPath $path).Hash
        (Get-AegiNextKoumoriPackage $source 'win-x64').ModelId | Should -Be 'koumori-publish-test'
        (Get-FileHash -LiteralPath $path).Hash | Should -BeExactly $before
    }
}
