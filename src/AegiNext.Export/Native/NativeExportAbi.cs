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

        throw new NotSupportedException($"The native export and preview media cores do not match: export {exportVersion}, decode {decodeVersion}," +
            $" export capabilities {exportCapabilities}, decode capabilities {decodeCapabilities}; load path: {libraryPath}. Rebuild Workbench and the worker with the same configuration.");
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

        throw new NotSupportedException($"Native export ABI mismatch: required ABI {VERSION}, actual ABI {nativeVersion};" +
            $" request structure requires {REQUEST_SIZE} bytes, actual {requestSize} bytes; process architecture: {RuntimeInformation.ProcessArchitecture};" +
            $" result structure requires {RESULT_SIZE} bytes, actual {resultSize} bytes;" +
            $" overlay structure requires {OVERLAY_SIZE} bytes, actual {overlaySize} bytes;" +
            $" load path: {libraryPath}. Run the following command for the current Debug/Release configuration: " +
            "pwsh -NoProfile -File ./build.ps1 -Target Workbench -Configuration Debug (or Release), and use the complete application and worker from the same configuration.");
    }
}
