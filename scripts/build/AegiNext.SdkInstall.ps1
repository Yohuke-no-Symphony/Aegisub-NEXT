#Requires -Version 7.2

function Invoke-AegiNextSdkCommand
{
    param([string] $FilePath, [string[]] $Arguments, [string] $WorkingDirectory, [hashtable] $Environment = @{})
    $result = Invoke-AegiNextCommand -FilePath $FilePath -Arguments $Arguments -WorkingDirectory $WorkingDirectory -Environment $Environment -StreamOutput
    if ($result.ExitCode -ne 0)
    {
        $detail = ($result.Output -split '\r?\n' | Select-Object -Last 20) -join [Environment]::NewLine
        $failure = [InvalidOperationException]::new("SDK command failed: $FilePath (exit $($result.ExitCode)). $detail")
        $failure.Data['ExitCode'] = $result.ExitCode
        throw $failure
    }
}

function Get-AegiNextSdkArchive
{
    param([hashtable] $Source, [string] $RepositoryRoot)
    if ($Source.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or !([uri]$Source.url).IsAbsoluteUri -or ([uri]$Source.url).Scheme -ne 'https')
    {
        throw 'SDK downloads require an HTTPS URL and a locked SHA256.'
    }
    $directory = Join-Path $RepositoryRoot '.dependencies/downloads'
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $archive = Join-Path $directory "$($Source.sha256)-$($Source.archive)"
    if (Test-Path -LiteralPath $archive -PathType Leaf)
    {
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -eq $Source.sha256)
        {
            return $archive
        }
        Remove-Item -LiteralPath $archive -Force
    }
    $temporary = "$archive.$([guid]::NewGuid().ToString('N')).partial"
    try
    {
        Write-Information -InformationAction Continue -MessageData "Downloading locked SDK source: $($Source.url)"
        Invoke-WebRequest -Uri $Source.url -OutFile $temporary -MaximumRetryCount 2 -RetryIntervalSec 2 -ErrorAction Stop
        if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $Source.sha256)
        {
            throw "SHA256 mismatch for $($Source.url). The downloaded file will not be extracted."
        }
        Move-Item -LiteralPath $temporary -Destination $archive -Force
        return $archive
    }
    finally
    {
        if (Test-Path -LiteralPath $temporary)
        {
            Remove-Item -LiteralPath $temporary -Force
        }
    }
}

function Expand-AegiNextSdkArchive
{
    param([string] $Archive, [string] $Destination, [string] $ArchiveRoot)
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    if ($Archive.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase))
    {
        [IO.Compression.ZipFile]::ExtractToDirectory($Archive, $Destination, $true)
    }
    elseif ($Archive.EndsWith('.7z', [StringComparison]::OrdinalIgnoreCase))
    {
        $command = Find-AegiNextCommand '7z'
        if (!$command)
        {
            throw '7z is required to extract the Windows FFmpeg SDK. Install Scoop main/7zip.'
        }
        Invoke-AegiNextSdkCommand $command @('x', '-y', "-o$Destination", $Archive) $Destination
    }
    else
    {
        $command = Find-AegiNextCommand 'tar'
        if (!$command)
        {
            throw 'tar is required to extract SDK source archives.'
        }
        Invoke-AegiNextSdkCommand $command @('-xf', $Archive, '-C', $Destination) $Destination
    }
    if ($ArchiveRoot)
    {
        $root = Join-Path $Destination $ArchiveRoot
    }
    else
    {
        $directories = @(Get-ChildItem -LiteralPath $Destination -Directory)
        if ($directories.Count -ne 1)
        {
            throw "Expected one source root in $Archive; found $($directories.Count)."
        }
        $root = $directories[0].FullName
    }
    if (!(Test-Path -LiteralPath $root -PathType Container))
    {
        throw "The locked archive root does not exist: $root."
    }
    return $root
}

function Install-AegiNextSdkTool
{
    param([object] $HostInfo, [string[]] $Names, [string] $RepositoryRoot)
    $tools = if ($HostInfo.Platform -eq 'Windows')
    {
        if ('ffmpeg' -in $Names) { @{ '7z' = 'main/7zip' } } else { @{} }
    }
    else
    {
        $result = @{ cmake = 'cmake'; ninja = 'ninja' }
        if ('ffmpeg' -in $Names -or 'libplacebo' -in $Names -or 'dav1d' -in $Names)
        {
            $result['pkg-config'] = 'pkgconf'
        }
        if ('libplacebo' -in $Names -or 'dav1d' -in $Names)
        {
            $result['meson'] = 'meson'
            $result['python3'] = 'python@3.14'
        }
        if ($HostInfo.Architecture -eq 'X64' -and ('ffmpeg' -in $Names -or 'dav1d' -in $Names -or 'libvpx' -in $Names -or 'svt-av1' -in $Names))
        {
            $result['nasm'] = 'nasm'
        }
        $result
    }
    foreach ($tool in $tools.Keys | Sort-Object)
    {
        if (Find-AegiNextCommand $tool)
        {
            continue
        }
        $managerName = if ($HostInfo.Platform -eq 'Windows') { 'scoop' } else { 'brew' }
        $manager = Find-AegiNextCommand $managerName
        if (!$manager)
        {
            throw "Install $tool before preparing project SDKs; $managerName is unavailable."
        }
        Invoke-AegiNextSdkCommand $manager @('install', $tools[$tool]) $RepositoryRoot
        if (!(Find-AegiNextCommand $tool))
        {
            throw "$tool is still unavailable after installation. Reload the package-manager environment."
        }
    }
    if ($HostInfo.Platform -eq 'MacOS')
    {
        foreach ($tool in @('xcrun', 'make', 'tar'))
        {
            if (!(Find-AegiNextCommand $tool))
            {
                throw "Apple SDK source builds require $tool. Install/select Command Line Tools or Xcode."
            }
        }
    }
}

