#Requires -Version 7.2

function Set-AegiNextMacAppBundle
{
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Low')]
    param([Parameter(Mandatory)][string] $RepositoryRoot, [Parameter(Mandatory)][string] $ContentsDirectory,
        [Parameter(Mandatory)][version] $BaseVersion, [Parameter(Mandatory)][ValidateRange(1, 65534)][int] $BuildNumber,
        [Parameter(Mandatory)][version] $MinimumOSVersion)
    if ($BaseVersion.Build -lt 0 -or $BaseVersion.Revision -ge 0) { throw "Invalid macOS base version: $BaseVersion" }
    $icon = Join-Path $RepositoryRoot 'src/AegiNext.Desktop/Assets/AppIcon.icns'
    if (!(Test-Path -LiteralPath $icon -PathType Leaf)) { throw "Missing macOS app icon: $icon" }
    if (!$PSCmdlet.ShouldProcess($ContentsDirectory, 'Install app icon and bundle metadata')) { return }
    $resources = Join-Path $ContentsDirectory 'Resources'
    [IO.Directory]::CreateDirectory($resources) | Out-Null
    Copy-Item -LiteralPath $icon -Destination (Join-Path $resources 'AppIcon.icns')
    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleDisplayName</key><string>AegiNext</string><key>CFBundleName</key><string>AegiNext</string>
<key>CFBundleIdentifier</key><string>org.aeginext.desktop</string><key>CFBundleExecutable</key><string>aegi-next</string>
<key>CFBundlePackageType</key><string>APPL</string><key>CFBundleShortVersionString</key><string>$BaseVersion</string>
<key>CFBundleVersion</key><string>$BuildNumber</string><key>LSMinimumSystemVersion</key><string>$MinimumOSVersion</string>
<key>CFBundleIconFile</key><string>AppIcon.icns</string>
<key>NSHighResolutionCapable</key><true/><key>NSPrincipalClass</key><string>NSApplication</string></dict></plist>
"@
    Set-Content -LiteralPath (Join-Path $ContentsDirectory 'Info.plist') -Value $plist -Encoding utf8NoBOM
}

function New-AegiNextMacDiskImage
{
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Low')]
    param([Parameter(Mandatory)][string] $AppDirectory, [Parameter(Mandatory)][string] $PublishDirectory,
        [Parameter(Mandatory)][string] $ProductVersion, [Parameter(Mandatory)][ValidateSet('osx-arm64', 'osx-x64')][string] $RuntimeIdentifier)
    $null = ConvertTo-AegiNextProductVersion -Value $ProductVersion
    if (!(Test-Path -LiteralPath $AppDirectory -PathType Container)) { throw "Missing macOS app bundle: $AppDirectory" }
    $image = Join-Path $PublishDirectory "AegiNext-$ProductVersion-$RuntimeIdentifier.dmg"
    if (Test-Path -LiteralPath $image) { throw "Disk image destination already exists: $image" }
    if (!$PSCmdlet.ShouldProcess($image, 'Create and verify compressed macOS disk image')) { return }
    $staging = Join-Path ([IO.Path]::GetTempPath()) "AegiNext-dmg-$([Guid]::NewGuid().ToString('N'))"
    try
    {
        [IO.Directory]::CreateDirectory($staging) | Out-Null
        $stagedApp = Join-Path $staging 'AegiNext.app'
        $null = Invoke-AegiNextPublishCommand '/usr/bin/ditto' @('--rsrc', '--extattr', '--acl', $AppDirectory, $stagedApp) $PublishDirectory -StreamOutput
        $null = Invoke-AegiNextPublishCommand '/bin/ln' @('-s', '/Applications', (Join-Path $staging 'Applications')) $PublishDirectory
        $null = Invoke-AegiNextPublishCommand '/usr/bin/codesign' @('--verify', '--deep', '--strict', $stagedApp) $PublishDirectory -StreamOutput
        $null = Invoke-AegiNextPublishCommand '/usr/bin/hdiutil' @('create', '-srcfolder', $staging, '-volname', 'AegiNext', '-format', 'UDZO', $image) $PublishDirectory -StreamOutput
        $null = Invoke-AegiNextPublishCommand '/usr/bin/hdiutil' @('verify', $image) $PublishDirectory -StreamOutput
        if (!(Test-Path -LiteralPath $image -PathType Leaf)) { throw "Disk image was not created: $image" }
        return $image
    }
    finally
    {
        if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    }
}
