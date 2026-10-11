#Requires -Version 7.2
Set-StrictMode -Version Latest
Import-Module ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../build/AegiNext.Build.psm1'))) -Force
. (Join-Path $PSScriptRoot 'AegiNext.MacDependencies.ps1')
. (Join-Path $PSScriptRoot 'AegiNext.WindowsDependencies.ps1')
. (Join-Path $PSScriptRoot 'AegiNext.PublishMetadata.ps1')
. (Join-Path $PSScriptRoot 'AegiNext.AppBundle.ps1')
. (Join-Path $PSScriptRoot 'AegiNext.WindowsInstaller.ps1')

function Invoke-AegiNextPublishCommand
{
    param([string] $FilePath, [string[]] $Arguments, [string] $WorkingDirectory, [switch] $StreamOutput)
    $result = Invoke-AegiNextCommand -FilePath $FilePath -Arguments $Arguments -WorkingDirectory $WorkingDirectory -StreamOutput:$StreamOutput
    if ($result.ExitCode -ne 0)
    {
        throw "$FilePath failed ($($result.ExitCode)): $($result.Output)"
    }
    return $result.Output
}

function Get-AegiNextBinaryKind
{
    param([string] $Path)
    $stream = [IO.File]::OpenRead($Path)
    try
    {
        if ($stream.Length -lt 4) { return 'Other' }
        $reader = [IO.BinaryReader]::new($stream)
        $magic = $reader.ReadUInt32()
        if ($magic -in @(0xfeedfaceL, 0xfeedfacfL, 0xcefaedfeL, 0xcffaedfeL, 0xcafebabeL, 0xbebafecaL)) { return 'MachO' }
        if (($magic -band 0xffff) -ne 0x5a4d -or $stream.Length -lt 64) { return 'Other' }
        $stream.Position = 60
        $header = $reader.ReadUInt32()
        if ($header + 24 -gt $stream.Length) { throw "Invalid PE header: $Path" }
        $stream.Position = $header
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "Invalid PE signature: $Path" }
        $machine = $reader.ReadUInt16()
        $stream.Position = $header + 24
        $optionalMagic = $reader.ReadUInt16()
        $directories = if ($optionalMagic -eq 0x20b) { $header + 24 + 112 } else { $header + 24 + 96 }
        if ($directories + 120 -le $stream.Length)
        {
            $stream.Position = $directories + 112
            if ($reader.ReadUInt32() -ne 0) { return 'Managed' }
        }
        return "PE-$machine"
    }
    finally { $stream.Dispose() }
}

function Copy-AegiNextLicenseFile
{
    param([string[]] $Roots, [string] $Destination)
    $records = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($root in $Roots)
    {
        if (!$root -or !(Test-Path -LiteralPath $root -PathType Container) -or !$seen.Add($root)) { continue }
        $files = @(Get-ChildItem -LiteralPath $root -File | Where-Object Name -match '^(LICENSE|LICENCE|COPYING|COPYRIGHT|NOTICE|PATENTS|THIRD.?PARTY)')
        foreach ($subdirectory in @('share/licenses', 'share/doc', 'licenses', 'doc', 'docs'))
        {
            $directory = Join-Path $root $subdirectory
            if (Test-Path -LiteralPath $directory -PathType Container)
            {
                $files += Get-ChildItem -LiteralPath $directory -File -Recurse | Where-Object Name -match 'LICENSE|LICENCE|COPYING|COPYRIGHT|NOTICE|PATENTS'
            }
        }
        foreach ($file in $files)
        {
            $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            $target = Join-Path $Destination "$($hash.Substring(0, 12))-$($file.Name)"
            [IO.Directory]::CreateDirectory($Destination) | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
            $records.Add([pscustomobject]@{ PackageRoot = $root; Source = $file.FullName; File = [IO.Path]::GetFileName($target); Sha256 = $hash })
        }
    }
    return $records.ToArray()
}