function Add-AegiNextSdkDependency
{
    param([string] $Name, [hashtable] $Manifest, [string] $Platform, [Collections.Generic.List[string]] $Ordered,
        [Collections.Generic.HashSet[string]] $Visiting, [Collections.Generic.HashSet[string]] $Visited)
    if ($visited.Contains($Name))
    {
        return
    }
    if (!$Manifest.packages.ContainsKey($Name) -or !$visiting.Add($Name))
    {
        throw "Unknown or cyclic SDK dependency: $Name."
    }
    $package = $Manifest.packages[$Name]
    if ($package.ContainsKey('platforms') -and $Platform -notin $package.platforms)
    {
        throw "Project SDK $Name does not support $Platform."
    }
    if ($Platform -eq 'Windows')
    {
        if (!$package.ContainsKey('windows'))
        {
            throw "Project SDK $Name does not support Windows."
        }
    }
    elseif ($Platform -eq 'MacOS')
    {
        foreach ($dependency in $package.dependencies)
        {
            Add-AegiNextSdkDependency $dependency $Manifest $Platform $Ordered $Visiting $Visited
        }
    }
    else
    {
        throw "Unsupported SDK platform: $Platform."
    }
    $null = $visiting.Remove($Name)
    $null = $visited.Add($Name)
    $ordered.Add($Name)
}

function Get-AegiNextSdkInstallOrder
{
    param([hashtable] $Manifest, [string[]] $Names, [string] $Platform)
    $ordered = [Collections.Generic.List[string]]::new()
    $visiting = [Collections.Generic.HashSet[string]]::new()
    $visited = [Collections.Generic.HashSet[string]]::new()
    foreach ($name in $Names)
    {
        Add-AegiNextSdkDependency $name $Manifest $Platform $ordered $visiting $visited
    }
    return $ordered.ToArray()
}

function Restore-AegiNextInterruptedSdk
{
    param([string] $Root, [string] $Name, [string] $RepositoryRoot, [object] $HostInfo, [hashtable] $Manifest)
    $backup = "$Root.previous"
    if (!(Test-Path -LiteralPath $backup))
    {
        return
    }
    $rid = Get-AegiNextRuntimeIdentifier -HostInfo $HostInfo
    foreach ($path in @($backup, $Root))
    {
        if (!(Test-Path -LiteralPath $path))
        {
            continue
        }
        $receiptPath = Join-Path $path '.aeginext-sdk.json'
        $receipt = if (Test-Path -LiteralPath $receiptPath -PathType Leaf) { Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json -AsHashtable }
        if (!$receipt -or $receipt.owner -ne 'AegiNext' -or $receipt.name -ne $Name -or $receipt.rid -ne $rid)
        {
            throw "Cannot recover an unmanaged SDK replacement: $path. Existing files are preserved."
        }
    }
    $valid = Test-AegiNextInstalledSdk $Root $Manifest $Name $HostInfo
    if ($valid)
    {
        try
        {
            Test-AegiNextPreparedSdk $Root $RepositoryRoot $HostInfo $Manifest $Name
        }
        catch
        {
            $valid = $false
        }
    }
    if ($valid)
    {
        Remove-Item -LiteralPath $backup -Recurse -Force
        return
    }
    if (Test-Path -LiteralPath $Root)
    {
        $preserved = Join-Path $RepositoryRoot ".dependencies/.work/interrupted-$rid-$Name-$([guid]::NewGuid().ToString('N'))"
        [IO.Directory]::CreateDirectory((Split-Path $preserved)) | Out-Null
        Move-Item -LiteralPath $Root -Destination $preserved
    }
    Move-Item -LiteralPath $backup -Destination $Root
    Write-Information -InformationAction Continue -MessageData "Restored the previous project SDK after an interrupted replacement: $Name."
}

