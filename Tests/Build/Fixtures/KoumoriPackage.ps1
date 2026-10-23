function New-KoumoriTestPackage
{
    param([string] $Directory, [bool] $Trained = $true, [bool] $Contextual = $false, [bool] $GenerationSnap = $false)
    [IO.Directory]::CreateDirectory($Directory) | Out-Null
    $manifest = @{
        schemaVersion = 1; modelId = 'koumori-publish-test'; inputName = 'features'; outputName = 'probabilities'
        inputFrames = 2048; outputFrames = 1024; contextFrames = 512; slots = 3
        channels = @('coverage', 'start', 'end'); training = @{ trained = $Trained; purpose = 'trained_candidate'; qualityAcceptance = @{ status = 'passed' } }
    }
    foreach ($kind in @('model', 'frontend', 'decoder'))
    {
        $filename = if ($kind -eq 'model') { 'model.onnx' } else { "$kind.json" }
        $path = Join-Path $Directory $filename
        [IO.File]::WriteAllText($path, "$kind fixture")
        $manifest["${kind}File"] = $filename
        $manifest["${kind}Sha256"] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    if ($Contextual -or $GenerationSnap)
    {
        $manifest.schemaVersion = 2
        $manifest.featureCount = 82
        $manifest.sampleRate = 16000
        $manifest.architecture = 'whisper-small-local-fusion-v1'
        $manifest.encoderInputName = 'mel'
        $manifest.encoderOutputName = 'hidden'
        $manifest.contextInputName = 'context'
        foreach ($kind in @('encoder', 'encoderFrontend', 'encoderLicense'))
        {
            $filename = switch ($kind)
            {
                'encoder' { 'encoder.onnx' }
                'encoderFrontend' { 'encoder-frontend.json' }
                'encoderLicense' { 'WHISPER-LICENSE.txt' }
            }
            $path = Join-Path $Directory $filename
            [IO.File]::WriteAllText($path, "$kind fixture")
            $manifest["${kind}File"] = $filename
            $manifest["${kind}Sha256"] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    if ($GenerationSnap)
    {
        $manifest.schemaVersion = 3
        $manifest.architecture = 'whisper-small-generation-snap-v1'
        $manifest.snapModelFile = 'snap-model.onnx'
        [IO.File]::WriteAllText((Join-Path $Directory 'snap-model.onnx'), 'independent snap fixture')
        $manifest.snapModelSha256 = (Get-FileHash -LiteralPath (Join-Path $Directory 'snap-model.onnx') -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Directory 'manifest.json')
}
