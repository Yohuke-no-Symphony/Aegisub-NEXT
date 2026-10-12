using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal static class SubtitleKaraokeStyleJsonMigration
{
    internal static void Upgrade(JsonObject document, JsonSerializerOptions options)
    {
        if (document["subtitles"] is not JsonArray lines)
        {
            return;
        }
        foreach (var item in lines)
        {
            if (item is not JsonObject line)
            {
                continue;
            }
            if (line.ContainsKey("karaokeStyleSpans"))
            {
                throw new JsonException("Legacy projects cannot contain karaoke text-range styles not declared by their version.");
            }
            var spans = ImmutableArray.CreateBuilder<SubtitleKaraokeStyleSpan>();
            Extract(line["karaoke"], spans, options);
            Extract(line["inactiveKaraoke"], spans, options);
            var ordered = ImmutableArray.CreateBuilder<SubtitleKaraokeStyleSpan>();
            foreach (var span in spans.OrderBy(span => span.Utf16Start))
            {
                SubtitleKaraokeStyleEditing.Append(ordered, span);
            }
            if (ordered.Count > 0)
            {
                line["karaokeStyleSpans"] = JsonSerializer.SerializeToNode(ordered.ToImmutable(), options);
            }
        }
    }

    private static void Extract(JsonNode? node, ImmutableArray<SubtitleKaraokeStyleSpan>.Builder spans,
        JsonSerializerOptions options)
    {
        if (node is not JsonArray segments)
        {
            return;
        }
        foreach (var item in segments)
        {
            if (item is not JsonObject segment)
            {
                continue;
            }
            if (!segment.ContainsKey("activeStyle") || !segment.ContainsKey("inactiveStyle"))
            {
                throw new JsonException("A legacy karaoke segment is missing the required visual override fields.");
            }
            var active = segment["activeStyle"]?.Deserialize<KaraokeVisualStyleOverride>(options);
            var inactive = segment["inactiveStyle"]?.Deserialize<KaraokeVisualStyleOverride>(options);
            if (active is { HasOverrides: true } || inactive is { HasOverrides: true })
            {
                var start = segment["utf16Start"]?.Deserialize<int>(options) ?? throw new JsonException("The legacy karaoke range start is missing.");
                var length = segment["utf16Length"]?.Deserialize<int>(options) ?? throw new JsonException("The legacy karaoke range length is missing.");
                spans.Add(new(start, length, active, inactive));
            }
            segment.Remove("activeStyle");
            segment.Remove("inactiveStyle");
        }
    }
}
