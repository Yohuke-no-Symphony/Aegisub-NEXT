using System.Globalization;
using AegiNext.Desktop.I18n;
using AegiNext.Export;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Editing;

/// <summary>共享的压制参数草稿和模式投影，不拥有会话、个人库或工程事务。</summary>
public class VideoExportSettingsViewModel : ObservableObject
{
    private static readonly string[] encodingPresets =
        ["veryslow", "slower", "slow", "medium", "fast", "faster", "veryfast", "superfast", "ultrafast"];
    private string crfText = "20";
    private string audioBitrateText = "192";
    private string videoBitrateText = "8";
    private decimal? videoBitrate = 8;
    private bool useHardwareEncoder;
    private int codec;
    private string[] codecs = [];
    private int speed = 3;
    private string[] speeds = [];
    private int qualityMode;
    private int softwareQualityMode;
    private int bitrateMode;
    private string[] qualityModes = [];
    private string[] bitrateModes = [];
    private decimal? crf = 20;
    private int audioMode;
    private string[] audioModes = [];
    private decimal? audioBitrate = 192;
    private bool refreshingChoices;

    /// <summary>创建独立压制参数草稿并初始化当前语言选项。</summary>
    public VideoExportSettingsViewModel()
    {
        RefreshLanguage();
    }

    /// <summary>刷新显示选项并保留模式、选择与所有原始草稿。</summary>
    public void RefreshLanguage()
    {
        RefreshChoices([Localization.Get("Workbench.Automatic"), "H.264", "HEVC / H.265"],
            [Localization.Get("Workbench.Fast"), Localization.Get("Workbench.Medium"), Localization.Get("Workbench.Slow")],
            [Localization.Get("Workbench.Copy"), "AAC", Localization.Get("Workbench.NoAudio")]);
    }

    internal event EventHandler? ChoicesRefreshing;
    internal event EventHandler? ChoicesRefreshed;

    public int Codec
    {
        get => codec;
        set
        {
            if (!refreshingChoices)
            {
                SetProperty(ref codec, value);
            }
        }
    }

    public bool UseHardwareEncoder
    {
        get => useHardwareEncoder;
        set
        {
            if (!SetProperty(ref useHardwareEncoder, value))
            {
                return;
            }

            QualityMode = value ? 1 : softwareQualityMode;
            OnPropertyChanged(nameof(IsSoftwareEncoding));
            OnPropertyChanged(nameof(CanSelectQualityMode));
        }
    }

    public bool IsSoftwareEncoding => !UseHardwareEncoder;
    public bool CanSelectQualityMode => !UseHardwareEncoder;
    public bool IsCrfMode => QualityMode == 0;
    public bool IsBitrateMode => QualityMode == 1;
    public bool IsAacAudio => AudioMode == 1;

    public int QualityMode
    {
        get => qualityMode;
        set
        {
            if (refreshingChoices || UseHardwareEncoder && value == 0 || !SetProperty(ref qualityMode, value))
            {
                return;
            }

            if (!UseHardwareEncoder)
            {
                softwareQualityMode = value;
            }
            OnPropertyChanged(nameof(IsCrfMode));
            OnPropertyChanged(nameof(IsBitrateMode));
        }
    }

    public int BitrateMode
    {
        get => bitrateMode;
        set
        {
            if (!refreshingChoices)
            {
                SetProperty(ref bitrateMode, value);
            }
        }
    }

    public string[] QualityModes
    {
        get => qualityModes;
        private set => SetProperty(ref qualityModes, value);
    }

    public string[] BitrateModes
    {
        get => bitrateModes;
        private set => SetProperty(ref bitrateModes, value);
    }

    public decimal? VideoBitrate
    {
        get => videoBitrate;
        set => SetProperty(ref videoBitrate, value);
    }

    public string VideoBitrateText
    {
        get => videoBitrateText;
        set => SetProperty(ref videoBitrateText, value);
    }

    public string[] Codecs
    {
        get => codecs;
        set => SetProperty(ref codecs, value);
    }

