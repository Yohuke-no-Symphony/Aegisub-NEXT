using AegiNext.Desktop.I18n;
using AegiNext.Export;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Editing;

internal sealed class AudioFileFormatChoice(AudioFileFormat format) : ObservableObject
{
    private string label = string.Empty;

    public AudioFileFormat Format { get; } = format;
    public string Label => label;

    internal void RefreshLanguage()
    {
        var text = Format switch
        {
            AudioFileFormat.Copy => Localization.Get("Workbench.AudioFormatCopy"),
            AudioFileFormat.Wav => Localization.Get("Workbench.AudioFormatWav"),
            AudioFileFormat.Flac => "FLAC (.flac)",
            AudioFileFormat.Mp3 => "MP3 (.mp3)",
            AudioFileFormat.Aac => "AAC (.m4a)",
            AudioFileFormat.Opus => "Opus (.opus)",
            _ => throw new ArgumentOutOfRangeException(nameof(Format))
        };
        SetProperty(ref label, text, nameof(Label));
    }
}
