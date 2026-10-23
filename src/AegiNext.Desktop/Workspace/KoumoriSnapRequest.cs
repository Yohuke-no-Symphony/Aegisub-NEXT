using AegiNext.Koumori;

namespace AegiNext.Desktop.Workspace;

internal sealed record KoumoriSnapRequest(string ContextIdentity, KoumoriAnalysisRange Range);
