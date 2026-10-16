#Requires -Version 7.2

function Get-AegiNextNsisCompiler
{
    param([Parameter(Mandatory)][string] $RepositoryRoot, [string] $NsisPath)
    $compiler = $NsisPath
    if (!$compiler)
    {
        $compiler = Find-AegiNextCommand 'makensis'
        if (!$compiler)
        {
            foreach ($prefix in @(${env:ProgramFiles(x86)}, $env:ProgramFiles))
            {
                if (!$prefix) { continue }
                $candidate = Join-Path $prefix 'NSIS/makensis.exe'
                if (Test-Path -LiteralPath $candidate -PathType Leaf) { $compiler = $candidate; break }
            }
        }
    }
    if (!$compiler -or !(Test-Path -LiteralPath $compiler -PathType Leaf))
    {
        throw 'Missing NSIS compiler. Install NSIS 3.11 or newer and add makensis to PATH, or supply -NsisPath with the makensis.exe path.'
    }
    $compiler = (Resolve-Path -LiteralPath $compiler).ProviderPath
    $output = (Invoke-AegiNextPublishCommand $compiler @('/VERSION') $RepositoryRoot).Trim()
    if ($output -notmatch '^v?(\d+\.\d+(?:\.\d+)?)$' -or [version]$Matches[1] -lt [version]'3.11')
    {
        throw "Requires NSIS 3.11 or newer; found '$output' at $compiler."
    }
    return [pscustomobject]@{ Path = $compiler; Version = $Matches[1] }
}

function ConvertTo-AegiNextNsisLiteral
{
    param([Parameter(Mandatory)][AllowEmptyString()][string] $Value, [switch] $CompileTime)
    if ($Value -match '[\x00-\x1f]') { throw 'NSIS strings cannot contain control characters.' }
    if ($Value.Contains('${')) { throw 'NSIS paths cannot contain preprocessor references.' }
    if ($CompileTime) { return $Value.Replace('"', '$\"') }
    return $Value.Replace('$', '$$').Replace('"', '$\"')
}

