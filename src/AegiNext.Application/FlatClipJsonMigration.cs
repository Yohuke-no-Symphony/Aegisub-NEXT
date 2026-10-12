using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

internal static class FlatClipJsonMigration
{
    internal static void Upgrade(JsonObject document, JsonSerializerOptions options)
    {
        if (document.ContainsKey("tracks") || document["subtitleTracks"] is not JsonArray tracks ||
            document["subtitles"] is not JsonArray subtitles || document["layers"] is not JsonArray layers)
        {
            throw new JsonException("The legacy project is missing subtitle tracks, subtitles, or composition layers, or contains both old and new track fields.");
        }
        var trackIds = tracks.Select(track => RequiredId(track as JsonObject, "id")).ToHashSet();
        if (trackIds.Count != tracks.Count)
        {
            throw new JsonException("The legacy project contains duplicate track IDs.");
        }
        var subtitleTracks = new Dictionary<Guid, Guid>();
        foreach (var node in subtitles)
        {
            var line = node as JsonObject ?? throw new JsonException("A legacy subtitle cannot be null.");
            var subtitleId = RequiredId(line, "id");
            var trackId = RequiredId(line, "trackId");
            if (!subtitleTracks.TryAdd(subtitleId, trackId) || !trackIds.Contains(trackId))
            {
                throw new JsonException("A legacy subtitle has a duplicate ID or references a missing track.");
            }
            line.Remove("trackId");
        }

        var flattened = new JsonArray();
        var sceneTracks = new Dictionary<Guid, Guid>();
        var groups = new HashSet<Guid>();
        var layerIds = new HashSet<Guid>();
        foreach (var node in layers)
        {
            Flatten(node as JsonObject ?? throw new JsonException("A legacy layer cannot be null."), 0,
                flattened, tracks, trackIds, subtitleTracks, sceneTracks, groups, layerIds, options);
        }
        var ownership = ArrangeTracks(flattened, tracks, trackIds, options);
        document.Remove("subtitleTracks");
        document["tracks"] = ownership.Tracks;
        document["layers"] = flattened;
        UpgradeViewState(document["timelineViewState"] as JsonObject, ownership.Owners, sceneTracks, groups);
    }

    private static void Flatten(JsonObject layer, int depth, JsonArray flattened, JsonArray tracks,
        HashSet<Guid> trackIds, Dictionary<Guid, Guid> subtitleTracks, Dictionary<Guid, Guid> sceneTracks,
        HashSet<Guid> groups, HashSet<Guid> layerIds, JsonSerializerOptions options)
    {
        var id = RequiredId(layer, "id");
        if (depth > 32 || !layerIds.Add(id) || layerIds.Count > 10000 ||
            layer.ContainsKey("trackId") || layer["children"] is not JsonArray children ||
            layer["kind"] is not JsonValue kindValue || !kindValue.TryGetValue<string>(out var kind))
        {
            throw new JsonException("The legacy composition tree is too deep or large, contains duplicate IDs, or has invalid fields.");
        }
        if (kind == "GROUP")
        {
            var metadata = layer.DeepClone().AsObject();
            metadata.Remove("children");
            metadata["kind"] = "SHAPE";
            metadata["trackId"] = ProjectTrack.DEFAULT_TRACK_ID;
            var group = metadata.Deserialize<ProjectLayer>(options) ?? throw new JsonException("A legacy group cannot be null.");
            if (group.SubtitleId is not null || group.Shape is not null || group.Image is not null)
            {
                throw new JsonException("A legacy group cannot contain subtitle, shape, or image payloads.");
            }
            if (group.Transform != new LayerTransform() || !group.Opacity.Equals(1d) ||
                group.Blend != BlendMode.NORMAL || group.Blur != 0 || !group.Tracks.IsEmpty ||
                group.MotionPath is not null || group.Mask is not null)
            {
                throw CannotFlatten(group);
            }
            ProjectValidator.Validate(new() { Layers = [group with { Shape = new(ShapeKind.RECTANGLE, 1, 1) }] });
            foreach (var childNode in children)
            {
                var child = childNode as JsonObject ?? throw new JsonException("A legacy group child cannot be null.");
                var start = child["start"]?.Deserialize<MediaTime>(options) ?? throw new JsonException("A legacy group child is missing its start time.");
                var end = child["end"]?.Deserialize<MediaTime>(options) ?? throw new JsonException("A legacy group child is missing its end time.");
                var blend = child["blend"]?.Deserialize<BlendMode>(options) ?? throw new JsonException("A legacy group child is missing its blend mode.");
                if (start < group.Start || end > group.End || blend != BlendMode.NORMAL)
                {
                    throw CannotFlatten(group);
                }
            }
            groups.Add(id);
            foreach (var child in children)
            {
                Flatten(child!.AsObject(), depth + 1, flattened, tracks, trackIds, subtitleTracks,
                    sceneTracks, groups, layerIds, options);
            }
            return;
        }
        if (children.Count != 0)
        {
            throw new JsonException("Only legacy layer groups can contain child nodes.");
        }
        var clip = layer.DeepClone().AsObject();
        clip.Remove("children");
        if (kind == "SUBTITLE")
        {
            var subtitleId = RequiredId(layer, "subtitleId");
            if (!subtitleTracks.TryGetValue(subtitleId, out var trackId))
            {
                throw new JsonException("A legacy subtitle layer references a missing subtitle.");
            }
            clip["trackId"] = trackId;
        }
        else
        {
            var trackId = CreateSceneTrackId(id, trackIds);
            sceneTracks.Add(id, trackId);
            var track = new ProjectTrack { Id = trackId, Name = CreateSceneTrackName(layer["name"]?.GetValue<string>() ?? "Clip") };
            tracks.Add(JsonSerializer.SerializeToNode(track, options));
            clip["trackId"] = trackId;
        }
        flattened.Add(clip);
    }

