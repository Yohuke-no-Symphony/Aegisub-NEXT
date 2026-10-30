#Requires -Version 7.2

function Get-AegiNextMsvcRuntime
{
    param([string] $RepositoryRoot, [string[]] $RuntimeDependencyDirectory = @())
    foreach ($directory in $RuntimeDependencyDirectory)
    {
        $runtime = Join-Path $directory 'vcruntime140.dll'
        if (!(Test-Path -LiteralPath $runtime -PathType Leaf))
        {
            continue
        }
        $runtime = (Resolve-Path -LiteralPath $runtime).ProviderPath
        $runtimeDirectory = Split-Path $runtime -Parent
        $info = Get-AegiNextMsvcRuntimeBinaryInfo $runtime
        if ($info.Architecture -ne 'x64')
        {
            throw "The explicit MSVC runtime requires x64 native code: $runtime"
        }
        return [pscustomobject]@{
            RuntimeDirectory = $runtimeDirectory
            LicenseRoot = $runtimeDirectory
            Version = $info.Version
            Source = 'Explicit'
        }
    }

    $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64' }
    $root = Find-AegiNextProjectSdk $RepositoryRoot $hostInfo 'msvc-runtime'
    if ($root)
    {
        $root = (Resolve-Path -LiteralPath $root).ProviderPath
        $manifest = Get-AegiNextDependencyManifest
        if (!(Test-AegiNextInstalledSdk $root $manifest 'msvc-runtime' $hostInfo))
        {
            throw "The project MSVC runtime is incomplete or its lock changed. Run build.ps1 -Target Workbench -InstallDependencies to repair $root."
        }
        Test-AegiNextPreparedSdk $root $RepositoryRoot $hostInfo $manifest 'msvc-runtime'
        $runtime = Join-Path $root 'bin/vcruntime140.dll'
        $info = Get-AegiNextMsvcRuntimeBinaryInfo $runtime
        return [pscustomobject]@{
            RuntimeDirectory = Join-Path $root 'bin'
            LicenseRoot = $root
            Version = $info.Version
            Source = 'ProjectSdk'
        }
    }

    $roots = [Collections.Generic.List[string]]::new()
    if ($env:VCToolsRedistDir)
    {
        $roots.Add($env:VCToolsRedistDir)
    }
    $vswhere = Find-AegiNextCommand 'vswhere'
    if (!$vswhere -and ${env:ProgramFiles(x86)})
    {
        $installed = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        if (Test-Path -LiteralPath $installed -PathType Leaf)
        {
            $vswhere = $installed
        }
    }
    if ($vswhere)
    {
        $result = Invoke-AegiNextCommand $vswhere @('-products', '*', '-format', 'json', '-utf8') (Get-Location).Path
        if ($result.ExitCode -ne 0)
        {
            throw "Visual Studio runtime discovery failed: $($result.Output)"
        }
        foreach ($instance in @($result.Output | ConvertFrom-Json))
        {
            $redist = Join-Path $instance.installationPath 'VC/Redist/MSVC'
            if (!(Test-Path -LiteralPath $redist -PathType Container))
            {
                continue
            }
            foreach ($version in Get-ChildItem -LiteralPath $redist -Directory)
            {
                if ($version.Name -match '^14\.\d+\.\d+$')
                {
                    $roots.Add($version.FullName)
                }
            }
        }
    }
    $candidates = foreach ($redistRoot in $roots | Select-Object -Unique)
    {
        $x64 = Join-Path $redistRoot 'x64'
        if (!(Test-Path -LiteralPath $x64 -PathType Container))
        {
            continue
        }
        foreach ($crt in Get-ChildItem -LiteralPath $x64 -Directory -Filter 'Microsoft.VC*.CRT')
        {
            $runtime = Join-Path $crt.FullName 'vcruntime140.dll'
            if (!(Test-Path -LiteralPath $runtime -PathType Leaf))
            {
                continue
            }
            $info = Get-AegiNextMsvcRuntimeBinaryInfo $runtime
            if ($info.Architecture -ne 'x64')
            {
                continue
            }
            [pscustomobject]@{
                RuntimeDirectory = $crt.FullName
                LicenseRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../publish/licenses/msvc-runtime'))
                Version = $info.Version
                Source = 'VisualStudio'
            }
        }
    }
    return $candidates | Sort-Object { [version]$_.Version } -Descending | Select-Object -First 1
}
