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
    private string qualityText = "20";
    private string audioBitrateText = "192";
    private string videoBitrateText = "8";
    private decimal? videoBitrate = 8;
    private bool useHardwareEncoder;
    private VideoCodec selectedCodec;
    private readonly VideoCodecChoice[] codecs = VideoExportCapabilities.All.Select(value => new VideoCodecChoice(value.Codec)).ToArray();
    private readonly VideoContainerChoice[] containerChoices =
        [new(VideoContainerFormat.MP4), new(VideoContainerFormat.MKV), new(VideoContainerFormat.WEBM)];
    private VideoContainerChoice[] containers = [];
    private VideoContainerFormat selectedContainer = VideoContainerFormat.MP4;
    private string? sourceAudioCodec;
    private bool audioConvertedToOpus;
    private int speed = 3;
    private string[] speeds = [];
    private int qualityMode;
    private int softwareQualityMode;
    private int bitrateMode;
    private string[] qualityModes = [];
    private string[] bitrateModes = [];
    private decimal? quality = 20;
    private int audioMode;
    private string[] audioModes = [];
    private decimal? audioBitrate = 192;
    private bool refreshingChoices;

    /// <summary>创建独立压制参数草稿并初始化当前语言选项。</summary>
    public VideoExportSettingsViewModel()
    {
        UpdateContainers();
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
        get => Array.FindIndex(codecs, value => value.Codec == selectedCodec);
        set
        {
            if (!refreshingChoices && value >= 0 && value < codecs.Length)
            {
                SelectedCodec = codecs[value].Codec;
            }
        }
    }

    public VideoCodec SelectedCodec
    {
        get => selectedCodec;
        set
        {
            if (refreshingChoices || selectedCodec == value)
            {
                return;
            }

            var previous = Capabilities;
            var next = VideoExportCapabilities.Get(value);
            var raw = QualityText;
            var resetQuality = decimal.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) &&
                IsValidNumber(number, previous.QualityMinimum, previous.QualityMaximum, 1) &&
                (number == previous.DefaultQuality || !IsValidNumber(number, next.QualityMinimum, next.QualityMaximum, 1));
            refreshingChoices = true;
            try
            {
                ChoicesRefreshing?.Invoke(this, EventArgs.Empty);
                selectedCodec = value;
                var hardwareChanged = useHardwareEncoder && !CanUseHardwareEncoder;
                if (hardwareChanged)
                {
                    useHardwareEncoder = false;
                    qualityMode = softwareQualityMode;
                }
                var qualityChanged = resetQuality && quality != next.DefaultQuality;
                if (resetQuality)
                {
                    quality = next.DefaultQuality;
                    raw = next.DefaultQuality.ToString(CultureInfo.CurrentCulture);
                }
                UpdateContainers();
                RefreshCodecProperties();
                OnPropertyChanged(nameof(Containers));
                RefreshContainerProperties();
                if (hardwareChanged)
                {
                    RefreshEncodingModeProperties();
                }
                if (qualityChanged)
                {
                    OnPropertyChanged(nameof(Quality));
                }
                RefreshAudioProperties();
                QualityText = raw;
            }
            finally
            {
                CompleteChoicesRefresh();
            }
        }
    }

    public VideoCodecChoice? SelectedCodecChoice
    {
        get => codecs.FirstOrDefault(value => value.Codec == SelectedCodec);
        set
        {
            if (value is not null)
            {
                SelectedCodec = value.Codec;
            }
        }
    }

    public IReadOnlyList<VideoContainerChoice> Containers => containers;

    public int Container
    {
        get => Array.FindIndex(containers, value => value.Container == selectedContainer);
        set
        {
            if (!refreshingChoices && value >= 0 && value < containers.Length)
            {
                SelectedContainer = containers[value].Container;
            }
        }
    }

    public VideoContainerFormat SelectedContainer
    {
        get => selectedContainer;
        set
        {
            if (refreshingChoices || selectedContainer == value)
            {
                return;
            }
            if (containers.All(choice => choice.Container != value))
            {
                throw new ArgumentException("The container is incompatible with the selected video codec.", nameof(value));
            }

            selectedContainer = value;
            audioConvertedToOpus = false;
            NormalizeAudioForContainer();
            RefreshContainerProperties();
            RefreshAudioProperties();
        }
    }

    public VideoContainerChoice? SelectedContainerChoice
    {
        get => containers.Single(value => value.Container == SelectedContainer);
        set
        {
            if (value is not null && containers.Contains(value))
            {
                SelectedContainer = value.Container;
            }
        }
    }

    public bool IsWebM => SelectedContainer == VideoContainerFormat.WEBM;
    public bool HasAudioCompatibilityHint => IsWebM;
    public string AudioCompatibilityHint => IsWebM
        ? Localization.Get(audioConvertedToOpus ? "Workbench.WebMAudioConvertedHint" : "Workbench.WebMAudioHint")
        : string.Empty;

    internal void UpdateSourceAudioCodec(string? codec)
    {
        codec = string.IsNullOrWhiteSpace(codec) ? null : codec.ToLowerInvariant();
        if (sourceAudioCodec == codec)
        {
            return;
        }

        sourceAudioCodec = codec;
        if (NormalizeAudioForContainer())
        {
            RefreshAudioProperties();
        }
    }

    private VideoCodecCapabilities Capabilities => VideoExportCapabilities.Get(SelectedCodec);
    public decimal QualityMinimum => Capabilities.QualityMinimum;
    public decimal QualityMaximum => Capabilities.QualityMaximum;
    public bool CanUseHardwareEncoder => Capabilities.SupportsHardwareOnCurrentPlatform;
    public bool SupportsWebM => Capabilities.Extensions.Contains(".webm");

    public bool UseHardwareEncoder
    {
        get => useHardwareEncoder;
        set
        {
            if (value && !CanUseHardwareEncoder || useHardwareEncoder == value)
            {
                return;
            }

            useHardwareEncoder = value;
            qualityMode = value ? 1 : softwareQualityMode;
            RefreshEncodingModeProperties();
        }
    }

    public bool IsSoftwareEncoding => !UseHardwareEncoder;
    public bool CanSelectQualityMode => !UseHardwareEncoder;
    public bool IsQualityMode => QualityMode == 0;
    public bool IsBitrateMode => QualityMode == 1;
    public bool IsVideoBitrateActive => IsBitrateMode || Capabilities.QualityMode == VideoRateControlMode.CQ;
    public bool IsEncodedAudio => AudioMode is (int)AudioExportMode.Aac or (int)AudioExportMode.Opus;

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
            OnPropertyChanged(nameof(IsQualityMode));
            OnPropertyChanged(nameof(IsBitrateMode));
            OnPropertyChanged(nameof(IsVideoBitrateActive));
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

    public IReadOnlyList<VideoCodecChoice> Codecs => codecs;

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

    public decimal? Quality
    {
        get => quality;
        set => SetProperty(ref quality, value);
    }

    public int AudioMode
    {
        get => audioMode;
        set
        {
            if (refreshingChoices || audioMode == value)
            {
                return;
            }

            audioMode = value;
            audioConvertedToOpus = false;
            NormalizeAudioForContainer();
            RefreshAudioProperties();
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

    public string QualityText
    {
        get => qualityText;
        set => SetProperty(ref qualityText, value);
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
            foreach (var choice in codecs)
            {
                choice.UpdateLabel(choice.Codec switch
                {
                    VideoCodec.Auto => codecOptions[0],
                    VideoCodec.H264 => codecOptions[1],
                    VideoCodec.Hevc => codecOptions[2],
                    VideoCodec.Vp8 => "VP8",
                    VideoCodec.Vp9 => "VP9",
                    VideoCodec.Av1 => "AV1",
                    _ => throw new InvalidOperationException()
                });
            }
            Speeds = [Localization.Get("Workbench.SpeedVeryslow"), Localization.Get("Workbench.SpeedSlower"),
                speedOptions[2], speedOptions[1], speedOptions[0], Localization.Get("Workbench.SpeedFaster"),
                Localization.Get("Workbench.SpeedVeryfast"), Localization.Get("Workbench.SpeedSuperfast"),
                Localization.Get("Workbench.SpeedUltrafast")];
            AudioModes = [audioOptions[0], audioOptions[1], audioOptions[2], "Opus"];
            RefreshQualityModes();
            BitrateModes = [Localization.Get("Workbench.VariableBitrate"), Localization.Get("Workbench.ConstantBitrate")];
            OnPropertyChanged(nameof(Codec));
            OnPropertyChanged(nameof(Speed));
            OnPropertyChanged(nameof(AudioMode));
            OnPropertyChanged(nameof(QualityMode));
            OnPropertyChanged(nameof(BitrateMode));
            OnPropertyChanged(nameof(AudioCompatibilityHint));
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
        RequireIndex(Codec, codecs.Length, "CodecCombo");
        RequireIndex(Container, containers.Length, "ContainerCombo");
        RequireIndex(Speed, encodingPresets.Length, "SpeedCombo");
        RequireIndex(AudioMode, AudioModes.Length, "AudioModeCombo");
        RequireIndex(QualityMode, 2, "QualityModeCombo");
        if (IsBitrateMode)
        {
            RequireIndex(BitrateMode, 2, "BitrateModeCombo");
        }

        var capturedQuality = CaptureNumber(QualityText, Quality, Capabilities.DefaultQuality, QualityMinimum, QualityMaximum,
            1, IsQualityMode, "QualityInput", "Workbench.Quality");
        var video = CaptureNumber(VideoBitrateText, VideoBitrate, 8, 0.1m, 200, 1000000, IsVideoBitrateActive,
            "VideoBitrateInput", "Workbench.VideoBitrate");
        var audio = CaptureNumber(AudioBitrateText, AudioBitrate, 192, 32, 512, 1000, IsEncodedAudio,
            "AudioBitrateInput", "Workbench.AudioBitrate");
        var settings = new VideoExportSettings
        {
            Codec = SelectedCodec,
            Container = SelectedContainer,
            EncodingMode = UseHardwareEncoder ? VideoEncodingMode.HARDWARE : VideoEncodingMode.SOFTWARE,
            RateControlMode = IsQualityMode ? Capabilities.QualityMode : BitrateMode == 0 ? VideoRateControlMode.VBR : VideoRateControlMode.CBR,
            Preset = EncodingPreset,
            Quality = capturedQuality,
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
        if (settings.RateControlMode is VideoRateControlMode.CRF or VideoRateControlMode.CQ)
        {
            Quality = settings.Quality;
        }
        if (settings.RateControlMode != VideoRateControlMode.CRF)
        {
            VideoBitrate = settings.VideoBitrate / 1000000m;
        }
        if (settings.AudioMode is AudioExportMode.Aac or AudioExportMode.Opus)
        {
            AudioBitrate = settings.AudioBitrate / 1000m;
        }
    }

    internal void ApplySettings(VideoExportSettings settings)
    {
        settings = VideoExportCapabilities.ProjectPresetToCurrentPlatform(settings);
        var presetIndex = Array.IndexOf(encodingPresets, settings.Preset);
        refreshingChoices = true;
        try
        {
            ChoicesRefreshing?.Invoke(this, EventArgs.Empty);
            selectedCodec = settings.Codec;
            selectedContainer = settings.Container;
            audioConvertedToOpus = false;
            speed = presetIndex;
            useHardwareEncoder = settings.EncodingMode == VideoEncodingMode.HARDWARE;
            qualityMode = settings.RateControlMode is VideoRateControlMode.CRF or VideoRateControlMode.CQ ? 0 : 1;
            softwareQualityMode = useHardwareEncoder ? softwareQualityMode : qualityMode;
            bitrateMode = settings.RateControlMode == VideoRateControlMode.CBR ? 1 : 0;
            audioMode = (int)settings.AudioMode;
            UpdateContainers();
            RefreshCodecProperties();
            OnPropertyChanged(nameof(Containers));
            RefreshContainerProperties();
            QualityText = settings.Quality.ToString(CultureInfo.CurrentCulture);
            VideoBitrateText = (settings.VideoBitrate / 1000000m).ToString(CultureInfo.CurrentCulture);
            AudioBitrateText = (settings.AudioBitrate / 1000m).ToString(CultureInfo.CurrentCulture);
            Quality = settings.Quality;
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
            OnPropertyChanged(nameof(IsQualityMode));
            OnPropertyChanged(nameof(IsBitrateMode));
            OnPropertyChanged(nameof(IsEncodedAudio));
        }
        finally
        {
            CompleteChoicesRefresh();
        }
    }

    private void CompleteChoicesRefresh()
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

    private void RefreshCodecProperties()
    {
        RefreshQualityModes();
        OnPropertyChanged(nameof(SelectedCodec));
        OnPropertyChanged(nameof(SelectedCodecChoice));
        OnPropertyChanged(nameof(Codec));
        OnPropertyChanged(nameof(QualityMinimum));
        OnPropertyChanged(nameof(QualityMaximum));
        OnPropertyChanged(nameof(CanUseHardwareEncoder));
        OnPropertyChanged(nameof(SupportsWebM));
        OnPropertyChanged(nameof(IsVideoBitrateActive));
    }

    private void RefreshQualityModes()
    {
        QualityModes = [Localization.Get(Capabilities.QualityMode == VideoRateControlMode.CQ
            ? "Workbench.ConstrainedQuality" : "Workbench.ConstantQuality"), Localization.Get("Workbench.BitrateQuality")];
    }

    private void UpdateContainers()
    {
        containers = [.. VideoExportCapabilities.GetContainers(SelectedCodec)
            .Select(format => containerChoices.Single(choice => choice.Container == format))];
        if (containers.All(choice => choice.Container != selectedContainer))
        {
            selectedContainer = VideoExportCapabilities.GetDefaultContainer(SelectedCodec);
            audioConvertedToOpus = false;
        }
        NormalizeAudioForContainer();
    }

    private void RefreshContainerProperties()
    {
        OnPropertyChanged(nameof(SelectedContainer));
        OnPropertyChanged(nameof(SelectedContainerChoice));
        OnPropertyChanged(nameof(Container));
        OnPropertyChanged(nameof(IsWebM));
        OnPropertyChanged(nameof(HasAudioCompatibilityHint));
        OnPropertyChanged(nameof(AudioCompatibilityHint));
    }

    private bool NormalizeAudioForContainer()
    {
        if (!IsWebM || audioMode != (int)AudioExportMode.Aac &&
            (audioMode != (int)AudioExportMode.Copy || sourceAudioCodec is null or "opus" or "vorbis"))
        {
            return false;
        }

        audioMode = (int)AudioExportMode.Opus;
        audioConvertedToOpus = true;
        return true;
    }

    private void RefreshAudioProperties()
    {
        OnPropertyChanged(nameof(AudioMode));
        OnPropertyChanged(nameof(IsEncodedAudio));
        OnPropertyChanged(nameof(AudioCompatibilityHint));
    }

    private void RefreshEncodingModeProperties()
    {
        OnPropertyChanged(nameof(UseHardwareEncoder));
        OnPropertyChanged(nameof(IsSoftwareEncoding));
        OnPropertyChanged(nameof(CanSelectQualityMode));
        OnPropertyChanged(nameof(QualityMode));
        OnPropertyChanged(nameof(IsQualityMode));
        OnPropertyChanged(nameof(IsBitrateMode));
        OnPropertyChanged(nameof(IsVideoBitrateActive));
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
