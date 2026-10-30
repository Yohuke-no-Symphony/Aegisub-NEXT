#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    Import-Module (Join-Path $sourceRoot 'scripts/publish/AegiNext.Publish.psm1') -Force
    . (Join-Path $sourceRoot 'scripts/publish/AegiNext.ModelPackage.ps1')

    function New-ModelPublishFixture
    {
        param([string] $Directory, [int] $SchemaVersion = 3, [string] $EncoderLicenseName = 'WHISPER-LICENSE.txt')
        [IO.Directory]::CreateDirectory($Directory) | Out-Null
        $manifest = [ordered]@{ schemaVersion = $SchemaVersion; modelId = "model-publish-schema-$SchemaVersion" }
        $definitions = @(
            @{ Prefix = 'model'; Name = 'model.onnx' }
            @{ Prefix = 'frontend'; Name = 'frontend.json' }
            @{ Prefix = 'decoder'; Name = 'decoder.json' }
        )
        if ($SchemaVersion -ge 2)
        {
            $definitions += @(
                @{ Prefix = 'encoder'; Name = 'encoder.onnx' }
                @{ Prefix = 'encoderFrontend'; Name = 'encoder-frontend.json' }
                @{ Prefix = 'encoderLicense'; Name = $EncoderLicenseName }
            )
        }
        if ($SchemaVersion -eq 3)
        {
            $definitions += @{ Prefix = 'snapModel'; Name = 'snap-model.onnx' }
        }
        foreach ($definition in $definitions)
        {
            $path = Join-Path $Directory $definition.Name
            [IO.File]::WriteAllText($path, "fixture payload $($definition.Name)", [Text.UTF8Encoding]::new($false))
            $manifest[$definition.Prefix + 'File'] = $definition.Name
            $manifest[$definition.Prefix + 'Sha256'] = (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
        }
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Directory 'manifest.json') -Encoding utf8NoBOM
        return $manifest
    }

    function Set-ModelPublishInventory
    {
        param([string] $Directory, [string] $RuntimeIdentifier, [object] $ModelMetadata)
        $inventory = @{
            SchemaVersion = 1; ProductVersion = '1.2.3'; RuntimeIdentifier = $RuntimeIdentifier; SelfContained = $true
            Koumori = $ModelMetadata
            Files = @(foreach ($file in Get-ChildItem -LiteralPath $Directory -File -Recurse | Where-Object Name -ne 'package-manifest.json')
            {
                @{ Path = [IO.Path]::GetRelativePath($Directory, $file.FullName); Sha256 = (Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant(); Bytes = $file.Length }
            })
        }
        $inventory | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Directory 'package-manifest.json') -Encoding utf8NoBOM
    }
}

