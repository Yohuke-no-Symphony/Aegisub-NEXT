namespace AegiNext.Koumori;

/// <summary>
/// Immutable predictions for a requested media range; callers apply project time mapping.
/// </summary>
public sealed record KoumoriAnalysisResult(
    string ModelId,
    KoumoriAnalysisRange Range,
    IReadOnlyList<KoumoriSegment> Segments,
    IReadOnlyList<KoumoriBoundary> Boundaries);
