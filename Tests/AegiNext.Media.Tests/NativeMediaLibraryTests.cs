using AegiNext.Media.Runtime;

namespace AegiNext.Media.Tests;

/// <summary>公共原生模块加载入口只允许应用拥有的固定模块。</summary>
public sealed class NativeMediaLibraryTests
{
    /// <summary>外部路径、遍历路径与未知模块在路径解析和原生加载前拒绝。</summary>
    [Theory]
    [InlineData("../aeginext_export")]
    [InlineData("/tmp/aeginext_export")]
    [InlineData("aeginext_export.dll")]
    [InlineData("unrelated")]
    public void RejectsPathsAndUnknownModules(string moduleName)
    {
        Assert.Throws<ArgumentException>(() => NativeMediaLibrary.GetPath(moduleName));
        Assert.Throws<ArgumentException>(() => NativeMediaLibrary.Load(moduleName));
    }
}
