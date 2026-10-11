using System.Runtime.InteropServices;

namespace AegiNext.Export;

internal static class NativeExportAbi
{
    internal const uint VERSION = 6;
    internal const uint REQUEST_SIZE = 88;
    internal const uint RESULT_SIZE = 488;
    internal const uint OVERLAY_SIZE = 24;
    private const uint CORE_VERSION = 1;
    private const uint CORE_CAPABILITIES = 15;

    internal static void ValidateCore(uint exportVersion, uint decodeVersion, uint exportCapabilities, uint decodeCapabilities, string libraryPath)
    {
        if (exportVersion == CORE_VERSION && decodeVersion == exportVersion && exportCapabilities == decodeCapabilities &&
            (exportCapabilities & CORE_CAPABILITIES) == CORE_CAPABILITIES)
        {
            return;
        }

        throw new NotSupportedException($"原生导出与预览媒体核心不匹配：导出 {exportVersion}，解码 {decodeVersion}，" +
            $"导出能力 {exportCapabilities}，解码能力 {decodeCapabilities}；加载路径 {libraryPath}。请重新构建同配置 Workbench 与 worker。");
    }

    internal static void Validate(uint nativeVersion, string libraryPath)
    {
        var requestSize = Marshal.SizeOf<NativeExportRequest>();
        var resultSize = Marshal.SizeOf<NativeExportResultInfo>();
        var overlaySize = Marshal.SizeOf<NativeExportOverlayInfo>();
        if (nativeVersion == VERSION && requestSize == REQUEST_SIZE && resultSize == RESULT_SIZE && overlaySize == OVERLAY_SIZE)
        {
            return;
        }

        throw new NotSupportedException($"原生导出 ABI 不匹配：需要 ABI {VERSION}，实际 ABI {nativeVersion}；" +
            $"请求结构需要 {REQUEST_SIZE} 字节，实际 {requestSize} 字节；进程架构 {RuntimeInformation.ProcessArchitecture}；" +
            $"结果结构需要 {RESULT_SIZE} 字节，实际 {resultSize} 字节；" +
            $"前景结构需要 {OVERLAY_SIZE} 字节，实际 {overlaySize} 字节；" +
            $"加载路径 {libraryPath}。请按当前 Debug/Release 配置运行 " +
            "pwsh -NoProfile -File ./build.ps1 -Target Workbench -Configuration Debug（或 Release），使用完整的同配置应用与 worker。");
    }
}
