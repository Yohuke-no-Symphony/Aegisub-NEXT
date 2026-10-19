using System.Collections.Immutable;

namespace AegiNext.Application.Fonts;

internal sealed record FontFamilyReference(string FamilyName, ImmutableArray<FontFileReference> Files);
