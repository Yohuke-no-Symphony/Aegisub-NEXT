#Requires -Version 7.2

function Get-AegiNextExportCapabilityCheck
{
    param([string] $RepositoryRoot, [object] $HostInfo, [string] $FfmpegRoot)
    $name = if ($HostInfo.Platform -eq 'Windows') { 'ffmpeg.exe' } else { 'ffmpeg' }
    $executable = Join-Path $FfmpegRoot "bin/$name"
    foreach ($capability in @(
        @{ Name = 'libx264'; Kind = 'encoder'; Format = 'yuv420p' },
        @{ Name = 'libx265'; Kind = 'encoder'; Format = 'yuv420p10le' },
        @{ Name = 'libvpx'; Kind = 'encoder'; Format = 'yuv420p' },
        @{ Name = 'libvpx-vp9'; Kind = 'encoder'; Format = 'yuv420p' },
        @{ Name = 'libvpx-vp9'; Kind = 'encoder'; Format = 'yuv420p10le' },
        @{ Name = 'libsvtav1'; Kind = 'encoder'; Format = 'yuv420p' },
        @{ Name = 'libsvtav1'; Kind = 'encoder'; Format = 'yuv420p10le' },
        @{ Name = 'aac'; Kind = 'encoder'; Format = 'fltp' },
        @{ Name = 'libopus'; Kind = 'encoder'; Format = 'flt' },
        @{ Name = 'mp4'; Kind = 'muxer'; Format = '' },
        @{ Name = 'matroska'; Kind = 'muxer'; Format = '' },
        @{ Name = 'webm'; Kind = 'muxer'; Format = '' }
    ))
    {
        try
        {
            $result = Invoke-AegiNextCommand $executable @('-hide_banner', '-h', "$($capability.Kind)=$($capability.Name)") $RepositoryRoot
        }
        catch
        {
            return Get-AegiNextCheck 'ExportCapabilities' 'Invalid' "Cannot inspect selected FFmpeg export capabilities: $($_.Exception.Message)"
        }
        $kind = if ($capability.Kind -eq 'encoder') { 'Encoder' } else { 'Muxer' }
        $header = '(?m)^' + $kind + '\s+' + [regex]::Escape($capability.Name) + '\s+\['
        $format = '(?m)^\s*Supported (?:pixel|sample) formats:\s+[^\r\n]*(?<!\S)' + [regex]::Escape($capability.Format) + '(?!\S)'
        if ($result.ExitCode -ne 0 -or $result.Output -notmatch $header -or ($capability.Format -and $result.Output -notmatch $format))
        {
            return Get-AegiNextCheck 'ExportCapabilities' 'Invalid' "The selected SDK requires $($capability.Kind) $($capability.Name) $($capability.Format). Existing packages are not automatically replaced."
        }
    }
    Get-AegiNextCheck 'ExportCapabilities' 'Ready' 'Selected FFmpeg SDK: H.264, HEVC, VP8, VP9 and SVT-AV1 (VP9/AV1 8-bit and 10-bit), AAC/Opus, MP4, Matroska and WebM muxers.'
}
