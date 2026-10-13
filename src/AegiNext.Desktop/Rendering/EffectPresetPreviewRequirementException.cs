namespace AegiNext.Desktop.Rendering;

internal sealed class EffectPresetPreviewRequirementException : Exception
{
    /// <summary>携带统一样例不能提供的语义依赖和原脚本位置，交给界面层本地化。</summary>
    public EffectPresetPreviewRequirementException(EffectPresetPreviewRequirement requirement, int line, int column)
        : base("The standard preview sample has no motion path; the effect requires an existing path.")
    {
        Requirement = requirement;
        Line = line;
        Column = column;
    }

    public EffectPresetPreviewRequirement Requirement { get; }
    public int Line { get; }
    public int Column { get; }
}
