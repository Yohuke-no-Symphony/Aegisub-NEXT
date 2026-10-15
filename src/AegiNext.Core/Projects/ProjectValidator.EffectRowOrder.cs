namespace AegiNext.Core.Projects;

public static partial class ProjectValidator
{
    private const int MAXIMUM_EFFECT_ROW_IDENTITIES = 100000;

    private static void EffectRowOrder(ProjectLayer layer)
    {
        Require(!layer.EffectRowOrder.IsDefault && layer.EffectRowOrder.Length <= MAXIMUM_EFFECT_ROW_IDENTITIES,
            "特效栈顺序无效或超过身份预算。");
        var identities = new HashSet<TimelineEffectRowIdentity>();
        foreach (var identity in layer.EffectRowOrder)
        {
            Require(identity is not null && identities.Add(identity), "特效栈顺序包含空身份或重复身份。");
            identity.Validate();
        }
    }
}