function Install-AegiNextProjectSdk
{
    param([string] $RepositoryRoot, [object] $HostInfo, [string[]] $Names, [ValidateRange(1, 128)][int] $Jobs = 2)
    if (!$Names.Count)
    {
        return
    }
    $manifest = Get-AegiNextDependencyManifest
    $order = @(Get-AegiNextSdkInstallOrder $manifest $Names $HostInfo.Platform)
    $rid = Get-AegiNextRuntimeIdentifier -HostInfo $HostInfo
    $directory = Join-Path $RepositoryRoot '.dependencies'
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $lock = $null
    try
    {
        try
        {
            $lock = [IO.File]::Open((Join-Path $directory '.install.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        }
        catch
        {
            throw 'Another project SDK installation is running. Wait for it to finish and retry.'
        }
        foreach ($name in $order)
        {
            Restore-AegiNextInterruptedSdk (Get-AegiNextProjectSdkRoot $RepositoryRoot $HostInfo $name) $name $RepositoryRoot $HostInfo $manifest
        }
        $pending = @($order | Where-Object {
            !(Test-AegiNextInstalledSdk (Get-AegiNextProjectSdkRoot $RepositoryRoot $HostInfo $_) $manifest $_ $HostInfo)
        })
        if (!$pending.Count)
        {
            Write-Information -InformationAction Continue -MessageData "Locked project SDKs are ready: $rid."
            return
        }
        Install-AegiNextSdkTool $HostInfo $order $RepositoryRoot
        foreach ($name in $pending)
        {
            $root = Get-AegiNextProjectSdkRoot $RepositoryRoot $HostInfo $name
            $backup = "$root.previous"
            if (Test-Path -LiteralPath $backup)
            {
                throw "An interrupted SDK replacement needs inspection: $backup. Existing files are preserved."
            }
            if (Test-Path -LiteralPath $root)
            {
                $receiptPath = Join-Path $root '.aeginext-sdk.json'
                $receipt = if (Test-Path -LiteralPath $receiptPath -PathType Leaf) { Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json -AsHashtable }
                if (!$receipt -or $receipt.owner -ne 'AegiNext' -or $receipt.name -ne $name -or $receipt.rid -ne $rid)
                {
                    throw "Refusing to replace an unmanaged SDK at $root. Move it aside or select it explicitly with an SDK root parameter."
                }
            }
            $work = Join-Path $directory ".work/$rid-$name-$([guid]::NewGuid().ToString('N'))"
            $stage = Join-Path $work 'sdk'
            [IO.Directory]::CreateDirectory($stage) | Out-Null
            $promoted = $false
            $succeeded = $false
            try
            {
                Write-Information -InformationAction Continue -MessageData "Preparing project SDK: $name $($manifest.packages[$name].version) / $rid"
                Invoke-AegiNextSdkRecipe $RepositoryRoot $HostInfo $manifest $name $work $stage $Jobs
                if ($HostInfo.Platform -eq 'MacOS')
                {
                    Repair-AegiNextSdkPath $stage $root $RepositoryRoot $HostInfo
                }
                Test-AegiNextPreparedSdk $stage $RepositoryRoot $HostInfo $manifest $name -SkipRuntime
                $files = [ordered]@{}
                foreach ($file in Get-ChildItem -LiteralPath $stage -File -Recurse | Sort-Object FullName)
                {
                    $relative = [IO.Path]::GetRelativePath($stage, $file.FullName).Replace('\', '/')
                    $files[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                }
                $receipt = [ordered]@{
                    owner = 'AegiNext'; name = $name; version = $manifest.packages[$name].version; rid = $rid; installPrefix = [IO.Path]::GetFullPath($root)
                    fingerprint = Get-AegiNextSdkFingerprint $manifest $name $HostInfo.Platform
                    installedAt = [DateTime]::UtcNow.ToString('O'); files = $files
                }
                $receipt | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $stage '.aeginext-sdk.json') -Encoding utf8
                [IO.Directory]::CreateDirectory((Split-Path $root)) | Out-Null
                if (Test-Path -LiteralPath $root)
                {
                    Move-Item -LiteralPath $root -Destination $backup
                }
                Move-Item -LiteralPath $stage -Destination $root
                $promoted = $true
                Test-AegiNextPreparedSdk $root $RepositoryRoot $HostInfo $manifest $name
                if (Test-Path -LiteralPath $backup)
                {
                    Remove-Item -LiteralPath $backup -Recurse -Force
                }
                $succeeded = $true
            }
            catch
            {
                if ($promoted -and (Test-Path -LiteralPath $root))
                {
                    Remove-Item -LiteralPath $root -Recurse -Force
                }
                if (Test-Path -LiteralPath $backup)
                {
                    Move-Item -LiteralPath $backup -Destination $root
                }
                Write-Information -InformationAction Continue -MessageData "SDK preparation failed; build logs and sources remain at $work."
                throw
            }
            finally
            {
                if ($succeeded -and (Test-Path -LiteralPath $work))
                {
                    Remove-Item -LiteralPath $work -Recurse -Force
                }
            }
        }
    }
    finally
    {
        if ($lock)
        {
            $lock.Dispose()
        }
    }
}
