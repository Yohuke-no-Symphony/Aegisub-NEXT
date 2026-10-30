#Requires -Version 7.2

function Get-AegiNextWindowsCompilerRuntime
{
    param([string] $Compiler)
    if (!$Compiler -or ![IO.Path]::IsPathFullyQualified($Compiler) -or !(Test-Path -LiteralPath $Compiler -PathType Leaf))
    {
        throw 'The validated Windows compiler must have an existing absolute path.'
    }
    $canonical = Resolve-AegiNextCanonicalPath $Compiler
    $directory = Split-Path $canonical -Parent
    return [pscustomobject]@{ Compiler = $canonical; RuntimeDirectory = $directory; LicenseRoot = (Split-Path $directory -Parent); VerifiedTarget = 'x86_64-w64-mingw32' }
}

function Get-AegiNextWindowsMsvcRuntime
{
    param([string] $RepositoryRoot, [string[]] $RuntimeDependencyDirectory = @())
    return Get-AegiNextMsvcRuntime -RepositoryRoot $RepositoryRoot -RuntimeDependencyDirectory $RuntimeDependencyDirectory
}

function Copy-AegiNextWindowsDependencyClosure
{
    param([string] $Payload, [string[]] $SearchDirectories)
    $objdump = Find-AegiNextCommand 'objdump'
    if (!$objdump) { throw 'MinGW objdump is required to verify the Windows dependency closure.' }
    $queue = [Collections.Generic.Queue[string]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $records = [Collections.Generic.List[object]]::new()
    $sources = @{}
    foreach ($file in Get-ChildItem -LiteralPath $Payload -File -Recurse)
    {
        $kind = Get-AegiNextBinaryKind $file.FullName
        if ($kind.StartsWith('PE-')) { $queue.Enqueue($file.FullName); $sources[$file.FullName] = $file.FullName }
    }
    while ($queue.Count)
    {
        $binary = $queue.Dequeue()
        if (!$seen.Add($binary)) { continue }
        if ((Get-AegiNextBinaryKind $binary) -ne 'PE-34404') { throw "Windows package requires x64 native code: $binary" }
        $output = Invoke-AegiNextPublishCommand $objdump @('-p', $binary) $Payload
        $dependencies = @([regex]::Matches($output, '(?mi)^\s*DLL Name:\s*(\S+)') | ForEach-Object { $_.Groups[1].Value })
        foreach ($name in $dependencies)
        {
            if ($name -match '^(api-ms-win-|ext-ms-win-)') { continue }
            $systemNames = @('kernel32.dll', 'kernelbase.dll', 'ntdll.dll', 'advapi32.dll', 'bcrypt.dll', 'bcryptprimitives.dll', 'crypt32.dll',
                'cryptbase.dll', 'cryptsp.dll', 'ncrypt.dll', 'user32.dll', 'gdi32.dll', 'gdi32full.dll', 'fontsub.dll', 'dwmapi.dll', 'dwrite.dll', 'd2d1.dll', 'd3d11.dll', 'd3d12.dll',
                'dxgi.dll', 'msvcrt.dll', 'ucrtbase.dll', 'ole32.dll', 'oleaut32.dll', 'combase.dll', 'comctl32.dll', 'shell32.dll', 'shlwapi.dll',
                'shcore.dll', 'secur32.dll', 'sspicli.dll', 'rpcrt4.dll', 'sechost.dll', 'ws2_32.dll', 'mswsock.dll', 'winmm.dll', 'winhttp.dll',
                'wininet.dll', 'wldap32.dll', 'normaliz.dll', 'version.dll', 'psapi.dll', 'dbghelp.dll', 'avrt.dll', 'setupapi.dll', 'cfgmgr32.dll',
                'hid.dll', 'imm32.dll', 'uxtheme.dll', 'propsys.dll', 'powrprof.dll', 'wintrust.dll', 'wtsapi32.dll', 'iphlpapi.dll', 'dnsapi.dll',
                'netapi32.dll', 'userenv.dll', 'urlmon.dll', 'winspool.drv', 'msimg32.dll', 'opengl32.dll', 'glu32.dll', 'mfplat.dll', 'mf.dll',
                'mfreadwrite.dll', 'mfuuid.dll', 'vfw32.dll', 'avicap32.dll', 'msacm32.dll', 'strmiids.dll', 'dinput8.dll', 'dsound.dll', 'xinput1_4.dll', 'xinput9_1_0.dll')
            if ($name.ToLowerInvariant() -in $systemNames) { continue }
            $destination = Join-Path $Payload $name
            $candidate = $null
            foreach ($directory in @($Payload, (Split-Path $binary)) + $SearchDirectories)
            {
                $path = Join-Path $directory $name
                if (Test-Path -LiteralPath $path -PathType Leaf)
                {
                    $candidate = (Resolve-Path -LiteralPath $path).ProviderPath
                    break
                }
            }
            if (!$candidate) { throw "Missing dependency $name imported by $binary. Only selected SDK/native, project runtime and Visual Studio x64 CRT redistributable directories are searched. Run build.ps1 -Target Workbench -InstallDependencies to prepare the locked runtime, or supply -RuntimeDependencyDirectory with matching x64 runtime files and -LicenseDirectory with their notices." }
            if ($candidate -ne $destination)
            {
                if (Test-Path -LiteralPath $destination)
                {
                    if ((Get-FileHash -LiteralPath $candidate).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) { throw "Conflicting Windows dependency $name." }
                }
                else { Copy-Item -LiteralPath $candidate -Destination $destination }
            }
            if (!$sources.ContainsKey($destination)) { $sources[$destination] = $candidate }
            $queue.Enqueue($destination)
            if ((Split-Path $binary) -ne $Payload)
            {
                $adjacent = Join-Path (Split-Path $binary) $name
                if (!(Test-Path -LiteralPath $adjacent)) { Copy-Item -LiteralPath $destination -Destination $adjacent }
            }
        }
        $source = if ($sources.ContainsKey($binary)) { $sources[$binary] } else { $binary }
        $records.Add([pscustomobject]@{ Source = $source; SourceSha256 = (Get-FileHash -LiteralPath $binary).Hash.ToLowerInvariant(); Path = [IO.Path]::GetRelativePath($Payload, $binary); Architecture = 'x64'; Dependencies = $dependencies })
    }
    return $records.ToArray()
}