function Get-AegiNextNugetLicenseRoot
{
    param([string] $RepositoryRoot, [string] $RuntimeIdentifier)
    $assets = Join-Path $RepositoryRoot "src/AegiNext.Desktop/obj/$RuntimeIdentifier/project.assets.json"
    if (!(Test-Path -LiteralPath $assets)) { throw "Missing restored dependency inventory: $assets" }
    $document = Get-Content -LiteralPath $assets -Raw | ConvertFrom-Json -AsHashtable
    foreach ($library in $document.libraries.Values)
    {
        if ($library.type -ne 'package') { continue }
        foreach ($folder in $document.packageFolders.Keys)
        {
            $path = Join-Path $folder $library.path
            if (Test-Path -LiteralPath $path -PathType Container) { $path; break }
        }
    }
}

function Test-AegiNextPublishedLocalization
{
    param([Parameter(Mandatory)][string] $PayloadDirectory)
    $directory = Join-Path $PayloadDirectory 'i18n'
    if (!(Test-Path -LiteralPath $directory -PathType Container)) { throw "Missing published language directory: $directory" }
    $required = @{ 'en-US.json' = 'en-US'; 'zh-CN.json' = 'zh-CN'; 'ja-JP.json' = 'ja-JP' }
    foreach ($name in $required.Keys)
    {
        if (!(Test-Path -LiteralPath (Join-Path $directory $name) -PathType Leaf)) { throw "Missing published language package: $name" }
    }

    $identifiers = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $languages = [Collections.Generic.List[object]]::new()
    $encoding = [Text.UTF8Encoding]::new($false, $true)
    foreach ($file in Get-ChildItem -LiteralPath $directory -Filter '*.json' -File | Sort-Object Name)
    {
        $document = $null
        try
        {
            $bytes = [IO.File]::ReadAllBytes($file.FullName)
            if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xef -and $bytes[1] -eq 0xbb -and $bytes[2] -eq 0xbf) { throw 'Language JSON must use UTF-8 without a BOM.' }
            $document = [Text.Json.JsonDocument]::Parse($encoding.GetString($bytes))
            $root = $document.RootElement
            if ($root.ValueKind -ne [Text.Json.JsonValueKind]::Object) { throw 'Language JSON must be an object.' }
            $fields = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($field in $root.EnumerateObject())
            {
                if (!$fields.Add($field.Name)) { throw "Duplicate language field: $($field.Name)." }
            }

            $name = $root.GetProperty('LanguageName')
            $identifier = $root.GetProperty('LanguageID')
            $strings = $root.GetProperty('Strings')
            if ($name.ValueKind -ne [Text.Json.JsonValueKind]::String -or [string]::IsNullOrWhiteSpace($name.GetString())) { throw 'LanguageName must be a nonempty string.' }
            if ($identifier.ValueKind -ne [Text.Json.JsonValueKind]::String -or [string]::IsNullOrWhiteSpace($identifier.GetString())) { throw 'LanguageID must be a nonempty culture identifier.' }
            $culture = [Globalization.CultureInfo]::GetCultureInfo($identifier.GetString())
            if ($culture.Equals([Globalization.CultureInfo]::InvariantCulture) -or $identifier.GetString() -ieq 'system') { throw 'LanguageID must identify a named culture and cannot be system.' }
            if ($strings.ValueKind -ne [Text.Json.JsonValueKind]::Object) { throw 'Strings must be a string dictionary.' }
            $keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($entry in $strings.EnumerateObject())
            {
                if ([string]::IsNullOrWhiteSpace($entry.Name) -or $entry.Value.ValueKind -ne [Text.Json.JsonValueKind]::String) { throw 'Translation keys must be nonempty and values must be strings.' }
                if (!$keys.Add($entry.Name)) { throw "Duplicate translation key: $($entry.Name)." }
            }
            if (!$identifiers.Add($culture.Name)) { throw "Duplicate published LanguageID: $($culture.Name)." }
            if ($required.ContainsKey($file.Name) -and $culture.Name -ine $required[$file.Name]) { throw "Expected LanguageID $($required[$file.Name]) in $($file.Name)." }
            $languages.Add([pscustomobject]@{ LanguageName = $name.GetString(); LanguageID = $culture.Name })
        }
        catch { throw "Invalid published language package '$($file.FullName)': $($_.Exception.Message)" }
        finally
        {
            if ($null -ne $document) { $document.Dispose() }
        }
    }
    return $languages.ToArray()
}

