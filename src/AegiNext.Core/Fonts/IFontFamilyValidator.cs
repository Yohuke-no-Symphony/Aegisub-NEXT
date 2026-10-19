namespace AegiNext.Core.Fonts;

/// <summary>验证字体文件可解析且包含声明的字体族，不安装字体或修改输入。</summary>
public interface IFontFamilyValidator
{
    /// <summary>验证完整原始字体文件及其声明的族归属。</summary>
    void Validate(FontFamilyBundle family);
}
