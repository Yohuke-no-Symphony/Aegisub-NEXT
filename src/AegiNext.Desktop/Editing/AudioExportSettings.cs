using AegiNext.Export;

namespace AegiNext.Desktop.Editing;

internal sealed record AudioExportSettings(AudioFileFormat Format, int Bitrate);