    public int Speed
    {
        get => speed;
        set
        {
            if (!refreshingChoices && SetProperty(ref speed, value))
            {
                OnPropertyChanged(nameof(EncodingPreset));
            }
        }
    }

    public string EncodingPreset => Speed >= 0 && Speed < encodingPresets.Length ? encodingPresets[Speed] : string.Empty;

    public string[] Speeds
    {
        get => speeds;
        set => SetProperty(ref speeds, value);
    }

    public decimal? Crf
    {
        get => crf;
        set => SetProperty(ref crf, value);
    }

    public int AudioMode
    {
        get => audioMode;
        set
        {
            if (!refreshingChoices && SetProperty(ref audioMode, value))
            {
                OnPropertyChanged(nameof(IsAacAudio));
            }
        }
    }

    public string[] AudioModes
    {
        get => audioModes;
        set => SetProperty(ref audioModes, value);
    }

    public decimal? AudioBitrate
    {
        get => audioBitrate;
        set => SetProperty(ref audioBitrate, value);
    }

    public string CrfText
    {
        get => crfText;
        set => SetProperty(ref crfText, value);
    }

    public string AudioBitrateText
    {
        get => audioBitrateText;
        set => SetProperty(ref audioBitrateText, value);
    }

    internal void RefreshChoices(string[] codecOptions, string[] speedOptions, string[] audioOptions)
    {
        refreshingChoices = true;
        try
        {
            ChoicesRefreshing?.Invoke(this, EventArgs.Empty);
            Codecs = codecOptions;
            Speeds = [Localization.Get("Workbench.SpeedVeryslow"), Localization.Get("Workbench.SpeedSlower"),
                speedOptions[2], speedOptions[1], speedOptions[0], Localization.Get("Workbench.SpeedFaster"),
                Localization.Get("Workbench.SpeedVeryfast"), Localization.Get("Workbench.SpeedSuperfast"),
                Localization.Get("Workbench.SpeedUltrafast")];
            AudioModes = audioOptions;
            QualityModes = ["CRF", "Mbps"];
            BitrateModes = [Localization.Get("Workbench.VariableBitrate"), Localization.Get("Workbench.ConstantBitrate")];
            OnPropertyChanged(nameof(Codec));
            OnPropertyChanged(nameof(Speed));
            OnPropertyChanged(nameof(AudioMode));
            OnPropertyChanged(nameof(QualityMode));
            OnPropertyChanged(nameof(BitrateMode));
        }
        finally
        {
            try
            {
                ChoicesRefreshed?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                refreshingChoices = false;
            }
        }
    }

    internal VideoExportSettings CaptureSettings(bool forPreset = false)
    {
        RequireIndex(Codec, 3, "CodecCombo");
        RequireIndex(Speed, encodingPresets.Length, "SpeedCombo");
        RequireIndex(AudioMode, 3, "AudioModeCombo");
        RequireIndex(QualityMode, 2, "QualityModeCombo");
        if (IsBitrateMode)
        {
            RequireIndex(BitrateMode, 2, "BitrateModeCombo");
        }

        var quality = CaptureNumber(CrfText, Crf, 20, 0, 51, 1, IsCrfMode, "CrfInput", "Workbench.Quality");
        var video = CaptureNumber(VideoBitrateText, VideoBitrate, 8, 0.1m, 200, 1000000, IsBitrateMode,
            "VideoBitrateInput", "Workbench.VideoBitrate");
        var audio = CaptureNumber(AudioBitrateText, AudioBitrate, 192, 32, 512, 1000, IsAacAudio,
            "AudioBitrateInput", "Workbench.AudioBitrate");
        var settings = new VideoExportSettings
        {
            Codec = (VideoCodec)Codec,
            EncodingMode = UseHardwareEncoder ? VideoEncodingMode.HARDWARE : VideoEncodingMode.SOFTWARE,
            RateControlMode = IsCrfMode ? VideoRateControlMode.CRF : BitrateMode == 0 ? VideoRateControlMode.VBR : VideoRateControlMode.CBR,
            Preset = EncodingPreset,
            Crf = quality,
            VideoBitrate = video,
            AudioMode = (AudioExportMode)AudioMode,
            AudioBitrate = audio
        };
        if (forPreset)
        {
            VideoExportSettingsValidator.ValidatePreset(settings);
        }
        else
        {
            VideoExportSettingsValidator.Validate(settings);
        }
        return settings;
    }

