using System.Runtime.InteropServices;

namespace AegiNext.Rendering.Fonts;

internal static unsafe partial class HarfBuzzSubsetNativeMethods
{
    private const string LIBRARY = "libHarfBuzzSharp";

    [LibraryImport(LIBRARY, EntryPoint = "hb_blob_create")]
    internal static partial nint CreateBlob(byte* data, uint length, int mode, nint userData, nint destroy);

    [LibraryImport(LIBRARY, EntryPoint = "hb_blob_destroy")]
    internal static partial void DestroyBlob(nint blob);

    [LibraryImport(LIBRARY, EntryPoint = "hb_blob_get_data")]
    internal static partial byte* GetBlobData(nint blob, out uint length);

    [LibraryImport(LIBRARY, EntryPoint = "hb_face_create")]
    internal static partial nint CreateFace(nint blob, uint index);

    [LibraryImport(LIBRARY, EntryPoint = "hb_face_destroy")]
    internal static partial void DestroyFace(nint face);

    [LibraryImport(LIBRARY, EntryPoint = "hb_face_get_glyph_count")]
    internal static partial uint GetGlyphCount(nint face);

    [LibraryImport(LIBRARY, EntryPoint = "hb_face_reference_blob")]
    internal static partial nint ReferenceBlob(nint face);

    [LibraryImport(LIBRARY, EntryPoint = "hb_subset_input_create_or_fail")]
    internal static partial nint CreateInput();

    [LibraryImport(LIBRARY, EntryPoint = "hb_subset_input_destroy")]
    internal static partial void DestroyInput(nint input);

    [LibraryImport(LIBRARY, EntryPoint = "hb_subset_input_keep_everything")]
    internal static partial void KeepEverything(nint input);

    [LibraryImport(LIBRARY, EntryPoint = "hb_subset_input_get_flags")]
    internal static partial uint GetFlags(nint input);

    [LibraryImport(LIBRARY, EntryPoint = "hb_subset_input_set_flags")]
    internal static partial void SetFlags(nint input, uint flags);

    [LibraryImport(LIBRARY, EntryPoint = "hb_subset_input_pin_axis_location")]
    internal static partial int PinAxis(nint input, nint face, uint tag, float value);

    [LibraryImport(LIBRARY, EntryPoint = "hb_subset_or_fail")]
    internal static partial nint Prepare(nint face, nint input);
}