Describe 'Optional Koumori model packaging' {
    BeforeEach {
        $caseRoot = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $repository = Join-Path $caseRoot 'repository'
        $models = Join-Path $repository 'models/koumori'
        $payload = Join-Path $caseRoot 'payload'
    }

    It 'omits a model for an absent or README-only source without creating an output directory' {
        Get-AegiNextKoumoriModelPackage $models | Should -BeNullOrEmpty
        Copy-AegiNextKoumoriModel $repository $payload | Should -BeNullOrEmpty
        Test-Path -LiteralPath $payload | Should -BeFalse
        [IO.Directory]::CreateDirectory($models) | Out-Null
        Set-Content -LiteralPath (Join-Path $models 'README.md') -Value 'model download instructions'
        Get-AegiNextKoumoriModelPackage $models | Should -BeNullOrEmpty
        Copy-AegiNextKoumoriModel $repository $payload | Should -BeNullOrEmpty
        Test-Path -LiteralPath $payload | Should -BeFalse
    }

    It 'copies only the manifest references for schema <SchemaVersion> and records portable metadata' -TestCases @(
        @{ SchemaVersion = 1; ExpectedCount = 4 }
        @{ SchemaVersion = 2; ExpectedCount = 7 }
        @{ SchemaVersion = 3; ExpectedCount = 8 }
    ) {
        param($SchemaVersion, $ExpectedCount)
        $manifest = New-ModelPublishFixture $models $SchemaVersion
        Set-Content -LiteralPath (Join-Path $models 'README.md') -Value 'instructions'
        Set-Content -LiteralPath (Join-Path $models 'checkpoint.pt') -Value 'training checkpoint'
        Set-Content -LiteralPath (Join-Path $models 'unused.onnx') -Value 'not a selected runtime model'
        $package = Get-AegiNextKoumoriModelPackage $models
        $package.Files.Count | Should -Be $ExpectedCount
        $package.Files.Name | Should -Not -Contain 'README.md'
        $package.Files.Name | Should -Not -Contain 'checkpoint.pt'
        $package.Files.Name | Should -Not -Contain 'unused.onnx'
        $metadata = Copy-AegiNextKoumoriModel $repository $payload
        $metadata.Directory | Should -Be 'models/koumori'
        $metadata.ModelId | Should -Be $manifest.modelId
        $metadata.SchemaVersion | Should -Be $SchemaVersion
        $metadata.FileCount | Should -Be $ExpectedCount
        $metadata.ManifestSha256 | Should -Be (Get-FileHash -LiteralPath (Join-Path $models 'manifest.json')).Hash.ToLowerInvariant()
        $destination = Join-Path $payload 'models/koumori'
        @(Get-ChildItem -LiteralPath $destination -File).Count | Should -Be $ExpectedCount
        foreach ($file in $package.Files)
        {
            (Get-FileHash -LiteralPath (Join-Path $destination $file.Name)).Hash.ToLowerInvariant() | Should -Be $file.Sha256
            (Get-Item -LiteralPath (Join-Path $destination $file.Name)).Length | Should -Be $file.Bytes
        }
        if ($SchemaVersion -eq 1)
        {
            $metadata.EncoderLicenseFile | Should -BeNullOrEmpty
        }
        else
        {
            $metadata.EncoderLicenseFile | Should -Be 'WHISPER-LICENSE.txt'
        }
        { Copy-AegiNextKoumoriModel $repository $payload } | Should -Throw '*already exists*'
    }

    It 'rejects a runtime payload without its manifest (<Name>)' -TestCases @(
        @{ Name = 'model.onnx' }
        @{ Name = 'frontend.json' }
        @{ Name = 'decoder.json' }
        @{ Name = 'encoder-frontend.json' }
    ) {
        param($Name)
        [IO.Directory]::CreateDirectory($models) | Out-Null
        Set-Content -LiteralPath (Join-Path $models $Name) -Value 'incomplete runtime package'
        { Copy-AegiNextKoumoriModel $repository $payload } | Should -Throw '*Missing Koumori model manifest*'
        Test-Path -LiteralPath $payload | Should -BeFalse
    }

    It 'rejects invalid direct-child filenames (<Filename>)' -TestCases @(
        @{ Filename = '../outside.onnx' }
        @{ Filename = 'nested/model.onnx' }
        @{ Filename = 'nested\model.onnx' }
        @{ Filename = 'model.onnx:stream' }
        @{ Filename = '.' }
        @{ Filename = '..' }
        @{ Filename = '' }
    ) {
        param($Filename)
        $manifest = New-ModelPublishFixture $models 1
        $manifest.modelFile = $Filename
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $models 'manifest.json') -Encoding utf8NoBOM
        { Get-AegiNextKoumoriModelPackage $models } | Should -Throw '*direct children*'
    }

    It 'rejects duplicate case aliases and file aliases before copying' -TestCases @(
        @{ Defect = 'property-alias' }
        @{ Defect = 'payload-alias' }
        @{ Defect = 'manifest-alias' }
    ) {
        param($Defect)
        $manifest = New-ModelPublishFixture $models 1
        if ($Defect -eq 'payload-alias')
        {
            $manifest.frontendFile = 'MODEL.ONNX'
        }
        elseif ($Defect -eq 'manifest-alias')
        {
            $manifest.modelFile = 'manifest.json'
        }
        $json = $manifest | ConvertTo-Json -Depth 5
        if ($Defect -eq 'property-alias')
        {
            $json = $json.Replace('"modelId":', '"ModelId":"duplicate","modelId":')
        }
        Set-Content -LiteralPath (Join-Path $models 'manifest.json') -Value $json -Encoding utf8NoBOM
        { Copy-AegiNextKoumoriModel $repository $payload } | Should -Throw '*Duplicate Koumori*'
        Test-Path -LiteralPath $payload | Should -BeFalse
    }

    It 'requires the exact camelCase model field <Field>' -TestCases @(
        @{ Field = 'schemaVersion'; Alias = 'SchemaVersion' }
        @{ Field = 'modelFile'; Alias = 'ModelFile' }
    ) {
        param($Field, $Alias)
        $null = New-ModelPublishFixture $models 1
        $manifestPath = Join-Path $models 'manifest.json'
        $json = [IO.File]::ReadAllText($manifestPath).Replace(('"' + $Field + '"'), ('"' + $Alias + '"'))
        [IO.File]::WriteAllText($manifestPath, $json, [Text.UTF8Encoding]::new($false))
        { Get-AegiNextKoumoriModelPackage $models } | Should -Throw
    }

    It 'rejects unsupported schema, absent files, empty payloads and bad digests (<Defect>)' -TestCases @(
        @{ Defect = 'schema' }
        @{ Defect = 'missing-file' }
        @{ Defect = 'empty-file' }
        @{ Defect = 'sha256-format' }
        @{ Defect = 'sha256-mismatch' }
        @{ Defect = 'schema3-snap-name' }
        @{ Defect = 'old-schema-snap' }
    ) {
        param($Defect)
        $manifest = New-ModelPublishFixture $models 3
        switch ($Defect)
        {
            'schema' { $manifest.schemaVersion = 4 }
            'missing-file' { Remove-Item -LiteralPath (Join-Path $models 'encoder.onnx') }
            'empty-file' { [IO.File]::WriteAllBytes((Join-Path $models 'encoder.onnx'), [byte[]]::new(0)) }
            'sha256-format' { $manifest.modelSha256 = 'invalid hash' }
            'sha256-mismatch' { $manifest.modelSha256 = 'a' * 64 }
            'schema3-snap-name' { $manifest.snapModelFile = 'model-snap.onnx' }
            'old-schema-snap' { $manifest.schemaVersion = 2 }
        }
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $models 'manifest.json') -Encoding utf8NoBOM
        { Copy-AegiNextKoumoriModel $repository $payload } | Should -Throw
        Test-Path -LiteralPath $payload | Should -BeFalse
    }

    It 'rejects a referenced symbolic link instead of copying an external model' {
        $null = New-ModelPublishFixture $models 1
        $external = Join-Path $caseRoot 'external.onnx'
        Move-Item -LiteralPath (Join-Path $models 'model.onnx') -Destination $external
        try
        {
            [IO.File]::CreateSymbolicLink((Join-Path $models 'model.onnx'), $external) | Out-Null
        }
        catch
        {
            Set-ItResult -Skipped -Because 'This host does not allow symbolic-link creation.'
            return
        }
        { Copy-AegiNextKoumoriModel $repository $payload } | Should -Throw '*symbolic links*'
        Test-Path -LiteralPath $payload | Should -BeFalse
    }

    It 'detects source changes after preflight when verifying the copied bytes' {
        $null = New-ModelPublishFixture $models 3
        $package = Get-AegiNextKoumoriModelPackage $models
        Add-Content -LiteralPath (Join-Path $models 'model.onnx') -Value 'changed after preflight'
        { Copy-AegiNextKoumoriModel -RepositoryRoot $repository -Payload $payload -ModelPackage $package } | Should -Throw '*Copied Koumori model SHA-256 mismatch*'
    }

    It 'collects the encoder license selected by the manifest even with a custom filename' {
        $null = New-ModelPublishFixture $models 2 'upstream-terms.txt'
        $metadata = Copy-AegiNextKoumoriModel $repository $payload
        $metadata.EncoderLicenseFile | Should -Be 'upstream-terms.txt'
        $notices = @(Copy-AegiNextLicenseFile -Roots @((Join-Path $payload 'models/koumori')) -Destination (Join-Path $payload 'licenses') -FileNames @($metadata.EncoderLicenseFile))
        $notices.Count | Should -Be 1
        $notices[0].Sha256 | Should -Be (Get-FileHash -LiteralPath (Join-Path $models 'upstream-terms.txt')).Hash.ToLowerInvariant()
    }
}