    internal void AcceptSettings(VideoExportSettings settings)
    {
        if (settings.RateControlMode == VideoRateControlMode.CRF)
        {
            Crf = settings.Crf;
        }
        else
        {
            VideoBitrate = settings.VideoBitrate / 1000000m;
        }
        if (settings.AudioMode == AudioExportMode.Aac)
        {
            AudioBitrate = settings.AudioBitrate / 1000m;
        }
    }

    internal void ApplySettings(VideoExportSettings settings)
    {
        VideoExportSettingsValidator.ValidatePreset(settings);
        var presetIndex = Array.IndexOf(encodingPresets, settings.Preset);
        refreshingChoices = true;
        try
        {
            codec = (int)settings.Codec;
            speed = presetIndex;
            useHardwareEncoder = settings.EncodingMode == VideoEncodingMode.HARDWARE;
            qualityMode = settings.RateControlMode == VideoRateControlMode.CRF ? 0 : 1;
            softwareQualityMode = useHardwareEncoder ? softwareQualityMode : qualityMode;
            bitrateMode = settings.RateControlMode == VideoRateControlMode.CBR ? 1 : 0;
            audioMode = (int)settings.AudioMode;
            CrfText = settings.Crf.ToString(CultureInfo.CurrentCulture);
            VideoBitrateText = (settings.VideoBitrate / 1000000m).ToString(CultureInfo.CurrentCulture);
            AudioBitrateText = (settings.AudioBitrate / 1000m).ToString(CultureInfo.CurrentCulture);
            Crf = settings.Crf;
            VideoBitrate = settings.VideoBitrate / 1000000m;
            AudioBitrate = settings.AudioBitrate / 1000m;
            OnPropertyChanged(nameof(Codec));
            OnPropertyChanged(nameof(Speed));
            OnPropertyChanged(nameof(EncodingPreset));
            OnPropertyChanged(nameof(UseHardwareEncoder));
            OnPropertyChanged(nameof(IsSoftwareEncoding));
            OnPropertyChanged(nameof(CanSelectQualityMode));
            OnPropertyChanged(nameof(QualityMode));
            OnPropertyChanged(nameof(BitrateMode));
            OnPropertyChanged(nameof(AudioMode));
            OnPropertyChanged(nameof(IsCrfMode));
            OnPropertyChanged(nameof(IsBitrateMode));
            OnPropertyChanged(nameof(IsAacAudio));
        }
        finally
        {
            refreshingChoices = false;
        }
    }

    private static void RequireIndex(int value, int count, string fieldKey)
    {
        if (value < 0 || value >= count)
        {
            throw new ExportSettingsValidationException(fieldKey, "Workbench.InvalidExportMode");
        }
    }

    private static int CaptureNumber(string text, decimal? retained, decimal fallback, decimal minimum,
        decimal maximum, int scale, bool required, string fieldKey, string localizationKey)
    {
        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
            IsValidNumber(value, minimum, maximum, scale))
        {
            return decimal.ToInt32(value * scale);
        }
        if (required)
        {
            throw new ExportSettingsValidationException(fieldKey, localizationKey);
        }

        var previous = retained is { } number && IsValidNumber(number, minimum, maximum, scale) ? number : fallback;
        return decimal.ToInt32(previous * scale);
    }

    private static bool IsValidNumber(decimal value, decimal minimum, decimal maximum, int scale)
    {
        if (value < minimum || value > maximum)
        {
            return false;
        }
        return value * scale == decimal.Truncate(value * scale);
    }
}