    private static (JsonArray Tracks, Dictionary<Guid, List<Guid>> Owners) ArrangeTracks(JsonArray clips,
        JsonArray legacyTracks, HashSet<Guid> reserved, JsonSerializerOptions options)
    {
        var originals = legacyTracks.OfType<JsonObject>().ToArray();
        var originalIds = originals.Select(track => RequiredId(track, "id")).ToArray();
        var parsed = clips.Select(clip => clip!.Deserialize<ProjectLayer>(options) ??
            throw new JsonException("A legacy clip cannot be null.")).ToArray();
        var allocation = new LegacyTrackOrderAllocator(parsed, originalIds).Allocate();
        var reverse = new LegacyTrackOrderAllocator(parsed, originalIds, true).Allocate();
        if (reverse.TrackOwners.Length < allocation.TrackOwners.Length)
        {
            allocation = reverse;
        }
        var owners = originalIds.ToDictionary(id => id, _ => new List<Guid>());
        var firstClips = new Guid[allocation.TrackOwners.Length];
        for (var index = 0; index < parsed.Length; index++)
        {
            var target = allocation.ClipTracks[index];
            if (firstClips[target] == Guid.Empty)
            {
                firstClips[target] = parsed[index].Id;
            }
        }

        var ids = new Guid[allocation.TrackOwners.Length];
        var tracks = new JsonObject[ids.Length];
        for (var node = 0; node < tracks.Length; node++)
        {
            var original = allocation.TrackOwners[node];
            var id = node < originals.Length ? originalIds[original] : CreateSceneTrackId(firstClips[node], reserved);
            ids[node] = id;
            tracks[node] = originals[original].DeepClone().AsObject();
            tracks[node]["id"] = id;
            owners[originalIds[original]].Add(id);
        }
        for (var index = 0; index < clips.Count; index++)
        {
            clips[index]!["trackId"] = ids[allocation.ClipTracks[index]];
        }

        return (new JsonArray(allocation.TrackOrder.Select(node => (JsonNode)tracks[node]).ToArray()), owners);
    }

    private static InvalidDataException CannotFlatten(ProjectLayer group)
    {
        return new($"Legacy layer group '{group.Name}' ({group.Id}) contains transforms, isolated composition, or time clipping and cannot be safely flattened into track clips; the original file was not modified.");
    }