function ConvertTo-AegiNextProductVersion
{
    param([Parameter(Mandatory)][string] $Value)
    $parsed = $null
    if (![version]::TryParse($Value, [ref]$parsed) -or $parsed.Build -lt 0 -or $parsed.Revision -ge 0 -or
        @($parsed.Major, $parsed.Minor, $parsed.Build | Where-Object { $_ -gt 65535 }).Count -gt 0)
    {
        throw "Invalid product version '$Value'. Use three numeric components between 0 and 65535."
    }
    return $parsed
}

function Test-AegiNextPublishedPackage
{
    param([Parameter(Mandatory)][string] $PackageDirectory)
    $root = [IO.Path]::GetFullPath($PackageDirectory)
    $manifestPath = Join-Path $root 'package-manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.SchemaVersion -ne 1 -or !$manifest.SelfContained) { throw 'Unsupported or incomplete package manifest.' }
    $null = ConvertTo-AegiNextProductVersion -Value $manifest.ProductVersion
    $prefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $manifest.Files)
    {
        $path = [IO.Path]::GetFullPath((Join-Path $root $file.Path))
        if (!$path.StartsWith($prefix, $comparison) -or !$expected.Add($path)) { throw "Invalid or duplicate manifest path: $($file.Path)" }
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing package file: $($file.Path)" }
        if ((Get-Item -LiteralPath $path).Length -ne $file.Bytes -or (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -cne $file.Sha256) { throw "Package hash mismatch: $($file.Path)" }
    }
    foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse)
    {
        if ($file.FullName -ne $manifestPath -and !$expected.Contains($file.FullName)) { throw "Unexpected package file: $($file.FullName)" }
    }
    $payload = switch ($manifest.RuntimeIdentifier)
    {
        { $_ -in @('osx-arm64', 'osx-x64') } { Join-Path $root 'AegiNext.app/Contents/MacOS' }
        'win-x64' { Join-Path $root 'AegiNext' }
        default { throw "Unsupported package runtime identifier: $($manifest.RuntimeIdentifier)." }
    }
    $languages = @(Test-AegiNextPublishedLocalization -PayloadDirectory $payload)
    return [pscustomobject]@{ RuntimeIdentifier = $manifest.RuntimeIdentifier; ProductVersion = $manifest.ProductVersion; FileCount = $expected.Count; HashesVerified = $true; LocalizationVerified = $true; LanguageIDs = @($languages.LanguageID) }
}

