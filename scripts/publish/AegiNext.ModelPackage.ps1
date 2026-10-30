#Requires -Version 7.2

function Get-AegiNextKoumoriModelPackage
{
    param([string] $ModelDirectory)
    if (!(Test-Path -LiteralPath $ModelDirectory -PathType Container))
    {
        return $null
    }
    $root = (Resolve-Path -LiteralPath $ModelDirectory).ProviderPath
    $manifestPath = Join-Path $root 'manifest.json'
    if (!(Test-Path -LiteralPath $manifestPath -PathType Leaf))
    {
        $modelFiles = @(Get-ChildItem -LiteralPath $root -File -Force | Where-Object {
            $_.Extension -eq '.onnx' -or $_.Name -in @('frontend.json', 'decoder.json', 'encoder-frontend.json')
        })
        if ($modelFiles.Count)
        {
            throw "Missing Koumori model manifest: $manifestPath"
        }
        return $null
    }
    if ($null -ne [IO.FileInfo]::new($manifestPath).LinkTarget)
    {
        throw "Koumori model files cannot be symbolic links: $manifestPath"
    }
    $manifestBytes = [IO.File]::ReadAllBytes($manifestPath)
    $manifestHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($manifestBytes)).ToLowerInvariant()
    $document = $null
    try
    {
        $document = [Text.Json.JsonDocument]::Parse([Text.Encoding]::UTF8.GetString($manifestBytes))
        if ($document.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Object)
        {
            throw 'The Koumori model manifest must be a JSON object.'
        }
        $properties = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
        $propertyNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($property in $document.RootElement.EnumerateObject())
        {
            if (!$propertyNames.Add($property.Name))
            {
                throw "Duplicate Koumori model manifest property: $($property.Name)"
            }
            $properties[$property.Name] = $property.Value
        }
        $schemaVersion = 0
        if (!$properties.ContainsKey('schemaVersion') -or $properties.schemaVersion.ValueKind -ne [Text.Json.JsonValueKind]::Number -or
            !$properties.schemaVersion.TryGetInt32([ref]$schemaVersion) -or $schemaVersion -notin @(1, 2, 3))
        {
            throw 'Unsupported Koumori model manifest schema. Expected schema 1, 2 or 3.'
        }
        if (!$properties.ContainsKey('modelId') -or $properties.modelId.ValueKind -ne [Text.Json.JsonValueKind]::String -or
            [string]::IsNullOrWhiteSpace($properties.modelId.GetString()))
        {
            throw 'The Koumori model manifest requires a modelId.'
        }
        $modelId = $properties.modelId.GetString()
        $prefixes = @('model', 'frontend', 'decoder')
        if ($schemaVersion -ge 2)
        {
            $prefixes += @('encoder', 'encoderFrontend', 'encoderLicense')
        }
        if ($schemaVersion -eq 3)
        {
            $prefixes += 'snapModel'
        }
        elseif ($properties.ContainsKey('snapModelFile') -or $properties.ContainsKey('snapModelSha256'))
        {
            throw 'Koumori snap model metadata requires schema 3.'
        }
        if ($schemaVersion -eq 1)
        {
            foreach ($key in @('encoderFile', 'encoderSha256', 'encoderFrontendFile', 'encoderFrontendSha256', 'encoderLicenseFile', 'encoderLicenseSha256'))
            {
                if ($properties.ContainsKey($key) -and $properties[$key].ValueKind -ne [Text.Json.JsonValueKind]::Null)
                {
                    throw 'Koumori encoder model metadata requires schema 2 or 3.'
                }
            }
        }
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $null = $names.Add('manifest.json')
        $files = [Collections.Generic.List[object]]::new()
        $files.Add([pscustomobject]@{ Name = 'manifest.json'; Source = $manifestPath; Sha256 = $manifestHash; Bytes = $manifestBytes.LongLength })
        $encoderLicenseFile = $null
        foreach ($prefix in $prefixes)
        {
            $fileKey = "${prefix}File"
            $hashKey = "${prefix}Sha256"
            if (!$properties.ContainsKey($fileKey) -or !$properties.ContainsKey($hashKey) -or
                $properties[$fileKey].ValueKind -ne [Text.Json.JsonValueKind]::String -or
                $properties[$hashKey].ValueKind -ne [Text.Json.JsonValueKind]::String)
            {
                throw "Missing Koumori model file or SHA-256 field: $fileKey / $hashKey"
            }
            $filename = $properties[$fileKey].GetString()
            $expectedHash = $properties[$hashKey].GetString()
            if ([string]::IsNullOrWhiteSpace($filename) -or $filename -in @('.', '..') -or
                $filename.Contains('/') -or $filename.Contains('\') -or $filename.Contains(':') -or $filename.Contains([char]0) -or
                $expectedHash -notmatch '\A[a-fA-F0-9]{64}\z')
            {
                throw "Koumori model files must be direct children with a SHA-256 digest: $fileKey"
            }
            if (!$names.Add($filename))
            {
                throw "Duplicate Koumori model file: $filename"
            }
            if ($prefix -eq 'snapModel' -and $filename -cne 'snap-model.onnx')
            {
                throw 'Koumori schema 3 requires snap-model.onnx.'
            }
            $path = Join-Path $root $filename
            if (!(Test-Path -LiteralPath $path -PathType Leaf))
            {
                throw "Missing Koumori model file: $path"
            }
            $info = [IO.FileInfo]::new($path)
            if ($null -ne $info.LinkTarget)
            {
                throw "Koumori model files cannot be symbolic links: $path"
            }
            if ($info.Length -eq 0)
            {
                throw "Koumori model files cannot be empty: $path"
            }
            $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($hash -ine $expectedHash)
            {
                throw "Koumori model SHA-256 mismatch: $path"
            }
            $files.Add([pscustomobject]@{ Name = $filename; Source = $path; Sha256 = $hash; Bytes = $info.Length })
            if ($prefix -eq 'encoderLicense')
            {
                $encoderLicenseFile = $filename
            }
        }
        return [pscustomobject]@{
            SourceDirectory = $root
            SchemaVersion = $schemaVersion
            ModelId = $modelId
            ManifestSha256 = $manifestHash
            EncoderLicenseFile = $encoderLicenseFile
            Files = $files.ToArray()
        }
    }
    finally
    {
        if ($null -ne $document)
        {
            $document.Dispose()
        }
    }
}

