using AegiNext.Core.Effects;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

internal sealed record PreparedEffectInstance(EffectInstance Source, CompiledEffectScript Plan);