function New-AegiNextWindowsInstaller
{
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Low')]
    param([Parameter(Mandatory)][string] $RepositoryRoot, [Parameter(Mandatory)][string] $PayloadDirectory,
        [Parameter(Mandatory)][string] $PublishDirectory, [Parameter(Mandatory)][string] $ProductVersion,
        [Parameter(Mandatory)][version] $FileVersion,
        [Parameter(Mandatory)][ValidateSet('win-x64')][string] $RuntimeIdentifier, [Parameter(Mandatory)][string] $NsisCompiler)
    $payload = (Resolve-Path -LiteralPath $PayloadDirectory).ProviderPath
    $publish = (Resolve-Path -LiteralPath $PublishDirectory).ProviderPath
    $scriptPath = Join-Path $RepositoryRoot 'scripts/publish/AegiNext.WindowsInstaller.nsi'
    $icon = Join-Path $RepositoryRoot 'src/AegiNext.Desktop/Assets/AppIcon.ico'
    foreach ($path in @($scriptPath, $icon))
    {
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing Windows installer asset: $path" }
    }
    foreach ($name in @('aegi-next.exe', 'aegn-exporter.exe', 'aegnc.exe'))
    {
        if (!(Test-Path -LiteralPath (Join-Path $payload $name) -PathType Leaf)) { throw "Missing published Windows executable: $name" }
    }
    $entries = @(Get-ChildItem -LiteralPath $payload -Recurse -Force)
    if (@($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -or
        ((Get-Item -LiteralPath $payload).Attributes -band [IO.FileAttributes]::ReparsePoint))
    {
        throw 'Windows installer payload cannot contain symbolic links or reparse points.'
    }
    $files = @($entries | Where-Object { !$_.PSIsContainer } | Sort-Object FullName)
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $entries)
    {
        $relative = [IO.Path]::GetRelativePath($payload, $entry.FullName).Replace('\', '/')
        if (!$paths.Add($relative)) { throw "Duplicate Windows installer path: $relative" }
        if ($relative -in @('Uninstall.exe', 'install-state.ini')) { throw "Reserved installer path in payload: $relative" }
    }
    $null = ConvertTo-AegiNextProductVersion -Value $ProductVersion
    $fileVersionText = $FileVersion.ToString()
    if ($FileVersion.Revision -lt 0 -or @($fileVersionText.Split('.') | Where-Object { [int]$_ -gt 65534 }).Count) { throw "Invalid Windows file version: $FileVersion" }
    $installer = Join-Path $publish "AegiNext-$ProductVersion-$RuntimeIdentifier-setup.exe"
    if (Test-Path -LiteralPath $installer) { throw "Installer destination already exists: $installer" }
    if (!$PSCmdlet.ShouldProcess($installer, 'Compile Windows NSIS installer')) { return }
    $staging = Join-Path ([IO.Path]::GetTempPath()) "AegiNext-nsis-$([Guid]::NewGuid().ToString('N'))"
    try
    {
        [IO.Directory]::CreateDirectory($staging) | Out-Null
        $include = Join-Path $staging 'payload.nsh'
        $lines = [Collections.Generic.List[string]]::new()
        $lines.Add('!macro AegiNextInstallFiles')
        foreach ($directory in $entries | Where-Object PSIsContainer | Sort-Object FullName)
        {
            $relative = [IO.Path]::GetRelativePath($payload, $directory.FullName).Replace('/', '\')
            $lines.Add('CreateDirectory "$INSTDIR\' + (ConvertTo-AegiNextNsisLiteral $relative) + '"')
        }
        foreach ($file in $files)
        {
            $relativeDirectory = [IO.Path]::GetRelativePath($payload, $file.DirectoryName).Replace('/', '\')
            $target = if ($relativeDirectory -eq '.') { '$INSTDIR' } else { '$INSTDIR\' + (ConvertTo-AegiNextNsisLiteral $relativeDirectory) }
            $lines.Add("SetOutPath `"$target`"")
            $lines.Add('File "/oname=' + (ConvertTo-AegiNextNsisLiteral $file.Name) + '" "' + (ConvertTo-AegiNextNsisLiteral $file.FullName -CompileTime) + '"')
        }
        $lines.Add('!macroend')
        $lines.Add('!macro AegiNextCheckInstalledFiles')
        foreach ($file in $files)
        {
            $relative = [IO.Path]::GetRelativePath($payload, $file.FullName).Replace('/', '\')
            $lines.Add('!insertmacro AegiNextCheckFile "$INSTDIR\' + (ConvertTo-AegiNextNsisLiteral $relative) + '"')
        }
        $lines.Add('!macroend')
        $lines.Add('!macro AegiNextUninstallFiles')
        foreach ($file in $files)
        {
            $relative = [IO.Path]::GetRelativePath($payload, $file.FullName).Replace('/', '\')
            $target = '$INSTDIR\' + (ConvertTo-AegiNextNsisLiteral $relative)
            $lines.Add("Delete `"$target`"")
            $lines.Add('!insertmacro AegiNextCheckDelete')
        }
        foreach ($directory in $entries | Where-Object PSIsContainer | Sort-Object { $_.FullName.Length } -Descending)
        {
            $relative = [IO.Path]::GetRelativePath($payload, $directory.FullName).Replace('/', '\')
            $lines.Add('RMDir "$INSTDIR\' + (ConvertTo-AegiNextNsisLiteral $relative) + '"')
        }
        $lines.Add('!macroend')
        [IO.File]::WriteAllLines($include, $lines, [Text.UTF8Encoding]::new($false))
        $definitions = [ordered]@{
            AEGINEXT_PRODUCT_VERSION = $ProductVersion
            AEGINEXT_FILE_VERSION = $fileVersionText
            AEGINEXT_OUTPUT_FILE = Join-Path $staging ([IO.Path]::GetFileName($installer))
            AEGINEXT_ICON_FILE = (Resolve-Path -LiteralPath $icon).ProviderPath
            AEGINEXT_PAYLOAD_INCLUDE = $include
            AEGINEXT_ESTIMATED_SIZE = [math]::Ceiling(($files | Measure-Object Length -Sum).Sum / 1024)
        }
        $configuration = Join-Path $staging 'configuration.nsh'
        $content = foreach ($entry in $definitions.GetEnumerator())
        {
            '!define ' + $entry.Key + ' "' + (ConvertTo-AegiNextNsisLiteral ([string]$entry.Value) -CompileTime) + '"'
        }
        [IO.File]::WriteAllLines($configuration, [string[]]$content, [Text.UTF8Encoding]::new($false))
        $null = Invoke-AegiNextPublishCommand $NsisCompiler @('/NOCD', '/WX', '/V3', '/INPUTCHARSET', 'UTF8', "/DAEGINEXT_CONFIG_FILE=$configuration", $scriptPath) $RepositoryRoot -StreamOutput
        $compiled = Join-Path $staging ([IO.Path]::GetFileName($installer))
        if (!(Test-Path -LiteralPath $compiled -PathType Leaf) -or (Get-Item -LiteralPath $compiled).Length -eq 0)
        {
            throw 'NSIS did not create a nonempty Windows installer.'
        }
        Move-Item -LiteralPath $compiled -Destination $installer
        return $installer
    }
    finally
    {
        if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    }
}
