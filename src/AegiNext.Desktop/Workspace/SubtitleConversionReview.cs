using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.SubtitleFormats;

namespace AegiNext.Desktop.Workspace;

internal sealed record SubtitleConversionReview(ImmutableArray<SubtitleFormatDiagnostic> Diagnostics,
    ImmutableArray<SubtitleLine> Subtitles, ImmutableArray<ProjectLayer> Layers = default)
{
    private const int PREVIEW_ELEMENT_LIMIT = 120;

    internal int AffectedSubtitleCount => Diagnostics.Where(item => item.SubtitleId.HasValue)
        .Select(item => item.SubtitleId).Distinct().Count();

    internal int AffectedClipCount => AffectedSubtitleCount + Diagnostics.Where(item => item.SubtitleId is null && item.LayerId.HasValue)
        .Select(item => item.LayerId).Distinct().Count();

    internal string FormatSummary()
    {
        return Diagnostics.Any(item => item.LayerId.HasValue)
            ? Localization.Format("Workbench.SubtitleConversionGraphicSummary", Diagnostics.Length, AffectedClipCount)
            : Localization.Format("Workbench.SubtitleConversionSummary", Diagnostics.Length, AffectedSubtitleCount);
    }

    internal string FormatDetails()
    {
        var positions = Subtitles.Select((line, index) => (Line: line, Number: index + 1))
            .ToDictionary(item => item.Line.Id);
        var clips = (Layers.IsDefault ? ImmutableArray<ProjectLayer>.Empty : Layers).ToDictionary(layer => layer.Id);
        var result = new StringBuilder();
        foreach (var group in Diagnostics.GroupBy(item => (item.SubtitleId, item.LayerId)).OrderBy(group =>
            group.Key.SubtitleId is { } id && positions.TryGetValue(id, out var position) ? position.Number : int.MaxValue))
        {
            if (result.Length > 0)
            {
                result.AppendLine();
            }
            if (group.Key.SubtitleId is { } id && positions.TryGetValue(id, out var position))
            {
                result.AppendLine(Localization.Format("Workbench.SubtitleConversionPosition", position.Number,
                    TimelineTimeText.Format(position.Line.Start), TimelineTimeText.Format(position.Line.End)));
                result.AppendLine(PreviewText(position.Line.Text));
            }
            else if (group.Key.LayerId is { } layerId && clips.TryGetValue(layerId, out var layer))
            {
                result.AppendLine(Localization.Format("Workbench.SubtitleConversionGraphicPosition", PreviewText(layer.Name),
                    TimelineTimeText.Format(layer.Start), TimelineTimeText.Format(layer.End)));
            }
            else
            {
                result.AppendLine(group.Key.SubtitleId is { } unknownId
                    ? Localization.Format("Workbench.SubtitleConversionUnknownPosition", unknownId)
                    : group.Key.LayerId is { } unknownLayerId
                        ? Localization.Format("Workbench.SubtitleConversionUnknownClip", unknownLayerId)
                        : Localization.Get("Workbench.SubtitleConversionGlobal"));
            }
            foreach (var diagnostic in group)
            {
                result.Append("  ").Append(diagnostic.Code).Append(": ").AppendLine(diagnostic.Message);
            }
        }
        return result.ToString().TrimEnd();
    }

    private static string PreviewText(string text)
    {
        var result = new StringBuilder();
        var elements = StringInfo.GetTextElementEnumerator(text);
        var count = 0;
        while (elements.MoveNext())
        {
            if (count++ == PREVIEW_ELEMENT_LIMIT)
            {
                result.Append('…');
                break;
            }
            result.Append(elements.GetTextElement().Replace("\r\n", " ", StringComparison.Ordinal)
                .Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '));
        }
        return result.ToString();
    }
}
