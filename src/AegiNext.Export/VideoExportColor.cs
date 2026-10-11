using AegiNext.Core.Media;

namespace AegiNext.Export;

/// <summary>成片实际编码使用的有效色彩参数；推断不会改写源媒体事实。</summary>
public sealed record VideoExportColor(int Range, int Matrix, int Primaries, int Transfer,
    int ChromaLocation, int AlphaMode, uint InferredFields, VideoCodec Codec = VideoCodec.Auto,
    int BitDepth = 0, string? PixelFormat = null, MediaMasteringDisplayInfo? MasteringDisplay = null,
    MediaContentLightInfo? ContentLight = null, uint OutputVerification = 0)
{
    internal MediaColorInfo ToMetadata()
    {
        return new()
        {
            Range = Range switch
            {
                1 => "tv", 2 => "pc", _ => null
            },
            Matrix = Matrix switch
            {
                0 => "gbr", 1 => "bt709", 5 => "bt470bg", 6 => "smpte170m", 9 => "bt2020nc", _ => null
            },
            Primaries = Primaries switch
            {
                1 => "bt709", 5 => "bt470bg", 6 => "smpte170m", 9 => "bt2020", _ => null
            },
            Transfer = Transfer switch
            {
                1 => "bt709", 6 => "smpte170m", 13 => "iec61966-2-1", 16 => "smpte2084", 18 => "arib-std-b67", _ => null
            },
            ChromaLocation = ChromaLocation switch
            {
                1 => "left", 2 => "center", 3 => "topleft", 4 => "top", 5 => "bottomleft", 6 => "bottom", _ => null
            }
        };
    }
}
