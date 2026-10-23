using System.Globalization;
using AegiNext.Core.Timing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Koumori;

internal sealed class KoumoriPreviewRow(MediaTime originalStart, MediaTime originalEnd, float score) : ObservableObject
{
    private bool isSelected = true;
    private readonly string initialStartText = Format(originalStart);
    private readonly string initialEndText = Format(originalEnd);
    private string startText = Format(originalStart);
    private string endText = Format(originalEnd);
    public string ScoreText { get; } = score.ToString("P0", CultureInfo.CurrentCulture);
    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
    public string StartText
    {
        get => startText;
        set => SetProperty(ref startText, value);
    }
    public string EndText
    {
        get => endText;
        set => SetProperty(ref endText, value);
    }
    internal bool TryRead(MediaTime duration, out MediaTime start, out MediaTime end)
    {
        start = end = default;
        if (!TryReadTime(StartText, initialStartText, originalStart, duration, out start) ||
            !TryReadTime(EndText, initialEndText, originalEnd, duration, out end))
        {
            return false;
        }
        return start >= MediaTime.Zero && end > start && end <= duration;
    }

    private static bool TryReadTime(string text, string originalText, MediaTime original,
        MediaTime duration, out MediaTime value)
    {
        value = original;
        if (text == originalText)
        {
            return true;
        }
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var seconds) ||
            seconds < 0 || seconds > (decimal)duration.Numerator / duration.Denominator)
        {
            return false;
        }
        var ticks = decimal.Round(seconds * TimeSpan.TicksPerSecond);
        if (ticks > long.MaxValue)
        {
            return false;
        }
        value = new((long)ticks, TimeSpan.TicksPerSecond);
        if (value > duration)
        {
            value = duration;
        }
        return true;
    }

    private static string Format(MediaTime value) => ((double)value.Numerator / value.Denominator).ToString("F3", CultureInfo.CurrentCulture);
}