    private static Guid RequiredId(JsonObject? owner, string property)
    {
        if (owner?[property] is not JsonValue value || !value.TryGetValue<Guid>(out var id) || id == Guid.Empty)
        {
            throw new JsonException($"The legacy project is missing a valid {property} ID.");
        }
        return id;
    }

    private static string CreateSceneTrackName(string clipName)
    {
        var clean = new string(clipName.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (clean.Length == 0)
        {
            return "Clip";
        }
        var enumerator = StringInfo.GetTextElementEnumerator(clean);
        var end = 0;
        while (enumerator.MoveNext())
        {
            var next = enumerator.ElementIndex + enumerator.GetTextElement().Length;
            if (next > 128)
            {
                break;
            }
            end = next;
        }
        return end == 0 ? "Clip" : clean[..end];
    }

    private static Guid CreateSceneTrackId(Guid layerId, HashSet<Guid> reserved)
    {
        for (var salt = 0; ; salt++)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"AegiNext.ProjectTrack.{layerId:D}.{salt}"));
            var id = new Guid(bytes.AsSpan(0, 16));
            if (id != Guid.Empty && reserved.Add(id))
            {
                return id;
            }
        }
    }

    private static void UpgradeViewState(JsonObject? state, Dictionary<Guid, List<Guid>> owners,
        Dictionary<Guid, Guid> sceneTracks, HashSet<Guid> groups)
    {
        if (state is null)
        {
            return;
        }
        if (state["collapsedTrackIds"] is JsonArray collapsed)
        {
            var ids = collapsed.Select(node => node?.GetValue<Guid>() ?? Guid.Empty).ToArray();
            if (ids.Length > 100000 || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            {
                throw new JsonException("A legacy collapsed track ID is invalid or duplicated.");
            }
            state["collapsedTrackIds"] = new JsonArray(ids.SelectMany(id =>
                {
                    var mapped = owners.GetValueOrDefault(id) ?? [];
                    if (sceneTracks.TryGetValue(id, out var sceneTrack))
                    {
                        mapped = mapped.Concat(owners[sceneTrack]).ToList();
                    }
                    return mapped.Count != 0 ? mapped : groups.Contains(id) ? [] : new List<Guid> { id };
                }).Distinct()
                .Select(id => JsonValue.Create(id) as JsonNode).ToArray());
        }
        if (state["collapsedAnimationRows"] is JsonArray rows)
        {
            var upgraded = new JsonArray();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var originalIdentities = new HashSet<(string Scope, Guid OwnerId, AnimationProperty Property)>();
            if (rows.Count > 100000)
            {
                throw new JsonException("Legacy collapsed animation rows exceed the budget.");
            }
            foreach (var node in rows)
            {
                var row = node?.DeepClone().AsObject() ?? throw new JsonException("A legacy animation row cannot be null.");
                var id = RequiredId(row, "ownerId");
                var scope = row["scope"]?.GetValue<string>();
                var propertyName = row["property"]?.GetValue<string>();
                if (row.Count != 3 || scope is not ("SCENE_LAYER" or "SUBTITLE_TRACK") ||
                    !Enum.TryParse<AnimationProperty>(propertyName, out var property) ||
                    !AnimationPropertyMetadata.CurrentProperties.Contains(property) ||
                    !originalIdentities.Add((scope, id, property)))
                {
                    throw new JsonException("A legacy collapsed animation row contains invalid or duplicate fields.");
                }
                if (scope == "SCENE_LAYER" && groups.Contains(id))
                {
                    continue;
                }
                var mappedId = scope == "SCENE_LAYER" ? sceneTracks.GetValueOrDefault(id, id) : id;
                row["scope"] = "TRACK";
                foreach (var trackId in owners.GetValueOrDefault(mappedId) ?? [mappedId])
                {
                    var mapped = row.DeepClone().AsObject();
                    mapped["ownerId"] = trackId;
                    if (identities.Add(mapped.ToJsonString()))
                    {
                        upgraded.Add(mapped);
                    }
                }
            }
            state["collapsedAnimationRows"] = upgraded;
        }
    }
}