Describe 'Packaged model verification is offline' {
    BeforeEach {
        $caseRoot = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $repository = Join-Path $caseRoot 'repository'
        $models = Join-Path $repository 'models/koumori'
        $packageRoot = Join-Path $caseRoot 'relocated package'
        $null = New-ModelPublishFixture $models 3
        Mock Get-AegiNextBuildVersion -ModuleName AegiNext.Publish { throw 'Model verification must not query source control.' }
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Publish { throw 'Model verification must not run tools.' }
    }

    It 'verifies a relocated model on <RuntimeIdentifier> without accessing its original source directory' -TestCases @(
        @{ RuntimeIdentifier = 'win-x64' }
        @{ RuntimeIdentifier = 'osx-arm64' }
        @{ RuntimeIdentifier = 'osx-x64' }
    ) {
        param($RuntimeIdentifier)
        $payload = if ($RuntimeIdentifier -eq 'win-x64') { Join-Path $packageRoot 'AegiNext' } else { Join-Path $packageRoot 'AegiNext.app/Contents/MacOS' }
        $metadata = Copy-AegiNextKoumoriModel $repository $payload
        $metadata.SourceDirectory = Join-Path $caseRoot 'source unavailable on target machine'
        $languages = Join-Path $payload 'i18n'
        [IO.Directory]::CreateDirectory($languages) | Out-Null
        Copy-Item -Path (Join-Path $sourceRoot 'src/AegiNext.Desktop/I18n/Languages/*.json') -Destination $languages
        Set-ModelPublishInventory $packageRoot $RuntimeIdentifier $metadata
        (Test-AegiNextPublishedPackage $packageRoot).HashesVerified | Should -BeTrue
        Should -Invoke Get-AegiNextBuildVersion -ModuleName AegiNext.Publish -Times 0 -Exactly
        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Publish -Times 0 -Exactly
    }

    It 'rejects inconsistent model metadata or internal digests even when the outer inventory is updated (<Defect>)' -TestCases @(
        @{ Defect = 'ModelId' }
        @{ Defect = 'SchemaVersion' }
        @{ Defect = 'ManifestSha256' }
        @{ Defect = 'FileCount' }
        @{ Defect = 'Directory' }
        @{ Defect = 'EncoderLicenseFile' }
        @{ Defect = 'PayloadDigest' }
    ) {
        param($Defect)
        $payload = Join-Path $packageRoot 'AegiNext'
        $metadata = Copy-AegiNextKoumoriModel $repository $payload
        $languages = Join-Path $payload 'i18n'
        [IO.Directory]::CreateDirectory($languages) | Out-Null
        Copy-Item -Path (Join-Path $sourceRoot 'src/AegiNext.Desktop/I18n/Languages/*.json') -Destination $languages
        switch ($Defect)
        {
            'ModelId' { $metadata.ModelId = 'unrelated model' }
            'SchemaVersion' { $metadata.SchemaVersion = 1 }
            'ManifestSha256' { $metadata.ManifestSha256 = 'a' * 64 }
            'FileCount' { $metadata.FileCount = 1 }
            'Directory' { $metadata.Directory = '../outside' }
            'EncoderLicenseFile' { $metadata.EncoderLicenseFile = $null }
            'PayloadDigest' { Add-Content -LiteralPath (Join-Path $payload 'models/koumori/model.onnx') -Value 'updated bytes with stale internal digest' }
        }
        Set-ModelPublishInventory $packageRoot 'win-x64' $metadata
        { Test-AegiNextPublishedPackage $packageRoot } | Should -Throw '*Koumori*'
    }
}
