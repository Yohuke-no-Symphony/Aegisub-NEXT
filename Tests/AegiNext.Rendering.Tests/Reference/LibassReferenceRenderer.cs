using System.Runtime.InteropServices;
using System.Text;

namespace AegiNext.Rendering.Tests.Reference;

internal sealed unsafe class LibassReferenceRenderer : IDisposable
{
    private readonly List<nint> dependencies = [];
    private readonly int width;
    private readonly int height;
    private nint globalLoader;
    private nint module;
    private nint library;
    private nint renderer;
    internal int Version { get; }

    internal LibassReferenceRenderer(int width = 640, int height = 360, bool useStorageResolution = true)
    {
        this.width = width;
        this.height = height;
        try
        {
            var preload = Environment.GetEnvironmentVariable("AEGINEXT_LIBASS_REFERENCE_PRELOAD");
            foreach (var path in (preload ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                dependencies.Add(LoadGlobal(AbsolutePath(path)));
            }
            var configured = Environment.GetEnvironmentVariable("AEGINEXT_LIBASS_REFERENCE_PATH") ??
                throw new InvalidOperationException("Configure AEGINEXT_LIBASS_REFERENCE_PATH.");
            module = NativeLibrary.Load(AbsolutePath(configured));
            Version = ((delegate* unmanaged[Cdecl]<int>)Export("ass_library_version"))();
            if (Version < 0x01702000)
            {
                throw new InvalidOperationException($"Reference requires libass 0.17.2 or later; found 0x{Version:X8}.");
            }
            library = ((delegate* unmanaged[Cdecl]<nint>)Export("ass_library_init"))();
            if (library == 0)
            {
                throw new InvalidOperationException("libass library initialization failed.");
            }
            renderer = ((delegate* unmanaged[Cdecl]<nint, nint>)Export("ass_renderer_init"))(library);
            if (renderer == 0)
            {
                throw new InvalidOperationException("libass renderer initialization failed.");
            }
            ((delegate* unmanaged[Cdecl]<nint, int, int, void>)Export("ass_set_frame_size"))(renderer, width, height);
            if (useStorageResolution)
            {
                ((delegate* unmanaged[Cdecl]<nint, int, int, void>)Export("ass_set_storage_size"))(renderer, width, height);
            }
            ((delegate* unmanaged[Cdecl]<nint, int, void>)Export("ass_set_hinting"))(renderer, 0);
            var font = Encoding.UTF8.GetBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf") + '\0');
            var family = "Noto Sans\0"u8.ToArray();
            fixed (byte* fontPointer = font, familyPointer = family)
            {
                ((delegate* unmanaged[Cdecl]<nint, byte*, byte*, int, byte*, int, void>)Export("ass_set_fonts"))
                    (renderer, fontPointer, familyPointer, 0, null, 1);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal SubtitleReferenceFrame Render(string source, long milliseconds)
    {
        ObjectDisposedException.ThrowIf(renderer == 0, this);
        var utf8 = Encoding.UTF8.GetBytes(source + '\0');
        nint track;
        fixed (byte* pointer = utf8)
        {
            track = ((delegate* unmanaged[Cdecl]<nint, byte*, nuint, byte*, nint>)Export("ass_read_memory"))
                (library, pointer, (nuint)utf8.Length - 1, null);
        }
        if (track == 0)
        {
            throw new InvalidDataException("libass could not parse the reference script.");
        }
        try
        {
            var changed = 0;
            var image = ((delegate* unmanaged[Cdecl]<nint, nint, long, int*, nint>)Export("ass_render_frame"))
                (renderer, track, milliseconds, &changed);
            var frame = new SubtitleReferenceFrame(width, height, new float[width * height * 4]);
            while (image != 0)
            {
                var value = *(LibassReferenceImage*)image;
                Composite(frame, value);
                image = value.Next;
            }
            return frame;
        }
        finally
        {
            ((delegate* unmanaged[Cdecl]<nint, void>)Export("ass_free_track"))(track);
        }
    }

    private static void Composite(SubtitleReferenceFrame frame, LibassReferenceImage image)
    {
        var opacity = (255 - (image.Color & 255)) / 255f;
        var red = (image.Color >> 24) / 255f;
        var green = ((image.Color >> 16) & 255) / 255f;
        var blue = ((image.Color >> 8) & 255) / 255f;
        for (var row = 0; row < image.Height; row++)
        {
            var y = row + image.Y;
            if (y < 0 || y >= frame.Height)
            {
                continue;
            }
            var bitmap = (byte*)image.Bitmap + row * image.Stride;
            for (var column = 0; column < image.Width; column++)
            {
                var x = column + image.X;
                if (x < 0 || x >= frame.Width)
                {
                    continue;
                }
                var alpha = opacity * bitmap[column] / 255;
                var index = (y * frame.Width + x) * 4;
                var remaining = 1 - alpha;
                frame.Pixels[index] = red * alpha + frame.Pixels[index] * remaining;
                frame.Pixels[index + 1] = green * alpha + frame.Pixels[index + 1] * remaining;
                frame.Pixels[index + 2] = blue * alpha + frame.Pixels[index + 2] * remaining;
                frame.Pixels[index + 3] = alpha + frame.Pixels[index + 3] * remaining;
            }
        }
    }

    private nint Export(string name) => NativeLibrary.GetExport(module, name);
    private static string AbsolutePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Reference libraries require existing absolute paths.", path);
        }
        return path;
    }

    private nint LoadGlobal(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return NativeLibrary.Load(path);
        }
        if (globalLoader == 0)
        {
            globalLoader = NativeLibrary.Load(OperatingSystem.IsMacOS() ? "/usr/lib/libSystem.B.dylib" : "libdl.so.2");
        }
        var bytes = Encoding.UTF8.GetBytes(path + '\0');
        fixed (byte* pointer = bytes)
        {
            var result = ((delegate* unmanaged[Cdecl]<byte*, int, nint>)NativeLibrary.GetExport(globalLoader, "dlopen"))
                (pointer, 2 | (OperatingSystem.IsMacOS() ? 8 : 0x100));
            if (result != 0)
            {
                return result;
            }
            var error = ((delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(globalLoader, "dlerror"))();
            throw new DllNotFoundException(Marshal.PtrToStringUTF8(error));
        }
    }

    public void Dispose()
    {
        if (renderer != 0)
        {
            ((delegate* unmanaged[Cdecl]<nint, void>)Export("ass_renderer_done"))(renderer);
            renderer = 0;
        }
        if (library != 0)
        {
            ((delegate* unmanaged[Cdecl]<nint, void>)Export("ass_library_done"))(library);
            library = 0;
        }
        if (module != 0)
        {
            NativeLibrary.Free(module);
            module = 0;
        }
        for (var index = dependencies.Count - 1; index >= 0; index--)
        {
            if (OperatingSystem.IsWindows())
            {
                NativeLibrary.Free(dependencies[index]);
            }
            else
            {
                ((delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(globalLoader, "dlclose"))(dependencies[index]);
            }
        }
        dependencies.Clear();
        if (globalLoader != 0)
        {
            NativeLibrary.Free(globalLoader);
            globalLoader = 0;
        }
    }
}
