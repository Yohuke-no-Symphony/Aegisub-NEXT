using System.Globalization;
using AegiNext.Export;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Editing;

internal sealed class AudioExportSettingsViewModel : ObservableObject
{
    private readonly AudioFileFormatChoice[] formats = Enum.GetValues<AudioFileFormat>()
        .Select(value => new AudioFileFormatChoice(value)).ToArray();
    private AudioFileFormat format;
    private decimal? bitrate = 192;
    private string bitrateText = "192";

    internal AudioExportSettingsViewModel()
    {
        RefreshLanguage();
    }

    public IReadOnlyList<AudioFileFormatChoice> Formats => formats;

    public AudioFileFormat Format
    {
        get => format;
        set
        {
            _ = AudioExportCapabilities.GetExtension(value);
            if (SetProperty(ref format, value))
            {
                OnPropertyChanged(nameof(SelectedFormatChoice));
                OnPropertyChanged(nameof(IsBitrateActive));
                OnPropertyChanged(nameof(BitrateMaximum));
            }
        }
    }

    public AudioFileFormatChoice? SelectedFormatChoice
    {
        get => formats.Single(value => value.Format == Format);
        set
        {
            if (value is not null && formats.Contains(value))
            {
                Format = value.Format;
            }
        }
    }

    public bool IsBitrateActive => AudioExportCapabilities.UsesBitrate(Format);
    public decimal BitrateMaximum => AudioExportCapabilities.GetMaximumBitrate(Format) / 1000m;

    public decimal? Bitrate
    {
        get => bitrate;
        set => SetProperty(ref bitrate, value);
    }

    public string BitrateText
    {
        get => bitrateText;
        set => SetProperty(ref bitrateText, value);
    }

    internal AudioExportSettings CaptureSettings()
    {
        if (!IsBitrateActive)
        {
            return new(Format, 192000);
        }

        if (!decimal.TryParse(BitrateText, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ||
            value < 32 || value > BitrateMaximum || value != decimal.Truncate(value))
        {
            throw new ExportSettingsValidationException("AudioExportBitrateInput", "Workbench.InvalidAudioExportBitrate");
        }

        return new(Format, decimal.ToInt32(value * 1000));
    }

    internal void AcceptSettings(AudioExportSettings settings)
    {
        if (AudioExportCapabilities.UsesBitrate(settings.Format))
        {
            Bitrate = settings.Bitrate / 1000m;
        }
    }

    internal void RefreshLanguage()
    {
        foreach (var choice in formats)
        {
            choice.RefreshLanguage();
        }
    }
}
