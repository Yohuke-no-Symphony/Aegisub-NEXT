using System.Runtime.InteropServices;
using AegiNext.Export;

namespace AegiNext.Export.Tests;

public sealed class NativeExportAbiTests
{
    [Fact]
    public void CurrentContractUsesTheCompleteHardwareRequestLayout()
    {
        Assert.Equal(6U, NativeExportAbi.VERSION);
        Assert.Equal(88, Marshal.SizeOf<NativeExportRequest>());
        Assert.Equal(64, Marshal.OffsetOf<NativeExportRequest>(nameof(NativeExportRequest.EncodingMode)).ToInt32());
        Assert.Equal(68, Marshal.OffsetOf<NativeExportRequest>(nameof(NativeExportRequest.VideoBitrate)).ToInt32());
        Assert.Equal(72, Marshal.OffsetOf<NativeExportRequest>(nameof(NativeExportRequest.DecodeMode)).ToInt32());
        Assert.Equal(76, Marshal.OffsetOf<NativeExportRequest>(nameof(NativeExportRequest.DecodeReserved)).ToInt32());
        Assert.Equal(80, Marshal.OffsetOf<NativeExportRequest>(nameof(NativeExportRequest.RateControlMode)).ToInt32());
        Assert.Equal(84, Marshal.OffsetOf<NativeExportRequest>(nameof(NativeExportRequest.RateControlReserved)).ToInt32());
        Assert.Equal(488, Marshal.SizeOf<NativeExportResultInfo>());
        Assert.Equal(72, Marshal.OffsetOf<NativeExportResultInfo>(nameof(NativeExportResultInfo.FallbackReason)).ToInt32());
        Assert.Equal(328, Marshal.OffsetOf<NativeExportResultInfo>(nameof(NativeExportResultInfo.RateControlMode)).ToInt32());
        Assert.Equal(332, Marshal.OffsetOf<NativeExportResultInfo>(nameof(NativeExportResultInfo.VideoBitrate)).ToInt32());
        Assert.Equal(336, Marshal.OffsetOf<NativeExportResultInfo>(nameof(NativeExportResultInfo.Quality)).ToInt32());
        Assert.Equal(340, Marshal.OffsetOf<NativeExportResultInfo>(nameof(NativeExportResultInfo.Verification)).ToInt32());
        Assert.Equal(344, Marshal.OffsetOf<NativeExportResultInfo>(nameof(NativeExportResultInfo.Codec)).ToInt32());
        Assert.Equal(384, Marshal.OffsetOf<NativeExportResultInfo>(nameof(NativeExportResultInfo.Hdr)).ToInt32());
        Assert.Equal(480, Marshal.OffsetOf<NativeExportResultInfo>(nameof(NativeExportResultInfo.OutputVerification)).ToInt32());
        Assert.Equal(484, Marshal.OffsetOf<NativeExportResultInfo>(nameof(NativeExportResultInfo.BitratePrecision)).ToInt32());
        Assert.Equal(96, Marshal.SizeOf<NativeExportHdrMetadata>());
        Assert.Equal(32, Marshal.SizeOf<NativeExportMuxRequest>());
        Assert.Equal(24, Marshal.SizeOf<NativeExportOverlayInfo>());
        Assert.Equal(8, Marshal.OffsetOf<NativeExportOverlayInfo>(nameof(NativeExportOverlayInfo.State)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<NativeExportOverlayInfo>(nameof(NativeExportOverlayInfo.Reserved)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<NativeExportOverlayInfo>(nameof(NativeExportOverlayInfo.Revision)).ToInt32());
        NativeExportAbi.Validate(6, Path.Combine(Path.GetTempPath(), "aeginext_export.dll"));
    }

    [Theory]
    [InlineData(0U, 1U, 15U, 15U)]
    [InlineData(1U, 2U, 15U, 15U)]
    [InlineData(1U, 1U, 3U, 3U)]
    [InlineData(1U, 1U, 7U, 7U)]
    [InlineData(1U, 1U, 15U, 7U)]
    public void SharedCoreVersionAndCapabilitiesMustMatch(uint exportVersion, uint decodeVersion, uint exportCapabilities, uint decodeCapabilities)
    {
        Assert.Throws<NotSupportedException>(() => NativeExportAbi.ValidateCore(exportVersion, decodeVersion,
            exportCapabilities, decodeCapabilities, Path.Combine(Path.GetTempPath(), "aeginext_export.dll")));
        NativeExportAbi.ValidateCore(1, 1, 15, 15, Path.Combine(Path.GetTempPath(), "aeginext_export.dll"));
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(1U)]
    [InlineData(2U)]
    [InlineData(3U)]
    [InlineData(4U)]
    public void MismatchIdentifiesActualLibraryAndMatchingConfigurationRecovery(uint version)
    {
        var library = Path.Combine(Path.GetTempPath(), "工程 native", "libaeginext_export.dylib");
        var error = Assert.Throws<NotSupportedException>(() => NativeExportAbi.Validate(version, library));
        Assert.Contains("required ABI 6", error.Message, StringComparison.Ordinal);
        Assert.Contains($"actual ABI {version}", error.Message, StringComparison.Ordinal);
        Assert.Contains(library, error.Message, StringComparison.Ordinal);
        Assert.Contains("-Target Workbench -Configuration", error.Message, StringComparison.Ordinal);
    }
}
