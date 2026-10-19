using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace AegiNext.Rendering.Fonts;

[SupportedOSPlatform("macos")]
internal static partial class MacFontFileLocator
{
    private const string CORE_TEXT = "/System/Library/Frameworks/CoreText.framework/CoreText";
    private const string CORE_FOUNDATION = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint UTF8_ENCODING = 0x08000100;

    internal static string? GetPath(string name)
    {
        var library = NativeLibrary.Load(CORE_TEXT);
        nint identifier = 0;
        nint font = 0;
        nint url = 0;
        try
        {
            identifier = CreateString(0, name, UTF8_ENCODING);
            if (identifier == 0)
            {
                return null;
            }
            font = CreateFont(identifier, 0, 0);
            if (font == 0)
            {
                return null;
            }
            var attribute = Marshal.ReadIntPtr(NativeLibrary.GetExport(library, "kCTFontURLAttribute"));
            url = CopyAttribute(font, attribute);
            if (url == 0)
            {
                return null;
            }
            var buffer = new byte[32768];
            if (GetFileSystemRepresentation(url, 1, buffer, buffer.Length) == 0)
            {
                return null;
            }
            var length = Array.IndexOf(buffer, (byte)0);
            return length < 0 ? null : Encoding.UTF8.GetString(buffer, 0, length);
        }
        finally
        {
            if (url != 0)
            {
                Release(url);
            }
            if (font != 0)
            {
                Release(font);
            }
            if (identifier != 0)
            {
                Release(identifier);
            }
            NativeLibrary.Free(library);
        }
    }

    [LibraryImport(CORE_FOUNDATION, EntryPoint = "CFStringCreateWithCString", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CreateString(nint allocator, string text, uint encoding);

    [LibraryImport(CORE_TEXT, EntryPoint = "CTFontCreateWithName")]
    private static partial nint CreateFont(nint name, double size, nint matrix);

    [LibraryImport(CORE_TEXT, EntryPoint = "CTFontCopyAttribute")]
    private static partial nint CopyAttribute(nint font, nint attribute);

    [LibraryImport(CORE_FOUNDATION, EntryPoint = "CFURLGetFileSystemRepresentation")]
    private static partial byte GetFileSystemRepresentation(nint url, byte resolveAgainstBase, [Out] byte[] buffer, nint length);

    [LibraryImport(CORE_FOUNDATION, EntryPoint = "CFRelease")]
    private static partial void Release(nint value);
}
