#Requires -Version 7.2

function Resolve-AegiNextKoumoriModelDirectory
{
    param([Parameter(Mandatory)][string] $RepositoryRoot, [string] $ModelDirectory)
    if ($ModelDirectory)
    {
        return [IO.Path]::GetFullPath($ModelDirectory)
    }
    $root = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'models/koumori'))
    if (Test-Path -LiteralPath (Join-Path $root 'manifest.json') -PathType Leaf)
    {
        return $root
    }
    if (Test-Path -LiteralPath $root)
    {
        if (!(Test-Path -LiteralPath $root -PathType Container) -or
            @(Get-ChildItem -LiteralPath $root -Force | Where-Object Name -CNotIn @('README.md', '.gitkeep')).Count)
        {
            throw "Missing Koumori model manifest: $(Join-Path $root 'manifest.json')"
        }
    }
    return $null
}

function Assert-AegiNextKoumoriRegularPath
{
    param([Parameter(Mandatory)][string] $Path)
    $current = [IO.Path]::GetFullPath($Path)
    while (![string]::IsNullOrWhiteSpace($current))
    {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
        if ($item.LinkType -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint))
        {
            throw "Missing or linked Koumori package path: $current"
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function Assert-AegiNextKoumoriUniqueJsonProperties
{
    param([Parameter(Mandatory)][string] $Content)
    $options = [Text.Json.JsonDocumentOptions]::new()
    $options.MaxDepth = 1024
    $document = [Text.Json.JsonDocument]::Parse($Content, $options)
    try
    {
        $pending = [Collections.Generic.Stack[Text.Json.JsonElement]]::new()
        $pending.Push($document.RootElement)
        while ($pending.Count -gt 0)
        {
            $element = $pending.Pop()
            if ($element.ValueKind -eq [Text.Json.JsonValueKind]::Object)
            {
                $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
                foreach ($property in $element.EnumerateObject())
                {
                    if (!$names.Add($property.Name)) { throw "Duplicate or aliased Koumori manifest property: $($property.Name)" }
                    $pending.Push($property.Value)
                }
            }
            elseif ($element.ValueKind -eq [Text.Json.JsonValueKind]::Array)
            {
                foreach ($value in $element.EnumerateArray()) { $pending.Push($value) }
            }
        }
    }
    finally
    {
        $document.Dispose()
    }
}

function Get-AegiNextKoumoriPackage
{
    param([Parameter(Mandatory)][string] $ModelDirectory, [Parameter(Mandatory)][string] $RuntimeIdentifier)
    if ($RuntimeIdentifier -notin @('win-x64', 'osx-arm64')) { throw "Koumori is not supported on $RuntimeIdentifier." }
    $root = [IO.Path]::GetFullPath($ModelDirectory)
    Assert-AegiNextKoumoriRegularPath $root
    $manifestPath = Join-Path $root 'manifest.json'
    if (!(Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Missing Koumori model manifest: $manifestPath" }
    Assert-AegiNextKoumoriRegularPath $manifestPath
    $content = Get-Content -LiteralPath $manifestPath -Raw
    Assert-AegiNextKoumoriUniqueJsonProperties $content
    $manifest = $content | ConvertFrom-Json -AsHashtable
    if ($manifest -isnot [Collections.IDictionary] -or $manifest.Keys -cnotcontains 'schemaVersion' -or
        ($manifest['schemaVersion'] -isnot [long] -and $manifest['schemaVersion'] -isnot [int]) -or
        $manifest['schemaVersion'] -notin @(1, 2, 3) -or [string]::IsNullOrWhiteSpace($manifest['modelId']) -or
        $manifest['inputName'] -cne 'features' -or $manifest['outputName'] -cne 'probabilities' -or
        $manifest['inputFrames'] -ne 2048 -or $manifest['outputFrames'] -ne 1024 -or $manifest['contextFrames'] -ne 512 -or
        $manifest['slots'] -lt 1 -or $manifest['slots'] -gt 64 -or ($manifest['channels'] -join ',') -cne 'coverage,start,end')
    {
        throw 'Unsupported Koumori model contract.'
    }
    $frontendIntegers = @{ featureCount = 82; sampleRate = 16000 }
    foreach ($field in $frontendIntegers.Keys)
    {
        $value = $manifest[$field]
        if ($null -eq $value)
        {
            if ($manifest['schemaVersion'] -in @(2, 3)) { throw "Missing Koumori frontend integer: $field" }
            continue
        }
        if (($value -isnot [int] -and $value -isnot [long]) -or $value -ne $frontendIntegers[$field])
        {
            throw "Invalid Koumori frontend integer: $field"
        }
    }
    $snapProperties = @($manifest.Keys | Where-Object { $_ -imatch '\AsnapModel(File|Sha256)\z' })
    $snapPayload = Get-Item -LiteralPath (Join-Path $root 'snap-model.onnx') -Force -ErrorAction SilentlyContinue
    if ($manifest['schemaVersion'] -ne 3 -and ($snapProperties.Count -gt 0 -or $null -ne $snapPayload -or
        $manifest['architecture'] -ceq 'whisper-small-generation-snap-v1'))
    {
        throw 'Koumori snap model fields and payload require schemaVersion 3.'
    }
    $architecture = if ($manifest['schemaVersion'] -eq 3) { 'whisper-small-generation-snap-v1' } else { 'whisper-small-local-fusion-v1' }
    if ($manifest['schemaVersion'] -in @(2, 3) -and ($manifest['architecture'] -cne $architecture -or
        $manifest['encoderInputName'] -cne 'mel' -or $manifest['encoderOutputName'] -cne 'hidden' -or
        $manifest['contextInputName'] -cne 'context'))
    {
        throw 'Unsupported Koumori contextual model contract.'
    }
    $files = [Collections.Generic.List[string]]::new()
    $files.Add('manifest.json')
    $usedNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $usedNames.Add('manifest.json') | Out-Null
    $canonicalNames = @{ model = 'model.onnx'; frontend = 'frontend.json'; decoder = 'decoder.json'; encoder = 'encoder.onnx'
        encoderFrontend = 'encoder-frontend.json'; encoderLicense = 'WHISPER-LICENSE.txt'; snapModel = 'snap-model.onnx' }
    $kinds = @('model', 'frontend', 'decoder')
    if ($manifest['schemaVersion'] -in @(2, 3))
    {
        $kinds += @('encoder', 'encoderFrontend', 'encoderLicense')
    }
    if ($manifest['schemaVersion'] -eq 3) { $kinds += 'snapModel' }
    foreach ($kind in $kinds)
    {
        $name = [string]$manifest["${kind}File"]
        $expected = [string]$manifest["${kind}Sha256"]
        if ($manifest.Keys -cnotcontains "${kind}File" -or $manifest.Keys -cnotcontains "${kind}Sha256" -or
            [string]::IsNullOrWhiteSpace($name) -or $name -ne [IO.Path]::GetFileName($name) -or
            $name.Contains('/') -or $name.Contains('\') -or $name.Contains(':') -or $name.Contains([char]0) -or
            $name -in @('.', '..') -or $expected -notmatch '\A[0-9a-fA-F]{64}\z')
        {
            throw "Invalid Koumori $kind file or digest."
        }
        if ($manifest['schemaVersion'] -eq 3 -and $name -cne $canonicalNames[$kind])
        {
            throw "Invalid Koumori $kind file: schemaVersion 3 requires $($canonicalNames[$kind])."
        }
        if (!$usedNames.Add($name)) { throw "Duplicate Koumori package filename: $name" }
        $path = Join-Path $root $name
        if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).LinkType)
        {
            throw "Missing or linked Koumori package file: $name"
        }
        Assert-AegiNextKoumoriRegularPath $path
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $expected) { throw "Koumori hash mismatch: $name" }
        $files.Add($name)
    }
    return [pscustomobject]@{ Root = $root; ModelId = $manifest['modelId']; Files = $files.ToArray(); ManifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() }
}

function Copy-AegiNextKoumoriPackage
{
    param([Parameter(Mandatory)][string] $ModelDirectory, [Parameter(Mandatory)][string] $PayloadDirectory,
        [Parameter(Mandatory)][string] $RuntimeIdentifier)
    $package = Get-AegiNextKoumoriPackage -ModelDirectory $ModelDirectory -RuntimeIdentifier $RuntimeIdentifier
    $target = Join-Path $PayloadDirectory 'models/koumori'
    if (Test-Path -LiteralPath $target) { throw "Koumori package destination already exists: $target" }
    [IO.Directory]::CreateDirectory($target) | Out-Null
    foreach ($name in $package.Files)
    {
        Copy-Item -LiteralPath (Join-Path $package.Root $name) -Destination (Join-Path $target $name)
    }
    $copied = Get-AegiNextKoumoriPackage -ModelDirectory $target -RuntimeIdentifier $RuntimeIdentifier
    if ($copied.ManifestSha256 -cne $package.ManifestSha256) { throw 'Koumori manifest changed during publication.' }
    return [pscustomobject]@{ ModelId = $package.ModelId; Directory = 'models/koumori'; ManifestSha256 = $package.ManifestSha256 }
}

function Get-AegiNextPublishedKoumoriMetadata
{
    param([Parameter(Mandatory)][string] $PayloadDirectory, [Parameter(Mandatory)][string] $RuntimeIdentifier,
        [object] $ExpectedPackage)
    $target = Join-Path $PayloadDirectory 'models/koumori'
    if (!(Test-Path -LiteralPath $target))
    {
        if ($null -ne $ExpectedPackage) { throw 'Missing published Koumori package.' }
        return $null
    }
    $package = Get-AegiNextKoumoriPackage -ModelDirectory $target -RuntimeIdentifier $RuntimeIdentifier
    $actualFiles = @(Get-ChildItem -LiteralPath $target -Recurse -Force)
    if ($actualFiles.Count -ne $package.Files.Count -or
        @($actualFiles | Where-Object { $_.PSIsContainer -or $_.Name -cnotin $package.Files }).Count)
    {
        throw 'Published Koumori package contains unexpected files.'
    }
    if ($null -ne $ExpectedPackage -and ($package.ModelId -cne $ExpectedPackage.ModelId -or
        $package.ManifestSha256 -cne $ExpectedPackage.ManifestSha256))
    {
        throw 'Published Koumori package does not match its source package.'
    }
    return [pscustomobject]@{ ModelId = $package.ModelId; Directory = 'models/koumori'; ManifestSha256 = $package.ManifestSha256 }
}
