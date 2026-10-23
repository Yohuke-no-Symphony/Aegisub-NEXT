using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AegiNext.Media.Audio;

internal static partial class NativeAudioMethods
{
    static NativeAudioMethods()
    {
        NativeMediaRuntime.Initialize();
    }

    internal const int ERROR_CAPACITY = 1024;
    private const string LIBRARY = "aeginext_audio";

    [LibraryImport(LIBRARY, EntryPoint = "an_audio_abi_version")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint AbiVersion();

    [LibraryImport(LIBRARY, EntryPoint = "an_audio_clock_snapshot_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint ClockSnapshotSize();

    [LibraryImport(LIBRARY, EntryPoint = "an_audio_output_create_system")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int CreateSystemOutput(out nint output, int rate, int channels, byte* error, uint capacity);

    [LibraryImport(LIBRARY, EntryPoint = "an_audio_output_snapshot")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int Snapshot(AudioOutputHandle output, ref NativeAudioClock snapshot, byte* error, uint capacity);

    [LibraryImport(LIBRARY, EntryPoint = "an_audio_decoder_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int CreateDecoder(out nint decoder, byte* error, uint capacity);

    [LibraryImport(LIBRARY, EntryPoint = "an_audio_decoder_set_timestamp_policy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int SetTimestampPolicy(AudioDecoderHandle decoder, int policy, byte* error, uint capacity);

    [LibraryImport(LIBRARY, EntryPoint = "an_audio_decoder_open", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int OpenDecoder(AudioDecoderHandle decoder, string path, int stream, int rate, int channels, byte* error, uint capacity);

    [LibraryImport(LIBRARY, EntryPoint = "an_audio_decoder_read")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int Read(AudioDecoderHandle decoder, float* samples, int frameCapacity, out int frames, out long startSample, byte* error, uint capacity);

    [LibraryImport(LIBRARY, EntryPoint = "an_audio_decoder_seek")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int Seek(AudioDecoderHandle decoder, long sample, byte* error, uint capacity);

    [LibraryImport(LIBRARY, EntryPoint = "an_audio_decoder_cancel")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void Cancel(AudioDecoderHandle decoder);

    [LibraryImport(LIBRARY, EntryPoint = "an_audio_decoder_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void DestroyDecoder(nint decoder);

    [LibraryImport(LIBRARY, EntryPoint = "an_audio_output_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int CreateOutput(out nint output, int rate, int channels, byte* error, uint capacity);

    [LibraryImport(LIBRARY, EntryPoint = "an_audio_output_write")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int Write(AudioOutputHandle output, float* samples, int frames, byte* error, uint capacity);

    [LibraryImport(LIBRARY, EntryPoint = "an_audio_output_pause")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int Pause(AudioOutputHandle output, int pause, byte* error, uint capacity);

    [LibraryImport(LIBRARY, EntryPoint = "an_audio_output_clear")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int Clear(AudioOutputHandle output, byte* error, uint capacity);

    [LibraryImport(LIBRARY, EntryPoint = "an_audio_output_queued")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Queued(AudioOutputHandle output);

    [LibraryImport(LIBRARY, EntryPoint = "an_audio_output_latency")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Latency(AudioOutputHandle output);

    [LibraryImport(LIBRARY, EntryPoint = "an_audio_output_gain")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int Gain(AudioOutputHandle output, float gain, byte* error, uint capacity);

    [LibraryImport(LIBRARY, EntryPoint = "an_audio_output_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void DestroyOutput(nint output);
}