function Copy-AegiNextKoumoriModel
{
    param([string] $RepositoryRoot, [string] $Payload, [object] $ModelPackage)
    if (!$PSBoundParameters.ContainsKey('ModelPackage'))
    {
        $ModelPackage = Get-AegiNextKoumoriModelPackage -ModelDirectory (Join-Path $RepositoryRoot 'models/koumori')
    }
    if ($null -eq $ModelPackage)
    {
        return $null
    }
    $destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path $Payload 'models/koumori'))
    if (Test-Path -LiteralPath $destination)
    {
        throw "The bundled Koumori model destination already exists: $destination"
    }
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    foreach ($file in $ModelPackage.Files)
    {
        $path = Join-Path $destination $file.Name
        Copy-Item -LiteralPath $file.Source -Destination $path
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $file.Sha256)
        {
            throw "Copied Koumori model SHA-256 mismatch: $path"
        }
    }
    return [pscustomobject]@{
        SourceDirectory = $ModelPackage.SourceDirectory
        Directory = 'models/koumori'
        SchemaVersion = $ModelPackage.SchemaVersion
        ModelId = $ModelPackage.ModelId
        ManifestSha256 = $ModelPackage.ManifestSha256
        FileCount = $ModelPackage.Files.Count
        EncoderLicenseFile = $ModelPackage.EncoderLicenseFile
    }
}
