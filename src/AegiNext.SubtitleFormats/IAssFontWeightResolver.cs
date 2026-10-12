using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

/// <summary>为 ASS 字重请求查找实际存在的命名字体变体，不创建或近似变体身份。</summary>
public interface IAssFontWeightResolver
{
    /// <summary>返回家族、字重、宽度及斜体特征完全匹配的真实变体；无法匹配时返回 null。</summary>
    SubtitleFontVariant? ResolveVariant(string fontFamily, int weight, int width, bool italic);
}
