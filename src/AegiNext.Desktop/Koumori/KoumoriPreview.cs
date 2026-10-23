using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Koumori;

namespace AegiNext.Desktop.Koumori;

internal sealed record KoumoriPreview(ProjectDocument Document, long Generation, long MediaEpoch,
    string MediaPath, MediaTime Origin, MediaTime Duration, string ModelDirectory, string ModelIdentity, long FileLength, DateTime LastWriteTimeUtc, KoumoriAnalysisResult Result);
