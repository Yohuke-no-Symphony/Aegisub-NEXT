using System.Text.Json;
using System.Text.Json.Nodes;

namespace AegiNext.Application;

internal static class PlaybackOriginJsonMigration
{
    private const int PLAYBACK_ORIGIN_VERSION = 6;

    internal static void Upgrade(JsonObject document, int sourceVersion)
    {
        if (sourceVersion >= PLAYBACK_ORIGIN_VERSION)
        {
            return;
        }
        if (document["media"] is JsonObject media)
        {
            if (media.ContainsKey("playbackOrigin"))
            {
                throw new JsonException("Legacy projects cannot contain playback-origin fields not declared by their version.");
            }
            media.Add("playbackOrigin", null);
        }
    }
}