function Invoke-AegiNextPublish
{
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $RepositoryRoot, [string] $RuntimeIdentifier,
        [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release', [version] $Version, [string] $FfmpegRoot, [string] $SdlRoot,
        [string] $OutputDirectory, [string] $LicenseDirectory, [string[]] $RuntimeDependencyDirectory = @(), [string] $SigningIdentity = '-', [switch] $SkipBuild, [switch] $CreateDmg,
        [switch] $CreateInstaller, [string] $NsisPath,
        [ValidateRange(1, 128)][int] $Jobs = 2)
    $productVersion = if ($null -ne $Version) { ConvertTo-AegiNextProductVersion -Value $Version.ToString() } else { $null }
    $hostInfo = Get-AegiNextHost
    $rid = Get-AegiNextRuntimeIdentifier -HostInfo $hostInfo -RuntimeIdentifier $RuntimeIdentifier
    if ($rid -notin @('osx-arm64', 'osx-x64', 'win-x64')) { throw "Unsupported publish RID $rid." }
    if ($CreateDmg -and $hostInfo.Platform -ne 'MacOS') { throw 'CreateDmg is only supported on macOS.' }
    if ($CreateInstaller -and $hostInfo.Platform -ne 'Windows') { throw 'CreateInstaller is only supported on Windows.' }
    if ($NsisPath -and !$CreateInstaller) { throw 'NsisPath requires -CreateInstaller.' }
    $nsisCompiler = if ($CreateInstaller) { Get-AegiNextNsisCompiler -RepositoryRoot $RepositoryRoot -NsisPath $NsisPath } else { $null }
    if ($null -eq $productVersion)
    {
        $Version = [version]([xml](Get-Content -LiteralPath (Join-Path $RepositoryRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
        $productVersion = ConvertTo-AegiNextProductVersion -Value $Version.ToString()
    }
    $versionText = $productVersion.ToString()
    $assemblyVersion = "$($productVersion.Major).$($productVersion.Minor).$($productVersion.Build).$([Math]::Max(0, $productVersion.Revision))"
    $report = Get-AegiNextEnvironment -RepositoryRoot $RepositoryRoot -HostInfo $hostInfo -Target Workbench -FfmpegRoot $FfmpegRoot -SdlRoot $SdlRoot
    if (!$report.Ready) { throw "Publish environment is not ready: $($report.Checks | Where-Object Status -in @('Missing','Invalid','Unsupported') | ConvertTo-Json -Compress)" }
    $compilerRuntime = if ($hostInfo.Platform -eq 'Windows') { Get-AegiNextWindowsCompilerRuntime $report.NativePrefixes['cxx'] } else { $null }
    if (!$SkipBuild)
    {
        if ((Invoke-AegiNextBuild -RepositoryRoot $RepositoryRoot -Target Workbench -Configuration $Configuration -RuntimeIdentifier $rid -FfmpegRoot $FfmpegRoot -SdlRoot $SdlRoot -Jobs $Jobs) -ne 0) { throw 'Workbench build failed.' }
        if ($hostInfo.Platform -eq 'MacOS' -and (Invoke-AegiNextBuild -RepositoryRoot $RepositoryRoot -Target Native -Configuration $Configuration -Jobs $Jobs) -ne 0) { throw 'macOS HDR module build failed.' }
    }
    $publishRoot = Join-Path $RepositoryRoot "artifacts/publish/$rid/$Configuration"
    if ($OutputDirectory) { $publishRoot = [IO.Path]::GetFullPath($OutputDirectory) }
    if (Test-Path -LiteralPath $publishRoot) { throw "Publish destination already exists: $publishRoot. Choose a fresh directory to preserve previous packages." }
    Write-Information -InformationAction Continue -MessageData "[publish] Output directory: $publishRoot"
    [IO.Directory]::CreateDirectory($publishRoot) | Out-Null
    $payload = if ($hostInfo.Platform -eq 'MacOS') { Join-Path $publishRoot 'AegiNext.app/Contents/MacOS' } else { Join-Path $publishRoot 'AegiNext' }
    [IO.Directory]::CreateDirectory($payload) | Out-Null
    $dotnet = Find-AegiNextCommand 'dotnet'
    $common = @('-c', $Configuration, '-r', $rid, "-p:AegiNextRuntimeIdentifier=$rid", '--self-contained', 'true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:UseAppHost=true', '-p:AegiNextPublishWorkerSeparately=true')
    $common += @("-p:Version=$versionText", "-p:InformationalVersion=$versionText", "-p:AssemblyVersion=$assemblyVersion", "-p:FileVersion=$assemblyVersion")
    foreach ($project in @('AegiNext.Desktop', 'AegiNext.ExportWorker'))
    {
        Write-Information -InformationAction Continue -MessageData "[publish] Publishing $project (self-contained, $rid)..."
        $null = Invoke-AegiNextPublishCommand $dotnet (@('publish', (Join-Path $RepositoryRoot "src/$project/$project.csproj"), '-o', $payload) + $common) $RepositoryRoot -StreamOutput
    }
    Write-Information -InformationAction Continue -MessageData '[publish] Copying native modules and FFmpeg tools...'
    $nativeRoot = Join-Path $RepositoryRoot "artifacts/native/$rid/$Configuration"
    $sourcePaths = @{}
    $modules = @('decode', 'audio', 'export') + $(if ($hostInfo.Platform -eq 'MacOS') { @('media') } else { @() })
    foreach ($module in $modules)
    {
        $filename = if ($hostInfo.Platform -eq 'MacOS') { "libaeginext_$module.dylib" } else { "aeginext_$module.dll" }
        $source = Join-Path $nativeRoot $filename
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing native module for ${rid}: $source" }
        $destination = Join-Path $payload $filename
        Copy-Item -LiteralPath $source -Destination $destination -Force
        $sourcePaths[[IO.Path]::GetFullPath($destination)] = [IO.Path]::GetFullPath($source)
    }
    $tools = Join-Path $payload 'tools'
    [IO.Directory]::CreateDirectory($tools) | Out-Null
    foreach ($tool in @('ffmpeg', 'ffprobe'))
    {
        $filename = $tool + $(if ($hostInfo.Platform -eq 'Windows') { '.exe' } else { '' })
        $source = Join-Path $report.NativePrefixes.ffmpeg "bin/$filename"
        $destination = Join-Path $tools $filename
        Copy-Item -LiteralPath $source -Destination $destination
        $sourcePaths[[IO.Path]::GetFullPath($destination)] = [IO.Path]::GetFullPath($source)
    }
    $toolSuffix = if ($hostInfo.Platform -eq 'Windows') { '.exe' } else { '' }
    $sourceIdentity = Get-AegiNextSourceIdentity $RepositoryRoot
    $manifest = [ordered]@{ SchemaVersion = 1; ProductVersion = $versionText; RuntimeIdentifier = $rid; SelfContained = $true; BuildTimeUtc = [DateTime]::UtcNow.ToString('O'); GitSha = $sourceIdentity.GitSha; WorkingTreeDirty = $sourceIdentity.WorkingTreeDirty; GitMetadataStatus = $sourceIdentity.Status; Tools = @("tools/ffmpeg$toolSuffix", "tools/ffprobe$toolSuffix"); ToolVersions = @(); RuntimeFrameworks = @(Get-AegiNextPublishedRuntimeFramework $payload); CompilerRuntime = $compilerRuntime; OperatingSystemPolicy = (Get-AegiNextRuntimeOperatingSystemPolicy $hostInfo); Dependencies = @(); MinimumOSVersion = $null; Licenses = @(); Files = @() }
    $licenseRoots = @($report.NativePrefixes.ffmpeg, $report.NativePrefixes.sdl3, $payload, $LicenseDirectory) + $RuntimeDependencyDirectory + @(Get-AegiNextNugetLicenseRoot $RepositoryRoot $rid)
    $requiredLicenseRoots = @($report.NativePrefixes.ffmpeg, $report.NativePrefixes.sdl3)
    if ($hostInfo.Platform -eq 'MacOS')
    {
        Write-Information -InformationAction Continue -MessageData '[publish] Resolving macOS runtime dependencies and bundle metadata...'
        $closure = Copy-AegiNextMacDependencyClosure -Payload $payload -RuntimeIdentifier $rid -SourcePaths $sourcePaths
        $manifest.Dependencies = $closure.Dependencies
        $manifest.MinimumOSVersion = $closure.MinimumOSVersion
        $manifest.OperatingSystemPolicy.ProductMinimumOSVerified = $true
        $licenseRoots += $closure.PackageRoots
        $requiredLicenseRoots += $closure.PackageRoots
        $contents = Split-Path $payload
        Set-AegiNextMacAppBundle -RepositoryRoot $RepositoryRoot -ContentsDirectory $contents -ProductVersion $versionText -MinimumOSVersion $closure.MinimumOSVersion
    }
    else
    {
        Write-Information -InformationAction Continue -MessageData '[publish] Resolving Windows runtime dependencies...'
        $manifest.Dependencies = @(Copy-AegiNextWindowsDependencyClosure -Payload $payload -SearchDirectories (@($nativeRoot, (Join-Path $report.NativePrefixes.ffmpeg 'bin'), (Join-Path $report.NativePrefixes.sdl3 'bin'), $compilerRuntime.RuntimeDirectory) + $RuntimeDependencyDirectory))
        $licenseRoots += $compilerRuntime.LicenseRoot
        $requiredLicenseRoots += @($compilerRuntime.LicenseRoot) + $RuntimeDependencyDirectory
    }
    Write-Information -InformationAction Continue -MessageData '[publish] Collecting third-party license notices...'
    $manifest.Licenses = @(Copy-AegiNextLicenseFile -Roots $licenseRoots -Destination (Join-Path $payload 'licenses'))
    if (!$manifest.Licenses.Count) { throw 'No third-party license notices were collected.' }
    foreach ($packageRoot in $requiredLicenseRoots | Select-Object -Unique)
    {
        if (!@($manifest.Licenses | Where-Object PackageRoot -eq $packageRoot).Count)
        {
            $leaf = Split-Path $packageRoot -Leaf
            $packageName = if ($leaf -eq 'current' -or $leaf -match '^\d+\.\d+') { Split-Path (Split-Path $packageRoot) -Leaf } else { $leaf }
            $supplement = if ($LicenseDirectory) { Join-Path $LicenseDirectory $packageName } else { $null }
            $supplementalNotices = if ($supplement) { @(Copy-AegiNextLicenseFile @($supplement) (Join-Path $payload 'licenses')) } else { @() }
            if (!$supplementalNotices.Count) { throw "Missing notices for native package $packageRoot. Supply -LicenseDirectory with a $packageName subdirectory containing the original notices." }
            foreach ($notice in $supplementalNotices) { $notice.PackageRoot = $packageRoot }
            $manifest.Licenses += $supplementalNotices
        }
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $payload 'media-runtime.json') -Encoding utf8NoBOM
    if ($hostInfo.Platform -eq 'MacOS')
    {
        $app = Split-Path (Split-Path $payload)
        Write-Information -InformationAction Continue -MessageData '[publish] Signing and verifying the macOS app...'
        $null = Invoke-AegiNextPublishCommand '/usr/bin/codesign' @('--force', '--deep', '--sign', $SigningIdentity, $app) $RepositoryRoot -StreamOutput
        $null = Invoke-AegiNextPublishCommand '/usr/bin/codesign' @('--verify', '--deep', '--strict', '--verbose=2', $app) $RepositoryRoot -StreamOutput
        if ($CreateDmg)
        {
            Write-Information -InformationAction Continue -MessageData '[publish] Creating and verifying the compressed DMG...'
            $null = New-AegiNextMacDiskImage -AppDirectory $app -PublishDirectory $publishRoot -ProductVersion $versionText -RuntimeIdentifier $rid
        }
    }
    Write-Information -InformationAction Continue -MessageData '[publish] Checking packaged FFmpeg and FFprobe versions...'
    $manifest.ToolVersions = @(Get-AegiNextPublishedToolVersion $payload $rid)
    if ($CreateInstaller)
    {
        $null = Test-AegiNextPublishedLocalization -PayloadDirectory $payload
        Write-Information -InformationAction Continue -MessageData '[publish] Creating the NSIS installer (compressing the complete application payload)...'
        $null = New-AegiNextWindowsInstaller -RepositoryRoot $RepositoryRoot -PayloadDirectory $payload -PublishDirectory $publishRoot -ProductVersion $versionText -RuntimeIdentifier $rid -NsisCompiler $nsisCompiler.Path
    }
    Write-Information -InformationAction Continue -MessageData '[publish] Writing and verifying the SHA-256 package manifest...'
    foreach ($file in Get-ChildItem -LiteralPath $publishRoot -File -Recurse)
    {
        $manifest.Files += [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($publishRoot, $file.FullName); Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); Bytes = $file.Length }
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $publishRoot 'package-manifest.json') -Encoding utf8NoBOM
    $null = Test-AegiNextPublishedPackage -PackageDirectory $publishRoot
    Write-Information -InformationAction Continue -MessageData "Published $versionText / $rid, self-contained: $publishRoot"
}

Export-ModuleMember -Function Invoke-AegiNextPublish, Get-AegiNextBinaryKind, Copy-AegiNextLicenseFile, Test-AegiNextPublishedPackage
